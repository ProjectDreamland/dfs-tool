using System.Runtime.InteropServices;

namespace Dfs.Models;

/// <summary>
/// Represents the header of a DFS file.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DfsHeader
{
    /// <summary>
    /// Magic number to identify the file.
    /// </summary>
    public int MagicNumber;

    /// <summary>
    /// Version number of the file.
    /// </summary>
    public int Version;

    /// <summary>
    /// Checksum of the .DFS file.
    /// </summary>
    public uint FileChecksum;

    /// <summary>
    /// Sector size in bytes.
    /// </summary>
    public int SectorSize;

    /// <summary>
    /// Split size in bytes (maximum).
    /// </summary>
    public uint MaxSplitSize;

    /// <summary>
    /// Total number of files in the filesystem.
    /// </summary>
    public int TotalFileCount;

    /// <summary>
    /// Number of sub files (*.000, *.001, etc...).
    /// </summary>
    public int SubFileCount;

    /// <summary>
    /// Length of the string table in bytes.
    /// </summary>
    public int StringTableLength;

    /// <summary>
    /// Offset to the sub file table.
    /// </summary>
    public uint SubFileTableOffset;

    /// <summary>
    /// Offset to the file entries.
    /// </summary>
    public uint FileEntriesOffset;

    /// <summary>
    /// Offset to the checksum table.
    /// </summary>
    public uint ChecksumTableOffset;

    /// <summary>
    /// Offset to the string table.
    /// </summary>
    public uint StringTableOffset;
}