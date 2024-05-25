using System.Runtime.InteropServices;

namespace DfsLib.Models;

/// <summary>
/// Represents a sub file entry in the DFS filesystem.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DfsSubFileEntry
{
    /// <summary>
    /// Offset of the sub file.
    /// </summary>
    public uint SubFileOffset;

    /// <summary>
    /// Index of the checksum for the sub file.
    /// </summary>
    public uint ChecksumIndex;
}