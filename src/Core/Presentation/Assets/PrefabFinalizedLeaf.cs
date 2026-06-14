using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabFinalizedLeaf
    {
        private readonly PrefabFinalizedVisual _visual;

        public PrefabFinalizedLeaf(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            int materialId = 0,
            PrefabMaterialBinding[]? materialBindings = null,
            in ProceduralMeshBounds localBounds = default)
        {
            MeshAssetId = meshAssetId;
            Descriptor = descriptor;
            StableId = stableId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
            _visual = descriptor.Type == MeshAssetType.ProceduralMesh
                ? PrefabFinalizedVisual.ProceduralMesh(meshAssetId, descriptor, stableId, position, rotation, scale, color, materialBindings ?? System.Array.Empty<PrefabMaterialBinding>(), localBounds)
                : PrefabFinalizedVisual.Mesh(meshAssetId, descriptor, stableId, position, rotation, scale, color, materialId, materialBindings, localBounds);
        }

        public int MeshAssetId { get; }

        public MeshAssetDescriptor Descriptor { get; }

        public int StableId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }

        public PrefabFinalizedVisual ToVisual() => _visual;
    }
}
