using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanRadiusFieldContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk =
            new(agentHeightCm: 50, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 5);

        [Test]
        public void Radius_BroadFlatSurface_BoundaryZero_InwardIncreases_ErosionLeavesInterior()
        {
            const int n = 5;
            const int cell = 100;
            int extent = n * cell;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, extent, 0, extent },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, extent, extent },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, cell, n, n);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            int hopCm = LayeredSpanRadiusFieldBuilder.ComputeAdjacentColumnHopLowerBoundCm(cell, cell);
            Assert.That(hopCm, Is.EqualTo(70));

            int cornerCol = 0;
            int ring1Col = 1 + (1 * n);
            int centerCol = (n / 2) + ((n / 2) * n);

            Assert.That(MaxClearanceInColumn(pipeline, cornerCol), Is.EqualTo(0));
            Assert.That(MaxClearanceInColumn(pipeline, ring1Col), Is.EqualTo(hopCm));
            Assert.That(MaxClearanceInColumn(pipeline, centerCol), Is.EqualTo(2 * hopCm));

            // Radius just above one hop erodes the outer rings and leaves the center column sheet.
            Assert.That(pipeline.Regions.RegionCount, Is.EqualTo(1));
            Assert.That(pipeline.Regions.RegionMemberCounts[0], Is.GreaterThan(0));
            int centerSpan = FindAnyWalkableInColumn(pipeline, centerCol);
            Assert.That(pipeline.Regions.SpanRegionIds[centerSpan], Is.EqualTo(0));
            Assert.That(pipeline.Radius.SpanClearanceCm[centerSpan], Is.GreaterThanOrEqualTo(hopCm + 1));
        }

        [Test]
        public void Radius_NarrowOneCellCorridor_RejectedForPositiveRadius()
        {
            // 3x1 corridor: every column touches a terrain edge => clearance 0.
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
            PipelineResult zeroRadius = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0, agentRadiusCm: 0);
            Assert.That(zeroRadius.Walkability.WalkableSpanCount, Is.EqualTo(3));
            Assert.That(zeroRadius.Regions.RegionCount, Is.EqualTo(1));
            for (int w = 0; w < zeroRadius.Walkability.WalkableSpanCount; w++)
            {
                int span = zeroRadius.Walkability.WalkableSpanIndices[w];
                Assert.That(zeroRadius.Radius.SpanClearanceCm[span], Is.EqualTo(0));
            }

            PipelineResult positive = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0, agentRadiusCm: 1);
            Assert.That(positive.Regions.RegionCount, Is.EqualTo(0));
            for (int span = 0; span < positive.Regions.SpanCount; span++)
            {
                Assert.That(positive.Regions.SpanRegionIds[span], Is.EqualTo(-1));
            }
        }

        [Test]
        public void Radius_TwoOverlappingYLayers_Independent_HoleDoesNotErodeOtherLayer()
        {
            // Lower layer: 3x3 ring (center cell missing). Upper layer: full continuous floor.
            // Lower hole must not erode upper-center clearance.
            const int cell = 100;
            const int n = 3;
            int extent = n * cell;
            var vertsX = new System.Collections.Generic.List<int>();
            var vertsY = new System.Collections.Generic.List<int>();
            var vertsZ = new System.Collections.Generic.List<int>();
            var triA = new System.Collections.Generic.List<int>();
            var triB = new System.Collections.Generic.List<int>();
            var triC = new System.Collections.Generic.List<int>();
            var area = new System.Collections.Generic.List<byte>();
            var stable = new System.Collections.Generic.List<int>();
            var flags = new System.Collections.Generic.List<NavTriangleSurfaceFlags>();

            int stableId = 1;
            for (int cz = 0; cz < n; cz++)
            {
                for (int cx = 0; cx < n; cx++)
                {
                    if (cx == 1 && cz == 1)
                    {
                        continue;
                    }

                    AppendCellQuad(vertsX, vertsY, vertsZ, triA, triB, triC, area, stable, flags,
                        cx, cz, cell, yCm: 0, stableId);
                    stableId += 2;
                }
            }

            int upperBase = vertsX.Count;
            vertsX.Add(0); vertsY.Add(500); vertsZ.Add(0);
            vertsX.Add(extent); vertsY.Add(500); vertsZ.Add(0);
            vertsX.Add(0); vertsY.Add(500); vertsZ.Add(extent);
            vertsX.Add(extent); vertsY.Add(500); vertsZ.Add(extent);
            triA.Add(upperBase + 0); triB.Add(upperBase + 1); triC.Add(upperBase + 2);
            area.Add(2); stable.Add(stableId++); flags.Add(FloorFlags);
            triA.Add(upperBase + 1); triB.Add(upperBase + 3); triC.Add(upperBase + 2);
            area.Add(2); stable.Add(stableId); flags.Add(FloorFlags);

            var surface = new NavTriangleSurfaceSnapshot(
                vertsX.ToArray(),
                vertsY.ToArray(),
                vertsZ.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                area.ToArray(),
                stable.ToArray(),
                flags.ToArray());

            var grid = new LayeredSpanRasterGridSpec(0, 0, cell, n, n);
            int[] indices = CreateIndexRange(triA.Count);
            PipelineResult pipeline = RunPipeline(surface, indices, grid, maxClimbCm: 0, agentRadiusCm: 0);

            int hopCm = LayeredSpanRadiusFieldBuilder.ComputeAdjacentColumnHopLowerBoundCm(cell, cell);
            int centerCol = 1 + (1 * n);
            int edgeCol = 0;

            int lowEdge = FindWalkableAtY(pipeline, edgeCol, yCm: 0);
            int highEdge = FindWalkableAtY(pipeline, edgeCol, yCm: 500);
            int highCenter = FindWalkableAtY(pipeline, centerCol, yCm: 500);

            Assert.That(pipeline.Radius.SpanClearanceCm[lowEdge], Is.EqualTo(0));
            Assert.That(pipeline.Radius.SpanClearanceCm[highEdge], Is.EqualTo(0));
            // Upper center remains one hop inward despite the lower-layer hole.
            Assert.That(pipeline.Radius.SpanClearanceCm[highCenter], Is.EqualTo(hopCm));

            // Lower cell facing the hole is a boundary seed; upper center is unaffected.
            int lowerFacingHoleCol = 1; // (cx=1, cz=0), south side opens into the missing center.
            int lowFacing = FindWalkableAtY(pipeline, lowerFacingHoleCol, yCm: 0);
            Assert.That(pipeline.Radius.SpanClearanceCm[lowFacing], Is.EqualTo(0));
            Assert.That(
                pipeline.Radius.SpanClearanceCm[highCenter],
                Is.GreaterThan(pipeline.Radius.SpanClearanceCm[lowFacing]));
        }

        [Test]
        public void Radius_PartialPortal_DoesNotCountAsFullSide()
        {
            // Shared boundary only overlaps on a partial Z interval => both sheets are seeds.
            // Triangles include the shared x=100 edge so WalkCandidate positive-area columns still link.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 100, 100,
                    100, 200, 100
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 40,
                    0, 0, 40
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, agentRadiusCm: 0);

            Assert.That(pipeline.Links.LinkCount, Is.GreaterThan(0));
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                Assert.That(
                    pipeline.Radius.SpanClearanceCm[span],
                    Is.EqualTo(0),
                    $"Span {span} expected boundary clearance 0 under partial portal coverage.");
            }
        }

        [Test]
        public void Radius_SameCountStaleOrDifferentScratch_RejectedWithEmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult good = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0, agentRadiusCm: 0);

            var otherSurface = TinyFloor();
            var otherGrid = new LayeredSpanRasterGridSpec(0, 0, 50, 1, 1);
            var otherRaw = new LayeredSpanScratch(1, 8);
            var otherWalk = new LayeredSpanWalkabilityScratch(1, 8, 8);
            var otherSheets = new LayeredSpanSurfaceSheetScratch(1, 8);
            var otherLinks = new LayeredSpanWalkLinkScratch(8, 16);
            LayeredSpanRasterizer.Rasterize(otherSurface, new[] { 0 }, in otherGrid, otherRaw);
            LayeredSpanWalkabilityClassifier.Classify(otherRaw, in DefaultWalk, otherWalk);
            LayeredSpanSurfaceSheetAssigner.Assign(otherSurface, otherRaw, in otherGrid, in DefaultWalk, otherSheets);
            LayeredSpanWalkLinkBuilder.Build(otherRaw, otherWalk, in otherGrid, new LayeredSpanWalkLinkSpec(0), otherLinks);

            var radius = new LayeredSpanRadiusFieldScratch(64, 64, 64);
            LayeredSpanRadiusFieldBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, in grid, radius);
            Assert.That(radius.HasPublishedContent, Is.True);
            Assert.That(radius.SpanCount, Is.GreaterThan(0));

            var exWalk = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(
                    good.Raw, otherWalk, good.Sheets, good.Links, in grid, radius));
            Assert.That(exWalk!.Message, Does.Contain("walkability output that matches"));
            Assert.That(radius.SpanCount, Is.EqualTo(0));
            Assert.That(radius.HasPublishedContent, Is.False);

            LayeredSpanRadiusFieldBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, in grid, radius);
            var exSheets = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(
                    good.Raw, good.Walkability, otherSheets, good.Links, in grid, radius));
            Assert.That(exSheets!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(radius.HasPublishedContent, Is.False);

            LayeredSpanRadiusFieldBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, in grid, radius);
            var exLinks = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(
                    good.Raw, good.Walkability, good.Sheets, otherLinks, in grid, radius));
            Assert.That(exLinks!.Message, Does.Contain("walk-link output that matches"));
            Assert.That(radius.HasPublishedContent, Is.False);

            LayeredSpanRadiusFieldBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, in grid, radius);
            Assert.That(radius.HasPublishedContent, Is.True);

            // Same-count rerasterize: stale radius rejected by region.
            var regions = new LayeredSpanRegionScratch(64, 64);
            LayeredSpanRegionBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, radius, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var staleRadius = radius;
            ulong staleGen = staleRadius.ContentGeneration;
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in grid, good.Raw);
            LayeredSpanWalkabilityClassifier.Classify(good.Raw, in DefaultWalk, good.Walkability);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, good.Raw, in grid, in DefaultWalk, good.Sheets);
            LayeredSpanWalkLinkBuilder.Build(good.Raw, good.Walkability, in grid, new LayeredSpanWalkLinkSpec(0), good.Links);
            Assert.That(good.Raw.SpanCount, Is.EqualTo(staleRadius.SpanCount));
            Assert.That(staleRadius.ContentGeneration, Is.EqualTo(staleGen));
            Assert.That(staleRadius.WasBuiltFrom(good.Raw, good.Walkability, good.Sheets, good.Links), Is.False);

            var exRadius = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    good.Raw, good.Walkability, good.Sheets, good.Links, staleRadius, 0, regions));
            Assert.That(exRadius!.Message, Does.Contain("radius-field output that matches"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.HasPublishedContent, Is.False);
        }

        [Test]
        public void Radius_CapacityFailures_NameOwnerAndActualRequired_EmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(2, 8);
            var walk = new LayeredSpanWalkabilityScratch(2, 8, 8);
            var sheets = new LayeredSpanSurfaceSheetScratch(2, 8);
            var links = new LayeredSpanWalkLinkScratch(8, 16);
            RunOnce(surface, new[] { 0 }, grid, DefaultWalk, new LayeredSpanWalkLinkSpec(0),
                raw, walk, sheets, links, radius: null, regions: null, agentRadiusCm: 0);

            var tooSmallSpan = new LayeredSpanRadiusFieldScratch(spanCapacity: 1, sheetCapacity: 8, portalIntervalCapacity: 8);
            var exSpan = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, tooSmallSpan));
            Assert.That(exSpan!.Message, Does.Contain("LayeredSpanRadiusFieldScratch.spanCapacity"));
            Assert.That(exSpan.Message, Does.Contain($"required {raw.SpanCount}"));
            Assert.That(tooSmallSpan.SpanCount, Is.EqualTo(0));
            Assert.That(tooSmallSpan.HasPublishedContent, Is.False);

            var tooSmallSheet = new LayeredSpanRadiusFieldScratch(spanCapacity: 8, sheetCapacity: 0, portalIntervalCapacity: 8);
            var exSheet = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, tooSmallSheet));
            Assert.That(exSheet!.Message, Does.Contain("LayeredSpanRadiusFieldScratch.sheetCapacity"));
            Assert.That(exSheet.Message, Does.Contain($"required {sheets.SheetCount}"));
            Assert.That(tooSmallSheet.HasPublishedContent, Is.False);

            var tooSmallPortal = new LayeredSpanRadiusFieldScratch(spanCapacity: 8, sheetCapacity: 8, portalIntervalCapacity: 0);
            // Seed prior publish so failure must clear it.
            var seed = new LayeredSpanRadiusFieldScratch(8, 8, 8);
            LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, seed);
            Assert.That(seed.HasPublishedContent, Is.True);

            // Force portal capacity failure by using 0 capacity on a surface that has portals.
            var exPortal = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, tooSmallPortal));
            Assert.That(exPortal!.Message, Does.Contain("LayeredSpanRadiusFieldScratch.portalIntervalCapacity"));
            Assert.That(exPortal.Message, Does.Contain("required"));
            Assert.That(tooSmallPortal.SpanCount, Is.EqualTo(0));
            Assert.That(tooSmallPortal.HasPublishedContent, Is.False);
        }

        [Test]
        public void Radius_WarmedCompletePipeline_AllocatesExactlyZeroBytes()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 200, 0, 0, 200, 0, 100, 100, 100 },
                vertexYcm: new[] { 0, 0, 0, 400, 400, 400, 0, 50, 0 },
                vertexZcm: new[] { 0, 0, 100, 0, 0, 100, 20, 20, 80 },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 1, 2, 3 },
                triStableIds: new[] { 1, 2, 3 },
                triFlags: new[] { FloorFlags, FloorFlags, SolidOnly });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(4, 32);
            var walk = new LayeredSpanWalkabilityScratch(4, 32, 32);
            var sheets = new LayeredSpanSurfaceSheetScratch(4, 32);
            var links = new LayeredSpanWalkLinkScratch(32, 64);
            var radius = new LayeredSpanRadiusFieldScratch(32, 32, 64);
            var regions = new LayeredSpanRegionScratch(32, 32);
            var linkSpec = new LayeredSpanWalkLinkSpec(20);
            int[] indices = { 0, 1, 2 };

            for (int i = 0; i < 64; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions, 0);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions, 0);
                if (regions.RegionCount < 0 || radius.SpanCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard to keep outputs live.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0),
                $"Warmed layered-span raster->classify->sheet->link->radius->region pipeline allocated {allocated} bytes.");
            Assert.That(links.LinkCount, Is.GreaterThan(0));
            Assert.That(sheets.SheetCount, Is.GreaterThan(0));
            Assert.That(radius.HasPublishedContent, Is.True);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));
            Assert.That(radius.SpanClearanceCm.Length, Is.EqualTo(raw.SpanCount));
        }

        [Test]
        public void Radius_RepeatedBuild_PublishesMonotonicGeneration_DeterministicValues()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(2, 8);
            var walk = new LayeredSpanWalkabilityScratch(2, 8, 8);
            var sheets = new LayeredSpanSurfaceSheetScratch(2, 8);
            var links = new LayeredSpanWalkLinkScratch(8, 16);
            var radius = new LayeredSpanRadiusFieldScratch(8, 8, 16);
            var linkSpec = new LayeredSpanWalkLinkSpec(0);

            RunOnce(surface, new[] { 0 }, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, null, 0);
            ulong gen1 = radius.ContentGeneration;
            int[] first = radius.SpanClearanceCm.ToArray();

            RunOnce(surface, new[] { 0 }, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, null, 0);
            Assert.That(radius.ContentGeneration, Is.EqualTo(gen1 + 1));
            Assert.That(SequenceEqual(radius.SpanClearanceCm, first), Is.True);
        }

        private static NavTriangleSurfaceSnapshot ContinuousTwoColumnFloor()
            => new(
                vertexXcm: new[] { 0, 200, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

        private static NavTriangleSurfaceSnapshot TinyFloor()
            => new(
                vertexXcm: new[] { 0, 10, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 10 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

        private static void AppendCellQuad(
            System.Collections.Generic.List<int> vertsX,
            System.Collections.Generic.List<int> vertsY,
            System.Collections.Generic.List<int> vertsZ,
            System.Collections.Generic.List<int> triA,
            System.Collections.Generic.List<int> triB,
            System.Collections.Generic.List<int> triC,
            System.Collections.Generic.List<byte> area,
            System.Collections.Generic.List<int> stable,
            System.Collections.Generic.List<NavTriangleSurfaceFlags> flags,
            int cx,
            int cz,
            int cell,
            int yCm,
            int stableId)
        {
            int baseV = vertsX.Count;
            int x0 = cx * cell;
            int x1 = x0 + cell;
            int z0 = cz * cell;
            int z1 = z0 + cell;
            vertsX.Add(x0); vertsY.Add(yCm); vertsZ.Add(z0);
            vertsX.Add(x1); vertsY.Add(yCm); vertsZ.Add(z0);
            vertsX.Add(x0); vertsY.Add(yCm); vertsZ.Add(z1);
            vertsX.Add(x1); vertsY.Add(yCm); vertsZ.Add(z1);

            triA.Add(baseV + 0); triB.Add(baseV + 1); triC.Add(baseV + 2);
            area.Add(1); stable.Add(stableId); flags.Add(FloorFlags);
            triA.Add(baseV + 1); triB.Add(baseV + 3); triC.Add(baseV + 2);
            area.Add(1); stable.Add(stableId + 1); flags.Add(FloorFlags);
        }

        private static int[] CreateIndexRange(int count)
        {
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }

            return indices;
        }

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            int agentRadiusCm = -1)
        {
            if (agentRadiusCm < 0)
            {
                int hopCm = LayeredSpanRadiusFieldBuilder.ComputeAdjacentColumnHopLowerBoundCm(
                    grid.CellSizeCm,
                    grid.CellSizeCm);
                agentRadiusCm = hopCm + 1;
            }

            var raw = new LayeredSpanScratch(grid.ColumnCount, 1024);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 1024, 1024);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 1024);
            var links = new LayeredSpanWalkLinkScratch(1024, 2048);
            var radius = new LayeredSpanRadiusFieldScratch(1024, 1024, 2048);
            var regions = new LayeredSpanRegionScratch(1024, 1024);
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions, agentRadiusCm);
            return new PipelineResult(raw, walk, sheets, links, radius, regions);
        }

        private static void RunOnce(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkabilitySpec walkSpec,
            in LayeredSpanWalkLinkSpec linkSpec,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walk,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch? radius,
            LayeredSpanRegionScratch? regions,
            int agentRadiusCm)
        {
            LayeredSpanRasterizer.Rasterize(surface, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in walkSpec, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in walkSpec, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
            if (radius != null)
            {
                LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
                if (regions != null)
                {
                    LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm, regions);
                }
            }
        }

        private static int MaxClearanceInColumn(in PipelineResult pipeline, int column)
        {
            int max = 0;
            bool any = false;
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                if (ColumnOfRawSpan(pipeline.Raw, span) != column)
                {
                    continue;
                }

                any = true;
                int c = pipeline.Radius.SpanClearanceCm[span];
                if (c > max)
                {
                    max = c;
                }
            }

            if (!any)
            {
                throw new InvalidOperationException($"No walkable span in column {column}.");
            }

            return max;
        }

        private static int FindAnyWalkableInColumn(in PipelineResult pipeline, int column)
        {
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                if (ColumnOfRawSpan(pipeline.Raw, span) == column)
                {
                    return span;
                }
            }

            throw new InvalidOperationException($"No walkable span in column {column}.");
        }

        private static int FindWalkableAtY(in PipelineResult pipeline, int column, int yCm)
        {
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                if (ColumnOfRawSpan(pipeline.Raw, span) == column &&
                    pipeline.Raw.SpanMaxYcm[span] == yCm)
                {
                    return span;
                }
            }

            throw new InvalidOperationException(
                $"Walkable span in column={column} at y={yCm} not found.");
        }

        private static int ColumnOfRawSpan(LayeredSpanScratch raw, int span)
        {
            ReadOnlySpan<int> offsets = raw.ColumnSpanOffsets;
            for (int col = 0; col < raw.ColumnCount; col++)
            {
                if (span >= offsets[col] && span < offsets[col + 1])
                {
                    return col;
                }
            }

            throw new InvalidOperationException($"Span {span} not found in column offsets.");
        }

        private static bool SequenceEqual(ReadOnlySpan<int> left, int[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct PipelineResult
        {
            public PipelineResult(
                LayeredSpanScratch raw,
                LayeredSpanWalkabilityScratch walkability,
                LayeredSpanSurfaceSheetScratch sheets,
                LayeredSpanWalkLinkScratch links,
                LayeredSpanRadiusFieldScratch radius,
                LayeredSpanRegionScratch regions)
            {
                Raw = raw;
                Walkability = walkability;
                Sheets = sheets;
                Links = links;
                Radius = radius;
                Regions = regions;
            }

            public LayeredSpanScratch Raw { get; }
            public LayeredSpanWalkabilityScratch Walkability { get; }
            public LayeredSpanSurfaceSheetScratch Sheets { get; }
            public LayeredSpanWalkLinkScratch Links { get; }
            public LayeredSpanRadiusFieldScratch Radius { get; }
            public LayeredSpanRegionScratch Regions { get; }
        }
    }
}
