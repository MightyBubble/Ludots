using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public enum MaterialAssetDomain : byte
    {
        Surface = 1,
        Mesh = 2,
        SkinnedMesh = 3,
        Decal = 4,
        VFX = 5,
        UI = 6,
    }

    [Flags]
    public enum MaterialAssetFlags : ushort
    {
        None = 0,
        SupportsPerInstanceCustomData = 1 << 0,
    }

    public readonly struct MaterialAssetDescriptor
    {
        public MaterialAssetDescriptor(int id, MaterialAssetDomain domain, string[] sourceUris, MaterialAssetFlags flags)
        {
            Id = id;
            Domain = domain;
            SourceUris = sourceUris ?? Array.Empty<string>();
            Flags = flags;
        }

        public int Id { get; }
        public MaterialAssetDomain Domain { get; }
        public string[] SourceUris { get; }
        public MaterialAssetFlags Flags { get; }
    }

    public sealed class PresentationMaterialRegistry
    {
        private readonly StringIntRegistry _ids;
        private MaterialAssetDescriptor[] _items;
        private bool[] _has;

        public PresentationMaterialRegistry(int capacity = 1024)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _items = new MaterialAssetDescriptor[capacity];
            _has = new bool[capacity];
        }

        public int Register(string key, MaterialAssetDomain domain, string[] sourceUris, MaterialAssetFlags flags)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            _items[id] = new MaterialAssetDescriptor(id, domain, sourceUris ?? Array.Empty<string>(), flags);
            _has[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out MaterialAssetDescriptor descriptor)
        {
            if ((uint)id < (uint)_items.Length && _has[id])
            {
                descriptor = _items[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length)
            {
                return;
            }

            int newLen = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLen);
            Array.Resize(ref _has, newLen);
        }
    }
}
