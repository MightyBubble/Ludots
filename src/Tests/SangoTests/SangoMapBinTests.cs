using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using Sango.Content.MapBin;

namespace Sango.Tests
{
    [TestFixture]
    public sealed class SangoMapBinTests
    {
        private const string RealMapPath = @"C:\001_AI\SangoPort\content\DefaultMap.bin";

        [Test]
        public void MapBin_SyntheticV10Archive_RoundTripsThroughWriteRead()
        {
            SangoMapBinArchive archive = BuildSyntheticV10Map();

            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                SangoMapBinIo.Write(writer, archive);

            buffer.Position = 0;
            using var reader = new BinaryReader(buffer, Encoding.UTF8, leaveOpen: true);
            SangoMapBinArchive loaded = SangoMapBinIo.Read(reader);

            Assert.That(reader.BaseStream.Position, Is.EqualTo(buffer.Length), "reader should consume the whole stream");

            Assert.That(loaded.Version, Is.EqualTo(10));
            Assert.That(loaded.WorkContent, Is.EqualTo("Default"));
            Assert.That(loaded.Width, Is.EqualTo(archive.Width));
            Assert.That(loaded.Height, Is.EqualTo(archive.Height));

            Assert.That(loaded.Grid.GridSize, Is.EqualTo(archive.Grid.GridSize));
            Assert.That(loaded.Grid.GridVertexCount, Is.EqualTo(archive.Grid.GridVertexCount));
            Assert.That(loaded.Grid.BoundsX, Is.EqualTo(archive.Grid.BoundsX));
            Assert.That(loaded.Grid.BoundsY, Is.EqualTo(archive.Grid.BoundsY));
            Assert.That(loaded.Grid.GridTextureName, Is.EqualTo(archive.Grid.GridTextureName));
            Assert.That(loaded.Grid.Cells, Is.EqualTo(archive.Grid.Cells));
            Assert.That(loaded.Grid.CellAt(1, 2), Is.EqualTo(archive.Grid.CellAt(1, 2)));

            Assert.That(loaded.Data.QuadSize, Is.EqualTo(archive.Data.QuadSize));
            Assert.That(loaded.Data.VertexCountX, Is.EqualTo(archive.Data.VertexCountX));
            Assert.That(loaded.Data.VertexCountY, Is.EqualTo(archive.Data.VertexCountY));
            Assert.That(loaded.Data.Vertices, Is.EqualTo(archive.Data.Vertices));

            Assert.That(loaded.Layer.Layers.Length, Is.EqualTo(archive.Layer.Layers.Length));
            for (int i = 0; i < loaded.Layer.Layers.Length; i++)
            {
                Assert.That(loaded.Layer.Layers[i].IsLit, Is.EqualTo(archive.Layer.Layers[i].IsLit));
                Assert.That(loaded.Layer.Layers[i].TextureScale, Is.EqualTo(archive.Layer.Layers[i].TextureScale));
                Assert.That(loaded.Layer.Layers[i].DiffuseTexNames, Is.EqualTo(archive.Layer.Layers[i].DiffuseTexNames));
                Assert.That(loaded.Layer.Layers[i].NormalTexNames, Is.EqualTo(archive.Layer.Layers[i].NormalTexNames));
                Assert.That(loaded.Layer.Layers[i].MaskTexNames, Is.EqualTo(archive.Layer.Layers[i].MaskTexNames));
            }

            Assert.That(loaded.Terrain.CellSize, Is.EqualTo(archive.Terrain.CellSize));

            Assert.That(loaded.Light.LightDirection, Is.EqualTo(archive.Light.LightDirection));
            Assert.That(loaded.Light.LightColor, Is.EqualTo(archive.Light.LightColor));
            Assert.That(loaded.Light.LightIntensity, Is.EqualTo(archive.Light.LightIntensity));
            Assert.That(loaded.Light.ShadowColor, Is.EqualTo(archive.Light.ShadowColor));
            Assert.That(loaded.Light.ShadowStrength, Is.EqualTo(archive.Light.ShadowStrength));

            Assert.That(loaded.Fog.FogColor, Is.EqualTo(archive.Fog.FogColor));
            Assert.That(loaded.Fog.FogStart, Is.EqualTo(archive.Fog.FogStart));
            Assert.That(loaded.Fog.FogEnd, Is.EqualTo(archive.Fog.FogEnd));
            Assert.That(loaded.Fog.FogDensity, Is.EqualTo(archive.Fog.FogDensity));

            Assert.That(loaded.BaseColor.EmbeddedTextures, Is.All.Null, "v10 base color carries no stream bytes");
            Assert.That(loaded.BaseColor.LegacyNames, Is.Null, "v10 base color carries no stream bytes");

            Assert.That(loaded.SkyBox.BlendStart, Is.EqualTo(-200f), "OnSave writes the sky_blend_start constant");
            Assert.That(loaded.SkyBox.BlendEnd, Is.EqualTo(-150f), "OnSave writes the sky_blend_end constant");
            Assert.That(loaded.SkyBox.Areas.Length, Is.EqualTo(archive.SkyBox.Areas.Length));
            for (int i = 0; i < loaded.SkyBox.Areas.Length; i++)
            {
                Assert.That(loaded.SkyBox.Areas[i].Bounds, Is.EqualTo(archive.SkyBox.Areas[i].Bounds));
                Assert.That(loaded.SkyBox.Areas[i].SeasonTextureNames, Is.EqualTo(archive.SkyBox.Areas[i].SeasonTextureNames));
            }

            Assert.That(loaded.Camera.SafeBorder, Is.EqualTo(archive.Camera.SafeBorder));

            Assert.That(loaded.Models.Objects, Is.EqualTo(archive.Models.Objects));

            Assert.That(loaded.Labels.Labels, Is.EqualTo(archive.Labels.Labels));
        }

        [Test]
        public void MapBin_DefaultMapBin_ReadsFullData()
        {
            if (!File.Exists(RealMapPath))
                Assert.Ignore($"Real map not present on this machine: {RealMapPath}");

            using var stream = File.OpenRead(RealMapPath);
            using var reader = new BinaryReader(stream);
            SangoMapBinArchive archive = SangoMapBinIo.Read(reader);

            Assert.That(reader.BaseStream.Position, Is.EqualTo(stream.Length), "reader should consume the whole file");

            Assert.That(archive.Version, Is.EqualTo(10));
            Assert.That(archive.Width, Is.GreaterThan(0));
            Assert.That(archive.Height, Is.GreaterThan(0));

            Dictionary<byte, int> histogram = archive.Grid.Cells.GroupBy(c => c.TerrainType)
                .ToDictionary(g => g.Key, g => g.Count());
            Assert.That(histogram.Count, Is.GreaterThanOrEqualTo(5), "terrainType histogram should cover at least 5 terrain kinds");
            Assert.That(archive.Grid.Cells.Length, Is.EqualTo(archive.Grid.BoundsX * archive.Grid.BoundsY));

            Assert.That(archive.Labels.Labels.Length, Is.GreaterThan(0));
            Assert.That(archive.Models.Objects.Length, Is.GreaterThan(0));

            TestContext.Out.WriteLine($"version={archive.Version} workContent={archive.WorkContent} width={archive.Width} height={archive.Height}");
            TestContext.Out.WriteLine($"grid bounds={archive.Grid.BoundsX}x{archive.Grid.BoundsY} gridSize={archive.Grid.GridSize} gridVertexCount={archive.Grid.GridVertexCount} quadSize={archive.Data.QuadSize}");
            TestContext.Out.WriteLine("terrainType histogram: " + string.Join(", ", histogram.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));
            TestContext.Out.WriteLine($"models={archive.Models.Objects.Length} labels={archive.Labels.Labels.Length} layers={archive.Layer.Layers.Length} skyAreas={archive.SkyBox.Areas.Length} cellSize={archive.Terrain.CellSize}");
            TestContext.Out.WriteLine("sample labels: " + string.Join(" | ", archive.Labels.Labels.Take(5).Select(l => $"{l.Text}@({l.Position.X:F0},{l.Position.Y:F0},{l.Position.Z:F0})")));
        }

        private static SangoMapBinArchive BuildSyntheticV10Map()
        {
            const int width = 12;
            const int height = 8;

            var cells = new MapGridCell[width * height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    cells[x * height + y] = new MapGridCell(
                        TerrainType: (byte)(1 + (x * height + y) % 6),
                        TerrainState: (x % 3) << 1,
                        AreaId: (ushort)(100 + y));

            var vertices = new MapVertexData[(width + 1) * (height + 1)];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new MapVertexData(
                    Height: (byte)(i % 251),
                    TextureIndex: (byte)(i % 36),
                    Water: (byte)(i % 3 == 0 ? 40 : 0));

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
                    GridTextureName = "grid_hex",
                    Cells = cells,
                },
                Data = new MapDataSection
                {
                    QuadSize = 5,
                    VertexCountX = width + 1,
                    VertexCountY = height + 1,
                    Vertices = vertices,
                },
                Layer = new MapLayerSection
                {
                    Layers = new[]
                    {
                        new MapLayerData
                        {
                            IsLit = true,
                            TextureScale = new Vector2(2.5f, -3.5f),
                            DiffuseTexNames = new[] { "layer_autumn.png", "layer_spring.png", "layer_summer.png", "layer_winter.png" },
                            NormalTexNames = new[] { "n0.png", "n1.png", "n2.png", "n3.png" },
                            MaskTexNames = new[] { "m0.png", "", "m2.png", "" },
                        },
                        new MapLayerData
                        {
                            IsLit = false,
                            TextureScale = new Vector2(1.25f, -1.75f),
                            DiffuseTexNames = new[] { "water_0.png", "water_1.png", "water_2.png", "water_3.png" },
                            NormalTexNames = new[] { "", "", "", "" },
                            MaskTexNames = new[] { "", "", "", "" },
                        },
                    },
                },
                Terrain = new MapTerrainSection { CellSize = 96 },
                Light = new MapLightSection
                {
                    LightDirection = { [0] = new Vector3(10f, 20f, 30f), [2] = new Vector3(-5f, 90f, 15f) },
                    LightColor = { [1] = new SangoRgbF(0.9f, 0.8f, 0.7f), [3] = new SangoRgbF(0.2f, 0.3f, 0.4f) },
                    LightIntensity = { [0] = 1.5f, [1] = 0.75f, [2] = 1.25f, [3] = 0.9f },
                    ShadowColor = { [0] = new SangoRgbF(0.4f, 0.5f, 0.6f) },
                    ShadowStrength = { [1] = 0.6f, [3] = 1.4f },
                },
                Fog = new MapFogSection
                {
                    FogColor = { [0] = new SangoRgbF(0.7f, 0.75f, 0.8f), [2] = new SangoRgbF(0.9f, 0.9f, 0.95f) },
                    FogStart = { [0] = 100.5f, [1] = 200.5f, [2] = 300.5f, [3] = 400.5f },
                    FogEnd = { [0] = 900.25f, [1] = 950.25f, [2] = 1000.25f, [3] = 1050.25f },
                    FogDensity = { [0] = 3.5f, [1] = 4.5f, [2] = 5.5f, [3] = 6.5f },
                },
                BaseColor = new MapBaseColorSection(),
                SkyBox = new MapSkyBoxSection
                {
                    BlendStart = -123.5f,
                    BlendEnd = -45.25f,
                    Areas = new[]
                    {
                        new MapSkyArea
                        {
                            Bounds = new SangoRectF(0f, 0f, 32768f, 32768f),
                            SeasonTextureNames = new[] { "sky_autumn.png", "sky_spring.png", "sky_summer.png", "sky_winter.png" },
                        },
                        new MapSkyArea
                        {
                            Bounds = new SangoRectF(100f, 200f, 300.5f, 400.25f),
                            SeasonTextureNames = new[] { "alt0.png", "", "alt2.png", "" },
                        },
                    },
                },
                Camera = new MapCameraSection { SafeBorder = 424.5f },
                Models = new MapModelsSection
                {
                    Objects = new[]
                    {
                        new MapModelObject { ObjId = 1, ObjType = 3, BindId = 7, ModelId = 2, Position = new Vector3(10f, 1.5f, 20f), Rotation = new Vector3(0f, 45f, 0f), Scale = new Vector3(1f, 1f, 1f) },
                        new MapModelObject { ObjId = 2, ObjType = 4, BindId = 0, ModelId = 5, Position = new Vector3(30.5f, 2.5f, 40.25f), Rotation = new Vector3(0f, 90f, 0f), Scale = new Vector3(2f, 2f, 2f) },
                        new MapModelObject { ObjId = 3, ObjType = 0, BindId = 9, ModelId = 1, Position = new Vector3(-5f, 0.5f, -6f), Rotation = new Vector3(10f, 180f, 0f), Scale = new Vector3(1.5f, 1.5f, 1.5f) },
                    },
                },
                Labels = new MapLabelSetSection
                {
                    Labels = new[]
                    {
                        new MapLabel { Text = "许昌", Position = new Vector3(1407f, 12f, 796f), Color = new SangoRgb32(255, 128, 0), FontSize = 24 },
                        new MapLabel { Text = "洛阳", Position = new Vector3(1500.5f, 8f, 900.25f), Color = new SangoRgb32(255, 255, 0), FontSize = 28 },
                        new MapLabel { Text = "成都", Position = new Vector3(700f, 6.5f, 2200f), Color = new SangoRgb32(0, 200, 255), FontSize = 20 },
                        new MapLabel { Text = "襄阳", Position = new Vector3(1200f, 9f, 1500f), Color = new SangoRgb32(200, 0, 255), FontSize = 22 },
                    },
                },
            };
        }
    }
}
