using System;

namespace Ludots.Core.Presentation.Config
{
    public sealed class WorldHudStringTable
    {
        private readonly string[] _table;
        private int _count;

        public int Count => _count;
        public int Capacity => _table.Length;

        public WorldHudStringTable(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _table = new string[capacity];
            _count = 1;
        }

        public int Register(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (_count >= _table.Length) return 0;
            int id = _count++;
            _table[id] = text;
            return id;
        }

        public string? TryGet(int id)
        {
            if ((uint)id >= (uint)_table.Length) return null;
            return _table[id];
        }
    }
}
