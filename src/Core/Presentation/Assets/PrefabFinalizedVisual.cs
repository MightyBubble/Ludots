using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabFinalizedVisual
    {
        private PrefabFinalizedVisual(
            PrefabVisualPartKind kind,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            int meshAssetId,
            in MeshAssetDescriptor meshDescriptor,
            int materialId,
            int effectAssetId,
            in Vector2 size,
            in Vector2 tiling,
            PrefabVfxSpawnMode vfxSpawnMode,
            bool terrainFacing)
        {
            Kind = kind;
            StableId = stableId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
            MeshAssetId = meshAssetId;
            MeshDescriptor = meshDescriptor;
            MaterialId = materialId;
            EffectAssetId = effectAssetId;
            Size = size;
            Tiling = tiling;
            VfxSpawnMode = vfxSpawnMode;
            TerrainFacing = terrainFacing;
        }

        public PrefabVisualPartKind Kind { get; }

        public int StableId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }

        public int MeshAssetId { get; }

        public MeshAssetDescriptor MeshDescriptor { get; }

        public int MaterialId { get; }

        public int EffectAssetId { get; }

        public Vector2 Size { get; }

        public Vector2 Tiling { get; }

        public PrefabVfxSpawnMode VfxSpawnMode { get; }

        public bool TerrainFacing { get; }

        public static PrefabFinalizedVisual Mesh(
            int meshAssetId,
            in MeshAssetDescriptor meshDescriptor,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color)
        {
            return new PrefabFinalizedVisual(
                PrefabVisualPartKind.Mesh,
                stableId,
                position,
                rotation,
                scale,
                color,
                meshAssetId,
                meshDescriptor,
                materialId: 0,
                effectAssetId: 0,
                size: Vector2.Zero,
                tiling: Vector2.Zero,
                vfxSpawnMode: PrefabVfxSpawnMode.Once,
                terrainFacing: false);
        }

        public static PrefabFinalizedVisual Decal(
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            int materialId,
            in Vector2 size)
        {
            return new PrefabFinalizedVisual(
                PrefabVisualPartKind.Decal,
                stableId,
                position,
                rotation,
                scale,
                color,
                meshAssetId: 0,
                meshDescriptor: default,
                materialId,
                effectAssetId: 0,
                size,
                tiling: Vector2.Zero,
                vfxSpawnMode: PrefabVfxSpawnMode.Once,
                terrainFacing: false);
        }

        public static PrefabFinalizedVisual Vfx(
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            int effectAssetId,
            PrefabVfxSpawnMode spawnMode)
        {
            return new PrefabFinalizedVisual(
                PrefabVisualPartKind.Vfx,
                stableId,
                position,
                rotation,
                scale,
                color,
                meshAssetId: 0,
                meshDescriptor: default,
                materialId: 0,
                effectAssetId,
                size: Vector2.Zero,
                tiling: Vector2.Zero,
                vfxSpawnMode: spawnMode,
                terrainFacing: false);
        }

        public static PrefabFinalizedVisual Surface(
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            int meshAssetId,
            in MeshAssetDescriptor meshDescriptor,
            int materialId,
            in Vector2 tiling,
            bool terrainFacing)
        {
            return new PrefabFinalizedVisual(
                PrefabVisualPartKind.Surface,
                stableId,
                position,
                rotation,
                scale,
                color,
                meshAssetId,
                meshDescriptor,
                materialId,
                effectAssetId: 0,
                size: Vector2.Zero,
                tiling,
                vfxSpawnMode: PrefabVfxSpawnMode.Once,
                terrainFacing);
        }
    }
}
