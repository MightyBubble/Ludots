using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationMaterialRegistry
    {
        public const string DefaultSurfaceKey = "default_surface";

        private readonly StringIntRegistry _ids;
        private MaterialAssetDescriptor[] _data;
        private bool[] _has;

        public PresentationMaterialRegistry(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.OrdinalIgnoreCase);
            _data = new MaterialAssetDescriptor[capacity];
            _has = new bool[capacity];

            Register(DefaultSurfaceKey, MaterialAssetDomain.Surface, new[] { "materials/default_surface.mat" }, MaterialAssetFlags.None);
        }

        public int Register(string key, MaterialAssetDomain domain, string[] sourceUris, MaterialAssetFlags flags)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            _data[id] = new MaterialAssetDescriptor(id, domain, sourceUris, flags);
            _has[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out MaterialAssetDescriptor descriptor)
        {
            if ((uint)id < (uint)_data.Length && _has[id])
            {
                descriptor = _data[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _data.Length)
            {
                return;
            }

            int next = Math.Max(_data.Length * 2, id + 1);
            Array.Resize(ref _data, next);
            Array.Resize(ref _has, next);
        }
    }
}
