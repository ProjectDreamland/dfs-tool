using System.Runtime.InteropServices;

namespace DfsLib.Models;

/// <summary>
/// Represents a file entry in the DFS filesystem.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct DfsFileEntry
{
    /// <summary>
    /// Offset of filename part 1 in the string table.
    /// </summary>
    public readonly uint FileNamePart1Offset;

    /// <summary>
    /// Offset of filename part 2 in the string table.
    /// </summary>
    public readonly uint FileNamePart2Offset;

    /// <summary>
    /// Offset of pathname in the string table.
    /// </summary>
    public readonly uint PathNameOffset;

    /// <summary>
    /// Offset of extension in the string table.
    /// </summary>
    public readonly uint ExtensionOffset;

    /// <summary>
    /// Offset of data.
    /// </summary>
    public readonly uint DataOffset;

    /// <summary>
    /// Length of the file.
    /// </summary>
    public readonly uint FileLength;

    public DfsFileEntry(uint fileNamePart1Offset, uint fileNamePart2Offset, uint pathNameOffset, uint extensionOffset, uint dataOffset, uint fileLength)
    {
        FileNamePart1Offset = fileNamePart1Offset;
        FileNamePart2Offset = fileNamePart2Offset;
        PathNameOffset = pathNameOffset;
        ExtensionOffset = extensionOffset;
        DataOffset = dataOffset;
        FileLength = fileLength;
    }
}
