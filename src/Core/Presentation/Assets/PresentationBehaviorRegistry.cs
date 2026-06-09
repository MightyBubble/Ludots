using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationBehaviorRegistry
    {
        private readonly StringIntRegistry _ids;
        private PresentationBehaviorDefinition[] _items;
        private bool[] _has;

        public PresentationBehaviorRegistry(int capacity = 128)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _items = new PresentationBehaviorDefinition[capacity];
            _has = new bool[capacity];
        }

        public int Register(string key, in PresentationBehaviorDefinition definition)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            PresentationBehaviorDefinition stored = definition;
            stored.BehaviorId = id;
            _items[id] = stored;
            _has[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out PresentationBehaviorDefinition definition)
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
            if (id < _items.Length)
            {
                return;
            }

            int newLength = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLength);
            Array.Resize(ref _has, newLength);
        }
    }
}
