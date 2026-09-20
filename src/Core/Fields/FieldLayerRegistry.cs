using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Fields
{
    public sealed class FieldLayerRegistry
    {
        private readonly StringIntRegistry _keys = new(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        private FieldLayerDefinition[] _definitions = new FieldLayerDefinition[16];
        private bool[] _registered = new bool[16];

        public FieldLayerId Register(
            string key,
            FieldLayerKind kind,
            int cellSizeCm,
            int chunkSizeCells,
            FieldLayerDefaultValue defaultValue,
            bool persistent,
            string writerDomain,
            int maxRegionIds)
        {
            int id = _keys.Register(key);
            EnsureCapacity(id);
            var layerId = new FieldLayerId(id);
            _definitions[id] = new FieldLayerDefinition(
                layerId, key, kind, cellSizeCm, chunkSizeCells, defaultValue, persistent, writerDomain, maxRegionIds);
            _registered[id] = true;
            return layerId;
        }

        public FieldLayerId GetId(string key)
        {
            int id = _keys.GetId(key);
            return id > 0 ? new FieldLayerId(id) : default;
        }

        public bool TryGet(FieldLayerId id, out FieldLayerDefinition definition)
        {
            if (id.Value <= 0 || (uint)id.Value >= (uint)_registered.Length || !_registered[id.Value])
            {
                definition = default;
                return false;
            }

            definition = _definitions[id.Value];
            return true;
        }

        public FieldLayerDefinition Get(FieldLayerId id)
        {
            if (!TryGet(id, out FieldLayerDefinition definition))
            {
                throw new InvalidOperationException($"Field layer id {id.Value} is not registered.");
            }

            return definition;
        }

        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 1; i < _registered.Length; i++)
                {
                    if (_registered[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Freeze() => _keys.Freeze();

        private void EnsureCapacity(int id)
        {
            if ((uint)id < (uint)_definitions.Length)
            {
                return;
            }

            int next = _definitions.Length;
            while (next <= id)
            {
                next *= 2;
            }

            Array.Resize(ref _definitions, next);
            Array.Resize(ref _registered, next);
        }
    }
}
