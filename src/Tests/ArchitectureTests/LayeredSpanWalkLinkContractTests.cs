using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanWalkLinkContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk =
            new(agentHeightCm: 50, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 5);

        [Test]
        public void WalkLinks_ContinuousRampAcrossColumns_LinksOnSharedBoundary()
        {
            // Continuous plane y = x/2 spanning closed columns [0,100] and [100,200].
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

            int left = pipeline.Walkability.WalkableSpanIndices[0];
            int right = pipeline.Walkability.WalkableSpanIndices[1];
            Assert.That(pipeline.Raw.SpanEastMinYcm[left], Is.EqualTo(pipeline.Raw.SpanWestMinYcm[right]));
            Assert.That(pipeline.Raw.SpanEastMinZcm[left], Is.EqualTo(0));
            Assert.That(pipeline.Raw.SpanEastMaxZcm[left], Is.EqualTo(50));
            Assert.That(pipeline.Raw.SpanWestMinZcm[right], Is.EqualTo(0));
            Assert.That(pipeline.Raw.SpanWestMaxZcm[right], Is.EqualTo(50));
            AssertHasLink(pipeline, left, right, LayeredSpanNeighborDirection.East);
            AssertHasLink(pipeline, right, left, LayeredSpanNeighborDirection.West);
        }

        [Test]
        public void WalkLinks_SameHeightDisjointAlongBoundaryHalves_DoesNotLink()
        {
            // Same Y on shared x=100, but left covers only z∈[0,40] and right only z∈[60,100].
            // WalkCandidate requires positive XZ area, so each floor stays in its own column.
            var surface = new NavTriangleSurfaceSnapshot(
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

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 40);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            int leftInCol0 = FindWalkable(pipeline, stableId: 1, column: 0);
            int rightInCol1 = FindWalkable(pipeline, stableId: 2, column: 1);
            Assert.That(pipeline.Raw.SpanEastMaxZcm[leftInCol0], Is.LessThanOrEqualTo(40));
            Assert.That(pipeline.Raw.SpanWestMinZcm[rightInCol1], Is.GreaterThanOrEqualTo(60));
            AssertNoLink(pipeline, leftInCol0, rightInCol1);
            AssertNoLink(pipeline, rightInCol1, leftInCol0);
        }

        [Test]
        public void WalkLinks_InteriorCloseButBoundaryDiscontinuous_DoesNotLink()
        {
            // Two floors at the same Y whose footprints never touch the shared closed boundary x=100.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 110, 190, 110 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 40);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            Assert.That(
                (pipeline.Raw.SpanBoundaryMask[0] & LayeredSpanBoundaryMask.East) == 0,
                Is.True);
            Assert.That(
                (pipeline.Raw.SpanBoundaryMask[1] & LayeredSpanBoundaryMask.West) == 0,
                Is.True);
            Assert.That(pipeline.Links.LinkCount, Is.EqualTo(0));
        }

        [Test]
        public void WalkLinks_VerticalWall_DoesNotCreateWalkLink()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 100, 100, 100 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 20, 20, 80 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 9 },
                triStableIds: new[] { 9 },
                triFlags: new[] { SolidOnly });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 40);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(pipeline.Links.LinkCount, Is.EqualTo(0));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(0));
        }

        [Test]
        public void SurfaceSheet_AdjacentCoplanarTrianglePieces_ShareOneSheet()
        {
            // Two coplanar floor triangles form a quad inside one closed column.
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
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.EqualTo(0));
            Assert.That(pipeline.Sheets.SpanSheetIds[1], Is.EqualTo(0));
        }

        [Test]
        public void SurfaceSheet_DisjointCoplanarPieces_StaySeparateSheets()
        {
            // Same plane and overlapping Y, but footprints never touch inside the closed cell.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    5, 35, 5,
                    65, 95, 65
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    5, 5, 35,
                    65, 65, 95
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.Not.EqualTo(pipeline.Sheets.SpanSheetIds[1]));
        }

        [Test]
        public void SurfaceSheet_AdjacentNonCoplanarSharingEdge_FormsOneSheet()
        {
            // Faceted continuous walkable surface: shared edge with different upward normals.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 0, 0, 5 },
                vertexZcm: new[] { 0, 0, 100, 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Raw.SpanNormalX[0] == pipeline.Raw.SpanNormalX[1] &&
                        pipeline.Raw.SpanNormalY[0] == pipeline.Raw.SpanNormalY[1] &&
                        pipeline.Raw.SpanNormalZ[0] == pipeline.Raw.SpanNormalZ[1], Is.False);
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(1));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.EqualTo(pipeline.Sheets.SpanSheetIds[1]));
        }

        [Test]
        public void SurfaceSheet_VertexOnlyContact_StaysSeparateSheets()
        {
            // Two walkable triangles touch only at one shared vertex inside the cell.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    10, 40, 10,
                    40, 70, 70
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    10, 10, 40,
                    10, 10, 40
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.Not.EqualTo(pipeline.Sheets.SpanSheetIds[1]));
        }

        [Test]
        public void SurfaceSheet_ExtremeIntCoordinates_CoplanarContactRemainsExact()
        {
            // Vertices near int extremes: pre-promotion int subtraction would overflow when testing coplanarity.
            const int lo = -2_000_000_000;
            const int hi = 2_000_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { lo, lo + 100, lo, lo + 100 },
                vertexYcm: new[] { hi, hi, hi, hi },
                vertexZcm: new[] { lo, lo, lo + 100, lo + 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(lo, lo, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(1));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.EqualTo(0));
            Assert.That(pipeline.Sheets.SpanSheetIds[1], Is.EqualTo(0));
        }

        [Test]
        public void SurfaceSheet_ExtremeIntOverlappingCoplanar_NoSharedEdge_FormsOneSheet()
        {
            // Huge coplanar floors with large raw normals (~65-bit components). No shared mesh edge:
            // one triangle places a strict-interior vertex in-cell inside the other so merge uses
            // signed plane distances + XZ contact — not overflow-prone normal×normal products.
            const int lo = -2_000_000_000;
            const int hi = 2_000_000_000;
            const int y = 1_000_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    lo, hi, lo,
                    -10, hi, -10
                },
                vertexYcm: new[]
                {
                    y, y, y,
                    y, y, y
                },
                vertexZcm: new[]
                {
                    lo, lo, hi,
                    -10, -10, hi
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(-50, -50, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Raw.SpanNormalY[0], Is.Not.EqualTo((Int128)0));
            Assert.That(pipeline.Raw.SpanNormalY[1], Is.Not.EqualTo((Int128)0));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(1));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.EqualTo(pipeline.Sheets.SpanSheetIds[1]));
        }

        [Test]
        public void SurfaceSheet_ExtremeIntOverlappingNonCoplanar_StaySeparateSheets()
        {
            // Same extreme overlap footprint as the coplanar case, but parallel planes offset by 1 cm.
            const int lo = -2_000_000_000;
            const int hi = 2_000_000_000;
            const int y = 1_000_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    lo, hi, lo,
                    -10, hi, -10
                },
                vertexYcm: new[]
                {
                    y, y, y,
                    y + 1, y + 1, y + 1
                },
                vertexZcm: new[]
                {
                    lo, lo, hi,
                    -10, -10, hi
                },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(-50, -50, 100, 1, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Raw.SpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SheetCount, Is.EqualTo(2));
            Assert.That(pipeline.Sheets.SpanSheetIds[0], Is.Not.EqualTo(pipeline.Sheets.SpanSheetIds[1]));
        }

        [Test]
        public void WalkLinks_MultiLevelOverlappingXz_LinksOnlyWithinLevel()
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
            Assert.That(pipeline.Links.LinkCount, Is.EqualTo(4));

            // Lower level spans are the first walkable in each column; upper are second.
            int lowL = pipeline.Walkability.WalkableSpanIndices[0];
            int highL = pipeline.Walkability.WalkableSpanIndices[1];
            int lowR = pipeline.Walkability.WalkableSpanIndices[2];
            int highR = pipeline.Walkability.WalkableSpanIndices[3];
            Assert.That(pipeline.Raw.SpanMaxYcm[lowL], Is.EqualTo(0));
            Assert.That(pipeline.Raw.SpanMaxYcm[highL], Is.EqualTo(500));

            AssertHasLink(pipeline, lowL, lowR, LayeredSpanNeighborDirection.East);
            AssertHasLink(pipeline, highL, highR, LayeredSpanNeighborDirection.East);
            AssertNoLink(pipeline, lowL, highR);
            AssertNoLink(pipeline, highL, lowR);
        }

        [Test]
        public void WalkLinks_ClosedSharedBoundaryWallAndFloors_UsesBoundaryCoverageOnly()
        {
            // Floors share the full x=100 edge; a high solid wall also sits on that closed shared boundary.
            // WalkCandidate floors stay in their positive-area columns; Solid wall still rasters on both.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 100, 100,
                    100, 200, 100,
                    100, 100, 100
                },
                vertexYcm: new[]
                {
                    0, 0, 0,
                    0, 0, 0,
                    1_000, 1_080, 1_000
                },
                vertexZcm: new[]
                {
                    0, 0, 100,
                    0, 0, 100,
                    30, 30, 70
                },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 1, 1, 9 },
                triStableIds: new[] { 1, 2, 3 },
                triFlags: new[] { FloorFlags, FloorFlags, SolidOnly });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0, 1, 2 }, grid, maxClimbCm: 5);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Links.LinkCount, Is.GreaterThanOrEqualTo(2));

            bool foundFloorLink = false;
            for (int w = 0; w < pipeline.Walkability.WalkableSpanCount; w++)
            {
                int span = pipeline.Walkability.WalkableSpanIndices[w];
                if (pipeline.Raw.SpanMaxYcm[span] != 0)
                {
                    continue;
                }

                int start = pipeline.Links.LinkOffsets[w];
                int end = pipeline.Links.LinkOffsets[w + 1];
                for (int i = start; i < end; i++)
                {
                    int neighbor = pipeline.Links.LinkNeighborSpanIndices[i];
                    if (pipeline.Raw.SpanMaxYcm[neighbor] == 0 &&
                        (pipeline.Links.LinkNeighborDirections[i] == LayeredSpanNeighborDirection.East ||
                         pipeline.Links.LinkNeighborDirections[i] == LayeredSpanNeighborDirection.West))
                    {
                        foundFloorLink = true;
                    }
                }
            }

            Assert.That(foundFloorLink, Is.True);
            for (int i = 0; i < pipeline.Raw.SpanCount; i++)
            {
                if (pipeline.Raw.SpanSurfaceFlags[i] == SolidOnly)
                {
                    Assert.That(pipeline.Walkability.SpanStatus[i], Is.EqualTo(LayeredSpanWalkabilityStatus.SolidOnly));
                    Assert.That(IndexOfWalkableOrNeg1(pipeline.Walkability, i), Is.EqualTo(-1));
                }
            }
        }

        [Test]
        public void WalkLinks_ExtremeIntCoordinates_BoundaryAndLinksRemainExact()
        {
            const int origin = 1_000_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { origin, origin + 200, origin },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { origin, origin, origin + 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(origin, origin, 100, 2, 1);
            PipelineResult pipeline = RunPipeline(surface, new[] { 0 }, grid, maxClimbCm: 0);

            Assert.That(pipeline.Walkability.WalkableSpanCount, Is.EqualTo(2));
            Assert.That(pipeline.Links.LinkCount, Is.EqualTo(2));
            int left = pipeline.Walkability.WalkableSpanIndices[0];
            Assert.That((pipeline.Raw.SpanBoundaryMask[left] & LayeredSpanBoundaryMask.East) != 0, Is.True);
            Assert.That(pipeline.Raw.SpanEastMinYcm[left], Is.EqualTo(0));
            Assert.That(pipeline.Raw.SpanEastMaxYcm[left], Is.EqualTo(0));
        }

        [Test]
        public void WalkLinks_DeterministicNeighborOrdering_WestEastNorthSouth()
        {
            // Seed one walkable per column with full boundary coverage so ordering is unambiguous.
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            var raw = new LayeredSpanScratch(9, 16);
            raw.PrepareColumns(9);
            Span<int> counts = raw.MutableColumnSpanCounts;
            for (int c = 0; c < 9; c++)
            {
                counts[c] = 1;
            }

            Span<int> offsets = raw.MutableColumnSpanOffsets;
            Span<int> cursors = raw.MutableFillCursor;
            int sum = 0;
            for (int c = 0; c < 9; c++)
            {
                offsets[c] = sum;
                cursors[c] = sum;
                sum += counts[c];
            }

            offsets[9] = sum;

            for (int c = 0; c < 9; c++)
            {
                int index = cursors[c]++;
                raw.WriteSpan(
                    index,
                    minYcm: 0,
                    maxYcm: 0,
                    triangleIndex: c,
                    stableTriangleId: c,
                    areaId: 1,
                    FloorFlags,
                    normalX: 0,
                    normalY: 1,
                    normalZ: 0,
                    LayeredSpanBoundaryMask.West |
                    LayeredSpanBoundaryMask.East |
                    LayeredSpanBoundaryMask.North |
                    LayeredSpanBoundaryMask.South,
                    westMinYcm: 0,
                    westMaxYcm: 0,
                    westMinZcm: 0,
                    westMaxZcm: 100,
                    eastMinYcm: 0,
                    eastMaxYcm: 0,
                    eastMinZcm: 0,
                    eastMaxZcm: 100,
                    northMinYcm: 0,
                    northMaxYcm: 0,
                    northMinXcm: 0,
                    northMaxXcm: 100,
                    southMinYcm: 0,
                    southMaxYcm: 0,
                    southMinXcm: 0,
                    southMaxXcm: 100);
            }

            raw.CommitSpanCount(sum);

            var walk = new LayeredSpanWalkabilityScratch(9, 16, 16);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
            var links = new LayeredSpanWalkLinkScratch(16, 64);
            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);

            int centerCol = 1 + (1 * 3);
            int centerWalkable = -1;
            for (int w = 0; w < walk.WalkableSpanCount; w++)
            {
                int span = walk.WalkableSpanIndices[w];
                if (ColumnOfRawSpan(raw, span) == centerCol)
                {
                    centerWalkable = w;
                    break;
                }
            }

            Assert.That(centerWalkable, Is.GreaterThanOrEqualTo(0));
            int start = links.LinkOffsets[centerWalkable];
            int end = links.LinkOffsets[centerWalkable + 1];
            Assert.That(end - start, Is.EqualTo(4));
            Assert.That(links.LinkNeighborDirections[start + 0], Is.EqualTo(LayeredSpanNeighborDirection.West));
            Assert.That(links.LinkNeighborDirections[start + 1], Is.EqualTo(LayeredSpanNeighborDirection.East));
            Assert.That(links.LinkNeighborDirections[start + 2], Is.EqualTo(LayeredSpanNeighborDirection.North));
            Assert.That(links.LinkNeighborDirections[start + 3], Is.EqualTo(LayeredSpanNeighborDirection.South));
        }

        [Test]
        public void WalkLinks_WarmedPipeline_AllocatesExactlyZeroBytes()
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
            var linkSpec = new LayeredSpanWalkLinkSpec(20);
            int[] indices = { 0, 1, 2 };

            for (int i = 0; i < 64; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links);
                if (links.LinkCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard to keep outputs live.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed layered-span walk-link pipeline allocated {allocated} bytes.");
            Assert.That(links.LinkCount, Is.GreaterThan(0));
            Assert.That(sheets.SheetCount, Is.GreaterThan(0));
        }

        [Test]
        public void SurfaceSheet_ColumnCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            var surface = TinyFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 50, 2, 2);
            var raw = new LayeredSpanScratch(4, 16);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in grid, raw);
            var sheets = new LayeredSpanSurfaceSheetScratch(columnCapacity: 1, spanCapacity: 16);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in DefaultWalk, sheets));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanSurfaceSheetScratch.columnCapacity"));
            Assert.That(ex.Message, Does.Contain("required 4"));
            Assert.That(sheets.SpanCount, Is.EqualTo(0));
            Assert.That(sheets.SheetCount, Is.EqualTo(0));
        }

        [Test]
        public void SurfaceSheet_SpanCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10 },
                vertexYcm: new[] { 0, 0, 0, 200, 200, 200 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var raw = new LayeredSpanScratch(1, 8);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in grid, raw);
            var sheets = new LayeredSpanSurfaceSheetScratch(columnCapacity: 1, spanCapacity: 1);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in DefaultWalk, sheets));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanSurfaceSheetScratch.spanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            Assert.That(sheets.SpanCount, Is.EqualTo(0));
            Assert.That(sheets.SheetCount, Is.EqualTo(0));
        }

        [Test]
        public void WalkLinks_WalkableSpanCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(2, 8);
            var walk = new LayeredSpanWalkabilityScratch(2, 8, 8);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
            var links = new LayeredSpanWalkLinkScratch(walkableSpanCapacity: 1, linkCapacity: 8);
            var linkSpec = new LayeredSpanWalkLinkSpec(10);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkLinkScratch.walkableSpanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            Assert.That(links.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(links.LinkCount, Is.EqualTo(0));
        }

        [Test]
        public void WalkLinks_LinkCapacityFailure_NamesOwnerAndRequired_EmptyOutput()
        {
            var surface = ContinuousTwoColumnFloor();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var raw = new LayeredSpanScratch(2, 8);
            var walk = new LayeredSpanWalkabilityScratch(2, 8, 8);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
            var links = new LayeredSpanWalkLinkScratch(walkableSpanCapacity: 8, linkCapacity: 1);
            var linkSpec = new LayeredSpanWalkLinkSpec(10);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanWalkLinkScratch.linkCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            Assert.That(links.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(links.LinkCount, Is.EqualTo(0));
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
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(surface, indices, grid, DefaultWalk, linkSpec, raw, walk, sheets, links);
            return new PipelineResult(raw, walk, sheets, links);
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
            LayeredSpanWalkLinkScratch links)
        {
            LayeredSpanRasterizer.Rasterize(surface, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in walkSpec, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in walkSpec, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
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

        private static void AssertHasLink(
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
                    return;
                }
            }

            Assert.Fail($"Expected link {sourceSpan} -{direction}-> {neighborSpan}.");
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
            int index = IndexOfWalkableOrNeg1(walkability, sourceSpan);
            if (index < 0)
            {
                throw new InvalidOperationException($"Walkable span {sourceSpan} not found.");
            }

            return index;
        }

        private static int IndexOfWalkableOrNeg1(LayeredSpanWalkabilityScratch walkability, int sourceSpan)
        {
            for (int i = 0; i < walkability.WalkableSpanCount; i++)
            {
                if (walkability.WalkableSpanIndices[i] == sourceSpan)
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly struct PipelineResult
        {
            public PipelineResult(
                LayeredSpanScratch raw,
                LayeredSpanWalkabilityScratch walkability,
                LayeredSpanSurfaceSheetScratch sheets,
                LayeredSpanWalkLinkScratch links)
            {
                Raw = raw;
                Walkability = walkability;
                Sheets = sheets;
                Links = links;
            }

            public LayeredSpanScratch Raw { get; }
            public LayeredSpanWalkabilityScratch Walkability { get; }
            public LayeredSpanSurfaceSheetScratch Sheets { get; }
            public LayeredSpanWalkLinkScratch Links { get; }
        }
    }
}
