using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Spatial;
using Ludots.Tool;
using NUnit.Framework;
using System;
using System.IO;
using Ludots.Platform.Abstractions;

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

        [Test]
        public void ReactStride4Converter_PreservesBiomeAreaAndBlockedAsSeparateLogicChannels()
        {
            string reactPath = Path.Combine(Path.GetTempPath(), "ludots-react-terrain-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                WriteReactStride4Map(
                    reactPath,
                    height: 3,
                    water: 0,
                    biome: 4,
                    vegetation: 2,
                    blocked: true,
                    areaId: 9);

                using var converted = new MemoryStream();
                ReactMapDataBinConverter.ConvertToVertexMapBinary(reactPath, converted);
                converted.Position = 0;
                VertexMap map = VertexMapBinary.Read(converted);
                var terrain = new VertexMapLogicTerrainField(map);
                LogicTerrainCell cell = terrain.GetCell(0, 0);

                Assert.That(map.GetBiome(0, 0), Is.EqualTo(4), "Visual biome must remain separate from terrain area.");
                Assert.That(map.GetChunk(0, 0, false)!.GetExtraByte(0, 0, 0), Is.EqualTo(9));
                Assert.That(map.IsBlocked(0, 0), Is.True);
                Assert.That(cell.AreaId, Is.EqualTo(9));
                Assert.That(cell.IsBlocked, Is.True);
                Assert.That(cell.HeightLevel, Is.EqualTo(3));
            }
            finally
            {
                if (File.Exists(reactPath))
                {
                    File.Delete(reactPath);
                }
            }
        }

        [Test]
        public void ReactStride4GridLoader_ProvidesProductionGridLogicTerrainWithoutHexVertexMap()
        {
            string reactPath = Path.Combine(Path.GetTempPath(), "ludots-react-grid-terrain-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                WriteReactStride4Map(
                    reactPath,
                    height: 2,
                    water: 0,
                    biome: 1,
                    vegetation: 0,
                    blocked: false,
                    areaId: 7);

                LogicTerrainField terrain = ReactMapDataBinConverter.ReadGridLogicTerrainField(
                    reactPath,
                    cellSizeCm: 125);
                LogicTerrainCell cell = terrain.GetCell(0, 0);

                Assert.That(terrain.Topology, Is.EqualTo(LogicTerrainTopology.Grid));
                Assert.That(terrain.HorizontalStepCm, Is.EqualTo(125));
                Assert.That(terrain.VerticalStepCm, Is.EqualTo(125));
                Assert.That(cell.HeightLevel, Is.EqualTo(2));
                Assert.That(cell.AreaId, Is.EqualTo(7));
                Assert.That(cell.IsBlocked, Is.False);

                var config = new NavBuildConfig(heightScaleMeters: 1f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
                bool ok = NavTileBuilder.TryBuildTile(terrain, 0, 0, 1, config, out NavTile tile, out NavBakeArtifact artifact);

                Assert.That(ok, Is.True, artifact.Message);
                Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            }
            finally
            {
                if (File.Exists(reactPath))
                {
                    File.Delete(reactPath);
                }
            }
        }

        [Test]
        public void ReactSparseGridLoader_DefaultsMissingChunksToFlatTerrain()
        {
            string reactPath = Path.Combine(Path.GetTempPath(), "ludots-react-sparse-grid-terrain-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                WriteReactSparseStride4Map(
                    reactPath,
                    widthChunks: 64,
                    heightChunks: 64,
                    residentChunkX: 7,
                    residentChunkY: 5,
                    height: 4,
                    water: 0,
                    biome: 2,
                    vegetation: 0,
                    blocked: true,
                    areaId: 11);

                LogicTerrainField terrain = ReactMapDataBinConverter.ReadGridLogicTerrainField(
                    reactPath,
                    cellSizeCm: 400);
                LogicTerrainCell authored = terrain.GetCell(7 * SpatialScaleDefaults.TerrainChunkCells, 5 * SpatialScaleDefaults.TerrainChunkCells);
                LogicTerrainCell missing = terrain.GetCell(0, 0);

                Assert.That(terrain.Topology, Is.EqualTo(LogicTerrainTopology.Grid));
                Assert.That(terrain.WidthChunks, Is.EqualTo(64));
                Assert.That(terrain.HeightChunks, Is.EqualTo(64));
                Assert.That(authored.HeightLevel, Is.EqualTo(4));
                Assert.That(authored.AreaId, Is.EqualTo(11));
                Assert.That(authored.IsBlocked, Is.True);
                Assert.That(missing.HeightLevel, Is.EqualTo(0));
                Assert.That(missing.AreaId, Is.EqualTo(0));
                Assert.That(missing.IsBlocked, Is.False);

                var config = new NavBuildConfig(heightScaleMeters: 1f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
                bool ok = NavTileBuilder.TryBuildTile(terrain, 0, 0, 1, config, out NavTile tile, out NavBakeArtifact artifact);

                Assert.That(ok, Is.True, artifact.Message);
                Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            }
            finally
            {
                if (File.Exists(reactPath))
                {
                    File.Delete(reactPath);
                }
            }
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

        private static void WriteReactStride4Map(
            string path,
            byte height,
            byte water,
            byte biome,
            byte vegetation,
            bool blocked,
            byte areaId)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(1);
            writer.Write(1);
            writer.Write((byte)4);

            var chunk = new byte[VertexChunk.TotalCells * 4];
            chunk[0] = (byte)(((height & 0x0F) << 4) | (water & 0x0F));
            chunk[1] = (byte)(((biome & 0x0F) << 4) | (vegetation & 0x0F));
            chunk[2] = blocked ? (byte)0b0000_1000 : (byte)0;
            chunk[3] = areaId;
            writer.Write(chunk);
        }

        private static void WriteReactSparseStride4Map(
            string path,
            int widthChunks,
            int heightChunks,
            int residentChunkX,
            int residentChunkY,
            byte height,
            byte water,
            byte biome,
            byte vegetation,
            bool blocked,
            byte areaId)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(widthChunks);
            writer.Write(heightChunks);
            writer.Write(ReactMapDataBinConverter.ReactSparseFormatVersion);
            writer.Write(1);
            writer.Write(residentChunkX);
            writer.Write(residentChunkY);

            var chunk = new byte[VertexChunk.TotalCells * 4];
            chunk[0] = (byte)(((height & 0x0F) << 4) | (water & 0x0F));
            chunk[1] = (byte)(((biome & 0x0F) << 4) | (vegetation & 0x0F));
            chunk[2] = blocked ? (byte)0b0000_1000 : (byte)0;
            chunk[3] = areaId;
            writer.Write(chunk);
        }
    }
}
