using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class MeshAssetRegistry
    {
        private readonly MeshAssetDescriptor[] _descriptors;
        private readonly Dictionary<string, int> _keyToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _nextAutoId;

        public MeshAssetRegistry(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _descriptors = new MeshAssetDescriptor[capacity];
            _nextAutoId = 100;

            RegisterWellKnown("cube", PrimitiveMeshAssetIds.Cube, MeshAssetDescriptor.Primitive(PrimitiveMeshAssetIds.Cube, PrimitiveMeshKind.Cube));
            RegisterWellKnown("sphere", PrimitiveMeshAssetIds.Sphere, MeshAssetDescriptor.Primitive(PrimitiveMeshAssetIds.Sphere, PrimitiveMeshKind.Sphere));
        }

        private void RegisterWellKnown(string key, int id, in MeshAssetDescriptor descriptor)
        {
            _descriptors[id] = descriptor;
            _keyToId[key] = id;
        }

        /// <summary>
        /// Register a descriptor by string key. Returns the assigned int runtime ID.
        /// If the key is already registered, overwrites the descriptor and returns the existing ID.
        /// </summary>
        public int Register(string key, in MeshAssetDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key must not be empty.", nameof(key));

            if (_keyToId.TryGetValue(key, out int existingId))
            {
                var updated = descriptor;
                updated.Id = existingId;
                _descriptors[existingId] = updated;
                return existingId;
            }

            int id = _nextAutoId++;
            if ((uint)id >= (uint)_descriptors.Length)
                throw new InvalidOperationException($"MeshAssetRegistry capacity ({_descriptors.Length}) exceeded.");

            var desc = descriptor;
            desc.Id = id;
            _descriptors[id] = desc;
            _keyToId[key] = id;
            return id;
        }

        /// <summary>
        /// Register by explicit int ID (backward compat for hardcoded PrimitiveMeshAssetIds).
        /// </summary>
        public void Register(in MeshAssetDescriptor descriptor)
        {
            if ((uint)descriptor.Id >= (uint)_descriptors.Length)
                throw new ArgumentOutOfRangeException(nameof(descriptor.Id));
            _descriptors[descriptor.Id] = descriptor;
        }

        public void RegisterPrimitive(int meshAssetId, PrimitiveMeshKind kind)
        {
            Register(MeshAssetDescriptor.Primitive(meshAssetId, kind));
        }

        public bool TryGetDescriptor(int meshAssetId, out MeshAssetDescriptor descriptor)
        {
            if ((uint)meshAssetId >= (uint)_descriptors.Length)
            {
                descriptor = default;
                return false;
            }
            descriptor = _descriptors[meshAssetId];
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

        public int ResolveIdOrZero(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            return _keyToId.TryGetValue(key, out int id) ? id : 0;
        }
    }
}
