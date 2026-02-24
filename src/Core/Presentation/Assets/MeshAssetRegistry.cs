using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class MeshAssetRegistry
    {
        private readonly PrimitiveMeshKind[] _primitiveKinds;

        public MeshAssetRegistry(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _primitiveKinds = new PrimitiveMeshKind[capacity];

            RegisterPrimitive(PrimitiveMeshAssetIds.Cube, PrimitiveMeshKind.Cube);
            RegisterPrimitive(PrimitiveMeshAssetIds.Sphere, PrimitiveMeshKind.Sphere);
        }

        public void RegisterPrimitive(int meshAssetId, PrimitiveMeshKind kind)
        {
            if ((uint)meshAssetId >= (uint)_primitiveKinds.Length) throw new ArgumentOutOfRangeException(nameof(meshAssetId));
            _primitiveKinds[meshAssetId] = kind;
        }

        public bool TryGetPrimitiveKind(int meshAssetId, out PrimitiveMeshKind kind)
        {
            if ((uint)meshAssetId >= (uint)_primitiveKinds.Length)
            {
                kind = default;
                return false;
            }

            kind = _primitiveKinds[meshAssetId];
            return kind != PrimitiveMeshKind.None;
        }
    }
}

