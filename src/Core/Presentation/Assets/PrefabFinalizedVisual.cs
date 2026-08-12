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
            PrefabMaterialBinding[]? materialBindings,
            in ProceduralMeshBounds localBounds,
            int effectAssetId,
            in Vector2 size,
            bool alignToSurface,
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
            MaterialBindings = materialBindings;
            LocalBounds = localBounds;
            EffectAssetId = effectAssetId;
            Size = size;
            AlignToSurface = alignToSurface;
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

        public PrefabMaterialBinding[]? MaterialBindings { get; }

        public ProceduralMeshBounds LocalBounds { get; }

        public int EffectAssetId { get; }

        public Vector2 Size { get; }

        public bool AlignToSurface { get; }

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
            in Vector4 color,
            int materialId = 0,
            PrefabMaterialBinding[]? materialBindings = null,
            in ProceduralMeshBounds localBounds = default)
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
                materialId,
                materialBindings,
                localBounds,
                effectAssetId: 0,
                size: Vector2.Zero,
                alignToSurface: false,
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
            in Vector2 size,
            bool alignToSurface)
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
                materialBindings: null,
                localBounds: default,
                effectAssetId: 0,
                size,
                alignToSurface,
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
            PrefabVfxSpawnMode spawnMode,
            int materialId = 0)
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
                materialId,
                materialBindings: null,
                localBounds: default,
                effectAssetId,
                size: Vector2.Zero,
                alignToSurface: false,
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
            bool terrainFacing,
            in ProceduralMeshBounds localBounds = default)
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
                materialBindings: null,
                localBounds,
                effectAssetId: 0,
                size: Vector2.Zero,
                alignToSurface: false,
                tiling,
                vfxSpawnMode: PrefabVfxSpawnMode.Once,
                terrainFacing);
        }

        public static PrefabFinalizedVisual ProceduralMesh(
            int meshAssetId,
            in MeshAssetDescriptor meshDescriptor,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabMaterialBinding[] materialBindings,
            in ProceduralMeshBounds localBounds)
        {
            int materialId = materialBindings != null && materialBindings.Length == 1
                ? materialBindings[0].MaterialAssetId
                : 0;
            return new PrefabFinalizedVisual(
                PrefabVisualPartKind.ProceduralMesh,
                stableId,
                position,
                rotation,
                scale,
                color,
                meshAssetId,
                meshDescriptor,
                materialId,
                materialBindings,
                localBounds,
                effectAssetId: 0,
                size: Vector2.Zero,
                alignToSurface: false,
                tiling: Vector2.Zero,
                vfxSpawnMode: PrefabVfxSpawnMode.Once,
                terrainFacing: false);
        }
    }
}
