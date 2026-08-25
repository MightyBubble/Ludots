using Ludots.Core.Presentation.Terrain;
using Ludots.Tool;
using NUnit.Framework;
using System;
using System.IO;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class TerrainControlMapBakeTests
    {
        [Test]
        public void TerrainWeightRules_Parse_MissingRockLayer_Throws()
        {
            string json = ValidRulesJson(rockBlock: null);

            var exception = Assert.Throws<InvalidOperationException>(() => TerrainWeightRules.Parse(json));
            Assert.That(exception!.Message, Does.Contain("rock"));
        }

        [Test]
        public void TerrainWeightRules_Parse_InvertedHeightBand_Throws()
        {
            string json = ValidRulesJson(grassBand: "{ \"riseStart\": 500, \"riseEnd\": 100, \"fallStart\": 1300, \"fallEnd\": 2100 }");

            var exception = Assert.Throws<InvalidOperationException>(() => TerrainWeightRules.Parse(json));
            Assert.That(exception!.Message, Does.Contain("riseStart < riseEnd < fallStart < fallEnd"));
        }

        [Test]
        public void TerrainWeightRules_Parse_NoiseAmplitudeAboveOne_Throws()
        {
            string json = ValidRulesJson(dirtNoiseAmplitude: "1.5");

            var exception = Assert.Throws<InvalidOperationException>(() => TerrainWeightRules.Parse(json));
            Assert.That(exception!.Message, Does.Contain("noiseAmplitude"));
        }

        [Test]
        public void TerrainWeightRules_Parse_UnknownLayer_Throws()
        {
            string json = ValidRulesJson(extraLayer: ",\n    \"snow\": { \"bandCm\": { \"riseStart\": 1, \"riseEnd\": 2, \"fallStart\": 3, \"fallEnd\": 4 }, \"slopeFactor\": 0, \"noiseFrequency\": 0, \"noiseAmplitude\": 0 }");

            var exception = Assert.Throws<InvalidOperationException>(() => TerrainWeightRules.Parse(json));
            Assert.That(exception!.Message, Does.Contain("snow"));
        }

        [Test]
        public void TerrainControlMapBaker_BakeRgba8_RepeatedBake_ProducesIdenticalBytes()
        {
            TerrainWeightRules rules = PeakRules(rockRiseStartCm: 3000, rockRiseEndCm: 3700);
            short[] heights = ConeHeights(columns: 33, rows: 33, peakCm: 3800);

            byte[] first = TerrainControlMapBaker.BakeRgba8(rules, heights, 33, 33, 3_200_000, 3_200_000, 33, 33);
            byte[] second = TerrainControlMapBaker.BakeRgba8(rules, heights, 33, 33, 3_200_000, 3_200_000, 33, 33);
            byte[] firstPng = WritePngToMemory(33, 33, first);
            byte[] secondPng = WritePngToMemory(33, 33, second);

            Assert.That(second, Is.EqualTo(first), "Same rules and heights must bake to identical RGBA bytes.");
            Assert.That(secondPng, Is.EqualTo(firstPng), "Same RGBA bytes must encode to identical PNG bytes.");
        }

        [Test]
        public void TerrainControlMapBaker_BakeRgba8_RaisedSnowline_ShiftsPeakFromRockToDirt()
        {
            short[] heights = ConeHeights(columns: 33, rows: 33, peakCm: 3800);
            const int centerOffset = ((16 * 33) + 16) * 4;

            byte[] defaultBake = TerrainControlMapBaker.BakeRgba8(
                PeakRules(rockRiseStartCm: 3000, rockRiseEndCm: 3700), heights, 33, 33, 3_200_000, 3_200_000, 33, 33);
            byte[] raisedBake = TerrainControlMapBaker.BakeRgba8(
                PeakRules(rockRiseStartCm: 9000, rockRiseEndCm: 9600), heights, 33, 33, 3_200_000, 3_200_000, 33, 33);

            Assert.That(defaultBake[centerOffset + 3], Is.GreaterThan(defaultBake[centerOffset + 2]),
                "Peak above the authored snowline must be rock-dominant.");
            Assert.That(defaultBake[centerOffset + 3], Is.GreaterThan(defaultBake[centerOffset + 1]));
            Assert.That(raisedBake[centerOffset + 3], Is.EqualTo(0),
                "Raising the snowline above the peak must remove rock weight entirely.");
            Assert.That(raisedBake[centerOffset + 2], Is.GreaterThan(raisedBake[centerOffset + 1]),
                "With the snowline raised, the peak must fall to the dirt band.");
            Assert.That(raisedBake[centerOffset + 2], Is.GreaterThan(raisedBake[centerOffset + 0]));
        }

        [Test]
        public void TerrainControlMapBaker_BakeRgba8_HeightOutsideAllBands_Throws()
        {
            TerrainWeightRules rules = PeakRules(rockRiseStartCm: 3000, rockRiseEndCm: 3700, bandShiftCm: 50000);
            short[] heights = ConeHeights(columns: 33, rows: 33, peakCm: 3800);

            Assert.Throws<InvalidDataException>(() =>
                TerrainControlMapBaker.BakeRgba8(rules, heights, 33, 33, 3_200_000, 3_200_000, 33, 33));
        }

        [Test]
        public void EastAsiaWeightRules_BakedAtPlateauCenter_DirtDominatesOverRock()
        {
            string modRoot = Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "east_asia_playable_terrain",
                "EastAsiaPlayableTerrainMod");
            TerrainWeightRules rules = TerrainWeightRules.Load(Path.Combine(modRoot, "assets", "terrain", "east_asia_weight_rules.json"));
            VisualHeightmapAsset heightmap;
            using (FileStream stream = File.OpenRead(Path.Combine(modRoot, "assets", "samples", "LudotsSample", "east_asia", "east_asia_continuous.vhtm")))
            {
                heightmap = VisualHeightmapBinary.Read(stream);
            }

            const int outColumns = 512;
            const int outRows = 293;
            byte[] rgba = TerrainControlMapBaker.BakeRgba8(
                rules,
                heightmap.HeightSamplesCm,
                heightmap.SampleColumns,
                heightmap.SampleRows,
                heightmap.Bounds.Width,
                heightmap.Bounds.Height,
                outColumns,
                outRows);

            int plateauColumn = (int)Math.Round(3584 * (outColumns - 1) / (double)(heightmap.SampleColumns - 1));
            int plateauRow = (int)Math.Round(2048 * (outRows - 1) / (double)(heightmap.SampleRows - 1));
            int offset = ((plateauRow * outColumns) + plateauColumn) * 4;

            Assert.That(rgba[offset + 2], Is.GreaterThan(200), "Plateau center must be dirt-dominant under the authored rules.");
            Assert.That(rgba[offset + 3], Is.LessThan(50), "Plateau center must stay below the authored snowline (no rock).");
        }

        private static TerrainWeightRules PeakRules(int rockRiseStartCm, int rockRiseEndCm, int bandShiftCm = 0)
        {
            return new TerrainWeightRules(
                "test_peak_weights",
                seaLevelCm: 0,
                outputFile: "assets/Textures/terrain_control_weights.png",
                outputColumns: 33,
                outputRows: 33,
                layers: new[]
                {
                    new TerrainWeightLayerRule(-30000 + bandShiftCm, -400 + bandShiftCm, 140 + bandShiftCm, 420 + bandShiftCm, slopeFactor: 0f, noiseFrequency: 0f, noiseAmplitude: 0f),
                    new TerrainWeightLayerRule(40 + bandShiftCm, 260 + bandShiftCm, 1300 + bandShiftCm, 2100 + bandShiftCm, slopeFactor: 0f, noiseFrequency: 0f, noiseAmplitude: 0f),
                    new TerrainWeightLayerRule(800 + bandShiftCm, 1500 + bandShiftCm, 2500 + bandShiftCm, 4500 + bandShiftCm, slopeFactor: 0f, noiseFrequency: 0f, noiseAmplitude: 0f),
                    new TerrainWeightLayerRule(rockRiseStartCm + bandShiftCm, rockRiseEndCm + bandShiftCm, 100000 + bandShiftCm, 110000 + bandShiftCm, slopeFactor: 0f, noiseFrequency: 0f, noiseAmplitude: 0f)
                });
        }

        private static short[] ConeHeights(int columns, int rows, int peakCm)
        {
            var heights = new short[columns * rows];
            int centerColumn = columns / 2;
            int centerRow = rows / 2;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    bool peak = column == centerColumn && row == centerRow;
                    heights[(row * columns) + column] = peak ? (short)peakCm : (short)100;
                }
            }

            return heights;
        }

        private static byte[] WritePngToMemory(int columns, int rows, byte[] rgba)
        {
            using var stream = new MemoryStream();
            TerrainControlMapBaker.WriteRgba8Png(stream, columns, rows, rgba);
            return stream.ToArray();
        }

        private static string ValidRulesJson(
            string? rockBlock = "\"rock\": { \"bandCm\": { \"riseStart\": 3000, \"riseEnd\": 3700, \"fallStart\": 100000, \"fallEnd\": 110000 }, \"slopeFactor\": -1.4, \"noiseFrequency\": 0.06, \"noiseAmplitude\": 0.2 }",
            string grassBand = "{ \"riseStart\": 40, \"riseEnd\": 260, \"fallStart\": 1300, \"fallEnd\": 2100 }",
            string dirtNoiseAmplitude = "0.3",
            string extraLayer = "")
        {
            string rockEntry = rockBlock == null ? string.Empty : ",\n    " + rockBlock;
            return "{\n" +
                "  \"id\": \"test_rules\",\n" +
                "  \"seaLevelCm\": 0,\n" +
                "  \"output\": { \"file\": \"assets/Textures/terrain_control_weights.png\", \"columns\": 33, \"rows\": 33 },\n" +
                "  \"layers\": {\n" +
                "    \"sand\": { \"bandCm\": { \"riseStart\": -30000, \"riseEnd\": -400, \"fallStart\": 140, \"fallEnd\": 420 }, \"slopeFactor\": 0.65, \"noiseFrequency\": 0.035, \"noiseAmplitude\": 0.1 },\n" +
                $"    \"grass\": {{ \"bandCm\": {grassBand}, \"slopeFactor\": 1.1, \"noiseFrequency\": 0.05, \"noiseAmplitude\": 0.35 }},\n" +
                $"    \"dirt\": {{ \"bandCm\": {{ \"riseStart\": 800, \"riseEnd\": 1500, \"fallStart\": 2500, \"fallEnd\": 3300 }}, \"slopeFactor\": 0.4, \"noiseFrequency\": 0.08, \"noiseAmplitude\": {dirtNoiseAmplitude} }}" +
                rockEntry +
                extraLayer +
                "\n  }\n" +
                "}\n";
        }

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
