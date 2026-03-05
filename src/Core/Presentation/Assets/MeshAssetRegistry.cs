using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class MeshAssetRegistry
    {
        private readonly StringIntRegistry _ids;
        private MeshAssetDescriptor[] _data;

        public MeshAssetRegistry(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, StringComparer.OrdinalIgnoreCase);
            _data = new MeshAssetDescriptor[capacity];

            RegisterPrimitive(WellKnownMeshKeys.Cube, PrimitiveMeshKind.Cube);
            RegisterPrimitive(WellKnownMeshKeys.Sphere, PrimitiveMeshKind.Sphere);
        }

        private void RegisterPrimitive(string key, PrimitiveMeshKind kind)
        {
            int id = _ids.Register(key);
            EnsureDataCapacity(id);
            _data[id] = MeshAssetDescriptor.Primitive(id, kind);
        }

        /// <summary>
        /// Register a descriptor by string key. Returns the assigned int runtime ID.
        /// If the key is already registered, overwrites the descriptor and returns the existing ID.
        /// </summary>
        public int Register(string key, in MeshAssetDescriptor descriptor)
        {
            int id = _ids.Register(key);
            EnsureDataCapacity(id);
            var desc = descriptor;
            desc.Id = id;
            _data[id] = desc;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGetDescriptor(int meshAssetId, out MeshAssetDescriptor descriptor)
        {
            if ((uint)meshAssetId >= (uint)_data.Length)
            {
                descriptor = default;
                return false;
            }
            descriptor = _data[meshAssetId];
            return descriptor.Type != MeshAssetType.None;
        }

        public bool TryGetPrimitiveKind(int meshAssetId, out PrimitiveMeshKind kind)
        {
            if (TryGetDescriptor(meshAssetId, out var desc) && desc.Type == MeshAssetType.Primitive)
            {
                kind = desc.PrimitiveKind;
                return kind != PrimitiveMeshKind.None;
            }
            kind = default;
            return false;
        }

        private void EnsureDataCapacity(int id)
        {
            if (id < _data.Length) return;
            int newLen = Math.Max(_data.Length * 2, id + 1);
            Array.Resize(ref _data, newLen);
        }
    }
}
