using System.Numerics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Assets
{
    public struct PrefabPart
    {
        public PrefabPartKind Kind;
        public int MeshAssetId;
        public string AssetKey;
        public string MaterialKey;
        public string SurfaceLayerKey;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public Vector4 ColorTint;
        public PrefabPartGrounding Grounding;
        public PresentationPayloadField[] Payload;

        public static PrefabPart Default(int meshAssetId)
        {
            return new PrefabPart
            {
                Kind = PrefabPartKind.Mesh,
                MeshAssetId = meshAssetId,
                LocalPosition = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = Vector3.One,
                ColorTint = Vector4.One,
                Grounding = PrefabPartGrounding.None,
                Payload = System.Array.Empty<PresentationPayloadField>(),
            };
        }

        public static PrefabPart Decal(string assetKey)
        {
            return NonMesh(PrefabPartKind.Decal, assetKey);
        }

        public static PrefabPart Vfx(string assetKey)
        {
            return NonMesh(PrefabPartKind.Vfx, assetKey);
        }

        public static PrefabPart Surface(string surfaceLayerKey)
        {
            PrefabPart part = NonMesh(PrefabPartKind.Surface, surfaceLayerKey);
            part.SurfaceLayerKey = surfaceLayerKey ?? string.Empty;
            return part;
        }

        private static PrefabPart NonMesh(PrefabPartKind kind, string assetKey)
        {
            return new PrefabPart
            {
                Kind = kind,
                AssetKey = assetKey ?? string.Empty,
                LocalPosition = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = Vector3.One,
                ColorTint = Vector4.One,
                Grounding = PrefabPartGrounding.None,
                Payload = System.Array.Empty<PresentationPayloadField>(),
            };
        }
    }
}
