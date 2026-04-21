using System.Numerics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabFinalizedVisual
    {
        public PrefabFinalizedVisual(
            PrefabPartKind kind,
            int meshAssetId,
            string assetKey,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PresentationPayloadField[] payload,
            string materialKey = "",
            string surfaceLayerKey = "")
        {
            Kind = kind;
            MeshAssetId = meshAssetId;
            AssetKey = assetKey ?? string.Empty;
            StableId = stableId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
            Payload = payload ?? System.Array.Empty<PresentationPayloadField>();
            MaterialKey = materialKey ?? string.Empty;
            SurfaceLayerKey = surfaceLayerKey ?? string.Empty;
        }

        public PrefabPartKind Kind { get; }

        public int MeshAssetId { get; }

        public string AssetKey { get; }

        public int StableId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }

        public PresentationPayloadField[] Payload { get; }

        public string MaterialKey { get; }

        public string SurfaceLayerKey { get; }

        public PresentationVisualRequest ToVisualRequest()
        {
            return new PresentationVisualRequest(
                ToRequestKind(Kind),
                StableId,
                AssetKey,
                Position,
                Rotation,
                Scale,
                Color,
                Arch.Core.Entity.Null,
                Arch.Core.Entity.Null,
                Payload,
                MaterialKey,
                SurfaceLayerKey);
        }

        private static PresentationVisualRequestKind ToRequestKind(PrefabPartKind kind)
        {
            return kind switch
            {
                PrefabPartKind.Decal => PresentationVisualRequestKind.Decal,
                PrefabPartKind.Vfx => PresentationVisualRequestKind.Vfx,
                PrefabPartKind.Surface => PresentationVisualRequestKind.Surface,
                _ => PresentationVisualRequestKind.None,
            };
        }
    }
}
