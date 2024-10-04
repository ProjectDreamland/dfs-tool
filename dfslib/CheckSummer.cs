using System;
using System.Collections.Generic;
using System.IO;

namespace DfsLib
{
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
            Init(chunkSize);
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
            int offset = 0;
            while (count > 0)
            {
                int bytesToProcess = Math.Min(_bytesToNextSum, count);
                _bytesProcessed += bytesToProcess;
                _bytesToNextSum -= bytesToProcess;
                count -= bytesToProcess;

                for (int i = 0; i < bytesToProcess; i++)
                {
                    _checksum = Crc16.Crc16ApplyByte(data[offset++], _checksum);
                }

                if (_bytesToNextSum == 0)
                {
                    _checksums.Add(_checksum);
                    _checksum = 0;
                    _bytesToNextSum = (int)_chunkSize;
                }
            }
        }

        public ushort[] ToUInt16Array()
        {
            return _checksums.ToArray();
        }

        public static uint CalculateChecksum(Stream stream)
        {
            ushort checksum = 0;
            var buffer = new byte[4096];
            int bytesRead;

            stream.Seek(0, SeekOrigin.Begin);
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    checksum = Crc16.Crc16ApplyByte(buffer[i], checksum);
                }
            }

            return checksum;
        }
    }
}