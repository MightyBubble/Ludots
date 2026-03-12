using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class VisualTemplateRegistry
    {
        private readonly StringIntRegistry _ids;
        private VisualTemplateDefinition[] _items;
        private bool[] _has;

        public VisualTemplateRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);
            _items = new VisualTemplateDefinition[capacity];
            _has = new bool[capacity];
        }

        public int Register(string key, in VisualTemplateDefinition definition)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            var item = definition;
            item.TemplateId = id;
            _items[id] = item;
            _has[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);
        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out VisualTemplateDefinition definition)
        {
            if ((uint)id < (uint)_items.Length && _has[id])
            {
                definition = _items[id];
                return true;
            }

            definition = default;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length) return;
            int newLength = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLength);
            Array.Resize(ref _has, newLength);
        }
    }
}
