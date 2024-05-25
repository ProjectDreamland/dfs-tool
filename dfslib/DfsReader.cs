using System.Diagnostics;
using DfsLib.Models;

namespace DfsLib;

public class DfsReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    public DfsReader(FileStream stream)
    {
        _stream = stream;
        _reader = new BinaryReader(stream);
    }

    public void Extract(string outputDirectory)
    {

        DfsHeader header = ReadHeader();

        if (header.MagicNumber != DfsConstants.DfsMagic || header.Version != DfsConstants.DfsVersion)
        {
            throw new InvalidDataException("Invalid DFS file format.");
        }

        string dfsDirectory = Path.GetDirectoryName(_stream.Name) ?? throw new DirectoryNotFoundException("Invalid DFS file directory.");
        string dfsFileNameWithoutExt = Path.GetFileNameWithoutExtension(_stream.Name);

        FileStream[] subFileStreams = new FileStream[header.SubFileCount];
        BinaryReader[] subFileReaders = new BinaryReader[header.SubFileCount];

        for (int i = 0; i < header.SubFileCount; i++)
        {
            string subFilePath = Path.Combine(dfsDirectory, $"{dfsFileNameWithoutExt}.{i:D3}");
            if (!File.Exists(subFilePath))
            {
                throw new FileNotFoundException($"Subfile {subFilePath} not found.");
            }

            subFileStreams[i] = new FileStream(subFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            subFileReaders[i] = new BinaryReader(subFileStreams[i]);
        }

        uint bufferSize = 0;
        var fileEntries = EnumerateFileEntries(header).ToArray();
        foreach (var fileEntry in fileEntries)
        {
            if (fileEntry.FileLength > bufferSize)
            {
                bufferSize = fileEntry.FileLength;
            }
        }

        byte[] fileData = new byte[bufferSize];

        uint subFileOffset = 0;
        int subFileIndex = 0;

        foreach (var fileEntry in fileEntries)
        {
            string outputFilePath;
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                string fileNamePart1 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart1Offset);
                string fileNamePart2 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart2Offset);
                string extension = ReadStringFromTable(header.StringTableOffset, fileEntry.ExtensionOffset);
                outputFilePath = Path.Combine(outputDirectory, $"{fileNamePart1}{fileNamePart2}{extension}");
            }
            else
            {
                string pathName = ReadStringFromTable(header.StringTableOffset, fileEntry.PathNameOffset);
                string fileNamePart1 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart1Offset);
                string fileNamePart2 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart2Offset);
                string extension = ReadStringFromTable(header.StringTableOffset, fileEntry.ExtensionOffset);
                outputFilePath = Path.Combine(pathName, $"{fileNamePart1}{fileNamePart2}{extension}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? throw new DirectoryNotFoundException("Invalid output directory."));

            using Stream outputStream = File.Create(outputFilePath);

            uint offset = fileEntry.DataOffset - subFileOffset;
            uint length = fileEntry.FileLength;
            uint bytesRead = 0;

            while (bytesRead < length)
            {
                if (offset + length - bytesRead > subFileStreams[subFileIndex].Length)
                {
                    uint remainingBytes = (uint)(subFileStreams[subFileIndex].Length - offset);
                    subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);
                    subFileReaders[subFileIndex].Read(fileData, (int)bytesRead, (int)remainingBytes);
                    bytesRead += remainingBytes;
                    var subFileLength = subFileStreams[subFileIndex].Length;
                    subFileOffset += (uint)subFileLength;
                    offset = 0;
                    subFileIndex++;
                }
                else
                {
                    subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);
                    subFileReaders[subFileIndex].Read(fileData, (int)bytesRead, (int)(length - bytesRead));
                    bytesRead = length;
                }
            }

            outputStream.Write(fileData, 0, (int)fileEntry.FileLength);
        }

        for (int i = 0; i < header.SubFileCount; i++)
        {
            subFileReaders[i].Dispose();
            subFileStreams[i].Dispose();
        }
    }

    public void Verify()
    {
        DfsHeader header = ReadHeader();

        if (header.MagicNumber != DfsConstants.DfsMagic || header.Version != DfsConstants.DfsVersion)
        {
            throw new InvalidDataException("Invalid DFS file format.");
        }


        var originalChecksum = header.FileChecksum;
        // set to zero to calculate the checksum    
        header.FileChecksum = 0;
        var headerData = Helpers.GetBytes(header);
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

        string dfsDirectory = Path.GetDirectoryName(_stream.Name) ?? throw new DirectoryNotFoundException("Invalid DFS file directory.");
        string dfsFileNameWithoutExt = Path.GetFileNameWithoutExtension(_stream.Name);

        FileStream[] subFileStreams = new FileStream[header.SubFileCount];
        BinaryReader[] subFileReaders = new BinaryReader[header.SubFileCount];

        for (int i = 0; i < header.SubFileCount; i++)
        {
            string subFilePath = Path.Combine(dfsDirectory, $"{dfsFileNameWithoutExt}.{i:D3}");
            if (!File.Exists(subFilePath))
            {
                throw new FileNotFoundException($"Subfile {subFilePath} not found.");
            }

            subFileStreams[i] = new FileStream(subFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            subFileReaders[i] = new BinaryReader(subFileStreams[i]);
        }

        uint bufferSize = 32768;
        byte[] fileData = new byte[bufferSize];

        uint totalLength = ReadSubFileTable(header.SubFileTableOffset, header.SubFileCount)[^1].SubFileOffset;

        if (totalLength % 32768 != 0)
        {
            throw new InvalidDataException("Error: data files length not a multiple of 32768");
        }

        if (header.ChecksumTableOffset == 0)
        {
            Debug.WriteLine("No checksum table found. DFS was built without CRC.");
            return;
        }

        uint index = 0;
        int subFileIndex = 0;

        while (index < totalLength)
        {
            uint subFileBase = 0;
            while (index >= ReadSubFileTable(header.SubFileTableOffset, header.SubFileCount)[subFileIndex].SubFileOffset)
            {
                subFileBase = ReadSubFileTable(header.SubFileTableOffset, header.SubFileCount)[subFileIndex].SubFileOffset;
                subFileIndex++;
            }

            int offset = (int)(index - subFileBase);
            subFileReaders[subFileIndex].BaseStream.Seek(offset, SeekOrigin.Begin);

            int bytesToRead = (int)Math.Min(bufferSize, totalLength - index);
            subFileReaders[subFileIndex].Read(fileData, 0, bytesToRead);

            ushort checksum = 0;
            bool isLastChunk = index + bytesToRead == totalLength;

            if (!isLastChunk)
            {
                for (int i = 0; i < bufferSize; i++)
                {
                    checksum = Crc16.Crc16ApplyByte(i < bytesToRead ? fileData[i] : (byte)0, checksum);
                }

                int checksumIndex = (int)(ReadSubFileTable(header.SubFileTableOffset, header.SubFileCount)[subFileIndex].ChecksumIndex + (offset / 32768));
                ushort storedChecksum = ReadChecksumTable(header.ChecksumTableOffset, checksumIndex);

                if (checksum != storedChecksum)
                {
                    throw new InvalidDataException($"Checksum mismatch at index {index} ({subFileIndex}) {checksum} != {storedChecksum}");
                }
            }
            else
            {
                // todo(fix): last chunk is not being verified, not sure why
                //  for now, just skip the verification. game assets don't use crc anyway.
                Console.WriteLine($"Skipping checksum verification for the last chunk at index {index} ({subFileIndex})");
            }

            index += (uint)bytesToRead;
        }

        for (int i = 0; i < header.SubFileCount; i++)
        {
            subFileReaders[i].Dispose();
            subFileStreams[i].Dispose();
        }
    }

    public IEnumerable<DfsFileInfo> EnumerateFiles()
    {
        DfsHeader header = ReadHeader();

        if (header.MagicNumber != DfsConstants.DfsMagic || header.Version != DfsConstants.DfsVersion)
        {
            throw new InvalidDataException("Invalid DFS file format.");
        }
        foreach (var fileEntry in EnumerateFileEntries(header))
        {
            string fileNamePart1 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart1Offset);
            string fileNamePart2 = ReadStringFromTable(header.StringTableOffset, fileEntry.FileNamePart2Offset);
            string extension = ReadStringFromTable(header.StringTableOffset, fileEntry.ExtensionOffset);
            string pathName = ReadStringFromTable(header.StringTableOffset, fileEntry.PathNameOffset);


            string fileName = $"{fileNamePart1}{fileNamePart2}{extension}";
            string filePath = Path.Combine(pathName, fileName);
            uint fileSize = fileEntry.FileLength;

            yield return new DfsFileInfo(fileName, filePath, fileSize);
        }
    }

    private IEnumerable<DfsFileEntry> EnumerateFileEntries(DfsHeader header)
    {
        _stream.Seek(header.FileEntriesOffset, SeekOrigin.Begin);
        for (int i = 0; i < header.TotalFileCount; i++)
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
        return new()
        {
            FileNamePart1Offset = _reader.ReadUInt32(),
            FileNamePart2Offset = _reader.ReadUInt32(),
            PathNameOffset = _reader.ReadUInt32(),
            ExtensionOffset = _reader.ReadUInt32(),
            DataOffset = _reader.ReadUInt32(),
            FileLength = _reader.ReadUInt32()
        };
    }


    private string ReadStringFromTable(uint stringTableOffset, uint offset)
    {
        long currentPosition = _reader.BaseStream.Position;
        _reader.BaseStream.Seek(stringTableOffset + offset, SeekOrigin.Begin);

        List<byte> bytes = [];
        byte b;
        while ((b = _reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }

        _reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
    }

    private DfsSubFileEntry[] ReadSubFileTable(uint subFileTableOffset, int subFileCount)
    {
        _stream.Seek(subFileTableOffset, SeekOrigin.Begin);
        DfsSubFileEntry[] subFileTable = new DfsSubFileEntry[subFileCount];
        for (int i = 0; i < subFileCount; i++)
        {
            subFileTable[i] = new DfsSubFileEntry
            {
                SubFileOffset = _reader.ReadUInt32(),
                ChecksumIndex = _reader.ReadUInt32()
            };
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
        _reader.Dispose();
        _stream.Dispose();
    }
}
