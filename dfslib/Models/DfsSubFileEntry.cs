using System.Runtime.InteropServices;

namespace DfsLib.Models;

/// <summary>
/// Represents a sub file entry in the DFS filesystem.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct DfsSubFileEntry
{
    /// <summary>
    /// Offset of the sub file.
    /// </summary>
    public readonly uint SubFileOffset;

    /// <summary>
    /// Index of the checksum for the sub file.
    /// </summary>
    public readonly uint ChecksumIndex;

    public DfsSubFileEntry(uint subFileOffset, uint checksumIndex)
    {
        SubFileOffset = subFileOffset;
        ChecksumIndex = checksumIndex;
    }
}