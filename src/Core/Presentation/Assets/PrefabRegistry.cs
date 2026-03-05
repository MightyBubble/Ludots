using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PrefabRegistry
    {
        private readonly StringIntRegistry _ids;
        private PrefabDefinition[] _data;

        public PrefabRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.OrdinalIgnoreCase);
            _data = new PrefabDefinition[capacity];
        }

        public int Register(string key, in PrefabDefinition definition)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            var def = definition;
            def.PrefabId = id;
            _data[id] = def;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int prefabId, out PrefabDefinition definition)
        {
            if ((uint)prefabId >= (uint)_data.Length)
            {
                definition = default;
                return false;
            }
            definition = _data[prefabId];
            return definition.PrefabId != 0;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _data.Length) return;
            int newLen = Math.Max(_data.Length * 2, id + 1);
            Array.Resize(ref _data, newLen);
        }
    }
}
