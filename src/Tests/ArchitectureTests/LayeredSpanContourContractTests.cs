using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanContourContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk =
            new(agentHeightCm: 50, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 5);

        [Test]
        public void Contour_FlatRectangle_OneFourCornerOuterRing()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Contours.ChartCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            Assert.That(p.Contours.RingSignedArea2[0], Is.GreaterThan(Int128.Zero));
            Assert.That(RingVertexCount(p.Contours, 0), Is.EqualTo(4));
            AssertRingCorners(p.Contours, 0, new[]
            {
                (0, 0), (100, 0), (100, 100), (0, 100)
            });
        }

        [Test]
        public void Contour_RasterAnnulus_OneOuterAndOneContainedHole()
        {
            // Strict corpus: eight 100cm cell quads around an empty center on a 3x3 grid.
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Regions.RegionCount, Is.EqualTo(1));
            Assert.That(p.Contours.ChartCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingCount, Is.EqualTo(2));
            Assert.That(p.Contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            Assert.That(p.Contours.RingKinds[1], Is.EqualTo(LayeredSpanContourRingKind.Hole));
            Assert.That(p.Contours.RingSignedArea2[0], Is.GreaterThan(Int128.Zero));
            Assert.That(p.Contours.RingSignedArea2[1], Is.LessThan(Int128.Zero));
            Assert.That(HoleContainedByExactlyOneOuter(p.Contours, holeRing: 1), Is.True);
        }

        [Test]
        public void Contour_ArbitraryAnnulusTriangleSoup_DoesNotWeakenRasterHoleContract()
        {
            // Residual: arbitrary annulus soup may fragment; this test only records determinism and
            // must not soften Contour_RasterAnnulus_OneOuterAndOneContainedHole.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 300, 300, 0,
                    100, 200, 200, 100
                },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[]
                {
                    0, 0, 300, 300,
                    100, 100, 200, 200
                },
                triA: new[] { 0, 1, 1, 2, 2, 3, 3, 0 },
                triB: new[] { 1, 5, 2, 6, 3, 7, 0, 4 },
                triC: new[] { 4, 4, 5, 5, 6, 6, 7, 7 },
                triAreaIds: new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                triFlags: new[]
                {
                    FloorFlags, FloorFlags, FloorFlags, FloorFlags,
                    FloorFlags, FloorFlags, FloorFlags, FloorFlags
                });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            PipelineResult a = RunPipeline(surface, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            PipelineResult b = RunPipeline(surface, new[] { 7, 6, 5, 4, 3, 2, 1, 0 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(PublicChecksum(a), Is.EqualTo(PublicChecksum(b)));
            Assert.That(a.Contours.HasPublishedContent, Is.True);
        }

        [Test]
        public void Contour_TranslatedExtremeOrigins_RelativeRingsAndArea2Match()
        {
            PipelineResult nearMin = RunRectangleAtOrigin(int.MinValue + 1_000, int.MinValue + 2_000);
            PipelineResult nearMax = RunRectangleAtOrigin(int.MaxValue - 1_200, int.MaxValue - 1_100);
            AssertRelativeRingChannelsEqual(nearMin.Contours, nearMax.Contours, nearMin.Contours.VertexXcm[0], nearMin.Contours.VertexZcm[0], nearMax.Contours.VertexXcm[0], nearMax.Contours.VertexZcm[0]);
            Assert.That(nearMin.Contours.RingSignedArea2[0], Is.EqualTo(nearMax.Contours.RingSignedArea2[0]));
            Assert.That(nearMin.Contours.RingSignedArea2[0], Is.GreaterThan(Int128.Zero));
        }

        [Test]
        public void Contour_VeryLargeMaxError_DoesNotOverflow()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: int.MaxValue);
            Assert.That(p.Contours.HasPublishedContent, Is.True);
            Assert.That(p.Contours.RingCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingSignedArea2[0], Is.GreaterThan(Int128.Zero));
        }

        [Test]
        public void Contour_NonadjacentCollinearOverlap_RejectedByFormalValidator()
        {
            // Production rings from cell sides cannot emit this; exercise the inclusive validator hook.
            // Nonadjacent edges (0,50)-(100,50) and (80,50)-(20,50) overlap on the same line.
            int[] x = { 0, 100, 100, 0, 0, 100, 80, 20 };
            int[] z = { 0, 0, 100, 100, 50, 50, 50, 50 };
            Assert.That(
                LayeredSpanContourBuilder.RingHasInclusiveSelfIntersectionForTests(x, z),
                Is.True);
        }

        [Test]
        public void Contour_HoleTouchingOuter_RejectedAndOutputCleared()
        {
            var scratch = CreateContourScratch();
            scratch.Prepare();
            Span<int> x = scratch.MutableVertexXcm;
            Span<int> z = scratch.MutableVertexZcm;
            Span<int> offsets = scratch.MutableRingOffsets;
            Span<LayeredSpanContourRingKind> kinds = scratch.MutableRingKinds;

            // Outer 0..300 square; hole shares the left edge (touching).
            x[0] = 0; z[0] = 0;
            x[1] = 300; z[1] = 0;
            x[2] = 300; z[2] = 300;
            x[3] = 0; z[3] = 300;
            x[4] = 0; z[4] = 100;
            x[5] = 100; z[5] = 100;
            x[6] = 100; z[6] = 200;
            x[7] = 0; z[7] = 200;
            offsets[0] = 0;
            offsets[1] = 4;
            offsets[2] = 8;
            kinds[0] = LayeredSpanContourRingKind.Outer;
            kinds[1] = LayeredSpanContourRingKind.Hole;
            scratch.SetRingCount(2);
            scratch.SetVertexCount(8);
            scratch.SetChartCount(1);
            scratch.MutableChartRingOffsets[0] = 0;
            scratch.MutableChartRingOffsets[1] = 2;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanContourBuilder.ValidateChartTopologyForTests(scratch, 0, 2, 8));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanContourBuilder"));
            Assert.That(scratch.HasPublishedContent, Is.False);
            Assert.That(scratch.RingCount, Is.EqualTo(0));
            Assert.That(scratch.VertexCount, Is.EqualTo(0));
        }

        [Test]
        public void Contour_OverlappingYLayers_SeparateChartsAndRings()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 0, 0, 0, 500, 500, 500, 500 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triAreaIds: new byte[] { 1, 1, 2, 2 },
                triStableIds: new[] { 10, 20, 30, 40 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Regions.RegionCount, Is.EqualTo(2));
            Assert.That(p.Contours.ChartCount, Is.EqualTo(2));
            Assert.That(p.Contours.RingCount, Is.EqualTo(2));
            Assert.That(p.Contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            Assert.That(p.Contours.RingKinds[1], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            Assert.That(p.Contours.ChartMinSpanIndices[0], Is.LessThan(p.Contours.ChartMinSpanIndices[1]));
        }

        [Test]
        public void Contour_ReconnectDuplicateColumn_SplitsChartsAndEmitsSeam()
        {
            // Two floors in column 0 at climb-linked heights, bridged through column 1 so region
            // connectivity reunites them while duplicate-column chart union must leave a seam.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 100, 0, 100,
                    100, 200, 100, 200,
                    0, 100, 0, 100
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    40, 40, 40, 40,
                    80, 80, 80, 80
                },
                vertexZcm: new[]
                {
                    0, 0, 100, 100,
                    0, 0, 100, 100,
                    0, 0, 100, 100
                },
                triA: new[] { 0, 1, 4, 5, 8, 9 },
                triB: new[] { 1, 3, 5, 7, 9, 11 },
                triC: new[] { 2, 2, 6, 6, 10, 10 },
                triAreaIds: new byte[] { 1, 1, 1, 1, 1, 1 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1, 2, 3, 4, 5 }, grid, maxClimbCm: 50, maxErrorCm: 0);

            Assert.That(p.Contours.ChartCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(p.Contours.SeamCount, Is.GreaterThan(0));
            Assert.That(p.Contours.ChartMinSpanIndices[0], Is.LessThan(p.Contours.ChartMinSpanIndices[1]));
            // Duplicate-column rule: vertically stacked same-region surfaces must not share one chart.
            Assert.That(p.Contours.ChartCount, Is.GreaterThanOrEqualTo(p.Regions.RegionCount == 1 ? 2 : 2));
        }

        [Test]
        public void Contour_StackedSameColumnFloors_RemainSeparateCharts()
        {
            // Two vertically stacked floors share every XZ column. Chart union must keep them as
            // distinct charts even when climb links reunite them into one region via a side ramp.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 200, 0, 200,
                    200, 300, 200, 300,
                    0, 200, 0, 200
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    40, 40, 40, 40,
                    80, 80, 80, 80
                },
                vertexZcm: new[]
                {
                    0, 0, 100, 100,
                    0, 0, 100, 100,
                    0, 0, 100, 100
                },
                triA: new[] { 0, 1, 4, 5, 8, 9 },
                triB: new[] { 1, 3, 5, 7, 9, 11 },
                triC: new[] { 2, 2, 6, 6, 10, 10 },
                triAreaIds: new byte[] { 1, 1, 1, 1, 1, 1 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1, 2, 3, 4, 5 }, grid, maxClimbCm: 50, maxErrorCm: 0);

            Assert.That(p.Contours.ChartCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(p.Contours.SeamCount, Is.GreaterThan(0));
            Assert.That(p.Contours.ChartMinSpanIndices[0], Is.LessThan(p.Contours.ChartMinSpanIndices[1]));
            Assert.That(p.Contours.RingCount, Is.GreaterThanOrEqualTo(2));
            for (int i = 0; i < p.Contours.RingCount; i++)
            {
                Assert.That(p.Contours.RingKinds[i], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            }
        }

        [Test]
        public void Contour_FlatSixtyFourCellTileAtNonzeroOrigin_WithHalo_RemainsCorrect()
        {
            const int cell = 100;
            const int cells = 64;
            const int halo = 2;
            int tileCm = cells * cell;
            int chunkX = 28;
            int chunkZ = 28;
            int originX = chunkX * tileCm;
            int originZ = chunkZ * tileCm;

            NavTriangleSurfaceSnapshot surface = FlatChunksSurface(chunkX, chunkZ, tileCm, haloChunks: 1);
            int cols = cells + (2 * halo);
            var grid = new LayeredSpanRasterGridSpec(originX - (halo * cell), originZ - (halo * cell), cell, cols, cols);
            var contourSpec = new LayeredSpanContourSpec(0, originX, originZ, originX + tileCm, originZ + tileCm);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunLargePipeline(surface, indices, grid, maxClimbCm: 40, contourSpec);

            Assert.That(p.Contours.HasPublishedContent, Is.True);
            Assert.That(p.Contours.ChartCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            Assert.That(RingVertexCount(p.Contours, 0), Is.EqualTo(256));
            AssertRingCorners(p.Contours, 0, new[]
            {
                (originX, originZ),
                (originX + tileCm, originZ),
                (originX + tileCm, originZ + tileCm),
                (originX, originZ + tileCm)
            });
        }

        [Test]
        public void Contour_FlatSixtyFourCellTile_RepeatedBuilds_StayUnderTimingBudget()
        {
            // Guard both growth rate and absolute warmed cost for the former quadratic scans.
            double best16 = MeasureWarmedFlatContourBestMs(cells: 16, iterations: 5, out double avg16);
            double best64 = MeasureWarmedFlatContourBestMs(cells: 64, iterations: 5, out double avg64);

            // Sheet count scales roughly 11.6x with halo=2. Quadratic work grows far faster;
            // column-indexed bleed stays near-linear. Generous multiplier rejects O(n^2) only.
            Assert.That(
                best64,
                Is.LessThan(best16 * 40.0),
                $"Flat64/Flat16 contour scale must stay near-linear after column-index bleed; " +
                $"best16={best16:F1} ms, best64={best64:F1} ms (ratio={(best16 <= 0 ? 0 : best64 / best16):F1}).");

            // The prior 193 ms/tile implementation must fail this guard.
            Assert.That(
                best64,
                Is.LessThan(160.0),
                $"Flat64 contour Build must stay below 160 ms after removing quadratic scans; " +
                $"best={best64:F1} ms, avg={avg64:F1} ms.");
            TestContext.WriteLine(
                $"Flat contour timing: 16-cell best={best16:F1} ms (avg={avg16:F1}); " +
                $"64-cell best={best64:F1} ms/Build (avg={avg64:F1} ms/Build).");
        }

        private static double MeasureWarmedFlatContourBestMs(
            int cells,
            int iterations,
            out double avgMs)
        {
            const int cell = 100;
            const int halo = 2;
            int tileCm = cells * cell;
            int chunkX = 12;
            int chunkZ = 7;
            int originX = chunkX * tileCm;
            int originZ = chunkZ * tileCm;

            NavTriangleSurfaceSnapshot surface = FlatChunksSurface(chunkX, chunkZ, tileCm, haloChunks: 1);
            int cols = cells + (2 * halo);
            var grid = new LayeredSpanRasterGridSpec(originX - (halo * cell), originZ - (halo * cell), cell, cols, cols);
            var contourSpec = new LayeredSpanContourSpec(0, originX, originZ, originX + tileCm, originZ + tileCm);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            // Size scratch to the raster, not a padded 16k bank — keeps the timed path cache-honest.
            int spanCap = checked(grid.ColumnCount * 2);
            var raw = new LayeredSpanScratch(grid.ColumnCount, spanCap);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, spanCap, spanCap);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, spanCap);
            var links = new LayeredSpanWalkLinkScratch(spanCap, spanCap * 4);
            var radius = new LayeredSpanRadiusFieldScratch(spanCap, spanCap, spanCap * 4);
            var regions = new LayeredSpanRegionScratch(spanCap, 64);
            var contours = new LayeredSpanContourScratch(
                grid.ColumnCount,
                spanCap,
                spanCap,
                chartCapacity: 8,
                ringCapacity: 8,
                vertexCapacity: 1024,
                edgeCapacity: 4096,
                seamCapacity: 64,
                portalIntervalCapacity: spanCap * 4,
                canonicalLinkCapacity: spanCap * 4,
                splitPointCapacity: 64);
            var linkSpec = new LayeredSpanWalkLinkSpec(40);

            LayeredSpanRasterizer.Rasterize(surface, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in DefaultWalk, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
            LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
            LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);
            for (int warm = 0; warm < 3; warm++)
            {
                LayeredSpanContourBuilder.Build(
                    raw, walk, sheets, links, radius, regions, in grid, in contourSpec, contours);
            }

            double bestMs = double.MaxValue;
            double totalMs = 0;
            int expectVerts = cells * 4;
            for (int i = 0; i < iterations; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LayeredSpanContourBuilder.Build(
                    raw, walk, sheets, links, radius, regions, in grid, in contourSpec, contours);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                totalMs += ms;
                if (ms < bestMs)
                {
                    bestMs = ms;
                }

                Assert.That(contours.ChartCount, Is.EqualTo(1));
                Assert.That(RingVertexCount(contours, 0), Is.EqualTo(expectVerts));
            }

            avgMs = totalMs / iterations;
            return bestMs;
        }

        [Test]
        public void Contour_SparseHighColumn_WithColumnCapacityAboveSheetCapacity_Succeeds()
        {
            // Eligible sheet lives in a high raster column id while sheetCapacity stays small.
            // columnCapacity is authored separately and must index that column without aliasing sheet SoA.
            const int cell = 100;
            const int countX = 32;
            const int countZ = 8;
            int highColX = countX - 1;
            int highColZ = countZ - 1;
            int minX = highColX * cell;
            int minZ = highColZ * cell;
            var surface = QuadFloor(minX, minZ, minX + cell, minZ + cell, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, cell, countX, countZ);
            Assert.That(grid.ColumnCount, Is.EqualTo(countX * countZ));
            Assert.That(grid.ColumnCount, Is.GreaterThan(8), "sparse case requires columnCapacity > sheetCapacity");

            const int sheetCap = 8;
            var raw = new LayeredSpanScratch(grid.ColumnCount, 32);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 32, 32);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 32);
            var links = new LayeredSpanWalkLinkScratch(32, 64);
            var radius = new LayeredSpanRadiusFieldScratch(32, sheetCap, 64);
            var regions = new LayeredSpanRegionScratch(32, 16);
            var contours = new LayeredSpanContourScratch(
                columnCapacity: grid.ColumnCount,
                spanCapacity: 32,
                sheetCapacity: sheetCap,
                chartCapacity: 8,
                ringCapacity: 8,
                vertexCapacity: 64,
                edgeCapacity: 64,
                seamCapacity: 16,
                portalIntervalCapacity: 64,
                canonicalLinkCapacity: 64,
                splitPointCapacity: 16);
            Assert.That(contours.ColumnCapacity, Is.GreaterThan(contours.SheetCapacity));

            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            var contourSpec = FullTarget(grid, 0);
            RunOnce(
                surface,
                new[] { 0, 1 },
                in grid,
                DefaultWalk,
                linkSpec,
                contourSpec,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours);

            Assert.That(contours.HasPublishedContent, Is.True);
            Assert.That(contours.ChartCount, Is.EqualTo(1));
            Assert.That(contours.RingCount, Is.EqualTo(1));
            Assert.That(contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            AssertRingCorners(contours, 0, new[]
            {
                (minX, minZ),
                (minX + cell, minZ),
                (minX + cell, minZ + cell),
                (minX, minZ + cell)
            });
        }

        [Test]
        public void Contour_AreaBoundary_SplitsChartsAndPreservesAreaIds()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triAreaIds: new byte[] { 1, 1, 2, 2 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Contours.ChartCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(p.Contours.RingCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(p.Contours.SeamCount, Is.GreaterThan(0));

            bool sawArea1 = false;
            bool sawArea2 = false;
            for (int i = 0; i < p.Contours.ChartCount; i++)
            {
                if (p.Contours.ChartAreaIds[i] == 1) sawArea1 = true;
                if (p.Contours.ChartAreaIds[i] == 2) sawArea2 = true;
            }

            Assert.That(sawArea1, Is.True);
            Assert.That(sawArea2, Is.True);

            for (int i = 0; i < p.Contours.RingCount; i++)
            {
                Assert.That(p.Contours.RingAreaIds[i], Is.EqualTo(p.Contours.ChartAreaIds[p.Contours.RingChartIds[i]]));
            }
        }

        [Test]
        public void Contour_TargetClip_ExcludesHaloAndMarksBorderMandatory()
        {
            // 3 columns; target covers only the middle cell.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 300, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            var contourSpec = new LayeredSpanContourSpec(
                maxSimplificationErrorCm: 0,
                targetMinXcm: 100,
                targetMinZcm: 0,
                targetMaxXcm: 200,
                targetMaxZcm: 100);
            PipelineResult p = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0, contourSpec);

            Assert.That(p.Contours.RingCount, Is.EqualTo(1));
            Assert.That(RingVertexCount(p.Contours, 0), Is.GreaterThanOrEqualTo(4));
            AssertRingCorners(p.Contours, 0, new[]
            {
                (100, 0), (200, 0), (200, 100), (100, 100)
            });

            int start = p.Contours.RingOffsets[0];
            int end = p.Contours.RingOffsets[1];
            for (int i = start; i < end; i++)
            {
                int x = p.Contours.VertexXcm[i];
                int z = p.Contours.VertexZcm[i];
                Assert.That(x, Is.GreaterThanOrEqualTo(100));
                Assert.That(x, Is.LessThanOrEqualTo(200));
                Assert.That(z, Is.GreaterThanOrEqualTo(0));
                Assert.That(z, Is.LessThanOrEqualTo(100));
                if (x == 100 || x == 200 || z == 0 || z == 100)
                {
                    Assert.That(p.Contours.VertexMandatory[i], Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void Contour_Simplification_PreservesMandatorySeamAndBorderEndpoints()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triAreaIds: new byte[] { 1, 1, 2, 2 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var zero = new LayeredSpanContourSpec(0, 0, 0, 200, 100);
            var err = new LayeredSpanContourSpec(50, 0, 0, 200, 100);
            PipelineResult exact = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, zero);
            PipelineResult simplified = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, err);

            Assert.That(simplified.Contours.SeamCount, Is.EqualTo(exact.Contours.SeamCount));
            Assert.That(simplified.Contours.ChartCount, Is.EqualTo(exact.Contours.ChartCount));

            // Every seam portal endpoint that lies on a published ring remains present.
            for (int s = 0; s < exact.Contours.SeamCount; s++)
            {
                int min = exact.Contours.SeamPortalMinAlongCm[s];
                int max = exact.Contours.SeamPortalMaxAlongCm[s];
                var dir = exact.Contours.SeamDirections[s];
                Assert.That(RingContainsPortalEndpoint(simplified.Contours, dir, min), Is.True);
                Assert.That(RingContainsPortalEndpoint(simplified.Contours, dir, max), Is.True);
            }
        }

        [Test]
        public void Contour_CapacityAndProvenanceFailures_ClearOutput()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(p.Contours.HasPublishedContent, Is.True);
            Assert.That(p.Contours.RingCount, Is.GreaterThan(0));

            var tiny = new LayeredSpanContourScratch(
                columnCapacity: grid.ColumnCount,
                spanCapacity: 64,
                sheetCapacity: 64,
                chartCapacity: 0,
                ringCapacity: 64,
                vertexCapacity: 64,
                edgeCapacity: 64,
                seamCapacity: 64,
                portalIntervalCapacity: 64,
                canonicalLinkCapacity: 64,
                splitPointCapacity: 64);
            var spec = FullTarget(grid, 0);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanContourBuilder.Build(
                    p.Raw, p.Walkability, p.Sheets, p.Links, p.Radius, p.Regions, in grid, in spec, tiny));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanContourScratch.chartCapacity"));
            Assert.That(tiny.HasPublishedContent, Is.False);
            Assert.That(tiny.RingCount, Is.EqualTo(0));
            Assert.That(tiny.VertexCount, Is.EqualTo(0));
            Assert.That(tiny.SeamCount, Is.EqualTo(0));

            // Stale same-count provenance: rebuild raw, keep old region chain.
            var raw2 = new LayeredSpanScratch(grid.ColumnCount, 64);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in grid, raw2);
            Assert.That(raw2.SpanCount, Is.EqualTo(p.Raw.SpanCount));
            Assert.That(p.Regions.WasBuiltFrom(raw2, p.Walkability, p.Sheets, p.Links, p.Radius), Is.False);

            var out2 = CreateContourScratch();
            ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanContourBuilder.Build(
                    raw2, p.Walkability, p.Sheets, p.Links, p.Radius, p.Regions, in grid, in spec, out2));
            Assert.That(ex!.Message, Does.Contain("identity and content generation"));
            Assert.That(out2.HasPublishedContent, Is.False);
            Assert.That(out2.RingCount, Is.EqualTo(0));
        }

        [Test]
        public void Contour_ShuffledTriangleOrder_StableIdsYieldIdenticalPublicChannels()
        {
            var forward = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triAreaIds: new byte[] { 1, 1, 1, 1 },
                triStableIds: new[] { 10, 20, 30, 40 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var reversed = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 5, 4, 1, 0 },
                triB: new[] { 7, 5, 3, 1 },
                triC: new[] { 6, 6, 2, 2 },
                triAreaIds: new byte[] { 1, 1, 1, 1 },
                triStableIds: new[] { 40, 30, 20, 10 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult a = RunPipeline(forward, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            PipelineResult b = RunPipeline(reversed, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(PublicChecksum(a), Is.EqualTo(PublicChecksum(b)));
        }

        [Test]
        public void Contour_WarmedFullChain_AllocatesZeroBytes()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triAreaIds: new byte[] { 1, 1, 2, 2 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(4, 32);
            var walk = new LayeredSpanWalkabilityScratch(4, 32, 32);
            var sheets = new LayeredSpanSurfaceSheetScratch(4, 32);
            var links = new LayeredSpanWalkLinkScratch(32, 64);
            var radius = new LayeredSpanRadiusFieldScratch(32, 32, 64);
            var regions = new LayeredSpanRegionScratch(32, 32);
            var contours = CreateContourScratch();
            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            var contourSpec = FullTarget(grid, 0);
            int[] indices = { 0, 1, 2, 3 };

            for (int i = 0; i < 64; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, contourSpec, raw, walk, sheets, links, radius, regions, contours);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, contourSpec, raw, walk, sheets, links, radius, regions, contours);
                if (contours.RingCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0),
                $"Warmed layered-span chain through contour allocated {allocated} bytes.");
            Assert.That(contours.HasPublishedContent, Is.True);
            Assert.That(contours.RingCount, Is.GreaterThan(0));
        }

        private static NavTriangleSurfaceSnapshot RasterAnnulusEightQuads()
        {
            var vx = new int[32];
            var vy = new int[32];
            var vz = new int[32];
            var a = new int[16];
            var b = new int[16];
            var c = new int[16];
            var areas = new byte[16];
            var stables = new int[16];
            var flags = new NavTriangleSurfaceFlags[16];
            int cell = 0;
            int stable = 1;
            for (int cz = 0; cz < 3; cz++)
            {
                for (int cx = 0; cx < 3; cx++)
                {
                    if (cx == 1 && cz == 1)
                    {
                        continue;
                    }

                    int minX = cx * 100;
                    int minZ = cz * 100;
                    int maxX = minX + 100;
                    int maxZ = minZ + 100;
                    int v = cell * 4;
                    vx[v] = minX; vy[v] = 0; vz[v] = minZ;
                    vx[v + 1] = maxX; vy[v + 1] = 0; vz[v + 1] = minZ;
                    vx[v + 2] = minX; vy[v + 2] = 0; vz[v + 2] = maxZ;
                    vx[v + 3] = maxX; vy[v + 3] = 0; vz[v + 3] = maxZ;
                    int t = cell * 2;
                    a[t] = v;
                    b[t] = v + 1;
                    c[t] = v + 2;
                    a[t + 1] = v + 1;
                    b[t + 1] = v + 3;
                    c[t + 1] = v + 2;
                    areas[t] = 1;
                    areas[t + 1] = 1;
                    stables[t] = stable++;
                    stables[t + 1] = stable++;
                    flags[t] = FloorFlags;
                    flags[t + 1] = FloorFlags;
                    cell++;
                }
            }

            return new NavTriangleSurfaceSnapshot(
                vertexXcm: vx,
                vertexYcm: vy,
                vertexZcm: vz,
                triA: a,
                triB: b,
                triC: c,
                triAreaIds: areas,
                triStableIds: stables,
                triFlags: flags);
        }

        private static NavTriangleSurfaceSnapshot QuadFloor(int minX, int minZ, int maxX, int maxZ, int y, byte area, int stable)
        {
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { minX, maxX, minX, maxX },
                vertexYcm: new[] { y, y, y, y },
                vertexZcm: new[] { minZ, minZ, maxZ, maxZ },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new[] { area, area },
                triStableIds: new[] { stable, stable + 1 },
                triFlags: new[] { FloorFlags, FloorFlags });
        }

        private static PipelineResult RunRectangleAtOrigin(int originX, int originZ)
        {
            var surface = QuadFloor(originX, originZ, originX + 100, originZ + 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(originX, originZ, 100, 1, 1);
            return RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);
        }

        private static void AssertRelativeRingChannelsEqual(
            LayeredSpanContourScratch a,
            LayeredSpanContourScratch b,
            int originAX,
            int originAZ,
            int originBX,
            int originBZ)
        {
            Assert.That(a.RingCount, Is.EqualTo(b.RingCount));
            Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That((long)a.VertexXcm[i] - originAX, Is.EqualTo((long)b.VertexXcm[i] - originBX));
                Assert.That((long)a.VertexZcm[i] - originAZ, Is.EqualTo((long)b.VertexZcm[i] - originBZ));
                Assert.That(a.VertexMandatory[i], Is.EqualTo(b.VertexMandatory[i]));
            }

            for (int i = 0; i < a.RingCount; i++)
            {
                Assert.That(a.RingKinds[i], Is.EqualTo(b.RingKinds[i]));
                Assert.That(a.RingSignedArea2[i], Is.EqualTo(b.RingSignedArea2[i]));
            }
        }

        private static long PublicChecksum(in PipelineResult p)
        {
            long sum = p.Contours.ChartCount;
            sum = (sum * 1_000_003L) + p.Contours.RingCount;
            sum = (sum * 1_000_003L) + p.Contours.VertexCount;
            sum = (sum * 1_000_003L) + p.Contours.SeamCount;
            for (int i = 0; i < p.Contours.ChartCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Contours.ChartRegionIds[i];
                sum = (sum * 1_000_003L) + p.Contours.ChartAreaIds[i];
                int minSpan = p.Contours.ChartMinSpanIndices[i];
                sum = (sum * 1_000_003L) + p.Raw.SpanStableTriangleIds[minSpan];
            }

            for (int i = 0; i < p.Contours.RingCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Contours.RingChartIds[i];
                sum = (sum * 1_000_003L) + p.Contours.RingRegionIds[i];
                sum = (sum * 1_000_003L) + p.Contours.RingAreaIds[i];
                Int128 area2 = p.Contours.RingSignedArea2[i];
                sum = (sum * 1_000_003L) + (long)(area2 & (Int128)ulong.MaxValue);
                sum = (sum * 1_000_003L) + (long)((area2 >> 64) & (Int128)ulong.MaxValue);
                sum = (sum * 1_000_003L) + (byte)p.Contours.RingKinds[i];
            }

            for (int i = 0; i < p.Contours.VertexCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Contours.VertexXcm[i];
                sum = (sum * 1_000_003L) + p.Contours.VertexZcm[i];
                int span = p.Contours.VertexSourceSpanIndices[i];
                sum = (sum * 1_000_003L) + p.Raw.SpanStableTriangleIds[span];
                sum = (sum * 1_000_003L) + p.Contours.VertexMandatory[i];
            }

            for (int i = 0; i < p.Contours.SeamCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Contours.SeamChartA[i];
                sum = (sum * 1_000_003L) + p.Contours.SeamChartB[i];
                sum = (sum * 1_000_003L) + (byte)p.Contours.SeamDirections[i];
                sum = (sum * 1_000_003L) + p.Contours.SeamPortalMinAlongCm[i];
                sum = (sum * 1_000_003L) + p.Contours.SeamPortalMaxAlongCm[i];
                sum = (sum * 1_000_003L) + p.Raw.SpanStableTriangleIds[p.Contours.SeamSpanA[i]];
                sum = (sum * 1_000_003L) + p.Raw.SpanStableTriangleIds[p.Contours.SeamSpanB[i]];
            }

            return sum;
        }

        private static void AssertRingCorners(
            LayeredSpanContourScratch contours,
            int ring,
            (int x, int z)[] expectedUnordered)
        {
            int start = contours.RingOffsets[ring];
            int count = RingVertexCount(contours, ring);
            Assert.That(count, Is.GreaterThanOrEqualTo(expectedUnordered.Length));
            for (int e = 0; e < expectedUnordered.Length; e++)
            {
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    if (contours.VertexXcm[start + i] == expectedUnordered[e].x &&
                        contours.VertexZcm[start + i] == expectedUnordered[e].z)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.That(found, Is.True, $"Missing corner ({expectedUnordered[e].x},{expectedUnordered[e].z}).");
            }
        }

        private static bool HoleContainedByExactlyOneOuter(LayeredSpanContourScratch contours, int holeRing)
        {
            int containing = 0;
            int hStart = contours.RingOffsets[holeRing];
            int hCount = RingVertexCount(contours, holeRing);
            for (int o = 0; o < contours.RingCount; o++)
            {
                if (contours.RingKinds[o] != LayeredSpanContourRingKind.Outer)
                {
                    continue;
                }

                if (AllHoleVerticesStrictlyInsideOuter(contours, hStart, hCount, o))
                {
                    containing++;
                }
            }

            return containing == 1;
        }

        private static bool AllHoleVerticesStrictlyInsideOuter(
            LayeredSpanContourScratch contours,
            int hStart,
            int hCount,
            int outerRing)
        {
            int oStart = contours.RingOffsets[outerRing];
            int oCount = RingVertexCount(contours, outerRing);
            for (int i = 0; i < hCount; i++)
            {
                if (!PointInRingStrict(
                        contours.VertexXcm[hStart + i],
                        contours.VertexZcm[hStart + i],
                        contours,
                        oStart,
                        oCount))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PointInRingStrict(
            int px,
            int pz,
            LayeredSpanContourScratch contours,
            int start,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                int xi = contours.VertexXcm[start + i];
                int zi = contours.VertexZcm[start + i];
                int xj = contours.VertexXcm[start + j];
                int zj = contours.VertexZcm[start + j];
                if (PointOnSegment(xi, zi, xj, zj, px, pz))
                {
                    return false;
                }
            }

            bool inside = false;
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                int xi = contours.VertexXcm[start + i];
                int zi = contours.VertexZcm[start + i];
                int xj = contours.VertexXcm[start + j];
                int zj = contours.VertexZcm[start + j];
                if ((zi > pz) != (zj > pz))
                {
                    Int128 lhs = ((Int128)pz - zi) * ((Int128)xj - xi);
                    Int128 rhs = ((Int128)px - xi) * ((Int128)zj - zi);
                    bool crossLeft = zj > zi ? lhs > rhs : lhs < rhs;
                    if (crossLeft)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static bool PointOnSegment(int ax, int az, int bx, int bz, int px, int pz)
        {
            Int128 cross = (((Int128)bx - ax) * ((Int128)pz - az)) - (((Int128)bz - az) * ((Int128)px - ax));
            if (cross != 0)
            {
                return false;
            }

            return px >= Math.Min(ax, bx) &&
                   px <= Math.Max(ax, bx) &&
                   pz >= Math.Min(az, bz) &&
                   pz <= Math.Max(az, bz);
        }

        private static bool RingContainsPortalEndpoint(
            LayeredSpanContourScratch contours,
            LayeredSpanNeighborDirection dir,
            int along)
        {
            for (int i = 0; i < contours.VertexCount; i++)
            {
                int x = contours.VertexXcm[i];
                int z = contours.VertexZcm[i];
                if (dir == LayeredSpanNeighborDirection.West || dir == LayeredSpanNeighborDirection.East)
                {
                    if (z == along)
                    {
                        return true;
                    }
                }
                else if (x == along)
                {
                    return true;
                }
            }

            return false;
        }

        private static int RingVertexCount(LayeredSpanContourScratch contours, int ring)
            => contours.RingOffsets[ring + 1] - contours.RingOffsets[ring];

        private static NavTriangleSurfaceSnapshot FlatChunksSurface(
            int chunkX,
            int chunkZ,
            int tileCm,
            int haloChunks)
        {
            int chunkCount = (2 * haloChunks + 1) * (2 * haloChunks + 1);
            int vertCount = chunkCount * 4;
            int triCount = chunkCount * 2;
            var vx = new int[vertCount];
            var vy = new int[vertCount];
            var vz = new int[vertCount];
            var ta = new int[triCount];
            var tb = new int[triCount];
            var tc = new int[triCount];
            var areas = new byte[triCount];
            var stables = new int[triCount];
            var tflags = new NavTriangleSurfaceFlags[triCount];
            int v = 0;
            int t = 0;
            int stable = 1;
            for (int dz = -haloChunks; dz <= haloChunks; dz++)
            {
                for (int dx = -haloChunks; dx <= haloChunks; dx++)
                {
                    int cx = chunkX + dx;
                    int cz = chunkZ + dz;
                    int x0 = cx * tileCm;
                    int z0 = cz * tileCm;
                    int x1 = x0 + tileCm;
                    int z1 = z0 + tileCm;
                    int iSw = v++;
                    int iSe = v++;
                    int iNe = v++;
                    int iNw = v++;
                    vx[iSw] = x0; vy[iSw] = 0; vz[iSw] = z0;
                    vx[iSe] = x1; vy[iSe] = 0; vz[iSe] = z0;
                    vx[iNe] = x1; vy[iNe] = 0; vz[iNe] = z1;
                    vx[iNw] = x0; vy[iNw] = 0; vz[iNw] = z1;
                    ta[t] = iSw; tb[t] = iSe; tc[t] = iNw; areas[t] = 1; stables[t] = stable++; tflags[t] = FloorFlags; t++;
                    ta[t] = iSe; tb[t] = iNe; tc[t] = iNw; areas[t] = 1; stables[t] = stable++; tflags[t] = FloorFlags; t++;
                }
            }

            return new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, tflags);
        }

        private static LayeredSpanContourScratch CreateContourScratch(int columnCapacity = 128)
            => new(
                columnCapacity: columnCapacity,
                spanCapacity: 128,
                sheetCapacity: 128,
                chartCapacity: 64,
                ringCapacity: 64,
                vertexCapacity: 512,
                edgeCapacity: 512,
                seamCapacity: 128,
                portalIntervalCapacity: 128,
                canonicalLinkCapacity: 256,
                splitPointCapacity: 128);

        private static LayeredSpanContourSpec FullTarget(in LayeredSpanRasterGridSpec grid, int maxErrorCm)
            => new(
                maxErrorCm,
                grid.OriginXcm,
                grid.OriginZcm,
                grid.ColumnMaxXcm(grid.ColumnCountX - 1),
                grid.ColumnMaxZcm(grid.ColumnCountZ - 1));

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            int maxErrorCm)
            => RunPipeline(surface, indices, grid, maxClimbCm, FullTarget(grid, maxErrorCm));

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            in LayeredSpanContourSpec contourSpec)
        {
            var raw = new LayeredSpanScratch(grid.ColumnCount, 128);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 128, 128);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 128);
            var links = new LayeredSpanWalkLinkScratch(128, 256);
            var radius = new LayeredSpanRadiusFieldScratch(128, 128, 256);
            var regions = new LayeredSpanRegionScratch(128, 64);
            var contours = CreateContourScratch(grid.ColumnCount);
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(surface, indices, grid, DefaultWalk, linkSpec, contourSpec, raw, walk, sheets, links, radius, regions, contours);
            return new PipelineResult(raw, walk, sheets, links, radius, regions, contours);
        }

        private static PipelineResult RunLargePipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            in LayeredSpanContourSpec contourSpec)
        {
            const int spanCap = 16384;
            var raw = new LayeredSpanScratch(grid.ColumnCount, spanCap);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, spanCap, spanCap);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, spanCap);
            var links = new LayeredSpanWalkLinkScratch(spanCap, spanCap * 4);
            var radius = new LayeredSpanRadiusFieldScratch(spanCap, spanCap, spanCap * 4);
            var regions = new LayeredSpanRegionScratch(spanCap, 4096);
            var contours = new LayeredSpanContourScratch(
                grid.ColumnCount, spanCap, spanCap, 1024, 2048, 16384, 16384, 4096, spanCap * 4, spanCap * 4, 4096);
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(surface, indices, grid, DefaultWalk, linkSpec, contourSpec, raw, walk, sheets, links, radius, regions, contours);
            return new PipelineResult(raw, walk, sheets, links, radius, regions, contours);
        }

        private static void RunOnce(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkabilitySpec walkSpec,
            in LayeredSpanWalkLinkSpec linkSpec,
            in LayeredSpanContourSpec contourSpec,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walk,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours)
        {
            LayeredSpanRasterizer.Rasterize(surface, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in walkSpec, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in walkSpec, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
            LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
            LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);
            LayeredSpanContourBuilder.Build(
                raw, walk, sheets, links, radius, regions, in grid, in contourSpec, contours);
        }

        private readonly struct PipelineResult
        {
            public PipelineResult(
                LayeredSpanScratch raw,
                LayeredSpanWalkabilityScratch walkability,
                LayeredSpanSurfaceSheetScratch sheets,
                LayeredSpanWalkLinkScratch links,
                LayeredSpanRadiusFieldScratch radius,
                LayeredSpanRegionScratch regions,
                LayeredSpanContourScratch contours)
            {
                Raw = raw;
                Walkability = walkability;
                Sheets = sheets;
                Links = links;
                Radius = radius;
                Regions = regions;
                Contours = contours;
            }

            public LayeredSpanScratch Raw { get; }
            public LayeredSpanWalkabilityScratch Walkability { get; }
            public LayeredSpanSurfaceSheetScratch Sheets { get; }
            public LayeredSpanWalkLinkScratch Links { get; }
            public LayeredSpanRadiusFieldScratch Radius { get; }
            public LayeredSpanRegionScratch Regions { get; }
            public LayeredSpanContourScratch Contours { get; }
        }
    }
}
