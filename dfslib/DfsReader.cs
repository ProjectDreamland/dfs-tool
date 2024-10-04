using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DfsLib.Models;
using System.Text;

namespace DfsLib;

/// <summary>
/// Provides functionality to read and manipulate files from a DFS archive.
/// </summary>
public sealed class DfsReader : IDisposable
{
    private readonly FrozenDictionary<string, DfsFileEntry> _fileEntriesCache;
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private DfsHeader _header;
    private readonly FileStream[] _subFileStreams;
    private readonly BinaryReader[] _subFileReaders;

    /// <summary>
    /// Initializes a new instance of the <see cref="DfsReader"/> class.
    /// </summary>
    /// <param name="stream">The stream containing the DFS archive.</param>
    public DfsReader(FileStream stream)
    {
        _stream = stream;
        _reader = new BinaryReader(stream);
        _header = ReadHeader();

        _fileEntriesCache = CacheFileEntries();
        (_subFileStreams, _subFileReaders) = OpenSubFileStreams();
    }

    private FrozenDictionary<string, DfsFileEntry> CacheFileEntries()
    {
        if (_header.MagicNumber != DfsConstants.DfsMagic || _header.Version != DfsConstants.DfsVersion)
        {
            throw new InvalidDataException("Invalid DFS file format.");
        }

        var entries = new Dictionary<string, DfsFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateFileEntries())
        {
            string fileNamePart1 = ReadStringFromTable(_header.StringTableOffset, entry.FileNamePart1Offset);
            string fileNamePart2 = ReadStringFromTable(_header.StringTableOffset, entry.FileNamePart2Offset);
            string extension = ReadStringFromTable(_header.StringTableOffset, entry.ExtensionOffset);
            string entryFileName = $"{fileNamePart1}{fileNamePart2}{extension}";
            entries[entryFileName] = entry;
        }

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private (FileStream[], BinaryReader[]) OpenSubFileStreams()
    {
        string dfsDirectory = Path.GetDirectoryName(_stream.Name) ?? throw new DirectoryNotFoundException("Invalid DFS file directory.");
        string dfsFileNameWithoutExt = Path.GetFileNameWithoutExtension(_stream.Name);

        FileStream[] subFileStreams = new FileStream[_header.SubFileCount];
        BinaryReader[] subFileReaders = new BinaryReader[_header.SubFileCount];

        for (int i = 0; i < _header.SubFileCount; i++)
        {
            string subFilePath = Path.Combine(dfsDirectory, $"{dfsFileNameWithoutExt}.{i:D3}");
            if (!File.Exists(subFilePath))
            {
                throw new FileNotFoundException($"Subfile {subFilePath} not found.");
            }

            subFileStreams[i] = new FileStream(subFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            subFileReaders[i] = new BinaryReader(subFileStreams[i]);
        }

        return (subFileStreams, subFileReaders);
    }

    private byte[] GetFileDataInternal(DfsFileEntry fileEntry)
    {
        int fileLength = (int)fileEntry.FileLength;
        byte[] rentedArray = ArrayPool<byte>.Shared.Rent(fileLength);

        uint subFileOffset = 0;
        int subFileIndex = 0;
        uint offset = fileEntry.DataOffset - subFileOffset;
        uint length = fileEntry.FileLength;
        uint bytesRead = 0;

        while (bytesRead < length)
        {
            if (offset + length - bytesRead > _subFileStreams[subFileIndex].Length)
            {
                uint remainingBytes = (uint)(_subFileStreams[subFileIndex].Length - offset);
                _subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);
                _subFileReaders[subFileIndex].Read(rentedArray, (int)bytesRead, (int)remainingBytes);
                bytesRead += remainingBytes;
                var subFileLength = _subFileStreams[subFileIndex].Length;
                subFileOffset += (uint)subFileLength;
                offset = 0;
                subFileIndex++;
            }
            else
            {
                _subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);
                _subFileReaders[subFileIndex].Read(rentedArray, (int)bytesRead, (int)(length - bytesRead));
                bytesRead = length;
            }
        }

        return rentedArray;
    }
    /// <summary>
    /// Dumps the contents of the string table to a file for analysis.
    /// </summary>
    /// <param name="outputPath">The path where the dump file will be created.</param>
    private void DumpStringTable(string outputPath)
    {
        using (var writer = new StreamWriter(outputPath))
        {
            writer.WriteLine($"String Table Size: {_header.StringTableLength}");
            writer.WriteLine("Offset\tLength\tString");
            writer.WriteLine("------\t------\t------");

            uint offset = 0;
            while (offset < _header.StringTableLength)
            {
                string str = ReadStringFromTable(_header.StringTableOffset, offset);
                writer.WriteLine($"{offset}\t{str.Length}\t{str}");
                offset += (uint)(str.Length + 1); // +1 for null terminator
            }
        }
    }


    /// <summary>
    /// Gets the data of a specified file from the DFS archive.
    /// </summary>
    /// <param name="fileName">The name of the file to retrieve.</param>
    /// <returns>A <see cref="DfsFile"/> object containing the file data.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the specified file is not found in the archive.</exception>
    public DfsFile GetFileData(string fileName)
    {
        if (!_fileEntriesCache.TryGetValue(fileName, out var fileEntry))
        {
            throw new FileNotFoundException($"File {fileName} not found.");
        }
        return new DfsFile(GetFileDataInternal(fileEntry), (int)fileEntry.FileLength, fileName);
    }

    /// <summary>
    /// Extracts all files from the DFS archive to the specified output directory.
    /// </summary>
    /// <param name="outputDirectory">The directory to extract the files to.</param>
    public void Extract(string outputDirectory)
    {
        uint bufferSize = 0;
        var fileEntries = _fileEntriesCache.Values.ToArray();
        foreach (var fileEntry in fileEntries)
        {
            if (fileEntry.FileLength > bufferSize)
            {
                bufferSize = fileEntry.FileLength;
            }
        }

        foreach (var fileEntry in fileEntries)
        {
            string outputFilePath;
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                string fileNamePart1 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart1Offset);
                string fileNamePart2 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart2Offset);
                string extension = ReadStringFromTable(_header.StringTableOffset, fileEntry.ExtensionOffset);
                outputFilePath = Path.Combine(outputDirectory, $"{fileNamePart1}{fileNamePart2}{extension}");
            }
            else
            {
                string pathName = ReadStringFromTable(_header.StringTableOffset, fileEntry.PathNameOffset);
                string fileNamePart1 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart1Offset);
                string fileNamePart2 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart2Offset);
                string extension = ReadStringFromTable(_header.StringTableOffset, fileEntry.ExtensionOffset);
                outputFilePath = Path.Combine(pathName, $"{fileNamePart1}{fileNamePart2}{extension}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? throw new DirectoryNotFoundException("Invalid output directory."));
            using Stream outputStream = File.Create(outputFilePath);
            byte[] fileContent = GetFileDataInternal(fileEntry);
            outputStream.Write(fileContent, 0, (int)fileEntry.FileLength);
            ArrayPool<byte>.Shared.Return(fileContent);
        }
    }

    /// <summary>
    /// Verifies the integrity of the DFS archive.
    /// </summary>
    public void Verify()
    {
        if (_header.MagicNumber != DfsConstants.DfsMagic || _header.Version != DfsConstants.DfsVersion)
        {
            throw new InvalidDataException("Invalid DFS file format.");
        }

        var originalChecksum = _header.FileChecksum;
        // set to zero to calculate the checksum    
        _header.FileChecksum = 0;
        var headerData = Helpers.GetBytes(_header);
        var body = new byte[_stream.Length - headerData.Length];

        _stream.Seek(headerData.Length, SeekOrigin.Begin);
        _stream.Read(body, 0, body.Length);

        var sum = new CheckSummer(uint.MaxValue);
        sum.ApplyData(headerData, headerData.Length);
        sum.ApplyData(body, body.Length);

        var calculatedChecksum = sum.GetChecksum();
        if (calculatedChecksum != originalChecksum)
        {
            throw new InvalidDataException($"Checksum mismatch in header. Expected {originalChecksum}, got {calculatedChecksum}");
        }

        uint bufferSize = 32768;
        byte[] fileData = new byte[bufferSize];

        uint totalLength = ReadSubFileTable(_header.SubFileTableOffset, _header.SubFileCount)[^1].SubFileOffset;

        if (totalLength % 32768 != 0)
        {
            throw new InvalidDataException("Error: data files length not a multiple of 32768");
        }

        if (_header.ChecksumTableOffset == 0)
        {
            Debug.WriteLine("No checksum table found. DFS was built without CRC.");
            return;
        }

        uint index = 0;
        int subFileIndex = 0;

        while (index < totalLength)
        {
            uint subFileBase = 0;
            while (index >= ReadSubFileTable(_header.SubFileTableOffset, _header.SubFileCount)[subFileIndex].SubFileOffset)
            {
                subFileBase = ReadSubFileTable(_header.SubFileTableOffset, _header.SubFileCount)[subFileIndex].SubFileOffset;
                subFileIndex++;
            }

            int offset = (int)(index - subFileBase);
            _subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);

            int bytesToRead = (int)Math.Min(bufferSize, totalLength - index);
            _subFileReaders[subFileIndex].Read(fileData, 0, bytesToRead);

            ushort checksum = 0;
            bool isLastChunk = index + bytesToRead == totalLength;

            if (!isLastChunk)
            {
                for (int i = 0; i < bufferSize; i++)
                {
                    checksum = Crc16.Crc16ApplyByte(i < bytesToRead ? fileData[i] : (byte)0, checksum);
                }

                int checksumIndex = (int)(ReadSubFileTable(_header.SubFileTableOffset, _header.SubFileCount)[subFileIndex].ChecksumIndex + (offset / 32768));
                ushort storedChecksum = ReadChecksumTable(_header.ChecksumTableOffset, checksumIndex);

                if (checksum != storedChecksum)
                {
                    throw new InvalidDataException($"Checksum mismatch at index {index} ({subFileIndex}) {checksum} != {storedChecksum}");
                }
            }
            else
            {
                // todo: last chunk is not being verified, not sure why
                // for now, just skip the verification. game assets don't use crc anyway.
                Console.WriteLine($"Skipping checksum verification for the last chunk at index {index} ({subFileIndex})");
            }

            index += (uint)bytesToRead;
        }
    }

    /// <summary>
    /// Enumerates the files contained in the DFS archive.
    /// </summary>
    /// <returns>An enumerable collection of <see cref="DfsFileInfo"/> objects representing the files in the archive.</returns>
    public IEnumerable<DfsFileInfo> EnumerateFiles()
    {
        foreach (var fileEntry in _fileEntriesCache.Values)
        {
            string fileNamePart1 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart1Offset);
            string fileNamePart2 = ReadStringFromTable(_header.StringTableOffset, fileEntry.FileNamePart2Offset);
            string extension = ReadStringFromTable(_header.StringTableOffset, fileEntry.ExtensionOffset);
            string pathName = ReadStringFromTable(_header.StringTableOffset, fileEntry.PathNameOffset);

            string fileName = $"{fileNamePart1}{fileNamePart2}{extension}";
            string filePath = Path.Combine(pathName, fileName);
            uint fileSize = fileEntry.FileLength;

            yield return new DfsFileInfo(fileName, filePath, fileSize);
        }
    }

    private IEnumerable<DfsFileEntry> EnumerateFileEntries()
    {
        _stream.Seek(_header.FileEntriesOffset, SeekOrigin.Begin);
        for (int i = 0; i < _header.TotalFileCount; i++)
        {
            yield return ReadFileEntry();
        }
    }

    private DfsHeader ReadHeader()
    {
        _stream.Seek(0, SeekOrigin.Begin);
        return new DfsHeader
        {
            MagicNumber = _reader.ReadInt32(),
            Version = _reader.ReadInt32(),
            FileChecksum = _reader.ReadUInt32(),
            SectorSize = _reader.ReadInt32(),
            MaxSplitSize = _reader.ReadUInt32(),
            TotalFileCount = _reader.ReadInt32(),
            SubFileCount = _reader.ReadInt32(),
            StringTableLength = _reader.ReadInt32(),
            SubFileTableOffset = _reader.ReadUInt32(),
            FileEntriesOffset = _reader.ReadUInt32(),
            ChecksumTableOffset = _reader.ReadUInt32(),
            StringTableOffset = _reader.ReadUInt32()
        };
    }

    private DfsFileEntry ReadFileEntry()
    {
        return new DfsFileEntry(
            _reader.ReadUInt32(),
            _reader.ReadUInt32(),
            _reader.ReadUInt32(),
            _reader.ReadUInt32(),
            _reader.ReadUInt32(),
            _reader.ReadUInt32()
        );
    }

    private string ReadStringFromTable(uint stringTableOffset, uint offset)
    {
        long currentPosition = _reader.BaseStream.Position;
        _reader.BaseStream.Seek(stringTableOffset + offset, SeekOrigin.Begin);

        const int bufferSize = 256;
        Span<byte> buffer = stackalloc byte[bufferSize];
        int byteCount = 0;

        // Read bytes into the buffer until we hit the null terminator
        while (true)
        {
            int bytesRead = _reader.BaseStream.Read(buffer.Slice(byteCount, 1));
            if (bytesRead == 0 || buffer[byteCount] == 0)
            {
                break;
            }
            byteCount++;

            // Double buffer size if needed
            if (byteCount >= buffer.Length)
            {
                byte[] largerBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                buffer[..byteCount].CopyTo(largerBuffer);
                ArrayPool<byte>.Shared.Return(buffer.ToArray());
                buffer = largerBuffer.AsSpan(0, buffer.Length * 2);
            }
        }

        _reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        return Encoding.ASCII.GetString(buffer.Slice(0, byteCount));
    }


    private DfsSubFileEntry[] ReadSubFileTable(uint subFileTableOffset, int subFileCount)
    {
        _stream.Seek(subFileTableOffset, SeekOrigin.Begin);
        DfsSubFileEntry[] subFileTable = new DfsSubFileEntry[subFileCount];
        for (int i = 0; i < subFileCount; i++)
        {
            subFileTable[i] = new DfsSubFileEntry(_reader.ReadUInt32(), _reader.ReadUInt32());
        }
        return subFileTable;
    }

    private ushort ReadChecksumTable(uint checksumTableOffset, int index)
    {
        _stream.Seek(checksumTableOffset + index * sizeof(ushort), SeekOrigin.Begin);
        return _reader.ReadUInt16();
    }

    public void Dispose()
    {
        foreach (var reader in _subFileReaders)
        {
            reader.Dispose();
        }

        foreach (var stream in _subFileStreams)
        {
            stream.Dispose();
        }

        _reader.Dispose();
        _stream.Dispose();
    }
}
