namespace DfsLib;

/// <summary>
/// Provides constant values for the DFS filesystem.
/// </summary>
internal static class DfsConstants
{
    /// <summary>
    /// The magic number used to identify the DFS file.
    /// </summary>
    public const int DfsMagic = 0x58444653; // 'XDFS'

    /// <summary>
    /// The version number of the DFS file format.
    /// </summary>
    public const int DfsVersion = 3;

    /// <summary>
    /// The extension used for DFS files.
    /// </summary>
    public const string DfsFileExt = ".DFS";

    /// <summary>
    /// The extension used for data files.
    /// </summary>
    public const string DataFileExt = ".000";


    public const int DefaultSectorSize = 2048;
    public const int DefaultSplitSize = 240 * 1024 * 1024; // Default split size of 240MB
    public const int DefaultChunkSize = 32768; // 32KB
}