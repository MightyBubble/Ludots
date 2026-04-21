using System.Numerics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabFinalizedLeaf
    {
        public PrefabFinalizedLeaf(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color)
        {
            MeshAssetId = meshAssetId;
            Descriptor = descriptor;
            StableId = stableId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
            Visual = new PrefabFinalizedVisual(
                PrefabPartKind.Mesh,
                meshAssetId,
                string.Empty,
                stableId,
                position,
                rotation,
                scale,
                color,
                System.Array.Empty<PresentationPayloadField>());
        }

        public int MeshAssetId { get; }

        public MeshAssetDescriptor Descriptor { get; }

        public int StableId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }

        public PrefabFinalizedVisual Visual { get; }
    }
}
