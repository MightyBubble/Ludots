using System;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Particles
{
    public sealed class ParticleVfxRegistry
    {
        private readonly StringIntRegistry _ids;
        private ParticleVfxAssetData?[] _data;

        public ParticleVfxRegistry(int capacity = 1024)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.Ordinal);
            _data = new ParticleVfxAssetData?[capacity];
        }

        public int Register(string key, ParticleVfxAssetData effect)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            if (!effect.IsValid)
            {
                throw new InvalidOperationException($"Particle VFX asset '{key}' is not valid.");
            }

            int id = _ids.Register(key);
            EnsureDataCapacity(id);
            _data[id] = effect;
            return id;
        }

        public int GetId(string key)
        {
            return _ids.GetId(key);
        }

        public string GetName(int id)
        {
            return _ids.GetName(id);
        }

        public bool TryGet(int id, out ParticleVfxAssetData effect)
        {
            if ((uint)id >= (uint)_data.Length || _data[id] == null)
            {
                effect = null!;
                return false;
            }

            effect = _data[id]!;
            return true;
        }

        private void EnsureDataCapacity(int id)
        {
            if (id < _data.Length)
            {
                return;
            }

            int newLength = Math.Max(_data.Length * 2, id + 1);
            Array.Resize(ref _data, newLength);
        }
    }
}
