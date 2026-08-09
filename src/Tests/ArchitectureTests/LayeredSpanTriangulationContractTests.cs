using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanTriangulationContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk =
            new(agentHeightCm: 50, minWalkableUpDotQ1M: 500_000, sameSurfaceToleranceCm: 5);

        [Test]
        public void Triangulation_ConvexSquare_TwoTrianglesCoverInterior()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            Assert.That(p.Triangulation.TriangleCount, Is.EqualTo(2));
            Assert.That(p.Triangulation.VertexCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(PointInAnyTriangle(p.Triangulation, 10, 90), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 90, 10), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 50, 50), Is.False);
        }

        [Test]
        public void Triangulation_ConcavePolygon_CoversWithoutOutside()
        {
            NavTriangleSurfaceSnapshot surface = LShapeThreeQuads();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 2);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            Assert.That(p.Triangulation.TriangleCount, Is.GreaterThan(0));
            Assert.That(PointInAnyTriangle(p.Triangulation, 10, 10), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 150, 10), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 10, 150), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 150, 150), Is.False);
            Assert.That(PointInAnyTriangle(p.Triangulation, 250, 50), Is.False);
        }

        [Test]
        public void Triangulation_StrictDonut_CoversOuterMinusHoleNeverFillsHole()
        {
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(p.Contours.RingCount, Is.EqualTo(2));
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            Assert.That(p.Triangulation.TriangleCount, Is.GreaterThan(0));

            for (int cz = 0; cz < 3; cz++)
            {
                for (int cx = 0; cx < 3; cx++)
                {
                    if (cx == 1 && cz == 1)
                    {
                        continue;
                    }

                    int centerX = grid.ColumnMinXcm(cx) + grid.CellSizeCm / 2;
                    int centerZ = grid.ColumnMinZcm(cz) + grid.CellSizeCm / 2;
                    Assert.That(
                        PointInAnyTriangle(p.Triangulation, centerX, centerZ, strictInterior: false),
                        Is.True,
                        $"Walkable cell center ({centerX},{centerZ}) must be covered.");
                }
            }

            Assert.That(PointInAnyTriangle(p.Triangulation, 150, 150), Is.False);
        }

        [Test]
        public void Triangulation_TwoHoles_Succeeds()
        {
            // 5x3 raster with two empty cells → one chart with outer + two holes.
            NavTriangleSurfaceSnapshot surface = RasterTwoHoleFiveByThree();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 5, 3);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(p.Contours.ChartCount, Is.EqualTo(1));
            Assert.That(p.Contours.RingCount, Is.EqualTo(3));
            int holeRings = 0;
            for (int r = 0; r < p.Contours.RingCount; r++)
            {
                if (p.Contours.RingKinds[r] == LayeredSpanContourRingKind.Hole)
                {
                    holeRings++;
                }
            }

            Assert.That(holeRings, Is.EqualTo(2));
            Assert.That(p.Triangulation.TriangleCount, Is.GreaterThan(0));
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 150, 150), Is.False);
            Assert.That(PointInAnyTriangle(p.Triangulation, 350, 150), Is.False);
            Assert.That(PointInAnyTriangle(p.Triangulation, 50, 50, strictInterior: false), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 250, 50, strictInterior: false), Is.True);
        }

        [Test]
        public void Triangulation_ShuffledTriangleOrder_DeterministicOutput()
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
        public void Triangulation_AllContourRingEdgesRemainConstraints()
        {
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);

            for (int ring = 0; ring < p.Contours.RingCount; ring++)
            {
                int chart = p.Contours.RingChartIds[ring];
                int start = p.Contours.RingOffsets[ring];
                int count = RingVertexCount(p.Contours, ring);
                for (int i = 0; i < count; i++)
                {
                    int j = i + 1 == count ? 0 : i + 1;
                    int x0 = p.Contours.VertexXcm[start + i];
                    int z0 = p.Contours.VertexZcm[start + i];
                    int x1 = p.Contours.VertexXcm[start + j];
                    int z1 = p.Contours.VertexZcm[start + j];
                    int va = FindPublishedVertex(p.Triangulation, chart, x0, z0);
                    int vb = FindPublishedVertex(p.Triangulation, chart, x1, z1);
                    Assert.That(va, Is.GreaterThanOrEqualTo(0), $"Missing published vertex ({x0},{z0}) chart {chart}.");
                    Assert.That(vb, Is.GreaterThanOrEqualTo(0), $"Missing published vertex ({x1},{z1}) chart {chart}.");
                    Assert.That(
                        HasConstrainedUndirectedEdge(p.Triangulation, va, vb),
                        Is.True,
                        $"Ring {ring} edge ({x0},{z0})-({x1},{z1}) must remain constrained.");
                }
            }
        }

        [Test]
        public void Triangulation_DelaunayFlipCase_AndCocircularSquareTie()
        {
            var square = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var squareGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult squareResult = RunPipeline(square, new[] { 0, 1 }, squareGrid, maxClimbCm: 0, maxErrorCm: 0);

            Assert.That(squareResult.Triangulation.TriangleCount, Is.EqualTo(2));
            Assert.That(squareResult.Triangulation.HasPublishedContent, Is.True);
            AssertLocallyDelaunay(squareResult.Triangulation);

            NavTriangleSurfaceSnapshot notch = RasterNotchedOuterRing();
            var notchGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            int[] indices = new int[notch.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult notchResult = RunPipeline(notch, indices, notchGrid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(notchResult.Triangulation.HasPublishedContent, Is.True);
            Assert.That(notchResult.Triangulation.TriangleCount, Is.GreaterThan(2));
            AssertLocallyDelaunay(notchResult.Triangulation);
        }

        [Test]
        public void Triangulation_ExactYOnSlope()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100 },
                vertexYcm: new[] { 0, 100, 0, 100 },
                vertexZcm: new[] { 0, 0, 100, 100 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });

            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 10, maxErrorCm: 0);

            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            int v = FindPublishedVertex(p.Triangulation, chart: 0, x: 100, z: 0);
            Assert.That(v, Is.GreaterThanOrEqualTo(0));
            Assert.That(p.Triangulation.VertexYcm[v], Is.EqualTo(100));
        }

        [Test]
        public void Triangulation_StackedSameXzCharts_DistinctYNoFalseAdjacency()
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

            Assert.That(p.Contours.ChartCount, Is.EqualTo(2));
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);

            bool sawLow = false;
            bool sawHigh = false;
            for (int i = 0; i < p.Triangulation.VertexCount; i++)
            {
                if (p.Triangulation.VertexYcm[i] == 0)
                {
                    sawLow = true;
                }

                if (p.Triangulation.VertexYcm[i] == 500)
                {
                    sawHigh = true;
                }
            }

            Assert.That(sawLow, Is.True);
            Assert.That(sawHigh, Is.True);
            AssertNoCrossChartAdjacency(p.Triangulation);
        }

        [Test]
        public void Triangulation_ChartSeamAndCrossTileBorderPortal()
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
            var contourSpec = FullTarget(grid, maxErrorCm: 0);
            PipelineResult seam = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, contourSpec);

            Assert.That(seam.Contours.ChartCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(seam.Contours.SeamCount, Is.GreaterThan(0));
            Assert.That(seam.Triangulation.HasPublishedContent, Is.True);
            Assert.That(CountCrossChartAdjacency(seam.Triangulation), Is.GreaterThan(0));

            var clipSurface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 300, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var clipGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            var clipContour = new LayeredSpanContourSpec(
                maxSimplificationErrorCm: 0,
                targetMinXcm: 100,
                targetMinZcm: 0,
                targetMaxXcm: 200,
                targetMaxZcm: 100);
            var clipTri = FullTriSpec(clipGrid, targetMinXcm: 100, targetMinZcm: 0, targetMaxXcm: 200, targetMaxZcm: 100);
            PipelineResult clip = RunPipeline(clipSurface, new[] { 0 }, clipGrid, maxClimbCm: 0, clipContour, clipTri);

            Assert.That(clip.Triangulation.PortalCount, Is.GreaterThan(0));
            bool sawPositivePortal = false;
            for (int i = 0; i < clip.Triangulation.PortalCount; i++)
            {
                int lx = clip.Triangulation.PortalLeftXcm[i];
                int lz = clip.Triangulation.PortalLeftZcm[i];
                int rx = clip.Triangulation.PortalRightXcm[i];
                int rz = clip.Triangulation.PortalRightZcm[i];
                if (lx != rx || lz != rz)
                {
                    sawPositivePortal = true;
                }
            }

            Assert.That(sawPositivePortal, Is.True);
        }

        [Test]
        public void Triangulation_PointContact_IsNotPortal()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            // Closed target with no outside walk-link must emit zero border portals.
            Assert.That(p.Triangulation.PortalCount, Is.EqualTo(0));
        }

        [Test]
        public void Triangulation_DisjointOutersInOneChart_BothComponentsTriangulate()
        {
            // U-bridge through halo: floors at (0,0) and (2,0) connect via row z=1 outside the clip.
            NavTriangleSurfaceSnapshot surface = MergeQuads(
                (0, 0, 100, 100, 1),
                (200, 0, 300, 100, 2),
                (0, 100, 100, 200, 3),
                (100, 100, 200, 200, 4),
                (200, 100, 300, 200, 5));
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 2);
            var contourSpec = new LayeredSpanContourSpec(
                maxSimplificationErrorCm: 0,
                targetMinXcm: 0,
                targetMinZcm: 0,
                targetMaxXcm: 300,
                targetMaxZcm: 100);
            var triSpec = FullTriSpec(grid, targetMinXcm: 0, targetMinZcm: 0, targetMaxXcm: 300, targetMaxZcm: 100);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            PipelineResult p = RunPipeline(surface, indices, grid, maxClimbCm: 0, contourSpec, triSpec);
            Assert.That(p.Regions.RegionCount, Is.EqualTo(1));
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 50, 50, strictInterior: false), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 250, 50, strictInterior: false), Is.True);
            Assert.That(PointInAnyTriangle(p.Triangulation, 150, 50), Is.False);

            // Contour may emit the clipped pads as separate charts; fold them into one chart with two
            // outer rings so the triangulation multi-outer path is contract-tested.
            int ringCount = p.Contours.RingCount;
            Assert.That(ringCount, Is.GreaterThanOrEqualTo(2));
            int outerRings = 0;
            for (int r = 0; r < ringCount; r++)
            {
                if (p.Contours.RingKinds[r] == LayeredSpanContourRingKind.Outer)
                {
                    outerRings++;
                }
            }

            Assert.That(outerRings, Is.GreaterThanOrEqualTo(2));

            p.Contours.SetChartCount(1);
            p.Contours.MutableChartRingOffsets[0] = 0;
            p.Contours.MutableChartRingOffsets[1] = ringCount;
            p.Contours.MutableChartRegionIds[0] = p.Regions.RegionCount > 0 ? 0 : 0;
            p.Contours.MutableChartAreaIds[0] = 1;
            for (int r = 0; r < ringCount; r++)
            {
                p.Contours.MutableRingChartIds[r] = 0;
            }

            var tri = CreateTriangulationScratch();
            LayeredSpanTriangulationBuilder.Build(
                surface,
                p.Raw,
                p.Walkability,
                p.Sheets,
                p.Links,
                p.Radius,
                p.Regions,
                p.Contours,
                in grid,
                in triSpec,
                tri);

            Assert.That(tri.HasPublishedContent, Is.True);
            Assert.That(PointInAnyTriangle(tri, 50, 50, strictInterior: false), Is.True);
            Assert.That(PointInAnyTriangle(tri, 250, 50, strictInterior: false), Is.True);
            Assert.That(PointInAnyTriangle(tri, 150, 50), Is.False);
            for (int i = 0; i < tri.VertexCount; i++)
            {
                Assert.That(tri.VertexChartIds[i], Is.EqualTo(0));
                Assert.That(tri.VertexZcm[i], Is.LessThanOrEqualTo(100));
            }
        }

        [Test]
        public void Triangulation_MultiChart_RingBoundsDoNotReadFollowingChartVertices()
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
            Assert.That(p.Contours.ChartCount, Is.EqualTo(2));
            Assert.That(p.Contours.RingCount, Is.EqualTo(2));

            int chart0RingEnd = p.Contours.RingOffsets[1];
            int chart1RingStart = p.Contours.RingOffsets[1];
            Assert.That(chart0RingEnd, Is.EqualTo(chart1RingStart));
            Assert.That(p.Contours.RingOffsets[2], Is.EqualTo(p.Contours.VertexCount));

            for (int i = 0; i < p.Triangulation.VertexCount; i++)
            {
                if (p.Triangulation.VertexChartIds[i] == 0)
                {
                    Assert.That(p.Triangulation.VertexYcm[i], Is.EqualTo(0));
                }
                else
                {
                    Assert.That(p.Triangulation.VertexChartIds[i], Is.EqualTo(1));
                    Assert.That(p.Triangulation.VertexYcm[i], Is.EqualTo(500));
                }
            }
        }

        [Test]
        public void Triangulation_TempConstraintFlags_DoNotCorruptPriorChartFlags()
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
            Assert.That(p.Triangulation.ConstrainedEdgeCount, Is.GreaterThan(0));

            for (int ring = 0; ring < p.Contours.RingCount; ring++)
            {
                int chart = p.Contours.RingChartIds[ring];
                int start = p.Contours.RingOffsets[ring];
                int count = RingVertexCount(p.Contours, ring);
                for (int i = 0; i < count; i++)
                {
                    int j = i + 1 == count ? 0 : i + 1;
                    int x0 = p.Contours.VertexXcm[start + i];
                    int z0 = p.Contours.VertexZcm[start + i];
                    int x1 = p.Contours.VertexXcm[start + j];
                    int z1 = p.Contours.VertexZcm[start + j];
                    int va = FindPublishedVertex(p.Triangulation, chart, x0, z0);
                    int vb = FindPublishedVertex(p.Triangulation, chart, x1, z1);
                    Assert.That(va, Is.GreaterThanOrEqualTo(0));
                    Assert.That(vb, Is.GreaterThanOrEqualTo(0));
                    Assert.That(HasConstrainedUndirectedEdge(p.Triangulation, va, vb), Is.True);
                    Assert.That(ConstrainedEdgeFlag(p.Triangulation, va, vb), Is.EqualTo((byte)1));
                }
            }
        }

        [Test]
        public void Triangulation_BorderPortals_WalkLinkCrossingContract()
        {
            // a) surface ending exactly at target boundary with no outside walk-link => 0 portals
            var closed = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var closedGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult closedP = RunPipeline(closed, new[] { 0, 1 }, closedGrid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(closedP.Triangulation.PortalCount, Is.EqualTo(0));

            // b) one crossing surface => exactly 1 positive-length portal (east face of middle cell)
            var crossSurface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 200, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
            var crossGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            var crossContour = new LayeredSpanContourSpec(
                maxSimplificationErrorCm: 0,
                targetMinXcm: 0,
                targetMinZcm: 0,
                targetMaxXcm: 100,
                targetMaxZcm: 100);
            var crossTri = FullTriSpec(crossGrid, 0, 0, 100, 100);
            PipelineResult cross = RunPipeline(crossSurface, new[] { 0 }, crossGrid, maxClimbCm: 0, crossContour, crossTri);
            Assert.That(cross.Triangulation.PortalCount, Is.EqualTo(1));
            Assert.That(
                cross.Triangulation.PortalLeftXcm[0] != cross.Triangulation.PortalRightXcm[0] ||
                cross.Triangulation.PortalLeftZcm[0] != cross.Triangulation.PortalRightZcm[0],
                Is.True);

            // c) stacked low/high crossing => exactly 2 distinct portals with distinct Y
            var stacked = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 200, 0, 0, 200, 0 },
                vertexYcm: new[] { 0, 0, 0, 500, 500, 500 },
                vertexZcm: new[] { 0, 0, 100, 0, 0, 100 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            PipelineResult stackedP = RunPipeline(stacked, new[] { 0, 1 }, crossGrid, maxClimbCm: 0, crossContour, crossTri);
            Assert.That(stackedP.Triangulation.PortalCount, Is.EqualTo(2));
            Assert.That(stackedP.Triangulation.PortalLeftYcm[0], Is.Not.EqualTo(stackedP.Triangulation.PortalLeftYcm[1]));

            // d) crossing whose endpoint fails agent radius => 0 portals
            PipelineResult radiusReject = RunPipelineWithAgentRadius(
                crossSurface,
                new[] { 0 },
                crossGrid,
                maxClimbCm: 0,
                crossContour,
                crossTri,
                agentRadiusCm: int.MaxValue);
            Assert.That(radiusReject.Triangulation.PortalCount, Is.EqualTo(0));

            // e) no duplicate records on the clip case
            var clipSurface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 300, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
            var clipGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            var clipContour = new LayeredSpanContourSpec(0, 100, 0, 200, 100);
            var clipTriSpec = FullTriSpec(clipGrid, 100, 0, 200, 100);
            PipelineResult clip = RunPipeline(clipSurface, new[] { 0 }, clipGrid, maxClimbCm: 0, clipContour, clipTriSpec);
            AssertNoDuplicatePortals(clip.Triangulation);
        }

        [Test]
        public void Triangulation_CrossChartSeamAdjacency_ExactIntervalRequired()
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
            PipelineResult positive = RunPipeline(surface, new[] { 0, 1, 2, 3 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(positive.Contours.SeamCount, Is.GreaterThan(0));
            Assert.That(CountCrossChartAdjacency(positive.Triangulation), Is.GreaterThan(0));

            // Stacked XYZ mismatch: same XZ seam geometry at different Y must not adjoin.
            var stacked = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200, 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0, 500, 500, 500, 500, 500, 500, 500, 500 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100, 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: new[] { 0, 1, 4, 5, 8, 9, 12, 13 },
                triB: new[] { 1, 3, 5, 7, 9, 11, 13, 15 },
                triC: new[] { 2, 2, 6, 6, 10, 10, 14, 14 },
                triAreaIds: new byte[] { 1, 1, 2, 2, 3, 3, 4, 4 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags });
            PipelineResult stackedP = RunPipeline(stacked, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            AssertNoCrossYLayerAdjacency(stackedP.Triangulation);
        }

        [Test]
        public void Triangulation_StaleOrDifferentScratchProvenance_RejectedClearsOutput()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult p = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            Assert.That(p.Triangulation.HasPublishedContent, Is.True);

            var raw2 = new LayeredSpanScratch(grid.ColumnCount, 64);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in grid, raw2);
            Assert.That(raw2.SpanCount, Is.EqualTo(p.Raw.SpanCount));
            Assert.That(p.Contours.WasBuiltFrom(raw2, p.Walkability, p.Sheets, p.Links, p.Radius, p.Regions), Is.False);

            var tri = CreateTriangulationScratch();
            var triSpec = FullTriSpec(grid);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanTriangulationBuilder.Build(
                    surface,
                    raw2,
                    p.Walkability,
                    p.Sheets,
                    p.Links,
                    p.Radius,
                    p.Regions,
                    p.Contours,
                    in grid,
                    in triSpec,
                    tri));
            Assert.That(ex!.Message, Does.Contain("identity and content generation"));
            Assert.That(tri.HasPublishedContent, Is.False);
            Assert.That(tri.TriangleCount, Is.EqualTo(0));
            Assert.That(tri.VertexCount, Is.EqualTo(0));
        }

        [Test]
        public void Triangulation_EachCapacityFailsIndependently_OwnerRequiredClearsOutput()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            PipelineResult square = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);

            AssertCapacityFailure(
                square,
                surface,
                grid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: 0,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.vertexCapacity",
                "required");

            AssertCapacityFailure(
                square,
                surface,
                grid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: 0,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.triangleCapacity",
                "required");

            AssertCapacityFailure(
                square,
                surface,
                grid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: 0,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.constrainedEdgeCapacity",
                "required");

            AssertCapacityFailure(
                square,
                surface,
                grid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: 0,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.polygonVertexCapacity",
                "required");

            AssertCapacityFailure(
                square,
                surface,
                grid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: 0,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.adjacencyEdgeCapacity",
                "required");

            NavTriangleSurfaceSnapshot donutSurface = RasterAnnulusEightQuads();
            var donutGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            int[] donutIndices = new int[donutSurface.TriangleCount];
            for (int i = 0; i < donutIndices.Length; i++)
            {
                donutIndices[i] = i;
            }

            PipelineResult donut = RunPipeline(donutSurface, donutIndices, donutGrid, maxClimbCm: 0, maxErrorCm: 0);

            AssertCapacityFailure(
                donut,
                donutSurface,
                donutGrid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: 0,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.bridgeCandidateCapacity",
                "required");

            AssertCapacityFailure(
                donut,
                donutSurface,
                donutGrid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: 3,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.polygonVertexCapacity",
                "required");

            AssertCapacityFailure(
                donut,
                donutSurface,
                donutGrid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: 0,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.ringWorkCapacity",
                "required");

            AssertCapacityFailure(
                donut,
                donutSurface,
                donutGrid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: generous.borderPortalCapacity,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: 0),
                "LayeredSpanTriangulationScratch.temporaryConstraintFlagCapacity",
                "required");

            var clipSurface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 300, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
            var clipGrid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 1);
            var clipContour = new LayeredSpanContourSpec(
                maxSimplificationErrorCm: 0,
                targetMinXcm: 100,
                targetMinZcm: 0,
                targetMaxXcm: 200,
                targetMaxZcm: 100);
            var clipTri = FullTriSpec(clipGrid, targetMinXcm: 100, targetMinZcm: 0, targetMaxXcm: 200, targetMaxZcm: 100);
            PipelineResult clip = RunPipeline(clipSurface, new[] { 0 }, clipGrid, maxClimbCm: 0, clipContour, clipTri);
            Assert.That(clip.Triangulation.PortalCount, Is.GreaterThan(0));

            AssertCapacityFailure(
                clip,
                clipSurface,
                clipGrid,
                generous => new LayeredSpanTriangulationScratch(
                    vertexCapacity: generous.vertexCapacity,
                    triangleCapacity: generous.triangleCapacity,
                    constrainedEdgeCapacity: generous.constrainedEdgeCapacity,
                    borderPortalCapacity: 0,
                    polygonVertexCapacity: generous.polygonVertexCapacity,
                    adjacencyEdgeCapacity: generous.adjacencyEdgeCapacity,
                    bridgeCandidateCapacity: generous.bridgeCandidateCapacity,
                    ringWorkCapacity: generous.ringWorkCapacity,
                    temporaryConstraintFlagCapacity: generous.temporaryConstraintFlagCapacity),
                "LayeredSpanTriangulationScratch.borderPortalCapacity",
                "required",
                clipTri);
        }

        [Test]
        public void Triangulation_FlatSixtyFourCellTileAtOpenWorldOrigin_WithHaloNeighbors_Succeeds()
        {
            // Regression: open-world 64-cell flat chunk with halo neighbor triangles produces a
            // 256-vertex axis-aligned outer ring (mandatory target-border samples every cell).
            // Ear clipping must strip exact-collinear actives or it collapses to a zero-area chain.
            const int cell = 100;
            const int cells = 64;
            const int halo = 2;
            int tileCm = cells * cell;
            int chunkX = 28;
            int chunkZ = 28;
            int originX = chunkX * tileCm;
            int originZ = chunkZ * tileCm;

            var chunks = new List<(int cx, int cz)>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    chunks.Add((chunkX + dx, chunkZ + dz));
                }
            }

            int vertCount = chunks.Count * 4;
            int triCount = chunks.Count * 2;
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
            foreach ((int cx, int cz) in chunks)
            {
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

            var surface = new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, tflags);
            int cols = cells + (2 * halo);
            var grid = new LayeredSpanRasterGridSpec(originX - (halo * cell), originZ - (halo * cell), cell, cols, cols);
            var contourSpec = new LayeredSpanContourSpec(0, originX, originZ, originX + tileCm, originZ + tileCm);
            var triSpec = new LayeredSpanTriangulationSpec(
                LayeredSpanHeightRounding.RoundHalfAwayFromZero,
                100_000,
                originX,
                originZ,
                originX + tileCm,
                originZ + tileCm,
                cell,
                cell);

            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            const int spanCap = 16384;
            var raw = new LayeredSpanScratch(grid.ColumnCount, spanCap);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, spanCap, spanCap);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, spanCap);
            var links = new LayeredSpanWalkLinkScratch(spanCap, spanCap * 4);
            var radius = new LayeredSpanRadiusFieldScratch(spanCap, spanCap, spanCap * 4);
            var regions = new LayeredSpanRegionScratch(spanCap, 4096);
            var contours = new LayeredSpanContourScratch(
                grid.ColumnCount, spanCap, spanCap, 1024, 2048, 16384, 16384, 4096, spanCap * 4, spanCap * 4, 4096);
            var triangulation = new LayeredSpanTriangulationScratch(
                16384, 32768, 32768, 4096, 16384, 98304, 16384, 2048, 32768);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            RunOnce(
                surface,
                indices,
                in grid,
                DefaultWalk,
                new LayeredSpanWalkLinkSpec(40),
                in contourSpec,
                in triSpec,
                agentRadiusCm: 30,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours,
                triangulation);
            sw.Stop();

            Assert.That(contours.HasPublishedContent, Is.True);
            Assert.That(contours.ChartCount, Is.EqualTo(1));
            Assert.That(contours.RingCount, Is.EqualTo(1));
            Assert.That(contours.RingKinds[0], Is.EqualTo(LayeredSpanContourRingKind.Outer));
            int ringVerts = contours.RingOffsets[1] - contours.RingOffsets[0];
            Assert.That(ringVerts, Is.EqualTo(256), "Mandatory target-border cell samples yield 64 verts/side.");
            Assert.That(triangulation.HasPublishedContent, Is.True);
            Assert.That(triangulation.TriangleCount, Is.EqualTo(2));
            Assert.That(triangulation.VertexCount, Is.GreaterThanOrEqualTo(4));
            // Published vertices include mandatory border samples; unique XZ corners must remain.
            var cornerKeys = new HashSet<(int X, int Z)>();
            for (int i = 0; i < triangulation.VertexCount; i++)
            {
                cornerKeys.Add((triangulation.VertexXcm[i], triangulation.VertexZcm[i]));
            }

            Assert.That(cornerKeys.Contains((originX, originZ)), Is.True);
            Assert.That(cornerKeys.Contains((originX + tileCm, originZ)), Is.True);
            Assert.That(cornerKeys.Contains((originX + tileCm, originZ + tileCm)), Is.True);
            Assert.That(cornerKeys.Contains((originX, originZ + tileCm)), Is.True);
            Assert.That(
                sw.ElapsedMilliseconds,
                Is.LessThan(15_000),
                $"64-cell flat tile bake stages must stay bounded; measured {sw.ElapsedMilliseconds} ms.");
            TestContext.WriteLine(
                $"Flat64 open-world tile pipeline measured {sw.ElapsedMilliseconds} ms; " +
                $"contourVerts={ringVerts}; tris={triangulation.TriangleCount}; verts={triangulation.VertexCount}.");
        }

        [Test]
        public void Triangulation_OpenWorldOriginTileZero_WithInteriorHole_DoesNotStallEarClip()
        {
            // Exact acceptance seam: open-world tile (0,0) at origin=(-204800,-204800),
            // cell=100, halo=2, maxSimplificationError=0, agentRadius=30, with an interior hole
            // (parking-wall footprint). Bridged-hole ear clipping must strip collinear/spike
            // actives without dissolving bridge duplicates or filling the hole.
            const int cell = 100;
            const int cells = 64;
            const int halo = 2;
            const int originX = -204800;
            const int originZ = -204800;
            int tileCm = cells * cell;
            int targetMaxX = originX + tileCm;
            int targetMaxZ = originZ + tileCm;

            // Parking-like hole around (-200000,-200000) covering ~5x5 cells after radius.
            const int holeMinCellX = 44;
            const int holeMaxCellX = 52;
            const int holeMinCellZ = 44;
            const int holeMaxCellZ = 52;

            var quads = new List<(int x0, int z0, int x1, int z1)>();
            for (int cz = -1; cz <= cells; cz++)
            {
                for (int cx = -1; cx <= cells; cx++)
                {
                    bool inTarget = cx >= 0 && cx < cells && cz >= 0 && cz < cells;
                    bool inHole = inTarget &&
                                  cx >= holeMinCellX && cx < holeMaxCellX &&
                                  cz >= holeMinCellZ && cz < holeMaxCellZ;
                    if (inHole)
                    {
                        continue;
                    }

                    int x0 = originX + cx * cell;
                    int z0 = originZ + cz * cell;
                    quads.Add((x0, z0, x0 + cell, z0 + cell));
                }
            }

            int vertCount = quads.Count * 4;
            int triCount = quads.Count * 2;
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
            foreach ((int x0, int z0, int x1, int z1) in quads)
            {
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

            var surface = new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, tflags);
            int cols = cells + (2 * halo);
            var grid = new LayeredSpanRasterGridSpec(originX - (halo * cell), originZ - (halo * cell), cell, cols, cols);
            var contourSpec = new LayeredSpanContourSpec(0, originX, originZ, targetMaxX, targetMaxZ);
            var triSpec = new LayeredSpanTriangulationSpec(
                LayeredSpanHeightRounding.RoundHalfAwayFromZero,
                100_000,
                originX,
                originZ,
                targetMaxX,
                targetMaxZ,
                cell,
                cell);

            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            const int spanCap = 16384;
            var raw = new LayeredSpanScratch(grid.ColumnCount, spanCap);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, spanCap, spanCap);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, spanCap);
            var links = new LayeredSpanWalkLinkScratch(spanCap, spanCap * 4);
            var radius = new LayeredSpanRadiusFieldScratch(spanCap, spanCap, spanCap * 4);
            var regions = new LayeredSpanRegionScratch(spanCap, 4096);
            var contours = new LayeredSpanContourScratch(
                grid.ColumnCount, spanCap, spanCap, 1024, 2048, 16384, 16384, 4096, spanCap * 4, spanCap * 4, 4096);
            var triangulation = new LayeredSpanTriangulationScratch(
                16384, 32768, 32768, 4096, 16384, 98304, 16384, 2048, 32768);

            Assert.DoesNotThrow(() => RunOnce(
                surface,
                indices,
                in grid,
                DefaultWalk,
                new LayeredSpanWalkLinkSpec(40),
                in contourSpec,
                in triSpec,
                agentRadiusCm: 30,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours,
                triangulation));

            Assert.That(contours.HasPublishedContent, Is.True);
            Assert.That(contours.ChartCount, Is.EqualTo(1));
            int holeRings = 0;
            for (int r = 0; r < contours.RingCount; r++)
            {
                if (contours.RingKinds[r] == LayeredSpanContourRingKind.Hole)
                {
                    holeRings++;
                }
            }

            Assert.That(holeRings, Is.EqualTo(1), "Interior parking-like omission must produce one hole ring.");
            Assert.That(triangulation.HasPublishedContent, Is.True);
            Assert.That(triangulation.TriangleCount, Is.GreaterThan(0));

            int holeRing = -1;
            for (int r = 0; r < contours.RingCount; r++)
            {
                if (contours.RingKinds[r] == LayeredSpanContourRingKind.Hole)
                {
                    holeRing = r;
                    break;
                }
            }

            Assert.That(holeRing, Is.GreaterThanOrEqualTo(0));
            int hStart = contours.RingOffsets[holeRing];
            int hCount = contours.RingOffsets[holeRing + 1] - hStart;
            long sumX = 0;
            long sumZ = 0;
            for (int i = 0; i < hCount; i++)
            {
                sumX += contours.VertexXcm[hStart + i];
                sumZ += contours.VertexZcm[hStart + i];
            }

            int holeProbeX = (int)(sumX / hCount);
            int holeProbeZ = (int)(sumZ / hCount);
            Assert.That(
                PointInAnyTriangle(triangulation, holeProbeX, holeProbeZ, strictInterior: false),
                Is.False,
                $"Hole probe ({holeProbeX},{holeProbeZ}) from contour ring must remain excluded.");
            for (int z = holeMinCellZ; z < holeMaxCellZ; z++)
            {
                for (int x = holeMinCellX; x < holeMaxCellX; x++)
                {
                    int sampleX = originX + (x * cell) + (cell / 2);
                    int sampleZ = originZ + (z * cell) + (cell / 2);
                    Assert.That(
                        PointInAnyTriangle(triangulation, sampleX, sampleZ, strictInterior: false),
                        Is.False,
                        $"Hole cell center ({sampleX},{sampleZ}) must remain excluded.");
                }
            }

            Assert.That(
                PointInAnyTriangle(triangulation, originX + cell / 2, originZ + cell / 2, strictInterior: false),
                Is.True,
                "Walkable corner of tile (0,0) must remain covered.");
            TestContext.WriteLine(
                $"Open-world tile(0,0)+hole: rings={contours.RingCount}; holeVerts={hCount}; " +
                $"tris={triangulation.TriangleCount}; verts={triangulation.VertexCount}; " +
                $"holeProbe=({holeProbeX},{holeProbeZ}).");
        }

        [Test]
        public void Triangulation_ExtremeWorldOrigins_NoOverflow()
        {
            PipelineResult nearMin = RunRectangleAtOrigin(int.MinValue + 1_000, int.MinValue + 2_000);
            PipelineResult nearMax = RunRectangleAtOrigin(int.MaxValue - 1_200, int.MaxValue - 1_100);

            Assert.That(nearMin.Triangulation.HasPublishedContent, Is.True);
            Assert.That(nearMax.Triangulation.HasPublishedContent, Is.True);
            AssertRelativeTriChannelsEqual(
                nearMin.Triangulation,
                nearMax.Triangulation,
                nearMin.Triangulation.VertexXcm[0],
                nearMin.Triangulation.VertexZcm[0],
                nearMax.Triangulation.VertexXcm[0],
                nearMax.Triangulation.VertexZcm[0]);
            Assert.That(nearMin.Triangulation.TriangleCount, Is.EqualTo(nearMax.Triangulation.TriangleCount));

            PipelineResult donutMin = RunAnnulusAtOrigin(int.MinValue + 5_000, int.MinValue + 6_000);
            PipelineResult donutMax = RunAnnulusAtOrigin(int.MaxValue - 5_500, int.MaxValue - 5_400);
            Assert.That(donutMin.Triangulation.HasPublishedContent, Is.True);
            Assert.That(donutMax.Triangulation.HasPublishedContent, Is.True);
            Assert.That(donutMin.Contours.RingCount, Is.EqualTo(2));
            Assert.That(donutMax.Contours.RingCount, Is.EqualTo(2));
            Assert.That(donutMin.Triangulation.TriangleCount, Is.EqualTo(donutMax.Triangulation.TriangleCount));
            Assert.That(
                PointInAnyTriangle(donutMin.Triangulation, int.MinValue + 5_000 + 150, int.MinValue + 6_000 + 150),
                Is.False);
            Assert.That(
                PointInAnyTriangle(donutMax.Triangulation, int.MaxValue - 5_500 + 150, int.MaxValue - 5_400 + 150),
                Is.False);
        }

        [Test]
        public void Triangulation_InvalidTopologyOrLocalRange_FailsExplicitly()
        {
            var surface = QuadFloor(0, 0, 100, 100, y: 0, area: 1, stable: 1);
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 1, 1);
            var zeroFlipSpec = FullTriSpec(grid, maxLawsonFlipCount: 0);
            PipelineResult cocircular = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0, zeroFlipSpec);
            Assert.That(cocircular.Triangulation.HasPublishedContent, Is.True);
            Assert.That(cocircular.Triangulation.TriangleCount, Is.EqualTo(2));

            PipelineResult baseline = RunPipeline(surface, new[] { 0, 1 }, grid, maxClimbCm: 0, maxErrorCm: 0);
            var tinyTri = new LayeredSpanTriangulationScratch(
                vertexCapacity: 1024,
                triangleCapacity: 0,
                constrainedEdgeCapacity: 2048,
                borderPortalCapacity: 256,
                polygonVertexCapacity: 1024,
                adjacencyEdgeCapacity: 6144,
                bridgeCandidateCapacity: 4096,
                ringWorkCapacity: 256,
                temporaryConstraintFlagCapacity: 2048);
            var triSpec = FullTriSpec(grid);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanTriangulationBuilder.Build(
                    surface,
                    baseline.Raw,
                    baseline.Walkability,
                    baseline.Sheets,
                    baseline.Links,
                    baseline.Radius,
                    baseline.Regions,
                    baseline.Contours,
                    in grid,
                    in triSpec,
                    tinyTri));
            Assert.That(ex!.Message, Does.Contain("LayeredSpanTriangulationScratch.triangleCapacity"));
            Assert.That(ex.Message, Does.Contain("required"));
            Assert.That(tinyTri.HasPublishedContent, Is.False);
            Assert.That(tinyTri.TriangleCount, Is.EqualTo(0));

            var raw2 = new LayeredSpanScratch(grid.ColumnCount, 64);
            LayeredSpanRasterizer.Rasterize(surface, new[] { 0, 1 }, in grid, raw2);
            var tri2 = CreateTriangulationScratch();
            ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanTriangulationBuilder.Build(
                    surface,
                    raw2,
                    baseline.Walkability,
                    baseline.Sheets,
                    baseline.Links,
                    baseline.Radius,
                    baseline.Regions,
                    baseline.Contours,
                    in grid,
                    in triSpec,
                    tri2));
            Assert.That(ex!.Message, Does.Contain("identity and content generation"));
            Assert.That(tri2.HasPublishedContent, Is.False);
        }

        [Test]
        public void Triangulation_WarmedFullChain_AllocatesZeroBytes()
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
            var triangulation = CreateTriangulationScratch();
            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            var contourSpec = FullTarget(grid, maxErrorCm: 0);
            var triSpec = FullTriSpec(grid);
            int[] indices = { 0, 1, 2, 3 };

            for (int i = 0; i < 64; i++)
            {
                RunOnce(
                    surface,
                    indices,
                    grid,
                    DefaultWalk,
                    linkSpec,
                    contourSpec,
                    triSpec,
                    raw,
                    walk,
                    sheets,
                    links,
                    radius,
                    regions,
                    contours,
                    triangulation);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(
                    surface,
                    indices,
                    grid,
                    DefaultWalk,
                    linkSpec,
                    contourSpec,
                    triSpec,
                    raw,
                    walk,
                    sheets,
                    links,
                    radius,
                    regions,
                    contours,
                    triangulation);
                if (triangulation.TriangleCount < 0)
                {
                    throw new InvalidOperationException("Unreachable guard.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0),
                $"Warmed layered-span chain through triangulation allocated {allocated} bytes.");
            Assert.That(triangulation.HasPublishedContent, Is.True);
            Assert.That(triangulation.TriangleCount, Is.GreaterThan(0));
        }

        [Test]
        public void Triangulation_WarmedHoleCellMesh_AllocatesZeroBytes()
        {
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads();
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 3, 3);
            var raw = new LayeredSpanScratch(grid.ColumnCount, 128);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 128, 128);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 128);
            var links = new LayeredSpanWalkLinkScratch(128, 256);
            var radius = new LayeredSpanRadiusFieldScratch(128, 128, 256);
            var regions = new LayeredSpanRegionScratch(128, 64);
            var contours = CreateContourScratch(grid.ColumnCount);
            var triangulation = CreateTriangulationScratch();
            var linkSpec = new LayeredSpanWalkLinkSpec(0);
            var contourSpec = FullTarget(grid, maxErrorCm: 0);
            var triSpec = FullTriSpec(grid);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            for (int i = 0; i < 64; i++)
            {
                RunOnce(
                    surface,
                    indices,
                    grid,
                    DefaultWalk,
                    linkSpec,
                    contourSpec,
                    triSpec,
                    raw,
                    walk,
                    sheets,
                    links,
                    radius,
                    regions,
                    contours,
                    triangulation);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                RunOnce(
                    surface,
                    indices,
                    grid,
                    DefaultWalk,
                    linkSpec,
                    contourSpec,
                    triSpec,
                    raw,
                    walk,
                    sheets,
                    links,
                    radius,
                    regions,
                    contours,
                    triangulation);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0),
                $"Warmed layered-span hole-cell chain allocated {allocated} bytes.");
            Assert.That(triangulation.HasPublishedContent, Is.True);
            Assert.That(triangulation.TriangleCount, Is.GreaterThan(0));
            Assert.That(PointInAnyTriangle(triangulation, 150, 150), Is.False);
        }

        private static NavTriangleSurfaceSnapshot RasterTwoHoleFiveByThree()
        {
            // 5x3 cells; leave (1,1) and (3,1) empty → two holes in one connected ring.
            const int maxCells = 13;
            var vx = new int[maxCells * 4];
            var vy = new int[maxCells * 4];
            var vz = new int[maxCells * 4];
            var a = new int[maxCells * 2];
            var b = new int[maxCells * 2];
            var c = new int[maxCells * 2];
            var areas = new byte[maxCells * 2];
            var stables = new int[maxCells * 2];
            var flags = new NavTriangleSurfaceFlags[maxCells * 2];
            int cell = 0;
            int stable = 1;
            for (int cz = 0; cz < 3; cz++)
            {
                for (int cx = 0; cx < 5; cx++)
                {
                    if ((cx == 1 && cz == 1) || (cx == 3 && cz == 1))
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

            int triCount = cell * 2;
            int vertCount = cell * 4;
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: vx.AsSpan(0, vertCount).ToArray(),
                vertexYcm: vy.AsSpan(0, vertCount).ToArray(),
                vertexZcm: vz.AsSpan(0, vertCount).ToArray(),
                triA: a.AsSpan(0, triCount).ToArray(),
                triB: b.AsSpan(0, triCount).ToArray(),
                triC: c.AsSpan(0, triCount).ToArray(),
                triAreaIds: areas.AsSpan(0, triCount).ToArray(),
                triStableIds: stables.AsSpan(0, triCount).ToArray(),
                triFlags: flags.AsSpan(0, triCount).ToArray());
        }

        private static NavTriangleSurfaceSnapshot RasterAnnulusEightQuads(int shiftXcm = 0, int shiftZcm = 0)
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

                    int minX = cx * 100 + shiftXcm;
                    int minZ = cz * 100 + shiftZcm;
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

        private static NavTriangleSurfaceSnapshot RasterNotchedOuterRing()
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
                    if (cx == 2 && cz == 0)
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

        private static NavTriangleSurfaceSnapshot LShapeThreeQuads()
        {
            return MergeQuads(
                (0, 0, 100, 100, 1),
                (100, 0, 200, 100, 2),
                (0, 100, 100, 200, 3));
        }

        private static NavTriangleSurfaceSnapshot MergeQuads(params (int minX, int minZ, int maxX, int maxZ, int stable)[] quads)
        {
            int quadCount = quads.Length;
            var vx = new int[quadCount * 4];
            var vy = new int[quadCount * 4];
            var vz = new int[quadCount * 4];
            var a = new int[quadCount * 2];
            var b = new int[quadCount * 2];
            var c = new int[quadCount * 2];
            var areas = new byte[quadCount * 2];
            var stables = new int[quadCount * 2];
            var flags = new NavTriangleSurfaceFlags[quadCount * 2];

            for (int q = 0; q < quadCount; q++)
            {
                (int minX, int minZ, int maxX, int maxZ, int stable) = quads[q];
                int v = q * 4;
                vx[v] = minX; vy[v] = 0; vz[v] = minZ;
                vx[v + 1] = maxX; vy[v + 1] = 0; vz[v + 1] = minZ;
                vx[v + 2] = minX; vy[v + 2] = 0; vz[v + 2] = maxZ;
                vx[v + 3] = maxX; vy[v + 3] = 0; vz[v + 3] = maxZ;
                int t = q * 2;
                a[t] = v;
                b[t] = v + 1;
                c[t] = v + 2;
                a[t + 1] = v + 1;
                b[t + 1] = v + 3;
                c[t + 1] = v + 2;
                areas[t] = 1;
                areas[t + 1] = 1;
                stables[t] = stable * 10;
                stables[t + 1] = stable * 10 + 1;
                flags[t] = FloorFlags;
                flags[t + 1] = FloorFlags;
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

        private static PipelineResult RunAnnulusAtOrigin(int originX, int originZ)
        {
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads(originX, originZ);
            var grid = new LayeredSpanRasterGridSpec(originX, originZ, 100, 3, 3);
            int[] indices = new int[surface.TriangleCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            return RunPipeline(surface, indices, grid, maxClimbCm: 0, maxErrorCm: 0);
        }

        private static void AssertCapacityFailure(
            PipelineResult pipeline,
            NavTriangleSurfaceSnapshot surface,
            LayeredSpanRasterGridSpec grid,
            Func<(int vertexCapacity, int triangleCapacity, int constrainedEdgeCapacity, int borderPortalCapacity, int polygonVertexCapacity, int adjacencyEdgeCapacity, int bridgeCandidateCapacity, int ringWorkCapacity, int temporaryConstraintFlagCapacity), LayeredSpanTriangulationScratch> makeScratch,
            string capacityToken,
            string requiredToken,
            LayeredSpanTriangulationSpec? triSpecOverride = null)
        {
            var generous = (
                vertexCapacity: 1024,
                triangleCapacity: 2048,
                constrainedEdgeCapacity: 2048,
                borderPortalCapacity: 256,
                polygonVertexCapacity: 1024,
                adjacencyEdgeCapacity: 6144,
                bridgeCandidateCapacity: 4096,
                ringWorkCapacity: 256,
                temporaryConstraintFlagCapacity: 2048);
            var scratch = makeScratch(generous);
            var triSpec = triSpecOverride ?? FullTriSpec(grid);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                LayeredSpanTriangulationBuilder.Build(
                    surface,
                    pipeline.Raw,
                    pipeline.Walkability,
                    pipeline.Sheets,
                    pipeline.Links,
                    pipeline.Radius,
                    pipeline.Regions,
                    pipeline.Contours,
                    in grid,
                    in triSpec,
                    scratch));
            Assert.That(ex!.Message, Does.Contain(capacityToken));
            Assert.That(ex.Message, Does.Contain(requiredToken));
            Assert.That(scratch.HasPublishedContent, Is.False);
            Assert.That(scratch.VertexCount, Is.EqualTo(0));
            Assert.That(scratch.TriangleCount, Is.EqualTo(0));
            Assert.That(scratch.ConstrainedEdgeCount, Is.EqualTo(0));
            Assert.That(scratch.PortalCount, Is.EqualTo(0));
        }

        private static void AssertRelativeTriChannelsEqual(
            LayeredSpanTriangulationScratch a,
            LayeredSpanTriangulationScratch b,
            int originAX,
            int originAZ,
            int originBX,
            int originBZ)
        {
            Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount));
            Assert.That(a.TriangleCount, Is.EqualTo(b.TriangleCount));
            Assert.That(a.ConstrainedEdgeCount, Is.EqualTo(b.ConstrainedEdgeCount));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That((long)a.VertexXcm[i] - originAX, Is.EqualTo((long)b.VertexXcm[i] - originBX));
                Assert.That((long)a.VertexZcm[i] - originAZ, Is.EqualTo((long)b.VertexZcm[i] - originBZ));
                Assert.That(a.VertexYcm[i], Is.EqualTo(b.VertexYcm[i]));
                Assert.That(a.VertexChartIds[i], Is.EqualTo(b.VertexChartIds[i]));
            }

            for (int i = 0; i < a.TriangleCount; i++)
            {
                Assert.That(a.TriChartIds[i], Is.EqualTo(b.TriChartIds[i]));
                Assert.That(a.TriRegionIds[i], Is.EqualTo(b.TriRegionIds[i]));
                Assert.That(a.TriAreaIds[i], Is.EqualTo(b.TriAreaIds[i]));
            }
        }

        private static void AssertLocallyDelaunay(LayeredSpanTriangulationScratch tri)
        {
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                CheckDelaunayEdge(t, 0, tri);
                CheckDelaunayEdge(t, 1, tri);
                CheckDelaunayEdge(t, 2, tri);
            }
        }

        private static void CheckDelaunayEdge(int tri, int slot, LayeredSpanTriangulationScratch triScratch)
        {
            int neighbor = slot switch
            {
                0 => triScratch.N0[tri],
                1 => triScratch.N1[tri],
                _ => triScratch.N2[tri]
            };
            if (neighbor < 0 || tri > neighbor)
            {
                return;
            }

            int va;
            int vb;
            int opp0;
            int opp1;
            switch (slot)
            {
                case 0:
                    va = triScratch.TriA[tri];
                    vb = triScratch.TriB[tri];
                    opp0 = triScratch.TriC[tri];
                    break;
                case 1:
                    va = triScratch.TriB[tri];
                    vb = triScratch.TriC[tri];
                    opp0 = triScratch.TriA[tri];
                    break;
                default:
                    va = triScratch.TriC[tri];
                    vb = triScratch.TriA[tri];
                    opp0 = triScratch.TriB[tri];
                    break;
            }

            opp1 = OppositeVertex(neighbor, va, vb, triScratch);
            if (HasConstrainedUndirectedEdge(triScratch, va, vb))
            {
                return;
            }

            int ax = triScratch.VertexXcm[va];
            int az = triScratch.VertexZcm[va];
            int bx = triScratch.VertexXcm[vb];
            int bz = triScratch.VertexZcm[vb];
            int cx = triScratch.VertexXcm[opp0];
            int cz = triScratch.VertexZcm[opp0];
            int dx = triScratch.VertexXcm[opp1];
            int dz = triScratch.VertexZcm[opp1];
            Assert.That(
                InCircleSign(ax, az, bx, bz, cx, cz, dx, dz),
                Is.LessThanOrEqualTo(0),
                $"Internal edge {va}-{vb} between tris {tri}/{neighbor} must be locally Delaunay.");
        }

        private static int OppositeVertex(
            int tri,
            int v0,
            int v1,
            LayeredSpanTriangulationScratch triScratch)
        {
            int a = triScratch.TriA[tri];
            int b = triScratch.TriB[tri];
            int c = triScratch.TriC[tri];
            if (a != v0 && a != v1)
            {
                return a;
            }

            if (b != v0 && b != v1)
            {
                return b;
            }

            return c;
        }

        private static void AssertNoCrossChartAdjacency(LayeredSpanTriangulationScratch tri)
        {
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                int chart = tri.TriChartIds[t];
                AssertNeighborChart(t, tri.N0[t], chart, tri);
                AssertNeighborChart(t, tri.N1[t], chart, tri);
                AssertNeighborChart(t, tri.N2[t], chart, tri);
            }
        }

        private static int CountCrossChartAdjacency(LayeredSpanTriangulationScratch tri)
        {
            int count = 0;
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                count += CountIfCrossChart(t, tri.N0[t], tri);
                count += CountIfCrossChart(t, tri.N1[t], tri);
                count += CountIfCrossChart(t, tri.N2[t], tri);
            }

            return count;
        }

        private static int CountIfCrossChart(int tri, int neighbor, LayeredSpanTriangulationScratch scratch)
        {
            if (neighbor < 0 || neighbor < tri)
            {
                return 0;
            }

            return scratch.TriChartIds[tri] != scratch.TriChartIds[neighbor] ? 1 : 0;
        }

        private static void AssertNoCrossYLayerAdjacency(LayeredSpanTriangulationScratch tri)
        {
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                AssertNeighborSameYLayer(t, tri.N0[t], tri);
                AssertNeighborSameYLayer(t, tri.N1[t], tri);
                AssertNeighborSameYLayer(t, tri.N2[t], tri);
            }
        }

        private static void AssertNeighborSameYLayer(int tri, int neighbor, LayeredSpanTriangulationScratch scratch)
        {
            if (neighbor < 0)
            {
                return;
            }

            int y0 = scratch.VertexYcm[scratch.TriA[tri]];
            int y1 = scratch.VertexYcm[scratch.TriA[neighbor]];
            Assert.That(y0, Is.EqualTo(y1), $"Triangles {tri}/{neighbor} must not adjoin across Y layers.");
        }

        private static byte ConstrainedEdgeFlag(LayeredSpanTriangulationScratch tri, int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            for (int i = 0; i < tri.ConstrainedEdgeCount; i++)
            {
                if (tri.ConstrainedEdgeA[i] == lo && tri.ConstrainedEdgeB[i] == hi)
                {
                    return tri.ConstrainedEdgeFlags[i];
                }
            }

            return 0;
        }

        private static void AssertNoDuplicatePortals(LayeredSpanTriangulationScratch tri)
        {
            for (int i = 0; i < tri.PortalCount; i++)
            {
                for (int j = i + 1; j < tri.PortalCount; j++)
                {
                    bool same =
                        tri.PortalSides[i] == tri.PortalSides[j] &&
                        tri.PortalLeftXcm[i] == tri.PortalLeftXcm[j] &&
                        tri.PortalLeftYcm[i] == tri.PortalLeftYcm[j] &&
                        tri.PortalLeftZcm[i] == tri.PortalLeftZcm[j] &&
                        tri.PortalRightXcm[i] == tri.PortalRightXcm[j] &&
                        tri.PortalRightYcm[i] == tri.PortalRightYcm[j] &&
                        tri.PortalRightZcm[i] == tri.PortalRightZcm[j] &&
                        tri.PortalSourceSpanIndices[i] == tri.PortalSourceSpanIndices[j] &&
                        tri.PortalNeighborSpanIndices[i] == tri.PortalNeighborSpanIndices[j] &&
                        tri.PortalClearanceCm[i] == tri.PortalClearanceCm[j];
                    bool reverse =
                        tri.PortalSides[i] == tri.PortalSides[j] &&
                        tri.PortalLeftXcm[i] == tri.PortalRightXcm[j] &&
                        tri.PortalLeftYcm[i] == tri.PortalRightYcm[j] &&
                        tri.PortalLeftZcm[i] == tri.PortalRightZcm[j] &&
                        tri.PortalRightXcm[i] == tri.PortalLeftXcm[j] &&
                        tri.PortalRightYcm[i] == tri.PortalLeftYcm[j] &&
                        tri.PortalRightZcm[i] == tri.PortalLeftZcm[j] &&
                        tri.PortalSourceSpanIndices[i] == tri.PortalSourceSpanIndices[j] &&
                        tri.PortalNeighborSpanIndices[i] == tri.PortalNeighborSpanIndices[j] &&
                        tri.PortalClearanceCm[i] == tri.PortalClearanceCm[j];
                    Assert.That(same || reverse, Is.False, $"Duplicate portal records at {i}/{j}.");
                }
            }
        }

        private static void AssertNeighborChart(
            int tri,
            int neighbor,
            int chart,
            LayeredSpanTriangulationScratch triScratch)
        {
            if (neighbor < 0)
            {
                return;
            }

            Assert.That(
                triScratch.TriChartIds[neighbor],
                Is.EqualTo(chart),
                $"Triangle {tri} must not have N adjacency across charts (neighbor {neighbor}).");
        }

        private static bool PointInAnyTriangle(LayeredSpanTriangulationScratch tri, int px, int pz)
            => PointInAnyTriangle(tri, px, pz, strictInterior: true);

        private static bool PointInAnyTriangle(
            LayeredSpanTriangulationScratch tri,
            int px,
            int pz,
            bool strictInterior)
        {
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                int a = tri.TriA[t];
                int b = tri.TriB[t];
                int c = tri.TriC[t];
                if (PointInTriangle(
                        px,
                        pz,
                        tri.VertexXcm[a],
                        tri.VertexZcm[a],
                        tri.VertexXcm[b],
                        tri.VertexZcm[b],
                        tri.VertexXcm[c],
                        tri.VertexZcm[c],
                        strictInterior))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            bool strictInterior)
        {
            Int128 o1 = Orient2(ax, az, bx, bz, px, pz);
            Int128 o2 = Orient2(bx, bz, cx, cz, px, pz);
            Int128 o3 = Orient2(cx, cz, ax, az, px, pz);
            if (strictInterior)
            {
                bool ccw = o1 > 0 && o2 > 0 && o3 > 0;
                bool cw = o1 < 0 && o2 < 0 && o3 < 0;
                return ccw || cw;
            }

            bool nonNeg = o1 >= 0 && o2 >= 0 && o3 >= 0;
            bool nonPos = o1 <= 0 && o2 <= 0 && o3 <= 0;
            return nonNeg || nonPos;
        }

        private static Int128 Orient2(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 bxL = (Int128)bx - ax;
            Int128 bzL = (Int128)bz - az;
            Int128 cxL = (Int128)cx - ax;
            Int128 czL = (Int128)cz - az;
            return (bxL * czL) - (bzL * cxL);
        }

        private static int InCircleSign(int ax, int az, int bx, int bz, int cx, int cz, int dx, int dz)
        {
            Int128 adx = (Int128)ax - dx;
            Int128 adz = (Int128)az - dz;
            Int128 bdx = (Int128)bx - dx;
            Int128 bdz = (Int128)bz - dz;
            Int128 cdx = (Int128)cx - dx;
            Int128 cdz = (Int128)cz - dz;
            Int128 abdet = (adx * bdz) - (bdx * adz);
            Int128 bcdet = (bdx * cdz) - (cdx * bdz);
            Int128 cadet = (cdx * adz) - (adx * cdz);
            Int128 alift = (adx * adx) + (adz * adz);
            Int128 blift = (bdx * bdx) + (bdz * bdz);
            Int128 clift = (cdx * cdx) + (cdz * cdz);
            Int128 det = (alift * bcdet) + (blift * cadet) + (clift * abdet);
            if (det > 0)
            {
                return 1;
            }

            if (det < 0)
            {
                return -1;
            }

            return 0;
        }

        private static int FindPublishedVertex(LayeredSpanTriangulationScratch tri, int chart, int x, int z)
        {
            for (int i = 0; i < tri.VertexCount; i++)
            {
                if (tri.VertexChartIds[i] == chart &&
                    tri.VertexXcm[i] == x &&
                    tri.VertexZcm[i] == z)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool HasConstrainedUndirectedEdge(LayeredSpanTriangulationScratch tri, int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            for (int i = 0; i < tri.ConstrainedEdgeCount; i++)
            {
                if (tri.ConstrainedEdgeA[i] == lo && tri.ConstrainedEdgeB[i] == hi)
                {
                    return true;
                }
            }

            return false;
        }

        private static int RingVertexCount(LayeredSpanContourScratch contours, int ring)
            => contours.RingOffsets[ring + 1] - contours.RingOffsets[ring];

        private static long PublicChecksum(in PipelineResult p)
        {
            long sum = p.Triangulation.VertexCount;
            sum = (sum * 1_000_003L) + p.Triangulation.TriangleCount;
            sum = (sum * 1_000_003L) + p.Triangulation.ConstrainedEdgeCount;
            sum = (sum * 1_000_003L) + p.Triangulation.PortalCount;

            for (int i = 0; i < p.Triangulation.VertexCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Triangulation.VertexXcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.VertexYcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.VertexZcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.VertexChartIds[i];
                sum = (sum * 1_000_003L) + p.Triangulation.VertexSourceSpanIndices[i];
            }

            for (int i = 0; i < p.Triangulation.TriangleCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Triangulation.TriA[i];
                sum = (sum * 1_000_003L) + p.Triangulation.TriB[i];
                sum = (sum * 1_000_003L) + p.Triangulation.TriC[i];
                sum = (sum * 1_000_003L) + p.Triangulation.TriChartIds[i];
                sum = (sum * 1_000_003L) + p.Triangulation.TriRegionIds[i];
                sum = (sum * 1_000_003L) + p.Triangulation.TriAreaIds[i];
                sum = (sum * 1_000_003L) + p.Triangulation.N0[i];
                sum = (sum * 1_000_003L) + p.Triangulation.N1[i];
                sum = (sum * 1_000_003L) + p.Triangulation.N2[i];
            }

            for (int i = 0; i < p.Triangulation.ConstrainedEdgeCount; i++)
            {
                sum = (sum * 1_000_003L) + p.Triangulation.ConstrainedEdgeA[i];
                sum = (sum * 1_000_003L) + p.Triangulation.ConstrainedEdgeB[i];
                sum = (sum * 1_000_003L) + p.Triangulation.ConstrainedEdgeFlags[i];
            }

            for (int i = 0; i < p.Triangulation.PortalCount; i++)
            {
                sum = (sum * 1_000_003L) + (byte)p.Triangulation.PortalSides[i];
                sum = (sum * 1_000_003L) + p.Triangulation.PortalLeftXcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.PortalLeftZcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.PortalRightXcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.PortalRightZcm[i];
                sum = (sum * 1_000_003L) + p.Triangulation.PortalClearanceCm[i];
            }

            return sum;
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

        private static LayeredSpanTriangulationScratch CreateTriangulationScratch()
            => new(
                vertexCapacity: 1024,
                triangleCapacity: 2048,
                constrainedEdgeCapacity: 2048,
                borderPortalCapacity: 256,
                polygonVertexCapacity: 1024,
                adjacencyEdgeCapacity: 6144,
                bridgeCandidateCapacity: 4096,
                ringWorkCapacity: 256,
                temporaryConstraintFlagCapacity: 2048);

        private static LayeredSpanContourSpec FullTarget(in LayeredSpanRasterGridSpec grid, int maxErrorCm)
            => new(
                maxErrorCm,
                grid.OriginXcm,
                grid.OriginZcm,
                grid.ColumnMaxXcm(grid.ColumnCountX - 1),
                grid.ColumnMaxZcm(grid.ColumnCountZ - 1));

        private static LayeredSpanTriangulationSpec FullTriSpec(in LayeredSpanRasterGridSpec grid, int maxLawsonFlipCount = 100_000)
            => FullTriSpec(
                grid,
                grid.OriginXcm,
                grid.OriginZcm,
                grid.ColumnMaxXcm(grid.ColumnCountX - 1),
                grid.ColumnMaxZcm(grid.ColumnCountZ - 1),
                maxLawsonFlipCount);

        private static LayeredSpanTriangulationSpec FullTriSpec(
            in LayeredSpanRasterGridSpec grid,
            int targetMinXcm,
            int targetMinZcm,
            int targetMaxXcm,
            int targetMaxZcm,
            int maxLawsonFlipCount = 100_000)
            => new(
                LayeredSpanHeightRounding.FloorTowardNegativeInfinity,
                maxLawsonFlipCount,
                targetMinXcm,
                targetMinZcm,
                targetMaxXcm,
                targetMaxZcm,
                grid.CellSizeCm,
                grid.CellSizeCm);

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            int maxErrorCm)
            => RunPipeline(surface, indices, grid, maxClimbCm, FullTarget(grid, maxErrorCm), FullTriSpec(grid));

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            int maxErrorCm,
            in LayeredSpanTriangulationSpec triSpec)
            => RunPipeline(surface, indices, grid, maxClimbCm, FullTarget(grid, maxErrorCm), triSpec);

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            in LayeredSpanContourSpec contourSpec)
            => RunPipeline(surface, indices, grid, maxClimbCm, contourSpec, FullTriSpec(grid));

        private static PipelineResult RunPipeline(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            in LayeredSpanContourSpec contourSpec,
            in LayeredSpanTriangulationSpec triSpec)
            => RunPipelineWithAgentRadius(surface, indices, grid, maxClimbCm, contourSpec, triSpec, agentRadiusCm: 0);

        private static PipelineResult RunPipelineWithAgentRadius(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            LayeredSpanRasterGridSpec grid,
            int maxClimbCm,
            in LayeredSpanContourSpec contourSpec,
            in LayeredSpanTriangulationSpec triSpec,
            int agentRadiusCm)
        {
            var raw = new LayeredSpanScratch(grid.ColumnCount, 128);
            var walk = new LayeredSpanWalkabilityScratch(grid.ColumnCount, 128, 128);
            var sheets = new LayeredSpanSurfaceSheetScratch(grid.ColumnCount, 128);
            var links = new LayeredSpanWalkLinkScratch(128, 256);
            var radius = new LayeredSpanRadiusFieldScratch(128, 128, 256);
            var regions = new LayeredSpanRegionScratch(128, 64);
            var contours = CreateContourScratch(grid.ColumnCount);
            var triangulation = CreateTriangulationScratch();
            var linkSpec = new LayeredSpanWalkLinkSpec(maxClimbCm);
            RunOnce(
                surface,
                indices,
                grid,
                DefaultWalk,
                linkSpec,
                contourSpec,
                triSpec,
                agentRadiusCm,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours,
                triangulation);
            return new PipelineResult(raw, walk, sheets, links, radius, regions, contours, triangulation);
        }

        private static void RunOnce(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkabilitySpec walkSpec,
            in LayeredSpanWalkLinkSpec linkSpec,
            in LayeredSpanContourSpec contourSpec,
            in LayeredSpanTriangulationSpec triSpec,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walk,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours,
            LayeredSpanTriangulationScratch triangulation)
            => RunOnce(
                surface,
                indices,
                in grid,
                in walkSpec,
                in linkSpec,
                in contourSpec,
                in triSpec,
                agentRadiusCm: 0,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours,
                triangulation);

        private static void RunOnce(
            NavTriangleSurfaceSnapshot surface,
            int[] indices,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkabilitySpec walkSpec,
            in LayeredSpanWalkLinkSpec linkSpec,
            in LayeredSpanContourSpec contourSpec,
            in LayeredSpanTriangulationSpec triSpec,
            int agentRadiusCm,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walk,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours,
            LayeredSpanTriangulationScratch triangulation)
        {
            LayeredSpanRasterizer.Rasterize(surface, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in walkSpec, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(surface, raw, in grid, in walkSpec, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
            LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
            LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm, regions);
            LayeredSpanContourBuilder.Build(
                raw, walk, sheets, links, radius, regions, in grid, in contourSpec, contours);
            LayeredSpanTriangulationBuilder.Build(
                surface,
                raw,
                walk,
                sheets,
                links,
                radius,
                regions,
                contours,
                in grid,
                in triSpec,
                triangulation);
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
                LayeredSpanContourScratch contours,
                LayeredSpanTriangulationScratch triangulation)
            {
                Raw = raw;
                Walkability = walkability;
                Sheets = sheets;
                Links = links;
                Radius = radius;
                Regions = regions;
                Contours = contours;
                Triangulation = triangulation;
            }

            public LayeredSpanScratch Raw { get; }
            public LayeredSpanWalkabilityScratch Walkability { get; }
            public LayeredSpanSurfaceSheetScratch Sheets { get; }
            public LayeredSpanWalkLinkScratch Links { get; }
            public LayeredSpanRadiusFieldScratch Radius { get; }
            public LayeredSpanRegionScratch Regions { get; }
            public LayeredSpanContourScratch Contours { get; }
            public LayeredSpanTriangulationScratch Triangulation { get; }
        }
    }
}
