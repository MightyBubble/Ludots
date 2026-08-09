using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanRasterizerContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        [Test]
        public void LayeredSpan_OverlappingXzFloors_BothRetainedAndYSorted()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10 },
                vertexYcm: new[] { 0, 0, 0, 500, 500, 500 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 100, 200 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 0,
                originZcm: 0,
                cellSizeCm: 100,
                columnCountX: 1,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 1, spanCapacity: 8);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in spec, scratch);

            Assert.That(scratch.ColumnCount, Is.EqualTo(1));
            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(2));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(100));
            Assert.That(scratch.SpanAreaIds[0], Is.EqualTo((byte)1));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(FloorFlags));
            Assert.That(scratch.SpanMinYcm[1], Is.EqualTo(500));
            Assert.That(scratch.SpanMaxYcm[1], Is.EqualTo(500));
            Assert.That(scratch.SpanStableTriangleIds[1], Is.EqualTo(200));
            Assert.That(scratch.SpanAreaIds[1], Is.EqualTo((byte)2));
            Assert.That(scratch.SpanSurfaceFlags[1], Is.EqualTo(FloorFlags));
        }

        [Test]
        public void LayeredSpan_FloorAndCeiling_OverlappingXz_BothRetainedWithDistinguishableFlags()
        {
            // Floor (Solid|WalkCandidate) under ceiling/wall-like Solid-only sheet; same XZ, different Y.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10 },
                vertexYcm: new[] { 0, 0, 0, 500, 500, 500 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 100, 200 },
                triFlags: new[] { FloorFlags, SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 0,
                originZcm: 0,
                cellSizeCm: 100,
                columnCountX: 1,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 1, spanCapacity: 8);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(100));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(FloorFlags));
            Assert.That(scratch.SpanMinYcm[1], Is.EqualTo(500));
            Assert.That(scratch.SpanStableTriangleIds[1], Is.EqualTo(200));
            Assert.That(scratch.SpanSurfaceFlags[1], Is.EqualTo(SolidOnly));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.Not.EqualTo(scratch.SpanSurfaceFlags[1]));
        }

        [Test]
        public void LayeredSpan_InputTriangleOrder_DoesNotChangeObservableSpans()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10, 20, 80, 20 },
                vertexYcm: new[] { 100, 100, 100, 300, 300, 300, 200, 200, 200 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90, 20, 20, 80 },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 3, 5, 7 },
                triStableIds: new[] { 30, 10, 20 },
                triFlags: new[] { FloorFlags, SolidOnly, FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var scratchA = new LayeredSpanScratch(1, 16);
            var scratchB = new LayeredSpanScratch(1, 16);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1, 2 }, in spec, scratchA);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 2, 0, 1 }, in spec, scratchB);

            Assert.That(scratchA.SpanCount, Is.EqualTo(3));
            Assert.That(scratchB.SpanCount, Is.EqualTo(scratchA.SpanCount));
            Assert.That(ToArray(scratchB.SpanMinYcm), Is.EqualTo(ToArray(scratchA.SpanMinYcm)));
            Assert.That(ToArray(scratchB.SpanMaxYcm), Is.EqualTo(ToArray(scratchA.SpanMaxYcm)));
            Assert.That(ToArray(scratchB.SpanTriangleIndices), Is.EqualTo(ToArray(scratchA.SpanTriangleIndices)));
            Assert.That(ToArray(scratchB.SpanStableTriangleIds), Is.EqualTo(ToArray(scratchA.SpanStableTriangleIds)));
            Assert.That(ToArray(scratchB.SpanAreaIds), Is.EqualTo(ToArray(scratchA.SpanAreaIds)));
            Assert.That(ToArray(scratchB.SpanSurfaceFlags), Is.EqualTo(ToArray(scratchA.SpanSurfaceFlags)));
            Assert.That(ToArray(scratchB.SpanNormalX), Is.EqualTo(ToArray(scratchA.SpanNormalX)));
            Assert.That(ToArray(scratchB.SpanNormalY), Is.EqualTo(ToArray(scratchA.SpanNormalY)));
            Assert.That(ToArray(scratchB.SpanNormalZ), Is.EqualTo(ToArray(scratchA.SpanNormalZ)));
            Assert.That(ToArray(scratchB.SpanBoundaryMask), Is.EqualTo(ToArray(scratchA.SpanBoundaryMask)));
            Assert.That(ToArray(scratchB.SpanWestMinYcm), Is.EqualTo(ToArray(scratchA.SpanWestMinYcm)));
            Assert.That(ToArray(scratchB.SpanWestMaxYcm), Is.EqualTo(ToArray(scratchA.SpanWestMaxYcm)));
            Assert.That(ToArray(scratchB.SpanWestMinZcm), Is.EqualTo(ToArray(scratchA.SpanWestMinZcm)));
            Assert.That(ToArray(scratchB.SpanWestMaxZcm), Is.EqualTo(ToArray(scratchA.SpanWestMaxZcm)));
            Assert.That(ToArray(scratchB.SpanEastMinYcm), Is.EqualTo(ToArray(scratchA.SpanEastMinYcm)));
            Assert.That(ToArray(scratchB.SpanEastMaxYcm), Is.EqualTo(ToArray(scratchA.SpanEastMaxYcm)));
            Assert.That(ToArray(scratchB.SpanEastMinZcm), Is.EqualTo(ToArray(scratchA.SpanEastMinZcm)));
            Assert.That(ToArray(scratchB.SpanEastMaxZcm), Is.EqualTo(ToArray(scratchA.SpanEastMaxZcm)));
            Assert.That(ToArray(scratchB.SpanNorthMinYcm), Is.EqualTo(ToArray(scratchA.SpanNorthMinYcm)));
            Assert.That(ToArray(scratchB.SpanNorthMaxYcm), Is.EqualTo(ToArray(scratchA.SpanNorthMaxYcm)));
            Assert.That(ToArray(scratchB.SpanNorthMinXcm), Is.EqualTo(ToArray(scratchA.SpanNorthMinXcm)));
            Assert.That(ToArray(scratchB.SpanNorthMaxXcm), Is.EqualTo(ToArray(scratchA.SpanNorthMaxXcm)));
            Assert.That(ToArray(scratchB.SpanSouthMinYcm), Is.EqualTo(ToArray(scratchA.SpanSouthMinYcm)));
            Assert.That(ToArray(scratchB.SpanSouthMaxYcm), Is.EqualTo(ToArray(scratchA.SpanSouthMaxYcm)));
            Assert.That(ToArray(scratchB.SpanSouthMinXcm), Is.EqualTo(ToArray(scratchA.SpanSouthMinXcm)));
            Assert.That(ToArray(scratchB.SpanSouthMaxXcm), Is.EqualTo(ToArray(scratchA.SpanSouthMaxXcm)));
            Assert.That(ToArray(scratchA.SpanStableTriangleIds), Is.EqualTo(new[] { 30, 20, 10 }));
            Assert.That(ToArray(scratchA.SpanSurfaceFlags), Is.EqualTo(new[] { FloorFlags, FloorFlags, SolidOnly }));
        }

        [Test]
        public void LayeredSpan_FullCellFloor_RecordsExactClosedBoundaryIntervals()
        {
            // Floor covers closed column [0,100]x[0,100] at y=0; all four boundaries are y=0.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var scratch = new LayeredSpanScratch(1, 8);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            for (int i = 0; i < scratch.SpanCount; i++)
            {
                Assert.That(scratch.SpanBoundaryMask[i], Is.EqualTo(
                    LayeredSpanBoundaryMask.West |
                    LayeredSpanBoundaryMask.East |
                    LayeredSpanBoundaryMask.North |
                    LayeredSpanBoundaryMask.South));
                Assert.That(scratch.SpanWestMinYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanWestMaxYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanEastMinYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanEastMaxYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanNorthMinYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanNorthMaxYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanSouthMinYcm[i], Is.EqualTo(0));
                Assert.That(scratch.SpanSouthMaxYcm[i], Is.EqualTo(0));
            }

            // Tri0=(0,0)-(100,0)-(0,100): full west/north; east/south are corner-only.
            int tri0 = scratch.SpanStableTriangleIds[0] == 1 ? 0 : 1;
            int tri1 = 1 - tri0;
            Assert.That(scratch.SpanWestMinZcm[tri0], Is.EqualTo(0));
            Assert.That(scratch.SpanWestMaxZcm[tri0], Is.EqualTo(100));
            Assert.That(scratch.SpanNorthMinXcm[tri0], Is.EqualTo(0));
            Assert.That(scratch.SpanNorthMaxXcm[tri0], Is.EqualTo(100));
            Assert.That(scratch.SpanEastMinZcm[tri0], Is.EqualTo(0));
            Assert.That(scratch.SpanEastMaxZcm[tri0], Is.EqualTo(0));
            // Tri1=(100,0)-(100,100)-(0,100): full east/south.
            Assert.That(scratch.SpanEastMinZcm[tri1], Is.EqualTo(0));
            Assert.That(scratch.SpanEastMaxZcm[tri1], Is.EqualTo(100));
            Assert.That(scratch.SpanSouthMinXcm[tri1], Is.EqualTo(0));
            Assert.That(scratch.SpanSouthMaxXcm[tri1], Is.EqualTo(100));
        }

        [Test]
        public void LayeredSpan_InteriorOnlyFloor_DoesNotInferMissingBoundaryFromCellMinMax()
        {
            // Triangle stays inside (10..90); cell min/max Y is 0, but no closed boundary is touched.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 90 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 7 },
                triFlags: new[] { FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var scratch = new LayeredSpanScratch(1, 4);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanBoundaryMask[0], Is.EqualTo(LayeredSpanBoundaryMask.None));
            Assert.That(scratch.SpanWestMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanEastMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanNorthMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanSouthMinYcm[0], Is.EqualTo(0));
        }

        [Test]
        public void LayeredSpan_SlopedTriangle_BoundaryYIsExactFaceHitNotWholeCellRange()
        {
            // Plane y=x over (0,0)-(100,0)-(0,100). East face x=100 only exists at z=0 => y=100.
            // Whole-cell span Y is [0,100]; east boundary must not copy that range.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var scratch = new LayeredSpanScratch(1, 4);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(100));
            Assert.That((scratch.SpanBoundaryMask[0] & LayeredSpanBoundaryMask.East) != 0, Is.True);
            Assert.That(scratch.SpanEastMinYcm[0], Is.EqualTo(100));
            Assert.That(scratch.SpanEastMaxYcm[0], Is.EqualTo(100));
            Assert.That(scratch.SpanEastMinZcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanEastMaxZcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanEastMinYcm[0], Is.Not.EqualTo(scratch.SpanMinYcm[0]));
        }

        [Test]
        public void LayeredSpan_VerticalWallExactlyOnColumnBoundary_RetainedInBothClosedColumns()
        {
            // Closed columns share x=100: col0=[0,100], col1=[100,200].
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 100, 100, 100 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 40, 40, 60 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 4 },
                triStableIds: new[] { 44 },
                triFlags: new[] { SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 0,
                originZcm: 0,
                cellSizeCm: 100,
                columnCountX: 2,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 2, spanCapacity: 4);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.ColumnCount, Is.EqualTo(2));
            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(1));
            Assert.That(scratch.ColumnSpanCounts[1], Is.EqualTo(1));
            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(100));
            Assert.That(scratch.SpanMinYcm[1], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[1], Is.EqualTo(100));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(44));
            Assert.That(scratch.SpanStableTriangleIds[1], Is.EqualTo(44));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(SolidOnly));
            Assert.That(scratch.SpanSurfaceFlags[1], Is.EqualTo(SolidOnly));
        }

        [Test]
        public void LayeredSpan_WalkFloorEndingOnColumnBoundary_NoGhostWalkSpanOutside()
        {
            // Floor covers only col0=[0,100]; shared closed edge x=100 must not create a WalkCandidate span in col1.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var scratch = new LayeredSpanScratch(2, 8);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in spec, scratch);

            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(2));
            Assert.That(scratch.ColumnSpanCounts[1], Is.EqualTo(0));
            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(FloorFlags));
            Assert.That(scratch.SpanSurfaceFlags[1], Is.EqualTo(FloorFlags));
        }

        [Test]
        public void LayeredSpan_VerticalSolidWallOnBoundary_StillBlocksBothClosedColumns()
        {
            // Solid-only vertical wall on x=100 still rasters conservatively into both closed columns.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 100, 100, 100 },
                vertexYcm: new[] { 0, 80, 0 },
                vertexZcm: new[] { 10, 10, 90 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 9 },
                triStableIds: new[] { 99 },
                triFlags: new[] { SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var scratch = new LayeredSpanScratch(2, 4);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(1));
            Assert.That(scratch.ColumnSpanCounts[1], Is.EqualTo(1));
            Assert.That(scratch.SpanCount, Is.EqualTo(2));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(SolidOnly));
            Assert.That(scratch.SpanSurfaceFlags[1], Is.EqualTo(SolidOnly));
        }

        [Test]
        public void LayeredSpan_VerticalWallStrictlyInsideColumn_DoesNotExpandToNeighbors()
        {
            // Wall at x=50 belongs only to closed col0=[0,100], not col1=[100,200].
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 50, 50, 50 },
                vertexYcm: new[] { 10, 90, 10 },
                vertexZcm: new[] { 40, 40, 60 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 5 },
                triStableIds: new[] { 55 },
                triFlags: new[] { SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 0,
                originZcm: 0,
                cellSizeCm: 100,
                columnCountX: 2,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 2, spanCapacity: 4);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(1));
            Assert.That(scratch.ColumnSpanCounts[1], Is.EqualTo(0));
            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(10));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(90));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(55));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(SolidOnly));
        }

        [Test]
        public void LayeredSpan_ExtremeCoordinateDegenerateSegment_CrossesSmallGridWithCorrectY()
        {
            // XZ-degenerate segment from int extremes across a small grid near a nonzero origin.
            // int subtract of endpoints/origin must not overflow away the hit.
            // Clipped Y is the exact lerp at the column X bounds (~50), not the far endpoint Y.
            const int origin = 1_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { int.MinValue, int.MaxValue, int.MinValue },
                vertexYcm: new[] { 20, 80, 20 },
                vertexZcm: new[] { origin + 50, origin + 50, origin + 50 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 6 },
                triStableIds: new[] { 66 },
                triFlags: new[] { SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: origin,
                originZcm: origin,
                cellSizeCm: 100,
                columnCountX: 1,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 1, spanCapacity: 4);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.ColumnSpanCounts[0], Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(50));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(51));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(66));
            Assert.That(scratch.SpanNormalY[0], Is.EqualTo((Int128)0));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(SolidOnly));
        }

        [Test]
        public void LayeredSpan_VerticalXzDegenerateTriangle_RetainedWithNormalYZero()
        {
            // Vertical wall along x=50, z in [40,60], y in [0,100].
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 50, 50, 50 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 40, 40, 60 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 9 },
                triStableIds: new[] { 77 },
                triFlags: new[] { SolidOnly });

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 40,
                originZcm: 40,
                cellSizeCm: 20,
                columnCountX: 1,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(1, 4);

            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(0));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(100));
            Assert.That(scratch.SpanNormalY[0], Is.EqualTo((Int128)0));
            Assert.That(scratch.SpanNormalX[0], Is.Not.EqualTo((Int128)0));
            Assert.That(scratch.SpanStableTriangleIds[0], Is.EqualTo(77));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(SolidOnly));
        }

        [Test]
        public void LayeredSpan_SlopedTriangle_ClippedYIsTighterThanWholeTriangleRange()
        {
            // Plane y = x over XZ triangle (0,0)-(100,0)-(0,100).
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 100, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            int wholeMinY = 0;
            int wholeMaxY = 100;

            var spec = new LayeredSpanRasterGridSpec(
                originXcm: 80,
                originZcm: 10,
                cellSizeCm: 10,
                columnCountX: 1,
                columnCountZ: 1);
            var scratch = new LayeredSpanScratch(1, 4);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch);

            Assert.That(scratch.SpanCount, Is.EqualTo(1));
            Assert.That(scratch.SpanMinYcm[0], Is.GreaterThan(wholeMinY));
            Assert.That(scratch.SpanMaxYcm[0], Is.LessThan(wholeMaxY));
            Assert.That(scratch.SpanMinYcm[0], Is.EqualTo(80));
            Assert.That(scratch.SpanMaxYcm[0], Is.EqualTo(90));
            Assert.That(scratch.SpanSurfaceFlags[0], Is.EqualTo(FloorFlags));
        }

        [Test]
        public void LayeredSpan_InvalidTriangleIndex_FailsExplicitly()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 10, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 10 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
            var spec = new LayeredSpanRasterGridSpec(0, 0, 10, 1, 1);
            var scratch = new LayeredSpanScratch(1, 4);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => LayeredSpanRasterizer.Rasterize(surface, new[] { 1 }, in spec, scratch));
            Assert.That(ex!.Message, Does.Contain("Triangle index 1"));
            Assert.That(ex.Message, Does.Contain("triangle count 1"));
            Assert.That(scratch.SpanCount, Is.EqualTo(0));
            Assert.That(scratch.ColumnCount, Is.EqualTo(0));
        }

        [Test]
        public void LayeredSpan_ColumnCapacityFailure_NamesOwnerAndRequired()
        {
            var surface = TinyFloor();
            var spec = new LayeredSpanRasterGridSpec(0, 0, 50, 2, 2);
            var scratch = new LayeredSpanScratch(columnCapacity: 2, spanCapacity: 16);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRasterizer.Rasterize(surface, new[] { 0 }, in spec, scratch));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanScratch.columnCapacity"));
            Assert.That(ex.Message, Does.Contain("required 4"));
            Assert.That(scratch.SpanCount, Is.EqualTo(0));
            Assert.That(scratch.ColumnCount, Is.EqualTo(0));
        }

        [Test]
        public void LayeredSpan_SpanCapacityFailure_NamesOwnerAndRequired()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10 },
                vertexYcm: new[] { 0, 0, 0, 400, 400, 400 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            var spec = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var scratch = new LayeredSpanScratch(columnCapacity: 1, spanCapacity: 1);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in spec, scratch));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanScratch.spanCapacity"));
            Assert.That(ex.Message, Does.Contain("required 2"));
            Assert.That(scratch.SpanCount, Is.EqualTo(0));
            Assert.That(scratch.ColumnCount, Is.EqualTo(0));
        }

        [Test]
        public void LayeredSpan_WarmedRasterize_AllocatesExactlyZeroBytes()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 90, 10, 10, 90, 10, 50, 50, 50 },
                vertexYcm: new[] { 0, 0, 0, 250, 250, 250, 0, 80, 0 },
                vertexZcm: new[] { 10, 10, 90, 10, 10, 90, 40, 40, 60 },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 1, 2, 3 },
                triStableIds: new[] { 1, 2, 3 },
                triFlags: new[] { FloorFlags, FloorFlags, SolidOnly });
            var spec = new LayeredSpanRasterGridSpec(0, 0, 50, 2, 2);
            var scratch = new LayeredSpanScratch(columnCapacity: 4, spanCapacity: 64);
            int[] indices = { 0, 1, 2 };

            // Warmup (JIT + first fill).
            for (int i = 0; i < 64; i++)
            {
                LayeredSpanRasterizer.Rasterize(surface, indices, in spec, scratch);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                LayeredSpanRasterizer.Rasterize(surface, indices, in spec, scratch);
                if (scratch.SpanCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard to keep scratch live.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed layered-span rasterize allocated {allocated} bytes.");
            Assert.That(scratch.SpanCount, Is.GreaterThan(0));
        }

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

        private static int[] ToArray(ReadOnlySpan<int> span)
        {
            var result = new int[span.Length];
            span.CopyTo(result);
            return result;
        }

        private static byte[] ToArray(ReadOnlySpan<byte> span)
        {
            var result = new byte[span.Length];
            span.CopyTo(result);
            return result;
        }

        private static NavTriangleSurfaceFlags[] ToArray(ReadOnlySpan<NavTriangleSurfaceFlags> span)
        {
            var result = new NavTriangleSurfaceFlags[span.Length];
            span.CopyTo(result);
            return result;
        }

        private static LayeredSpanBoundaryMask[] ToArray(ReadOnlySpan<LayeredSpanBoundaryMask> span)
        {
            var result = new LayeredSpanBoundaryMask[span.Length];
            span.CopyTo(result);
            return result;
        }

        private static Int128[] ToArray(ReadOnlySpan<Int128> span)
        {
            var result = new Int128[span.Length];
            span.CopyTo(result);
            return result;
        }
    }
}
