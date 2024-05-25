using System.Runtime.InteropServices;

namespace DfsLib.Models;

/// <summary>
/// Represents a file entry in the DFS filesystem.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DfsFileEntry
{
    /// <summary>
    /// Offset of filename part 1 in the string table.
    /// </summary>
    public uint FileNamePart1Offset;

    /// <summary>
    /// Offset of filename part 2 in the string table.
    /// </summary>
    public uint FileNamePart2Offset;

    /// <summary>
    /// Offset of pathname in the string table.
    /// </summary>
    public uint PathNameOffset;

    /// <summary>
    /// Offset of extension in the string table.
    /// </summary>
    public uint ExtensionOffset;

    /// <summary>
    /// Offset of data.
    /// </summary>
    public uint DataOffset;

    /// <summary>
    /// Length of the file.
    /// </summary>
    public uint FileLength;
}