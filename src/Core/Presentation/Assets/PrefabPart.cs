using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public struct PrefabPart
    {
        public PrefabVisualPartKind Kind;
        public int MeshAssetId;
        public int MaterialId;
        public int EffectAssetId;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public Vector4 ColorTint;
        public Vector2 Size;
        public Vector2 Tiling;
        public bool AlignToSurface;
        public bool TerrainFacing;
        public PrefabVfxSpawnMode VfxSpawnMode;
        public PrefabPartGrounding Grounding;

        public static PrefabPart Default(int meshAssetId)
        {
            return new PrefabPart
            {
                Kind = PrefabVisualPartKind.Mesh,
                MeshAssetId = meshAssetId,
                MaterialId = 0,
                EffectAssetId = 0,
                LocalPosition = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = Vector3.One,
                ColorTint = Vector4.One,
                Size = Vector2.Zero,
                Tiling = Vector2.One,
                AlignToSurface = false,
                TerrainFacing = false,
                VfxSpawnMode = PrefabVfxSpawnMode.Once,
                Grounding = PrefabPartGrounding.None,
            };
        }

        public static PrefabPart Decal(int materialId, Vector2 size)
        {
            PrefabPart part = Default(meshAssetId: 0);
            part.Kind = PrefabVisualPartKind.Decal;
            part.MaterialId = materialId;
            part.Size = size;
            part.AlignToSurface = true;
            return part;
        }

        public static PrefabPart Vfx(int effectAssetId, PrefabVfxSpawnMode spawnMode = PrefabVfxSpawnMode.Once)
        {
            PrefabPart part = Default(meshAssetId: 0);
            part.Kind = PrefabVisualPartKind.Vfx;
            part.EffectAssetId = effectAssetId;
            part.VfxSpawnMode = spawnMode;
            return part;
        }

        public static PrefabPart Surface(int meshAssetId, int materialId, Vector2 tiling)
        {
            PrefabPart part = Default(meshAssetId);
            part.Kind = PrefabVisualPartKind.Surface;
            part.MaterialId = materialId;
            part.Tiling = tiling;
            part.TerrainFacing = true;
            return part;
        }
    }
}
