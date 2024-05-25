using System;
using System.Collections.Generic;


namespace DfsLib;

internal class CheckSummer
{
    private List<ushort> _checksums;
    private ushort _checksum;
    private int _bytesProcessed;
    private int _bytesToNextSum;
    private uint _chunkSize;

    public CheckSummer(uint chunkSize)
    {
        _checksums = new List<ushort>(100000);
        Init(32768);
    }

    public void Init(uint chunkSize)
    {
        _checksums.Clear();
        _checksum = 0;
        _bytesProcessed = 0;
        _bytesToNextSum = (int)chunkSize;
        _chunkSize = chunkSize;
    }

    public ushort GetChecksum()
    {
        return _checksum;
    }

    public void ApplyData(byte[] data, int count)
    {
        while (count > 0)
        {
            int bytesToProcess = Math.Min(_bytesToNextSum, count);
            _bytesProcessed += bytesToProcess;
            _bytesToNextSum -= bytesToProcess;
            count -= bytesToProcess;

            while (bytesToProcess-- > 0)
            {
                _checksum = Crc16.Crc16ApplyByte(data[0], _checksum);
                data = data[1..];
            }

            if (_bytesToNextSum == 0)
            {
                _checksums.Add(_checksum);
                _checksum = 0;
                _bytesToNextSum = (int)_chunkSize;
            }
        }
    }

    public byte[] GetData()
    {
        byte[] result = new byte[_checksums.Count * sizeof(ushort)];
        Buffer.BlockCopy(_checksums.ToArray(), 0, result, 0, result.Length);
        return result;
    }

    public ushort[] ToUInt16Array()
    {
        var bytes = GetData();
        Console.WriteLine($"bytes: {bytes.Length}");
        var result = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    public static uint CalculateChecksum(Stream stream)
    {
        var currenPos = stream.Position;
        stream.Position = 0;
        try
        {
            var checksummer = new CheckSummer(uint.MaxValue);
            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                checksummer.ApplyData(buffer, bytesRead);
            }
            return checksummer.GetChecksum();
        }
        finally
        {
            stream.Position = currenPos;
        }
    }

    public int GetDataSize()
    {
        return _checksums.Count * sizeof(ushort);
    }

}
