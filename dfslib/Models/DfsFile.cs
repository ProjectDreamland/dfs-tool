using System;
using System.Buffers;

namespace DfsLib.Models;

/// <summary>
/// Represents a file in the DFS filesystem.
/// </summary>
public sealed class DfsFile : IDisposable
{
    /// <summary>
    /// Gets the file data as a read-only memory.
    /// </summary>
    public ReadOnlyMemory<byte> FileData { get; private set; }

    /// <summary>
    /// Gets the name of the file.
    /// </summary>
    public string FileName { get; private set; }

    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    public long Size { get; private set; }

    private readonly byte[] _rentedArray;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DfsFile"/> class.
    /// </summary>
    /// <param name="rentedArray">The rented array containing the file data.</param>
    /// <param name="length">The length of the file data.</param>
    /// <param name="fileName">The name of the file.</param>
    internal DfsFile(byte[] rentedArray, int length, string fileName)
    {
        _rentedArray = rentedArray;
        FileData = new ReadOnlyMemory<byte>(rentedArray, 0, length);
        FileName = fileName;
        Size = length;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="DfsFile"/> and optionally releases the managed resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            ArrayPool<byte>.Shared.Return(_rentedArray);
            _disposed = true;
        }
    }
}
