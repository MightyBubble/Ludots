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
using System.Security.Cryptography;
using System.Text.Json;

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

        [Test]
        public void EastAsiaPlayableTerrainAssets_LoadGridHexAndVisualHeightmapWithExpectedLandSeaContract()
        {
            string repoRoot = FindRepoRoot();
            string assetRoot = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "east_asia_playable_terrain",
                "EastAsiaPlayableTerrainMod",
                "assets");

            string gridPath = Path.Combine(assetRoot, "Data", "Maps", "east_asia_grid_map_data.bin");
            string hexReactSourcePath = Path.Combine(assetRoot, "Data", "Maps", "east_asia_hex_source_map_data.bin");
            string hexPath = Path.Combine(assetRoot, "Data", "Maps", "east_asia_hex.vtxm");
            string visualPath = Path.Combine(assetRoot, "terrain", "east_asia_continuous.vhtm");
            string profilePath = Path.Combine(assetRoot, "terrain", "east_asia_terrain_profile.json");

            LogicTerrainField gridTerrain = ReactLogicTerrainBinary.ReadGridLogicTerrainField(gridPath, cellSizeCm: 100);
            using FileStream hexStream = File.OpenRead(hexPath);
            VertexMap hexMap = VertexMapBinary.Read(hexStream);
            LogicTerrainField hexTerrain = new VertexMapLogicTerrainField(hexMap);
            using FileStream visualStream = File.OpenRead(visualPath);
            VisualHeightmapAsset visualAsset = VisualHeightmapBinary.Read(visualStream);
            EastAsiaProjectionContext projection = ReadProjectionContext(profilePath);
            string exported112Hash = ReadEditorImportHash(profilePath, "east_asia_strategy_editor_112x64_chunks_map_data.bin");

            Assert.That(gridTerrain.Topology, Is.EqualTo(LogicTerrainTopology.Grid));
            Assert.That(gridTerrain.WidthChunks, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultGridWidthChunks));
            Assert.That(gridTerrain.HeightChunks, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultGridHeightChunks));
            Assert.That(ComputeSha256(gridPath), Is.EqualTo(exported112Hash));
            Assert.That(ComputeSha256(hexReactSourcePath), Is.EqualTo(exported112Hash));
            Assert.That(hexTerrain.Topology, Is.EqualTo(LogicTerrainTopology.Hex));
            Assert.That(hexMap.WidthInChunks, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultHexWidthChunks));
            Assert.That(hexMap.HeightInChunks, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultHexHeightChunks));
            Assert.That(visualAsset.SampleColumns, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultVisualSampleColumns));
            Assert.That(visualAsset.SampleRows, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultVisualSampleRows));
            Assert.That(visualAsset.Bounds.Width, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultWorldWidthCm));
            Assert.That(visualAsset.Bounds.Height, Is.EqualTo(EastAsiaTerrainAssetGenerator.DefaultWorldHeightCm));

            LogicTerrainCell northChina = SampleLogicAtLonLat(gridTerrain, projection, lon: 116.0, lat: 38.0);
            LogicTerrainCell vietnamNorth = SampleLogicAtLonLat(gridTerrain, projection, lon: 106.0, lat: 21.0);
            LogicTerrainCell vietnamSouth = SampleLogicAtLonLat(gridTerrain, projection, lon: 106.0, lat: 10.0);
            LogicTerrainCell eastChinaSea = SampleLogicAtLonLat(gridTerrain, projection, lon: 125.0, lat: 30.0);
            LogicTerrainCell tibet = SampleLogicAtLonLat(gridTerrain, projection, lon: 88.0, lat: 32.0);

            Assert.That(northChina.HeightLevel, Is.GreaterThan(0), "North China must be authored as land, not sea level.");
            Assert.That(northChina.HasWater, Is.False, "North China must not be under water.");
            Assert.That(vietnamNorth.HeightLevel, Is.GreaterThan(0), "Northern Vietnam must be included as land.");
            Assert.That(vietnamSouth.HeightLevel, Is.GreaterThan(0), "Southern Vietnam must be included as land.");
            Assert.That(eastChinaSea.HasWater, Is.True, "Open sea must remain visibly below sea level.");
            Assert.That(tibet.HeightLevel, Is.GreaterThan(northChina.HeightLevel), "The Tibetan plateau must read higher than the North China plain.");

            LogicTerrainCell hexVietnamSouth = SampleLogicAtLonLat(hexTerrain, projection, lon: 106.0, lat: 10.0);
            Assert.That(hexVietnamSouth.HeightLevel, Is.GreaterThan(0), "HexGrid East Asia must keep complete Vietnam coverage.");

            short visualSea = SampleVisualAtLonLat(visualAsset, projection, lon: 125.0, lat: 30.0);
            short visualNorthChina = SampleVisualAtLonLat(visualAsset, projection, lon: 116.0, lat: 38.0);
            short visualVietnamSouth = SampleVisualAtLonLat(visualAsset, projection, lon: 106.0, lat: 10.0);
            short visualTibet = SampleVisualAtLonLat(visualAsset, projection, lon: 88.0, lat: 32.0);

            Assert.That(visualSea, Is.LessThan(0), "Visual sea must be below sea level.");
            Assert.That(visualNorthChina, Is.GreaterThan(0), "Visual North China plain must remain above sea level.");
            Assert.That(visualVietnamSouth, Is.GreaterThan(0), "Visual southern Vietnam must remain above sea level.");
            Assert.That(visualTibet, Is.GreaterThan(visualNorthChina + 800), "Visual height contrast must make plateau relief readable.");
        }

        [Test]
        public void EastAsiaPlayableTerrainMaps_StartInsideEditorTerrainVisibilityRange()
        {
            string repoRoot = FindRepoRoot();
            string assetRoot = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "east_asia_playable_terrain",
                "EastAsiaPlayableTerrainMod",
                "assets");

            string cameraPath = Path.Combine(assetRoot, "Configs", "Camera", "virtual_cameras.json");
            using JsonDocument cameraDocument = JsonDocument.Parse(File.ReadAllText(cameraPath));
            JsonElement camera = cameraDocument.RootElement[0];
            string cameraId = camera.GetProperty("id").GetString()!;
            float maxDistanceCm = camera.GetProperty("maxDistanceCm").GetSingle();

            Assert.That(cameraId, Is.EqualTo("EastAsia.Camera.PlayableTerrain"));
            float minDistanceCm = camera.GetProperty("minDistanceCm").GetSingle();
            Assert.That(maxDistanceCm, Is.GreaterThan(600000000f), "East Asia camera must support the full imported terrain scale.");
            Assert.That(camera.GetProperty("confineTargetToWorldBounds").GetBoolean(), Is.True, "East Asia camera must not edge-pan outside the imported terrain.");
            Assert.That(camera.GetProperty("panMode").GetString(), Is.EqualTo("Keyboard"), "East Asia editor camera must not drift when the pointer rests on CEF panels.");
            Assert.That(camera.TryGetProperty("targetHeightMode", out _), Is.False, "East Asia starts with flat camera target height so edge confinement cannot sample outside the imported heightmap.");

            foreach (string mapName in new[] { "east_asia_grid.json", "east_asia_hex.json", "east_asia_visual_heightmap.json" })
            {
                string mapPath = Path.Combine(assetRoot, "Maps", mapName);
                using JsonDocument mapDocument = JsonDocument.Parse(File.ReadAllText(mapPath));
                JsonElement defaultCamera = mapDocument.RootElement.GetProperty("DefaultCamera");

                Assert.That(defaultCamera.GetProperty("VirtualCameraId").GetString(), Is.EqualTo(cameraId), mapName);
                Assert.That(defaultCamera.GetProperty("DistanceCm").GetSingle(), Is.InRange(minDistanceCm, maxDistanceCm), mapName);
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

        private static LogicTerrainCell SampleLogicAtLonLat(
            LogicTerrainField terrain,
            EastAsiaProjectionContext projection,
            double lon,
            double lat)
        {
            LonLatToSampleIndex(projection, lon, lat, terrain.WidthCells, terrain.HeightCells, out int column, out int row);
            return terrain.GetCell(column, row);
        }

        private static short SampleVisualAtLonLat(
            VisualHeightmapAsset asset,
            EastAsiaProjectionContext projection,
            double lon,
            double lat)
        {
            LonLatToSampleIndex(projection, lon, lat, asset.SampleColumns, asset.SampleRows, out int column, out int row);
            return asset.HeightSamplesCm[(row * asset.SampleColumns) + column];
        }

        private static void LonLatToSampleIndex(
            EastAsiaProjectionContext projection,
            double lon,
            double lat,
            int columns,
            int rows,
            out int column,
            out int row)
        {
            bool projected = EastAsiaTerrainAssetGenerator.TryProjectLonLatToSourceUv(
                projection.Projection,
                projection.Extent,
                lon,
                lat,
                out double u,
                out double v);
            Assert.That(projected, Is.True, $"Point {lon},{lat} must project into the East Asia Albers canvas.");
            column = Math.Clamp((int)Math.Round(u * (columns - 1)), 0, columns - 1);
            row = Math.Clamp((int)Math.Round(v * (rows - 1)), 0, rows - 1);
        }

        private static EastAsiaProjectionContext ReadProjectionContext(string profilePath)
        {
            using JsonDocument profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            JsonElement root = profile.RootElement;
            JsonElement projection = root.GetProperty("projection");
            JsonElement extent = root.GetProperty("sourceRaster").GetProperty("projectedExtentM");
            return new EastAsiaProjectionContext(
                new EastAsiaTerrainAssetGenerator.EastAsiaProjectionSpec(
                    projection.GetProperty("kind").GetString() ?? string.Empty,
                    projection.GetProperty("centralMeridianDeg").GetDouble(),
                    projection.GetProperty("latitudeOfOriginDeg").GetDouble(),
                    projection.GetProperty("standardParallel1Deg").GetDouble(),
                    projection.GetProperty("standardParallel2Deg").GetDouble(),
                    projection.GetProperty("earthRadiusM").GetDouble()),
                new EastAsiaTerrainAssetGenerator.EastAsiaProjectedExtent(
                    extent.GetProperty("minX").GetDouble(),
                    extent.GetProperty("maxX").GetDouble(),
                    extent.GetProperty("minY").GetDouble(),
                    extent.GetProperty("maxY").GetDouble()));
        }

        private static string ReadEditorImportHash(string profilePath, string fileName)
        {
            using JsonDocument profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            foreach (JsonElement editorImport in profile.RootElement.GetProperty("source").GetProperty("editorImports").EnumerateArray())
            {
                if (string.Equals(editorImport.GetProperty("file").GetString(), fileName, StringComparison.Ordinal))
                {
                    return editorImport.GetProperty("sha256").GetString() ?? string.Empty;
                }
            }

            throw new InvalidDataException($"Editor import '{fileName}' was not found in East Asia terrain profile.");
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }

        private readonly record struct EastAsiaProjectionContext(
            EastAsiaTerrainAssetGenerator.EastAsiaProjectionSpec Projection,
            EastAsiaTerrainAssetGenerator.EastAsiaProjectedExtent Extent);

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "mods")) &&
                    File.Exists(Path.Combine(directory.FullName, "launcher.config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Ludots repository root from the test directory.");
        }
    }
}
