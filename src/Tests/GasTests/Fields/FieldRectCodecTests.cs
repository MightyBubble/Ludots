using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    [Category("benchmark")]
    public sealed class FieldRectCodecTests
    {
        [Test]
        public void CoalescePoints_MergesSolidRect()
        {
            var points = new List<(int X, int Y, int RegionId)>();
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 10; x <= 13; x++)
                {
                    points.Add((x, y, 3));
                }
            }

            List<FieldCellRectStroke> rects = FieldRectCodec.CoalescePoints(points);

            Assert.That(rects, Has.Count.EqualTo(1));
            Assert.That(rects[0].X0, Is.EqualTo(10));
            Assert.That(rects[0].Y0, Is.EqualTo(2));
            Assert.That(rects[0].X1, Is.EqualTo(13));
            Assert.That(rects[0].Y1, Is.EqualTo(5));
            Assert.That(rects[0].RegionId, Is.EqualTo(3));
            Assert.That(rects[0].CellCount, Is.EqualTo(16));
        }

        [Test]
        public void CoalesceFromField_RoundTripsTwoProvinces()
        {
            var field = new ChunkedField2D<int>(new FieldGridSpec2D(100, 16), defaultValue: 0);
            field.FillRect(0, 0, 31, 15, 1);
            field.FillRect(32, 0, 63, 15, 2);

            List<FieldCellRectStroke> rects = FieldRectCodec.CoalesceFromField(field);

            Assert.That(rects, Has.Count.EqualTo(2));
            Assert.That(rects.Exists(r => r.RegionId == 1 && r.CellCount == 32 * 16), Is.True);
            Assert.That(rects.Exists(r => r.RegionId == 2 && r.CellCount == 32 * 16), Is.True);
        }

        [Test]
        public void Benchmark_FillAndCoalesce_FiveThousandSquare_TwoHalves()
        {
            const int side = 5000;
            const int mid = side / 2 - 1;
            var field = new ChunkedField2D<int>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 16),
                defaultValue: 0,
                initialChunkCapacity: 1024);

            long fillStart = Stopwatch.GetTimestamp();
            field.FillRect(0, 0, mid, side - 1, 1);
            field.FillRect(mid + 1, 0, side - 1, side - 1, 2);
            double fillMs = ElapsedMs(fillStart, Stopwatch.GetTimestamp());

            long coalesceStart = Stopwatch.GetTimestamp();
            List<FieldCellRectStroke> rects = FieldRectCodec.CoalesceFromField(field);
            double coalesceMs = ElapsedMs(coalesceStart, Stopwatch.GetTimestamp());

            long jsonBytes = EstimateRectJsonBytes(rects);
            long cellJsonBytes = (long)side * side * 18L;

            Console.WriteLine(
                $"[Benchmark] FieldRectCodec.5000x5000.TwoHalves: " +
                $"nonDefault={field.NonDefaultCount} chunks={field.ChunkCount} rects={rects.Count} " +
                $"fillMs={fillMs:F1} coalesceMs={coalesceMs:F1} " +
                $"rectJsonBytes~{jsonBytes} cellJsonBytes~{cellJsonBytes} " +
                $"compressionRatio~{(double)cellJsonBytes / Math.Max(1, jsonBytes):F0}x");

            Assert.That(field.NonDefaultCount, Is.EqualTo((long)side * side));
            Assert.That(rects, Has.Count.EqualTo(2), "two solid halves must coalesce to two rects");
            Assert.That(jsonBytes, Is.LessThan(512), "authoring payload must stay tiny");
            Assert.That(fillMs, Is.LessThan(30_000d), "fill must finish under 30s on CI");
            Assert.That(coalesceMs, Is.LessThan(30_000d), "coalesce must finish under 30s on CI");
        }

        [Test]
        public void ApplyAuthoredRects_FiveThousandSquare_UsesTwoStrokes()
        {
            var catalog = new FieldLayerRegistry();
            catalog.Register(
                "layerBig", FieldLayerKind.DiscreteId, cellSizeCm: 100, chunkSizeCells: 16,
                FieldLayerDefaultValue.None, persistent: true, "test.writer", maxRegionIds: 8);
            var layer = new DiscreteIdFieldLayerData(catalog.Get(catalog.GetId("layerBig")));
            var asset = new FieldCellsAsset
            {
                LayerKey = "layerBig",
                RegionKeys = new[] { "east", "west" },
                Rects = new[]
                {
                    new FieldCellRectEntry(0, 0, 2499, 4999, "west"),
                    new FieldCellRectEntry(2500, 0, 4999, 4999, "east"),
                },
                Points = Array.Empty<FieldCellRegionEntry>(),
            };

            long start = Stopwatch.GetTimestamp();
            foreach (string key in asset.RegionKeys)
            {
                layer.Regions.Register(key);
            }

            foreach (FieldCellRectEntry rect in asset.Rects)
            {
                layer.Field.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, layer.Regions.GetId(rect.RegionKey));
            }

            double ms = ElapsedMs(start, Stopwatch.GetTimestamp());
            Console.WriteLine(
                $"[Benchmark] FieldSessionApply.5000x5000.Rects: ms={ms:F1} chunks={layer.Field.ChunkCount} nonDefault={layer.Field.NonDefaultCount}");

            Assert.That(layer.Field.Get(new FieldCell2D(0, 0)), Is.EqualTo(layer.Regions.GetId("west")));
            Assert.That(layer.Field.Get(new FieldCell2D(4999, 4999)), Is.EqualTo(layer.Regions.GetId("east")));
            Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(25_000_000L));
            Assert.That(ms, Is.LessThan(30_000d));
        }

        private static long EstimateRectJsonBytes(List<FieldCellRectStroke> rects)
        {
            long total = 32;
            foreach (FieldCellRectStroke stroke in rects)
            {
                total += 24 + DigitCount(stroke.X0) + DigitCount(stroke.Y0) + DigitCount(stroke.X1) +
                         DigitCount(stroke.Y1) + DigitCount(stroke.RegionId);
            }

            return total;
        }

        private static int DigitCount(int value)
        {
            int abs = Math.Abs(value);
            int digits = 1;
            while (abs >= 10)
            {
                abs /= 10;
                digits++;
            }

            return value < 0 ? digits + 1 : digits;
        }

        private static double ElapsedMs(long start, long stop) =>
            (stop - start) * 1000.0 / Stopwatch.Frequency;
    }
}
