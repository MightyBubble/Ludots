using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LogicTerrainFieldContractTests
    {
        [Test]
        public void VertexMapAdapter_PreservesWalkMaskSemantics()
        {
            var map = CreateFlatVertexMap();
            map.SetBlocked(10, 10, true);
            map.SetWaterHeight(20, 20, 4);

            var config = new NavBuildConfig(heightScaleMeters: 2f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            TriWalkMask legacy = WalkMaskBuilder.Build(map, 0, 0, config);
            TriWalkMask adapted = WalkMaskBuilder.Build(new VertexMapLogicTerrainField(map), 0, 0, config);

            Assert.That(adapted.WalkableTriangleCount, Is.EqualTo(legacy.WalkableTriangleCount));
            Assert.That(adapted.IsWalkable(10, 10, 0), Is.EqualTo(legacy.IsWalkable(10, 10, 0)));
            Assert.That(adapted.IsWalkable(20, 20, 0), Is.EqualTo(legacy.IsWalkable(20, 20, 0)));
        }

        [Test]
        public void FlatGridLogicTerrainField_BuildsNavTile()
        {
            var terrain = new FlatGridLogicTerrainField(
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.CellCm);
            var config = new NavBuildConfig(heightScaleMeters: 1f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);

            bool ok = NavTileBuilder.TryBuildTile(terrain, 0, 0, 1, config, out var tile, out var artifact);

            Assert.That(ok, Is.True, artifact.Message);
            Assert.That(tile, Is.Not.Null);
            Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(tile.TileId.ChunkX, Is.EqualTo(0));
            Assert.That(tile.TileId.ChunkY, Is.EqualTo(0));
        }

        [Test]
        public void VisualHeightmap_DoesNotChangeLogicWalkabilityUnlessExplicitlyProjected()
        {
            var terrain = new FlatGridLogicTerrainField(
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.CellCm);
            var config = new NavBuildConfig(heightScaleMeters: 1f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            int before = WalkMaskBuilder.Build(terrain, 0, 0, config).WalkableTriangleCount;

            var visual = CreateRaisedVisualHeightmap(SpatialScaleDefaults.TerrainChunkCells, SpatialScaleDefaults.TerrainChunkCells);
            int afterVisualOnly = WalkMaskBuilder.Build(terrain, 0, 0, config).WalkableTriangleCount;
            var projected = VisualHeightmapLogicTerrainProjection.ProjectToGrid(
                visual,
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.TerrainChunkCells,
                SpatialScaleDefaults.CellCm,
                LogicTerrainProjectionOptions.Default);

            Assert.That(afterVisualOnly, Is.EqualTo(before), "Visual heightmap must not implicitly mutate logic terrain.");
            Assert.That(projected.GetCell(32, 32).HeightLevel, Is.GreaterThan(terrain.GetCell(32, 32).HeightLevel));
        }

        private static VertexMap CreateFlatVertexMap()
        {
            var map = new VertexMap();
            map.Initialize(1, 1);

            for (int row = 0; row < VertexChunk.ChunkSize; row++)
            {
                for (int col = 0; col < VertexChunk.ChunkSize; col++)
                {
                    map.SetHeight(col, row, 0);
                    map.SetWaterHeight(col, row, 0);
                    map.SetRamp(col, row, false);
                    map.SetBlocked(col, row, false);
                }
            }

            return map;
        }

        private static VisualHeightmapRuntime CreateRaisedVisualHeightmap(int widthCells, int heightCells)
        {
            var samples = new short[checked(widthCells * heightCells)];
            samples[32 * widthCells + 32] = 500;
            var bounds = new WorldAabbCm(
                0,
                0,
                widthCells * SpatialScaleDefaults.CellCm,
                heightCells * SpatialScaleDefaults.CellCm);
            var asset = VisualHeightmapAsset.CreateSingleLayer(
                bounds,
                widthCells,
                heightCells,
                samples,
                interpolationMode: VisualHeightmapInterpolationMode.BilinearHeightfield);
            return new VisualHeightmapRuntime(asset);
        }
    }
}
