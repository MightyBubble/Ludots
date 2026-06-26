using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Vision
{
    public sealed class FogLayerRegistry
    {
        private readonly StringIntRegistry _keys = new(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        private FogLayerDefinition[] _definitions = new FogLayerDefinition[16];
        private bool[] _registered = new bool[16];

        public FogLayerId Register(string key, int cellSizeCm, int updateHz)
        {
            int id = _keys.Register(key);
            EnsureCapacity(id);
            var layerId = new FogLayerId(id);
            _definitions[id] = new FogLayerDefinition(layerId, key, cellSizeCm, updateHz);
            _registered[id] = true;
            return layerId;
        }

        public FogLayerId GetId(string key)
        {
            int id = _keys.GetId(key);
            return id > 0 ? new FogLayerId(id) : default;
        }

        public bool TryGet(FogLayerId id, out FogLayerDefinition definition)
        {
            if (id.Value <= 0 || (uint)id.Value >= (uint)_registered.Length || !_registered[id.Value])
            {
                definition = default;
                return false;
            }

            definition = _definitions[id.Value];
            return true;
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

        public int CopyLayerIds(Span<FogLayerId> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int i = 1; i < _registered.Length && written < destination.Length; i++)
            {
                if (_registered[i])
                {
                    destination[written++] = _definitions[i].Id;
                }
            }

            return written;
        }

        public FogLayerDefinition Get(FogLayerId id)
        {
            if (!TryGet(id, out FogLayerDefinition definition))
            {
                throw new InvalidOperationException($"Fog layer id {id.Value} is not registered.");
            }

            return definition;
        }

        public uint ToMask(FogLayerId id)
        {
            if (id.Value <= 0 || id.Value > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Fog layer masks support layer ids 1..32.");
            }

            return 1u << (id.Value - 1);
        }

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
