using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DfsLib.Models;

namespace DfsLib;

/// <summary>
/// Provides functionality to write files to a DFS archive.
/// </summary>
public sealed class DfsWriter : IDisposable
{
    private readonly string _outputPath;
    private readonly IEnumerable<string> _filePaths;
    private readonly HashSet<string> _sectorAlignedExtensions;
    private CheckSummer _checkSummer;
    private readonly Dictionary<string, int> _stringTable;
    private readonly List<DfsFileEntry> _fileEntries;
    private readonly List<DfsSubFileEntry> _subFileEntries;

    private List<ushort> _checksumTable;

    private int _currentDataFileIndex;
    private long _currentDataFileOffset;

    private readonly uint _sectorSize;
    private readonly uint _splitSize;
    private readonly uint _chunkSize;

    private readonly bool _enableCrc;

    /// <summary>
    /// Initializes a new instance of the <see cref="DfsWriter"/> class.
    /// </summary>
    /// <param name="outputPath">The output path for the DFS archive.</param>
    /// <param name="filePaths">The collection of file paths to include in the DFS archive.</param>
    /// <param name="sectorAlignedExtensions">The collection of file extensions that should be sector-aligned.</param>
    /// <param name="sectorSize">The sector size to use for the DFS archive.</param>
    /// <param name="splitSize">The split size to use for the DFS archive.</param>
    /// <param name="chunkSize">The chunk size to use for the DFS archive.</param>
    /// <param name="enableCrc">Whether to enable CRC checksums for the DFS archive.</param>
    public DfsWriter(string outputPath, IEnumerable<string> filePaths, IEnumerable<string> sectorAlignedExtensions, uint sectorSize = DfsConstants.DefaultSectorSize, uint splitSize = DfsConstants.DefaultSplitSize, uint chunkSize = DfsConstants.DefaultChunkSize, bool enableCrc = false)
    {
        _outputPath = outputPath;
        _filePaths = filePaths;
        _sectorAlignedExtensions = new HashSet<string>(sectorAlignedExtensions, StringComparer.OrdinalIgnoreCase);
        _sectorSize = sectorSize;
        _splitSize = splitSize;
        _enableCrc = enableCrc;
        _chunkSize = chunkSize;

        _checkSummer = new CheckSummer(chunkSize);
        _checksumTable = new List<ushort>();
        _stringTable = [];
        _fileEntries = [];
        _subFileEntries = [];

        _currentDataFileIndex = 0;
        _currentDataFileOffset = 0;

        Debug.Assert(sectorSize > 0);
        Debug.Assert((sectorSize & (sectorSize - 1)) == 0);
        Debug.Assert((splitSize % sectorSize) == 0);
    }

    /// <summary>
    /// Writes the DFS archive to the output path.
    /// </summary>
    public void Write()
    {
        _checkSummer.Init(_chunkSize);
        // Data
        int filesOutputCount = 0;
        int dataFilesCount = 0;
        Stream? dataFileStream = null;
        long dataPosition = 0;
        long filePosition = 0;
        bool isSectorAligned = false;
        bool isFirstFile = true;

        // Build data file name
        string dataFileName = Path.ChangeExtension(_outputPath, null);

        // Generate the pathname to the DFS file
        string dfsFilePath = _outputPath;

        // Read scripts and generate file list
        var sourceFiles = _filePaths.Select((path, index) => new { Path = path, Index = index }).ToList();

        // Loop through all files
        for (int i = 0; i < sourceFiles.Count; i++)
        {
            var currentFile = sourceFiles[i];
            var previousFile = sourceFiles[Math.Max(i - 1, 0)];
            var nextFile = sourceFiles[Math.Min(i + 1, sourceFiles.Count - 1)];

            // Assert that currentFile.Path is not empty
            Debug.Assert(!string.IsNullOrEmpty(currentFile.Path));

            // Split path into components
            string drive = Path.GetPathRoot(currentFile.Path);
            string directory = Path.GetDirectoryName(currentFile.Path);
            string fileName = Path.GetFileNameWithoutExtension(currentFile.Path);
            string extension = Path.GetExtension(currentFile.Path);


            string previousFileName = Path.GetFileNameWithoutExtension(previousFile.Path);
            string nextFileName = Path.GetFileNameWithoutExtension(nextFile.Path);

            // Search for common substring in previousFileName, fileName, and nextFileName
            (string substring1, string substring2) = FindCommonSubstring(previousFileName, fileName, nextFileName);

            // Add path substrings to dictionary
            uint pathIndex = GetStringOffset(drive + directory);
            uint fileNamePart1Index = GetStringOffset(substring1);
            uint fileNamePart2Index = GetStringOffset(substring2);
            uint extensionIndex = GetStringOffset(extension);

            // Check for sector alignment
            isSectorAligned = _sectorAlignedExtensions.Contains(extension);
            // Open the file
            using (Stream fileStream = File.OpenRead(currentFile.Path))
            {
                long fileLength = fileStream.Length;

                // Need to pad?
                int bytesToPad = (int)(isSectorAligned && dataFileStream != null
                    ? _sectorSize - (int)(_currentDataFileOffset % _sectorSize)
                    : 0);

                // Calculate total size of the file
                long totalFileSize = fileLength + bytesToPad;

                // Too big? (or the first time?)
                if (dataFileStream == null || _currentDataFileOffset + totalFileSize > _splitSize)
                {
                    // Close previous data file
                    if (dataFileStream != null)
                    {
                        int bytesToPadChunk = (int)(_chunkSize - (int)(_currentDataFileOffset % _chunkSize));
                        Pad(dataFileStream, bytesToPadChunk);
                        dataPosition += bytesToPadChunk;

                        _subFileEntries.Add(new DfsSubFileEntry((uint)dataPosition, (uint)(_enableCrc ? _checksumTable.Count : 0)));


                        dataFileStream.Dispose();

                        _checksumTable.AddRange(_checkSummer.ToUInt16Array());
                        _checkSummer = new CheckSummer((uint)_chunkSize);

                        // Update data file number
                        dataFilesCount++;
                    }

                    // Create new data file
                    string dataFilePath = $"{dataFileName}.{dataFilesCount:D3}";
                    dataFileStream = File.Create(dataFilePath);
                    _currentDataFileOffset = 0;

                    // Pad the output file
                    if (bytesToPad > 0)
                    {
                        Pad(dataFileStream, bytesToPad);
                        dataPosition += bytesToPad;
                        _currentDataFileOffset += bytesToPad;
                    }
                }

                // Assert that string table indices are valid
                Debug.Assert(pathIndex >= 0 && pathIndex < ushort.MaxValue);
                Debug.Assert(fileNamePart1Index >= 0 && fileNamePart1Index < ushort.MaxValue);
                Debug.Assert(fileNamePart2Index >= 0 && fileNamePart2Index < ushort.MaxValue);
                Debug.Assert(extensionIndex >= 0 && extensionIndex < ushort.MaxValue);

                // Add dfs_file record
                var dfsFileEntry = new DfsFileEntry(
                    (uint)fileNamePart1Index,
                    (uint)fileNamePart2Index,
                    (uint)pathIndex,
                    (uint)extensionIndex,
                    (uint)dataPosition,
                    (uint)fileLength
                );
                _fileEntries.Add(dfsFileEntry);

                // Update number of files output
                filesOutputCount++;
                // Copy data to output stream
                long fileBytesLeft = fileLength;
                byte[] buffer = new byte[_chunkSize];
                while (fileBytesLeft > 0)
                {
                    int bytesToCopy = (int)Math.Min(fileBytesLeft, _chunkSize);

                    int bytesRead = fileStream.Read(buffer, 0, bytesToCopy);
                    dataFileStream.Write(buffer, 0, bytesRead);
                    _checkSummer.ApplyData(buffer, bytesRead);
                    fileBytesLeft -= bytesRead;
                    dataPosition += bytesRead;
                    _currentDataFileOffset += bytesRead;

                }
            }
        }
        // Close data file
        if (dataFileStream is not null)
        {
            int bytesToPad = (int)(_chunkSize - (int)(_currentDataFileOffset % _chunkSize));
            Pad(dataFileStream, bytesToPad);
            dataPosition += bytesToPad;
            _currentDataFileOffset += bytesToPad;

            _subFileEntries.Add(new DfsSubFileEntry((uint)dataPosition, (uint)(_enableCrc ? _checksumTable.Count : 0)));

            dataFileStream.Dispose();

            _checksumTable.AddRange(_checkSummer.ToUInt16Array());
            _checkSummer = new CheckSummer((uint)_chunkSize);

            // Update data file number
            dataFilesCount++;
        }

        // Write the .DFS file
        using (var dfsStream = File.Create(dfsFilePath))
        using (var writer = new BinaryWriter(dfsStream))
        {
            // Write header
            var header = new DfsHeader
            {
                MagicNumber = DfsConstants.DfsMagic,
                Version = DfsConstants.DfsVersion,
                FileChecksum = 0, // Placeholder
                SectorSize = (int)_sectorSize,
                MaxSplitSize = (uint)_splitSize,
                TotalFileCount = filesOutputCount,
                SubFileCount = dataFilesCount,
                StringTableLength = GetStringTableSize(),
                SubFileTableOffset = 0, // Placeholder
                FileEntriesOffset = 0, // Placeholder
                ChecksumTableOffset = 0, // Placeholder
                StringTableOffset = 0 // Placeholder
            };

            writer.Write(header.MagicNumber);
            writer.Write(header.Version);
            writer.Write(header.FileChecksum);
            writer.Write(header.SectorSize);
            writer.Write(header.MaxSplitSize);
            writer.Write(header.TotalFileCount);
            writer.Write(header.SubFileCount);
            writer.Write(header.StringTableLength);
            writer.Write(header.SubFileTableOffset);
            writer.Write(header.FileEntriesOffset);
            writer.Write(header.ChecksumTableOffset);
            writer.Write(header.StringTableOffset);

            // Write sub-file table
            uint subFileTableOffset = (uint)writer.BaseStream.Position;
            foreach (var subFileEntry in _subFileEntries)
            {
                writer.Write(subFileEntry.SubFileOffset);
                writer.Write(subFileEntry.ChecksumIndex);
            }

            // Write file table
            uint fileTableOffset = (uint)writer.BaseStream.Position;
            foreach (var fileEntry in _fileEntries)
            {
                writer.Write(fileEntry.FileNamePart1Offset);
                writer.Write(fileEntry.FileNamePart2Offset);
                writer.Write(fileEntry.PathNameOffset);
                writer.Write(fileEntry.ExtensionOffset);
                writer.Write(fileEntry.DataOffset);
                writer.Write(fileEntry.FileLength);
            }

            // Write checksum table (if enabled)
            uint checksumTableOffset = 0;
            if (_enableCrc)
            {
                checksumTableOffset = (uint)writer.BaseStream.Position;
                foreach (var crc in _checksumTable)
                {
                    writer.Write(crc);
                }
            }

            // Write string table
            uint stringTableOffset = (uint)writer.BaseStream.Position;
            foreach (var entry in _stringTable)
            {
                writer.Write(Encoding.ASCII.GetBytes(entry.Key));
                writer.Write((byte)0);
            }

            // Pad the data to a multiple of 2K
            int padding = 2048 - (int)(writer.BaseStream.Position % 2048);
            if (padding < 2048)
            {
                writer.Write(new byte[padding]);
            }



            // Update header with offsets and checksum
            header.SubFileTableOffset = subFileTableOffset;
            header.FileEntriesOffset = fileTableOffset;
            header.ChecksumTableOffset = checksumTableOffset;
            header.StringTableOffset = stringTableOffset;
            header.FileChecksum = 0;

            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(header.MagicNumber);
            writer.Write(header.Version);
            var fileCheckSumOffset = writer.BaseStream.Position;
            writer.Write(header.FileChecksum);
            writer.Write(header.SectorSize);
            writer.Write(header.MaxSplitSize);
            writer.Write(header.TotalFileCount);
            writer.Write(header.SubFileCount);
            writer.Write(header.StringTableLength);
            writer.Write(header.SubFileTableOffset);
            writer.Write(header.FileEntriesOffset);
            writer.Write(header.ChecksumTableOffset);
            writer.Write(header.StringTableOffset);

            var checksum = CheckSummer.CalculateChecksum(dfsStream);

            header.FileChecksum = checksum;
            writer.Seek((int)fileCheckSumOffset, SeekOrigin.Begin);
            writer.Write(header.FileChecksum);

            // Assert that the number of data files matches the sub-file table count
            Debug.Assert(dataFilesCount == _subFileEntries.Count);
        }
    }

    private uint GetStringOffset(string value)
    {
        if (!_stringTable.TryGetValue(value, out int offset))
        {
            offset = GetStringTableSize();
            _stringTable[value] = offset;
        }
        return (uint)offset;
    }

    private int GetStringTableSize()
    {
        return _stringTable.Sum(entry => entry.Key.Length + 1);
    }

    private static void Pad(Stream stream, int count)
    {
        stream.Write(new byte[count], 0, count);
    }

    public void Dispose()
    {

    }

    private static (string, string) FindCommonSubstring(string str0, string str1, string str2)
    {
        int prevCommonLen = 0;
        int commonLen = 0;

        for (int i = 0; i < str1.Length && i < str0.Length && str1[i] == str0[i]; i++)
        {
            prevCommonLen++;
        }

        while (prevCommonLen > 0 && char.IsDigit(str1[prevCommonLen - 1]))
        {
            prevCommonLen--;
        }

        for (int i = 0; i < str1.Length && i < str2.Length && str1[i] == str2[i]; i++)
        {
            commonLen++;
        }

        while (commonLen > 0 && char.IsDigit(str1[commonLen - 1]))
        {
            commonLen--;
        }

        if (prevCommonLen > commonLen)
        {
            commonLen = prevCommonLen;
        }

        return (str1[..commonLen], str1[commonLen..]);
    }
}