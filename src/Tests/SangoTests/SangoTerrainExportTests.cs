using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sango.Content.MapBin;
using Sango.Content.TerrainExport;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;

namespace Sango.Tests
{
    [TestFixture]
    public sealed class SangoTerrainExportTests
    {
        private const string RealMapPath = @"C:\001_AI\SangoPort\content\DefaultMap.bin";

        [Test]
        public void TerrainExport_SyntheticArchive_HeightmapRoundTripsThroughChtmLoader()
        {
            SangoMapBinArchive archive = BuildSyntheticArchive();
            string dir = Path.Combine(Path.GetTempPath(), "sango-m03-" + Path.GetRandomFileName());
            try
            {
                SangoTerrainExportStats stats = SangoTerrainExporter.ExportToDirectory(archive, dir);
                Assert.That(File.Exists(Path.Combine(dir, SangoTerrainExporter.HeightmapFileName)), Is.True);
                Assert.That(File.Exists(Path.Combine(dir, SangoTerrainExporter.HexDataFileName)), Is.True);

                ContinuousHeightmapAsset asset;
                using (var stream = File.OpenRead(Path.Combine(dir, SangoTerrainExporter.HeightmapFileName)))
                {
                    asset = ContinuousHeightmapBinary.Read(stream);
                }

                Assert.That(asset.SampleColumns, Is.EqualTo(archive.Data.VertexCountY), "east axis columns");
                Assert.That(asset.SampleRows, Is.EqualTo(archive.Data.VertexCountX), "north axis rows");
                Assert.That(asset.HeightSamplesCm.Length, Is.EqualTo(asset.SampleColumns * asset.SampleRows));
                Assert.That(asset.Bounds.Width, Is.EqualTo(stats.WorldWidthCm));
                Assert.That(asset.Bounds.Height, Is.EqualTo(stats.WorldHeightCm));
                Assert.That(asset.Layers.Length, Is.EqualTo(1));
                Assert.That(asset.Layers[0].SampleCount, Is.EqualTo(asset.SamplesPerLayer));

                // Dry vertex (x=2,y=2, height=106): sample row=2, column=2 keeps height*50.
                Assert.That(SampleAt(asset, row: 2, column: 2), Is.EqualTo(106 * 50));

                // Water vertex below sea level keeps its bed height; water vertex above sea clamps to sea.
                int seaLevelCm = SangoTerrainScale.DeriveSeaLevelCm(archive);
                Assert.That(seaLevelCm, Is.EqualTo(100 * SangoTerrainScale.CmPerHeightUnit), "synthetic dominant water byte");
                Assert.That(SampleAt(asset, row: 1, column: 3), Is.EqualTo(20 * 50), "deep bed preserved under the sea clamp");
                Assert.That(SampleAt(asset, row: 7, column: 3), Is.EqualTo(seaLevelCm), "underwater hill clamped to sea level");
                for (int x = 0; x < archive.Data.VertexCountX; x++)
                for (int y = 0; y < archive.Data.VertexCountY; y++)
                {
                    MapVertexData v = archive.Data.VertexAt(x, y);
                    if (v.Water == 0) continue;
                    int got = SampleAt(asset, row: x, column: y);
                    Assert.That(got, Is.LessThanOrEqualTo(seaLevelCm),
                        $"water vertex ({x},{y}) height {got} must not poke above sea level");
                    if (v.Height * SangoTerrainScale.CmPerHeightUnit < seaLevelCm)
                        Assert.That(got, Is.EqualTo(v.Height * SangoTerrainScale.CmPerHeightUnit), $"bed preserved at ({x},{y})");
                }

                Assert.That(stats.WaterSamples, Is.EqualTo(archive.Data.Vertices.Count(v => v.Water > 0)));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void TerrainExport_SyntheticArchive_HexDataFileRoundTripsThroughVertexMapBinary()
        {
            SangoMapBinArchive archive = BuildSyntheticArchive();
            string dir = Path.Combine(Path.GetTempPath(), "sango-m03-" + Path.GetRandomFileName());
            try
            {
                SangoTerrainExporter.ExportToDirectory(archive, dir);

                VertexMap map;
                using (var stream = File.OpenRead(Path.Combine(dir, SangoTerrainExporter.HexDataFileName)))
                {
                    map = VertexMapBinary.Read(stream);
                }

                int cellsX = archive.Grid.BoundsX;
                int cellsY = archive.Grid.BoundsY;
                Assert.That(map.WidthInChunks, Is.EqualTo((cellsY + 63) / 64));
                Assert.That(map.HeightInChunks, Is.EqualTo((cellsX + 63) / 64));

                int gridVertexCount = System.Math.Max(1, archive.Grid.GridVertexCount);
                for (int gridX = 0; gridX < cellsX; gridX += 5)
                for (int gridY = 0; gridY < cellsY; gridY += 3)
                {
                    MapGridCell cell = archive.Grid.CellAt(gridX, gridY);
                    MapVertexData center = CenterVertex(archive, gridVertexCount, gridX, gridY);
                    Assert.That(GetExtraByte(map, gridY, gridX), Is.EqualTo(cell.TerrainType),
                        $"raw terrain byte preserved at grid ({gridX},{gridY})");
                    Assert.That(map.GetBiome(gridY, gridX), Is.EqualTo(SangoTerrainScale.ProjectTerrainTypeToBiome(cell.TerrainType)));
                    Assert.That(map.GetHeight(gridY, gridX), Is.EqualTo(SangoTerrainScale.QuantizeToNibbleLevel(center.Height)));
                    Assert.That(map.GetWaterHeight(gridY, gridX), Is.EqualTo(SangoTerrainScale.QuantizeToNibbleLevel(center.Water)));
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void TerrainExport_DefaultMapBin_ProducesEngineLoadableTerrain()
        {
            if (!File.Exists(RealMapPath))
                Assert.Ignore($"Real map not present on this machine: {RealMapPath}");

            using var stream = File.OpenRead(RealMapPath);
            using var reader = new BinaryReader(stream);
            SangoMapBinArchive archive = SangoMapBinIo.Read(reader);

            string dir = Path.Combine(Path.GetTempPath(), "sango-m03-" + Path.GetRandomFileName());
            try
            {
                SangoTerrainExportStats stats = SangoTerrainExporter.ExportToDirectory(archive, dir);

                ContinuousHeightmapAsset asset;
                using (var heightStream = File.OpenRead(Path.Combine(dir, SangoTerrainExporter.HeightmapFileName)))
                {
                    asset = ContinuousHeightmapBinary.Read(heightStream);
                }

                Assert.That(asset.SampleColumns, Is.EqualTo(1025), "(W+1) east columns of the 1024x1024 map");
                Assert.That(asset.SampleRows, Is.EqualTo(1025), "(H+1) north rows");
                Assert.That(asset.HeightSamplesCm.Length, Is.EqualTo(1025 * 1025));
                Assert.That(asset.Bounds.Width, Is.EqualTo(512000), "1024 quads x 5 m = 5120 m board extent");
                Assert.That(asset.Bounds.Height, Is.EqualTo(512000));

                Assert.That(stats.SeaLevelCm, Is.EqualTo(550), "dominant water byte 11 x 50 cm; map json SeaLevelCm/waterPlaneY must match");
                Assert.That(asset.HeightSamplesCm.Max(), Is.EqualTo(255 * 50), "peak land sample survives the water clamp");

                Assert.That(stats.WaterSamples, Is.GreaterThan(250_000), "ocean + rivers dominate the water mask");
                Assert.That(stats.WaterSamples, Is.LessThan(asset.HeightSamplesCm.LongLength / 2), "majority of the map stays dry land");

                VertexMap map;
                using (var hexStream = File.OpenRead(Path.Combine(dir, SangoTerrainExporter.HexDataFileName)))
                {
                    map = VertexMapBinary.Read(hexStream);
                }

                Assert.That(map.WidthInChunks, Is.EqualTo(4), "256 east cells / 64-cell chunks");
                Assert.That(map.HeightInChunks, Is.EqualTo(4), "256 north cells / 64-cell chunks");

                Dictionary<byte, int> sourceHistogram = archive.Grid.Cells
                    .GroupBy(c => c.TerrainType)
                    .ToDictionary(g => g.Key, g => g.Count());
                var roundTripped = new Dictionary<byte, int>();
                for (int row = 0; row < archive.Grid.BoundsX; row++)
                for (int column = 0; column < archive.Grid.BoundsY; column++)
                {
                    byte raw = GetExtraByte(map, column, row);
                    roundTripped[raw] = roundTripped.GetValueOrDefault(raw) + 1;
                }
                Assert.That(roundTripped, Is.EqualTo(sourceHistogram), "terrainType per 256x256 cell survives the .hex roundtrip");

                TestContext.Out.WriteLine(
                    $"height={asset.SampleColumns}x{asset.SampleRows} bounds={asset.Bounds.Width}x{asset.Bounds.Height}cm " +
                    $"sea={stats.SeaLevelCm}cm peakSpan={stats.PeakSpanCm}cm waterSamples={stats.WaterSamples} " +
                    $"hexChunks={map.WidthInChunks}x{map.HeightInChunks} terrainKinds={sourceHistogram.Count}");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        private static int SampleAt(ContinuousHeightmapAsset asset, int row, int column)
        {
            return asset.HeightSamplesCm[(row * asset.SampleColumns) + column];
        }

        private static byte GetExtraByte(VertexMap map, int column, int row)
        {
            var chunk = map.GetChunk(column, row, createIfMissing: false);
            return chunk?.GetExtraByte(column & VertexChunk.ChunkSizeMask, row & VertexChunk.ChunkSizeMask, 0) ?? (byte)0;
        }

        private static MapVertexData CenterVertex(SangoMapBinArchive archive, int gridVertexCount, int gridX, int gridY)
        {
            int vx = System.Math.Min(archive.Data.VertexCountX - 1, gridX * gridVertexCount + gridVertexCount / 2);
            int vy = System.Math.Min(archive.Data.VertexCountY - 1, gridY * gridVertexCount + gridVertexCount / 2);
            return archive.Data.VertexAt(vx, vy);
        }

        private static SangoMapBinArchive BuildSyntheticArchive()
        {
            const int width = 12;
            const int height = 8;

            var cells = new MapGridCell[width * height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    cells[x * height + y] = new MapGridCell(
                        TerrainType: (byte)(1 + (x + y) % 20),
                        TerrainState: 0,
                        AreaId: 1);

            var vertices = new MapVertexData[(width + 1) * (height + 1)];
            for (int x = 0; x <= width; x++)
                for (int y = 0; y <= height; y++)
                {
                    // Water band on rows y=3..5 at level 100; beds at 20 stay below sea, x>=6 hills at 180 clamp to it.
                    byte h;
                    byte water = 0;
                    if (y is 3 or 4 or 5)
                    {
                        water = 100;
                        h = x < 6 ? (byte)20 : (byte)180;
                    }
                    else
                    {
                        h = (byte)(30 + ((x * 31 + y * 7) % 200));
                    }
                    vertices[x * (height + 1) + y] = new MapVertexData(h, (byte)(x % 30), water);
                }

            return new SangoMapBinArchive
            {
                Version = 10,
                WorkContent = "Default",
                Width = width,
                Height = height,
                Grid = new MapGridSection
                {
                    GridSize = 5,
                    GridVertexCount = 1,
                    BoundsX = width,
                    BoundsY = height,
                    Cells = cells,
                },
                Data = new MapDataSection
                {
                    QuadSize = 5,
                    VertexCountX = width + 1,
                    VertexCountY = height + 1,
                    Vertices = vertices,
                },
            };
        }
    }
}
