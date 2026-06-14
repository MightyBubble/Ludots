using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabSurfacePart
    {
        public PrefabSurfacePart(int meshAssetId, int materialId, Vector2 tiling, bool terrainFacing = true)
        {
            MeshAssetId = meshAssetId;
            MaterialId = materialId;
            Tiling = tiling;
            TerrainFacing = terrainFacing;
        }

        public int MeshAssetId { get; }

        public int MaterialId { get; }

        public Vector2 Tiling { get; }

        public bool TerrainFacing { get; }
    }
}
