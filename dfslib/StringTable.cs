using System;
using System.Collections.Generic;
using System.Text;

namespace DfsLib
{
    internal class StringTable
    {
        private byte[] _buffer;
        private int _bufferLength;
        private int _nextOffset;
        private int _hashTableSize;
        private int[] _hashTable;
        private List<Entry> _entries;

        private const int InitialHashTableSize = 20000;

        public StringTable()
        {
            _hashTableSize = InitialHashTableSize;
            _hashTable = new int[_hashTableSize];
            Array.Fill(_hashTable, -1);
            _entries = new List<Entry>();
            _buffer = new byte[128 * 1024]; // Start with 128KB buffer
            _bufferLength = _buffer.Length;
        }

        private void ResizeHashTable(int newSize)
        {
            _hashTableSize = newSize;
            _hashTable = new int[_hashTableSize];
            Array.Fill(_hashTable, -1);

            for (int i = 0; i < _entries.Count; i++)
            {
                int index = (int)(_entries[i].Hash % (uint)_hashTableSize);
                while (_hashTable[index] != -1)
                {
                    index = (index + 1) % _hashTableSize;
                }
                _hashTable[index] = i;
            }
        }

        private static uint HashString(string str)
        {
            uint hash = 5381;
            foreach (char c in str.ToUpperInvariant())
            {
                hash = ((hash << 5) + hash) ^ c;
            }
            return hash;
        }

        public uint Add(string str)
        {
            str = str.ToUpperInvariant();
            uint hash = HashString(str);

            // Try to find existing match
            int index = (int)(hash % (uint)_hashTableSize);
            while (_hashTable[index] != -1)
            {
                if (_entries[_hashTable[index]].Hash == hash &&
                    string.CompareOrdinal(str, Encoding.ASCII.GetString(_buffer, _entries[_hashTable[index]].Offset, str.Length)) == 0)
                {
                    return (uint)_entries[_hashTable[index]].Offset;
                }
                index = (index + 1) % _hashTableSize;
            }

            // Add new entry
            if (_bufferLength - _nextOffset < str.Length + 1)
            {
                Array.Resize(ref _buffer, Math.Max(_bufferLength * 2, 128 * 1024));
                _bufferLength = _buffer.Length;
            }

            var entry = new Entry { Hash = hash, Offset = _nextOffset };
            _entries.Add(entry);

            Encoding.ASCII.GetBytes(str, 0, str.Length, _buffer, _nextOffset);
            _buffer[_nextOffset + str.Length] = 0;
            _nextOffset += str.Length + 1;

            if (_hashTableSize / 2 < _entries.Count)
            {
                ResizeHashTable(_hashTableSize * 2);
            }

            index = (int)(hash % (uint)_hashTableSize);
            while (_hashTable[index] != -1)
            {
                index = (index + 1) % _hashTableSize;
            }
            _hashTable[index] = _entries.Count - 1;

            return (uint)entry.Offset;
        }

        public int GetSaveSize() => _nextOffset;

        public void Save(BinaryWriter writer)
        {
            writer.Write(_buffer, 0, _nextOffset);
        }

        public IEnumerable<(int Offset, int Length, string String)> GetEntries()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                string str = Encoding.ASCII.GetString(_buffer, entry.Offset, GetStringLength(entry.Offset));
                yield return (entry.Offset, str.Length, str);
            }
        }

        private int GetStringLength(int offset)
        {
            int length = 0;
            while (offset + length < _nextOffset && _buffer[offset + length] != 0)
            {
                length++;
            }
            return length;
        }

        private struct Entry
        {
            public uint Hash;
            public int Offset;
        }
    }
}