using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanRegionContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk =
            new(agentHeightCm: 50, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 5);

        [Test]
        public void Regions_FragmentedAdjacentTrianglesInOneColumn_BecomeOneRegion()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(1));
            Assert.That(pipeline.Regions.RegionCount, Is.EqualTo(1));
            Assert.That(pipeline.Regions.SpanRegionIds[0], Is.EqualTo(0));
            Assert.That(pipeline.Regions.SpanRegionIds[1], Is.EqualTo(0));
            Assert.That(pipeline.Regions.RegionMinSpanIndices[0], Is.EqualTo(0));
            Assert.That(pipeline.Regions.RegionMemberCounts[0], Is.EqualTo(2));
        }

        [Test]
        public void Regions_ContinuousMultiColumnRamp_BecomesOneRegion()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 200, 0 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 10);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Links.LinkCount, Is.EqualTo(2));
            Assert.That(pipeline.Regions.RegionCount, Is.EqualTo(1));
            Assert.That(pipeline.Regions.SpanRegionIds[0], Is.EqualTo(0));
            Assert.That(pipeline.Regions.SpanRegionIds[1], Is.EqualTo(0));
            Assert.That(pipeline.Regions.RegionMemberCounts[0], Is.EqualTo(2));
        }

        [Test]
        public void Regions_OverlappingXzFloorsAtDifferentY_RemainSeparate()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 200, 0,
                    0, 200, 0
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    500, 500, 500
                },
                vertexZcm: new[]
                {
                    0, 0, 100,
                    0, 0, 100
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 10);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(4));
            Assert.That(pipeline.Regions.RegionCount, Is.EqualTo(2));

            int lowL = pipeline.Walkability.WalkableSpanIndices[0];
            int highL = pipeline.Walkability.WalkableSpanIndices[1];
            int lowR = pipeline.Walkability.WalkableSpanIndices[2];
            int highR = pipeline.Walkability.WalkableSpanIndices[3];
            Assert.That(pipeline.Raw.SpanMaxYcm[lowL], Is.EqualTo(0));
            Assert.That(pipeline.Raw.SpanMaxYcm[highL], Is.EqualTo(500));

            int lowRegion = pipeline.Regions.SpanRegionIds[lowL];
            int highRegion = pipeline.Regions.SpanRegionIds[highL];
            Assert.That(lowRegion, Is.EqualTo(0));
            Assert.That(highRegion, Is.EqualTo(1));
            Assert.That(pipeline.Regions.SpanRegionIds[lowR], Is.EqualTo(lowRegion));
            Assert.That(pipeline.Regions.SpanRegionIds[highR], Is.EqualTo(highRegion));
            Assert.That(pipeline.Regions.RegionMemberCounts[0], Is.EqualTo(2));
            Assert.That(pipeline.Regions.RegionMemberCounts[1], Is.EqualTo(2));
        }

        [Test]
        public void Regions_DisconnectedIsland_GetsNextDeterministicId()
        {
            // Left platform covers columns 0-1 only (x<=180); right island stays in column 2 (x>=220).
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 180, 0,
                    220, 300, 220
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 100,
                    0, 0, 100
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Regions.RegionCount, Is.EqualTo(2));
            Assert.That(pipeline.Regions.RegionMinSpanIndices[0], Is.LessThan(pipeline.Regions.RegionMinSpanIndices[1]));
            Assert.That(pipeline.Regions.RegionMinSpanIndices[0], Is.EqualTo(0));
            Assert.That(pipeline.Regions.SpanRegionIds[pipeline.Regions.RegionMinSpanIndices[1]], Is.EqualTo(1));
        }

        [Test]
        public void Regions_ReversedTriangleInputOrder_StableIdsYieldIdenticalAssignment()
        {
            var forward = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 100, 0, 100,
                    150, 250, 150
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 100, 100,
                    0, 0, 100
                },
                triA: new[] { 0, 1, 4 },
                triB: new[] { 1, 3, 5 },
                triC: new[] { 2, 2, 6 },
                triAreaIds: new byte[] { 1, 1, 1 },
                triStableIds: new[] { 10, 20, 30 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });

            var reversed = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    150, 250, 150,
                    0, 100, 0, 100
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 100,
                    0, 0, 100, 100
                },
                triA: new[] { 0, 3, 4 },
                triB: new[] { 1, 4, 6 },
                triC: new[] { 2, 5, 5 },
                triAreaIds: new byte[] { 1, 1, 1 },
                triStableIds: new[] { 30, 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            PipelineResult a = RunPipeline(forward, new[] { 0, 1, 2 }, grid, maxClimbCm: 0);
            PipelineResult b = RunPipeline(reversed, new[] { 0, 1, 2 }, grid, maxClimbCm: 0);

            Assert.That(a.Regions.RegionCount, Is.EqualTo(b.Regions.RegionCount));
            Assert.That(BuildRegionChecksum(a), Is.EqualTo(BuildRegionChecksum(b)));
            Assert.That(
                SequenceEqual(a.Regions.RegionMinSpanIndices, b.Regions.RegionMinSpanIndices),
                Is.True);
            Assert.That(
                SequenceEqual(a.Regions.RegionMemberCounts, b.Regions.RegionMemberCounts),
                Is.True);
        }

        [Test]
        public void WalkLinks_PortalInterval_EqualsSharedBoundaryOverlap_DisjointHalvesProduceNoLink()
        {
            var continuous = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 200, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult linked = RunPipeline(continuous, new[] { 0 }, grid, maxClimbCm: 0);

            Assert.That(linked.Links.LinkCount, Is.EqualTo(2));
            int left = linked.Walkability.WalkableSpanIndices[0];
            int right = linked.Walkability.WalkableSpanIndices[1];
            int expectedMin = Math.Max(linked.Raw.SpanEastMinZcm[left], linked.Raw.SpanWestMinZcm[right]);
            int expectedMax = Math.Min(linked.Raw.SpanEastMaxZcm[left], linked.Raw.SpanWestMaxZcm[right]);
            Assert.That(expectedMax, Is.GreaterThan(expectedMin));

            int eastLink = FindLinkIndex(linked, left, right, LayeredSpanNeighborDirection.East);
            int westLink = FindLinkIndex(linked, right, left, LayeredSpanNeighborDirection.West);
            Assert.That(linked.Links.LinkPortalMinAlongCm[eastLink], Is.EqualTo(expectedMin));
            Assert.That(linked.Links.LinkPortalMaxAlongCm[eastLink], Is.EqualTo(expectedMax));
            Assert.That(linked.Links.LinkPortalMinAlongCm[westLink], Is.EqualTo(expectedMin));
            Assert.That(linked.Links.LinkPortalMaxAlongCm[westLink], Is.EqualTo(expectedMax));

            var disjoint = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    50, 100, 100,
                    100, 150, 100
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 40,
                    60, 80, 100
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            PipelineResult noLink = RunPipeline(disjoint, new[] { 0, 1 }, grid, maxClimbCm: 40);
            int leftInCol0 = FindWalkable(noLink, stableId: 1, column: 0);
            int rightInCol1 = FindWalkable(noLink, stableId: 2, column: 1);
            AssertNoLink(noLink, leftInCol0, rightInCol1);
            AssertNoLink(noLink, rightInCol1, leftInCol0);
            Assert.That(noLink.Regions.SpanRegionIds[leftInCol0], Is.Not.EqualTo(noLink.Regions.SpanRegionIds[rightInCol1]));
        }

        [Test]
        public void Regions_StaleOrMismatchedScratch_RejectedWithEmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult good = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0);

            var otherSurface = TinyFloor();
            var otherGrid = new LayeredSpanRasterGridSpec(0, 0, 50, 1, 1);
            var otherRaw = new LayeredSpanScratch(1, 8);
            var otherWalk = new LayeredSpanWalkabilityScratch(1, 8, 8);
            var otherSheets = new LayeredSpanSurfaceSheetScratch(1, 8);
            LayeredSpanRasterizer.Rasterize(otherSurface, new[] { 0 }, in otherGrid, otherRaw);
            LayeredSpanWalkabilityClassifier.Classify(otherRaw, in DefaultWalk, otherWalk);
            LayeredSpanSurfaceSheetAssigner.Assign(otherSurface, otherRaw, in otherGrid, in DefaultWalk, otherSheets);

            var regions = new LayeredSpanRegionScratch(64, 64);
            // Seed a prior successful publish so failure must clear it.
            LayeredSpanRegionBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, good.Radius, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var exWalk = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    good.Raw,
                    otherWalk,
                    good.Sheets,
                    good.Links,
                    good.Radius,
                    agentRadiusCm: 0,
                    regions));
            Assert.That(exWalk!.Message, Does.Contain("walkability output that matches"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));

            LayeredSpanRegionBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, good.Radius, agentRadiusCm: 0, regions);
            var exSheet = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    good.Raw,
                    good.Walkability,
                    otherSheets,
                    good.Links,
                    good.Radius,
                    agentRadiusCm: 0,
                    regions));
            Assert.That(exSheet!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));

            var emptyLinks = new LayeredSpanWalkLinkScratch(64, 64);
            LayeredSpanRegionBuilder.Build(
                good.Raw, good.Walkability, good.Sheets, good.Links, good.Radius, agentRadiusCm: 0, regions);
            var exLinks = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    good.Raw,
                    good.Walkability,
                    good.Sheets,
                    emptyLinks,
                    good.Radius,
                    agentRadiusCm: 0,
                    regions));
            Assert.That(exLinks!.Message, Does.Contain("walk-link output that matches"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));
        }

        [Test]
        public void Regions_CapacityFailures_NameOwnerAndActualRequired_EmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(2, 8);
            var walk = new LayeredSpanWalkabilityScratch(2, 8, 8);
            var sheets = new LayeredSpanSurfaceSheetScratch(2, 8);
            var links = new LayeredSpanWalkLinkScratch(8, 16);
            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            var radius = new LayeredSpanRadiusFieldScratch(8, 8, 16);
            RunOnce(surface, new[] { 0 }, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions: null);

            var tooSmallSpan = new LayeredSpanRegionScratch(spanCapacity: 1, regionCapacity: 8);
            var exSpan = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, tooSmallSpan));
            Assert.That(exSpan!.Message, Does.Contain("LayeredSpanRegionScratch.spanCapacity"));
            Assert.That(exSpan.Message, Does.Contain($"required {raw.SpanCount}"));
            Assert.That(tooSmallSpan.SpanCount, Is.EqualTo(0));
            Assert.That(tooSmallSpan.RegionCount, Is.EqualTo(0));

            var tooSmallRegion = new LayeredSpanRegionScratch(spanCapacity: 8, regionCapacity: 0);
            var exRegion = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, tooSmallRegion));
            Assert.That(exRegion!.Message, Does.Contain("LayeredSpanRegionScratch.regionCapacity"));
            Assert.That(exRegion.Message, Does.Contain("required 1"));
            Assert.That(tooSmallRegion.SpanCount, Is.EqualTo(0));
            Assert.That(tooSmallRegion.RegionCount, Is.EqualTo(0));

            // Sheet/column owners stay on their scratches; confirm naming still present for those owners.
            var sheetCol = new LayeredSpanSurfaceSheetScratch(columnCapacity: 1, spanCapacity: 8);
            var exSheetCol = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in DefaultWalk, sheetCol));
            Assert.That(exSheetCol!.Message, Does.Contain("LayeredSpanSurfaceSheetScratch.columnCapacity"));
            Assert.That(exSheetCol.Message, Does.Contain($"required {raw.ColumnCount}"));
            Assert.That(sheetCol.SpanCount, Is.EqualTo(0));

            var linkWalk = new LayeredSpanWalkLinkScratch(walkableSpanCapacity: 1, linkCapacity: 8);
            var exLinkWalk = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, linkWalk));
            Assert.That(exLinkWalk!.Message, Does.Contain("LayeredSpanWalkLinkScratch.walkableSpanCapacity"));
            Assert.That(exLinkWalk.Message, Does.Contain($"required {walk.WalkableSpanCount}"));
            Assert.That(linkWalk.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(linkWalk.LinkCount, Is.EqualTo(0));
        }

        [Test]
        public void Regions_WarmedCompletePipeline_AllocatesExactlyZeroBytes()
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
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions);
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
            Assert.That(regions.SpanRegionIds.Length, Is.EqualTo(raw.SpanCount));
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

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm)
        {
            var raw = new LayeredSpanScratch(grid.ColumnCount, 64);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 64, 64);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 64);
            var links = new LayeredSpanWalkLinkScratch(64, 128);
            var radius = new LayeredSpanRadiusFieldScratch(64, 64, 128);
            var regions = new LayeredSpanRegionScratch(64, 64);
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links, radius, regions);
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
            LayeredSpanRegionScratch? regions)
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
                    LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);
                }
            }
        }

        private static long BuildRegionChecksum(in PipelineResult pipeline)
        {
            long checksum = pipeline.Regions.RegionCount;
            ReadOnlySpan<int> ids = pipeline.Regions.SpanRegionIds;
            for (int i = 0; i < ids.Length; i++)
            {
                checksum = (checksum * 1_000_003L) + ids[i] + 1;
                checksum = (checksum * 1_000_003L) + pipeline.Raw.SpanStableTriangleIds[i];
            }

            ReadOnlySpan<int> mins = pipeline.Regions.RegionMinSpanIndices;
            ReadOnlySpan<int> counts = pipeline.Regions.RegionMemberCounts;
            for (int r = 0; r < mins.Length; r++)
            {
                checksum = (checksum * 1_000_003L) + mins[r];
                checksum = (checksum * 1_000_003L) + counts[r];
            }

            return checksum;
        }

        private static bool SequenceEqual(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
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

        private static int FindLinkIndex(
            in PipelineResult pipeline,
            int sourceSpan,
            int neighborSpan,
            LayeredSpanNeighborDirection direction)
        {
            int walkableIndex = IndexOfWalkable(pipeline.Walkability, sourceSpan);
            int start = pipeline.Links.LinkOffsets[walkableIndex];
            int end = pipeline.Links.LinkOffsets[walkableIndex + 1];
            for (int i = start; i < end; i++)
            {
                if (pipeline.Links.LinkNeighborSpanIndices[i] == neighborSpan &&
                    pipeline.Links.LinkNeighborDirections[i] == direction)
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Link {sourceSpan} -{direction}-> {neighborSpan} not found.");
        }

        private static int FindWalkable(in PipelineResult pipeline, int stableId, int column)
        {
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                if (pipeline.Raw.SpanStableTriangleIds[span] == stableId &&
                    ColumnOfRawSpan(pipeline.Raw, span) == column)
                {
                    return span;
                }
            }

            throw new InvalidOperationException(
                $"Walkable span with stableId={stableId} in column={column} not found.");
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

        private static void AssertNoLink(in PipelineResult pipeline, int sourceSpan, int neighborSpan)
        {
            int walkableIndex = IndexOfWalkable(pipeline.Walkability, sourceSpan);
            int start = pipeline.Links.LinkOffsets[walkableIndex];
            int end = pipeline.Links.LinkOffsets[walkableIndex + 1];
            for (int i = start; i < end; i++)
            {
                if (pipeline.Links.LinkNeighborSpanIndices[i] == neighborSpan)
                {
                    Assert.Fail($"Did not expect link {sourceSpan} -> {neighborSpan}.");
                }
            }
        }

        private static int IndexOfWalkable(LayeredSpanWalkabilityScratch walkability, int sourceSpan)
        {
            for (int i = 0; i < walkability.WalkableSpanCount; i++)
            {
                if (walkability.WalkableSpanIndices[i] == sourceSpan)
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Walkable span {sourceSpan} not found.");
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
