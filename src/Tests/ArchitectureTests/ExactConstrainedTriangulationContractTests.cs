using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Stage E contracts for the unregistered production CDT geometry: ExactConstrainedTriangulation
    /// and ExactCdtTriangleSurfaceBaker. Every bake here calls the real ExactCdtTriangleSurfaceBaker request API
    /// directly (no copied algorithm, no service/adapter rewiring — the adapter swap lands after Stage F).
    /// </summary>
    [TestFixture]
    public sealed class ExactConstrainedTriangulationContractTests
    {
        private const string GroundLayerId = "Ground";
        private const int DefaultLawsonFlipCount = 100_000;

        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly int MinWalkableUpDotQ1M =
            LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(45f, "ExactConstrainedTriangulationContractTests");

        // ---------------------------------------------------------------- 1. Strict donut

        [Test]
        public void ExactCdtBake_StrictOuterWithCenterHole_CoversOuterNeverFillsHole()
        {
            NavTriangleSurfaceSnapshot surface = RasterAnnulusEightQuads();
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 300, 300, 1, 1, 0);
            NavTile tile = BakeSurface(surface, grid);

            Assert.That(tile.TriangleCount, Is.EqualTo(16), "All outer-ring triangles must survive; nothing may be dropped or invented.");
            Assert.That(CountTriangleConnectedComponents(tile), Is.EqualTo(1), "The outer ring is a single connected annulus.");
            AssertValidChecksum(tile);

            Assert.That(PointInTileTriangles(tile, 50, 50), Is.True, "Outer walkable sample (50,50) must be covered.");
            Assert.That(PointInTileTriangles(tile, 250, 50), Is.True, "Outer walkable sample (250,50) must be covered.");
            Assert.That(PointInTileTriangles(tile, 50, 250), Is.True, "Outer walkable sample (50,250) must be covered.");
            Assert.That(PointInTileTriangles(tile, 250, 250), Is.True, "Outer walkable sample (250,250) must be covered.");

            AssertHoleInteriorUncovered(tile, 150, 150);
            AssertHoleInteriorUncovered(tile, 120, 150);
            AssertHoleInteriorUncovered(tile, 150, 120);
            AssertHoleInteriorUncovered(tile, 180, 150);
            AssertHoleInteriorUncovered(tile, 150, 180);
        }

        [Test]
        public void ExactCdtBake_TwoHoles_BothRemainEmpty()
        {
            NavTriangleSurfaceSnapshot surface = RasterTwoHoleFiveByThree();
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 500, 300, 1, 1, 0);
            NavTile tile = BakeSurface(surface, grid);

            Assert.That(tile.TriangleCount, Is.EqualTo(26), "All 13 quads must survive; no silent geometry drop.");
            Assert.That(CountTriangleConnectedComponents(tile), Is.EqualTo(1), "Two interior holes keep the outer region one component.");
            AssertValidChecksum(tile);

            AssertHoleInteriorUncovered(tile, 150, 150);
            AssertHoleInteriorUncovered(tile, 350, 150);

            Assert.That(PointInTileTriangles(tile, 50, 50), Is.True);
            Assert.That(PointInTileTriangles(tile, 250, 50), Is.True);
            Assert.That(PointInTileTriangles(tile, 450, 50), Is.True);
            Assert.That(PointInTileTriangles(tile, 50, 250), Is.True);
            Assert.That(PointInTileTriangles(tile, 250, 250), Is.True);
            Assert.That(PointInTileTriangles(tile, 450, 250), Is.True);
        }

        // ---------------------------------------------------------------- 3. Building / spanning triangle

        [Test]
        public void ExactCdtBake_ObstacleBuildingTile_SingleComponentNoSilentDrop()
        {
            // Dense per-cell surface (the terrain compiler's flat path emits one quad per tile, so a
            // building inside a compiled tile would block the whole quad). 2x2 tile grid keeps the
            // north/west neighbors in range so border portals are emitted.
            const int cellsPerTile = 16;
            const int cellSizeCm = 100;
            const int tileSizeCm = cellsPerTile * cellSizeCm;
            NavTriangleSurfaceSnapshot surfaceSnapshot = BuildRasterSurface(cellsPerTile * 2, cellsPerTile * 2, cellSizeCm);
            var grid = new NavTriangleSurfaceTileGrid(0, 0, tileSizeCm, tileSizeCm, 2, 2, 200);
            NavTriangleSurfaceTileIndex surface = NavTriangleSurfaceTileIndex.Build(surfaceSnapshot, grid);

            var target = new NavBakeTileCoord(1, 1);
            NavTile openTile = BakeSurface(surface, target);
            Assert.That(openTile.TriangleCount, Is.EqualTo(cellsPerTile * cellsPerTile * 2));

            const int buildingLocalXcm = 800;
            const int buildingLocalZcm = 800;
            const int buildingRadiusCm = 150;
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "building",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(1600 + buildingLocalXcm, 1600 + buildingLocalZcm),
                        RadiusCm = buildingRadiusCm,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            NavTile tile = BakeSurface(surface, target, obstacles);
            Assert.That(tile.TriangleCount, Is.GreaterThan(0), "Tile must keep walkable geometry outside the building hole.");
            Assert.That(
                tile.TriangleCount,
                Is.LessThan(openTile.TriangleCount),
                "The building must actually remove blocked triangles (no silent no-op).");
            Assert.That(tile.TriangleCount, Is.GreaterThanOrEqualTo(openTile.TriangleCount - 64));
            AssertValidChecksum(tile);

            string componentSummary = SummarizeTriangleConnectedComponents(tile);
            Assert.That(
                CountTriangleConnectedComponents(tile),
                Is.EqualTo(1),
                "Obstacle tile must stay a single walkable component outside the building hole. " + componentSummary);
            Assert.That(
                CountInternalNeighborEdges(tile),
                Is.GreaterThan(tile.TriangleCount),
                "Dense obstacle tile must retain internal triangle adjacency. " + componentSummary);
            Assert.That(
                PointInTileTriangles(tile, 100, 500),
                Is.True,
                "West corridor sample (100,500) must stay covered after building bake.");
            Assert.That(
                PointInTileTriangles(tile, 100, 1300),
                Is.True,
                "West corridor sample (100,1300) must stay covered after building bake.");
            Assert.That(
                PointInTileTriangles(tile, buildingLocalXcm, buildingLocalZcm),
                Is.False,
                "Building disc center must not be covered by any triangle.");
            Assert.That(
                PortalSideCoversAlong(tile, NavPortalSide.North, alongCm: 100),
                Is.True,
                "Tile north border must keep a portal covering x=100 for baseline handoff.");
            Assert.That(CountClockwiseTriangles(tile), Is.EqualTo(0), "Detour funnel requires consistent CCW triangles.");
        }

        // ---------------------------------------------------------------- 4. Stacked same-XZ surfaces

        [Test]
        public void ExactCdtBake_StackedSameXzCharts_DistinctYNoFalseAdjacency()
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

            var grid = new NavTriangleSurfaceTileGrid(0, 0, 200, 200, 1, 1, 0);
            NavTile tile = BakeSurface(surface, grid);

            Assert.That(tile.TriangleCount, Is.EqualTo(4), "Both stacked floors must survive.");
            AssertValidChecksum(tile);

            bool sawLow = false;
            bool sawHigh = false;
            for (int i = 0; i < tile.VertexCount; i++)
            {
                if (tile.VertexYcm[i] == 0)
                {
                    sawLow = true;
                }

                if (tile.VertexYcm[i] == 500)
                {
                    sawHigh = true;
                }
            }

            Assert.That(sawLow, Is.True, "Lower floor y=0 must be present.");
            Assert.That(sawHigh, Is.True, "Upper floor y=500 must be present.");
            AssertNoCrossHeightAdjacency(tile);
        }

        // ---------------------------------------------------------------- 5. Obstacle vertical range / layer / area

        [Test]
        public void ExactCdtBake_ObstacleVerticalRangeAndLayer_BlockOnlyOverlappingFloor()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0, 0, 400, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 0, 300, 300, 300, 300 },
                vertexZcm: new[] { 0, 0, 400, 400, 0, 0, 400, 400 },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 1, 1, 2, 2 },
                triStableIds: new[] { 10, 20, 30, 40 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, 0);

            var groundObstacle = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "low-wall",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(200, 200),
                        RadiusCm = 80,
                        MinYcm = 0,
                        MaxYcm = 150
                    },
                    new NavObstacle
                    {
                        Id = "elevated-layer",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = "Elevated",
                        Center = new NavPointCm(200, 200),
                        RadiusCm = 500,
                        MinYcm = 0,
                        MaxYcm = 500
                    }
                }
            };

            NavTile blocked = BakeSurface(surface, grid, groundObstacle);
            Assert.That(blocked.TriangleCount, Is.EqualTo(2), "Only the upper floor survives the ground-layer obstacle.");
            for (int i = 0; i < blocked.TriangleCount; i++)
            {
                int a = blocked.TriA[i];
                int b = blocked.TriB[i];
                int c = blocked.TriC[i];
                Assert.That(blocked.VertexYcm[a], Is.EqualTo(300), "Surviving triangles are on the upper floor.");
                Assert.That(blocked.VertexYcm[b], Is.EqualTo(300));
                Assert.That(blocked.VertexYcm[c], Is.EqualTo(300));
                Assert.That(blocked.ActiveTriAreaIds[i], Is.EqualTo(2), "Input area ids must propagate to output triangles.");
            }

            Assert.That(
                ContainsVertexY(blocked, 0),
                Is.False,
                "Lower floor y=0 must be removed: obstacle [0,150) overlaps only the lower agent interval.");
            AssertValidChecksum(blocked);

            var elevatedOnly = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "elevated-layer",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = "Elevated",
                        Center = new NavPointCm(200, 200),
                        RadiusCm = 500,
                        MinYcm = 0,
                        MaxYcm = 500
                    }
                }
            };
            NavTile untouched = BakeSurface(surface, grid, elevatedOnly);
            Assert.That(
                untouched.TriangleCount,
                Is.EqualTo(4),
                "A different-layer obstacle must not block the ground layer (layer isolation).");
            Assert.That(ContainsVertexY(untouched, 0), Is.True);
            Assert.That(ContainsVertexY(untouched, 300), Is.True);
        }

        // ---------------------------------------------------------------- 6. Shuffled input determinism

        [Test]
        public void ExactCdtBake_ShuffledSourceTriangleOrder_IdenticalSerializedBytesAcrossThreeVariants()
        {
            // Three semantically identical source orders of the same two-quad surface
            // (stable ids stay attached to their triangles; only array order and vertex order change).
            NavTriangleSurfaceSnapshot forward = BuildTwoQuadSurface(
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triStableIds: new[] { 10, 20, 30, 40 });
            NavTriangleSurfaceSnapshot reversed = BuildTwoQuadSurface(
                triA: new[] { 5, 4, 1, 0 },
                triB: new[] { 7, 5, 3, 1 },
                triC: new[] { 6, 6, 2, 2 },
                triStableIds: new[] { 40, 30, 20, 10 });
            NavTriangleSurfaceSnapshot rotated = BuildTwoQuadSurface(
                triA: new[] { 4, 0, 5, 1 },
                triB: new[] { 5, 1, 7, 3 },
                triC: new[] { 6, 2, 6, 2 },
                triStableIds: new[] { 30, 10, 40, 20 });

            var grid = new NavTriangleSurfaceTileGrid(0, 0, 200, 100, 1, 1, 0);
            NavTile a = BakeSurface(forward, grid);
            NavTile b = BakeSurface(reversed, grid);
            NavTile c = BakeSurface(rotated, grid);

            byte[] ba = SerializeTile(a);
            byte[] bb = SerializeTile(b);
            byte[] bc = SerializeTile(c);
            Assert.That(bb, Is.EqualTo(ba), "Reversed source order must serialize byte-identical.");
            Assert.That(bc, Is.EqualTo(ba), "Rotated source order must serialize byte-identical.");
            Assert.That(ComputeChecksum(b), Is.EqualTo(ComputeChecksum(a)));
            Assert.That(ComputeChecksum(c), Is.EqualTo(ComputeChecksum(a)));
            Assert.That(a.TriangleCount, Is.EqualTo(4));
            Assert.That(a.VertexCount, Is.EqualTo(6));
        }

        // ---------------------------------------------------------------- 7. Large coordinates / exact predicates

        [Test]
        public void ExactTriangulation_LargeLocalCoordinates_ExactPredicatesSucceed()
        {
            // Base near int.MaxValue: predicate safety is on local deltas, and all spans stay small.
            const int baseX = int.MaxValue - 10_000;
            const int baseZ = int.MaxValue - 20_000;
            var polyX = new[] { baseX, baseX + 1000, baseX + 1500, baseX + 1000, baseX + 500, baseX };
            var polyZ = new[] { baseZ, baseZ, baseZ + 500, baseZ + 1000, baseZ + 1000, baseZ + 500 };
            var constA = new[] { 0, 3 };
            var constB = new[] { 3, 0 };

            var triA = new List<int>();
            var triB = new List<int>();
            var triC = new List<int>();
            ExactConstrainedTriangulation.TriangulatePolygon(
                polyX,
                polyZ,
                constA,
                constB,
                maxLawsonFlipCount: 1000,
                triA,
                triB,
                triC);

            Assert.That(triA.Count, Is.EqualTo(polyX.Length - 2), "Ear clipping of a simple polygon emits n-2 triangles.");
            for (int i = 0; i < triA.Count; i++)
            {
                AssertIndexInRange(triA[i], polyX.Length);
                AssertIndexInRange(triB[i], polyX.Length);
                AssertIndexInRange(triC[i], polyX.Length);
            }
        }

        [Test]
        public void ExactCdtBake_LargeWorldCoordinates_LocalOutputNoOverflow()
        {
            const int worldBase = 1_000_000_000;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { worldBase, worldBase + 200, worldBase, worldBase + 200 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { worldBase, worldBase, worldBase + 200, worldBase + 200 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags });
            var grid = new NavTriangleSurfaceTileGrid(worldBase, worldBase, 200, 200, 1, 1, 0);
            NavTile tile = BakeSurface(surface, grid);

            Assert.That(tile.TriangleCount, Is.EqualTo(2));
            Assert.That(tile.OriginXcm, Is.EqualTo(worldBase));
            Assert.That(tile.OriginZcm, Is.EqualTo(worldBase));
            for (int i = 0; i < tile.VertexCount; i++)
            {
                Assert.That(tile.VertexXcm[i], Is.InRange(0, 200), "Output vertices must be tile-local.");
                Assert.That(tile.VertexZcm[i], Is.InRange(0, 200));
            }

            AssertValidChecksum(tile);
        }

        // ---------------------------------------------------------------- 7b. Hard-fail diagnostics

        [Test]
        public void ExactTriangulation_TooFewVertices_HardFailsDiagnostically()
        {
            var triA = new List<int>();
            var triB = new List<int>();
            var triC = new List<int>();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ExactConstrainedTriangulation.TriangulatePolygon(
                    new[] { 0, 100 },
                    new[] { 0, 0 },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    DefaultLawsonFlipCount,
                    triA,
                    triB,
                    triC))!;
            Assert.That(ex.Message, Does.Contain("at least 3"));
            Assert.That(triA.Count, Is.EqualTo(0), "Failed triangulation must not emit partial output.");
        }

        [Test]
        public void ExactTriangulation_FullyDegenerateChain_HardFailsDiagnostically()
        {
            var triA = new List<int>();
            var triB = new List<int>();
            var triC = new List<int>();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ExactConstrainedTriangulation.TriangulatePolygon(
                    new[] { 0, 100, 200, 300 },
                    new[] { 0, 0, 0, 0 },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    DefaultLawsonFlipCount,
                    triA,
                    triB,
                    triC))!;
            Assert.That(ex.Message, Does.Contain("zero-area"));
        }

        [Test]
        public void ExactTriangulation_MaxLawsonFlipCountExceeded_HardFailsWithoutFallback()
        {
            // Convex quad whose fan diagonal violates Delaunay: vertex (100,40) lies inside the
            // circumcircle of (0,0),(100,0),(0,50), so one flip is mandatory.
            var triA = new List<int>();
            var triB = new List<int>();
            var triC = new List<int>();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ExactConstrainedTriangulation.TriangulatePolygon(
                    new[] { 0, 100, 100, 0 },
                    new[] { 0, 0, 40, 50 },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    maxLawsonFlipCount: 0,
                    triA,
                    triB,
                    triC))!;
            Assert.That(ex.Message, Does.Contain("maxLawsonFlipCount"));

            var okA = new List<int>();
            var okB = new List<int>();
            var okC = new List<int>();
            ExactConstrainedTriangulation.TriangulatePolygon(
                new[] { 0, 100, 100, 0 },
                new[] { 0, 0, 40, 50 },
                Array.Empty<int>(),
                Array.Empty<int>(),
                maxLawsonFlipCount: 10,
                okA,
                okB,
                okC);
            Assert.That(okA.Count, Is.EqualTo(2), "With budget the same quad triangulates after one flip.");
        }

        [Test]
        public void ExactCdtBake_MaxLawsonFlipCountExceeded_HardFailsDiagnostically()
        {
            NavTriangleSurfaceSnapshot surface = BuildFlipNeededQuadSurface();
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 200, 100, 1, 1, 0);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BakeSurface(surface, grid, maxLawsonFlipCount: 0))!;
            Assert.That(ex.Message, Does.Contain("ExactCdtTriangleSurfaceBaker exceeded maxLawsonFlipCount"));
        }

        [Test]
        public void ExactCdtBake_NonManifoldProjection_HardFailsAsUnsupportedInput()
        {
            // One sheet: A and B share an exact edge, B and C share an exact edge, while C overlaps
            // A's XZ interior at a different Y without sharing an edge — a folded non-manifold sheet.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 100, 200, 100, 200, 50 },
                vertexYcm: new[] { 0, 0, 0, 0, 500 },
                vertexZcm: new[] { 100, 100, 200, 200, 50 },
                triA: new[] { 0, 0, 2 },
                triB: new[] { 1, 2, 4 },
                triC: new[] { 2, 3, 3 },
                triAreaIds: new byte[] { 0, 0, 0 },
                triStableIds: new[] { 10, 20, 30 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 300, 300, 1, 1, 0);

            NavBakeUnsupportedInputException ex = Assert.Throws<NavBakeUnsupportedInputException>(() =>
                BakeSurface(surface, grid))!;
            Assert.That(ex.Algorithm, Is.EqualTo(NavBakeAlgorithmKind.ExactCdt));
            Assert.That(ex.Message, Does.Contain("non-manifold projection"));
        }

        [Test]
        public void ExactCdtBake_DegenerateZeroAreaSurface_ReturnsValidEmptyTileWithChecksum()
        {
            // Three collinear vertices: a zero-area face is degenerate and must not enter the mesh.
            // The baker's no-walkable-domain contract is a checksummed valid-empty tile, not a crash
            // and not invented geometry.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 200 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 0 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 10 },
                triFlags: new[] { FloorFlags });
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 300, 300, 1, 1, 0);
            NavTile tile = BakeSurface(surface, grid);

            Assert.That(tile.TriangleCount, Is.EqualTo(0));
            Assert.That(tile.VertexCount, Is.EqualTo(0));
            AssertValidChecksum(tile);
        }

        [Test]
        public void ExactCdtBake_UnknownObstacleKind_HardFailsDiagnostically()
        {
            NavTriangleSurfaceSnapshot surface = BuildTwoQuadSurface(
                triA: new[] { 0, 1, 4, 5 },
                triB: new[] { 1, 3, 5, 7 },
                triC: new[] { 2, 2, 6, 6 },
                triStableIds: new[] { 10, 20, 30, 40 });
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 200, 100, 1, 1, 0);
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "unknown-kind",
                        Enabled = true,
                        Kind = (NavObstacleKind)99,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(100, 50),
                        RadiusCm = 50,
                        MinYcm = 0,
                        MaxYcm = 200
                    }
                }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BakeSurface(surface, grid, obstacles))!;
            Assert.That(ex.Message, Does.Contain("not supported"));
        }

        // ---------------------------------------------------------------- 8. Portal Y v3 / checksum / path around hole

        [Test]
        public void ExactCdtBake_PortalsUseV3PortalY_AndChecksumIsValid()
        {
            // Two adjacent flat tiles: the seam must expose East/West portals carrying v3 LeftYcm/RightYcm.
            NavTriangleSurfaceSnapshot surface = BuildRasterSurface(16, 8, cellSizeCm: 100);
            var grid = new NavTriangleSurfaceTileGrid(0, 0, 800, 800, 2, 1, 200);
            NavTriangleSurfaceTileIndex index = NavTriangleSurfaceTileIndex.Build(surface, grid);
            NavTile west = BakeSurface(index, new NavBakeTileCoord(0, 0));
            NavTile east = BakeSurface(index, new NavBakeTileCoord(1, 0));

            Assert.That(west.OriginXcm, Is.EqualTo(0));
            Assert.That(east.OriginXcm, Is.EqualTo(800));
            Assert.That(west.TriangleCount, Is.GreaterThan(0));
            AssertValidChecksum(west);
            AssertValidChecksum(east);

            bool sawWestEastPortal = false;
            bool sawEastWestPortal = false;
            for (int i = 0; i < west.PortalCount; i++)
            {
                NavBorderPortal portal = west.ActivePortals[i];
                if (portal.Side != NavPortalSide.East)
                {
                    continue;
                }

                sawWestEastPortal = true;
                Assert.That(portal.LeftYcm, Is.EqualTo(0), "Seam portal left Y must be the surface Y (v3).");
                Assert.That(portal.RightYcm, Is.EqualTo(0), "Seam portal right Y must be the surface Y (v3).");
            }

            for (int i = 0; i < east.PortalCount; i++)
            {
                NavBorderPortal portal = east.ActivePortals[i];
                if (portal.Side != NavPortalSide.West)
                {
                    continue;
                }

                sawEastWestPortal = true;
                Assert.That(portal.LeftYcm, Is.EqualTo(0));
                Assert.That(portal.RightYcm, Is.EqualTo(0));
            }

            Assert.That(sawWestEastPortal, Is.True, "West tile must expose an East seam portal.");
            Assert.That(sawEastWestPortal, Is.True, "East tile must expose a West seam portal.");
        }

        [Test]
        public void ExactCdtBake_PathAroundHole_ReturnsOkAndDoesNotEnterDisc()
        {
            const int tileSizeCm = 3200;
            const int buildingLocalXcm = 1600;
            const int buildingLocalZcm = 1600;
            const int buildingRadiusCm = 400;
            NavTriangleSurfaceSnapshot surface = BuildRasterSurface(32, 32, cellSizeCm: 100);
            var grid = new NavTriangleSurfaceTileGrid(0, 0, tileSizeCm, tileSizeCm, 1, 1, 200);
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "building",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(buildingLocalXcm, buildingLocalZcm),
                        RadiusCm = buildingRadiusCm,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            NavTile tile = BakeSurface(surface, grid, obstacles);
            Assert.That(CountTriangleConnectedComponents(tile), Is.EqualTo(1), "Hole must not split the tile.");
            Assert.That(
                PointInTileTriangles(tile, buildingLocalXcm, buildingLocalZcm),
                Is.False,
                "Building disc center must not be covered.");

            const int startXcm = 500;
            const int startZcm = 1600;
            const int goalXcm = 2700;
            const int goalZcm = 1600;
            NavPathResult path = DetourNavQueryEngine.FindPath(
                new[] { tile },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: startXcm,
                startZcm: startZcm,
                goalXcm: goalXcm,
                goalZcm: goalZcm,
                maxPortals: 128);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"status={path.Status} points={path.PathXcm.Length} path={FormatPath(path)}");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(path.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(path.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(path.PathXcm[^1], Is.EqualTo(goalXcm));
            Assert.That(path.PathZcm[^1], Is.EqualTo(goalZcm));
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                long dx = path.PathXcm[i] - buildingLocalXcm;
                long dz = path.PathZcm[i] - buildingLocalZcm;
                Assert.That(
                    (dx * dx) + (dz * dz),
                    Is.GreaterThan((long)buildingRadiusCm * buildingRadiusCm),
                    $"Waypoint ({path.PathXcm[i]},{path.PathZcm[i]}) must stay outside the building disc. {FormatPath(path)}");
            }
        }

        // ---------------------------------------------------------------- helpers

        private static NavTile BakeSurface(
            NavTriangleSurfaceSnapshot surface,
            NavTriangleSurfaceTileGrid grid,
            INavObstacleSource obstacles = null,
            NavBakeTileCoord target = default,
            int maxLawsonFlipCount = DefaultLawsonFlipCount)
            => BakeSurface(
                NavTriangleSurfaceTileIndex.Build(surface, grid),
                grid,
                target,
                obstacles,
                maxLawsonFlipCount);

        private static NavTile BakeSurface(
            NavTriangleSurfaceTileIndex surfaceIndex,
            NavBakeTileCoord target,
            INavObstacleSource obstacles = null)
            => BakeSurface(
                surfaceIndex,
                surfaceIndex.Grid,
                target,
                obstacles,
                DefaultLawsonFlipCount);

        private static NavTile BakeSurface(
            NavTriangleSurfaceTileIndex surfaceIndex,
            NavTriangleSurfaceTileGrid grid,
            NavBakeTileCoord target,
            INavObstacleSource obstacles,
            int maxLawsonFlipCount)
        {
            var request = new ExactCdtTriangleSurfaceBakeRequest(
                surfaceIndex,
                target,
                new NavTileId(target.ChunkX, target.ChunkY),
                tileVersion: 1,
                buildConfigHash: 0UL,
                GroundLayerId,
                maxClimbCm: 40,
                MinWalkableUpDotQ1M,
                agentHeightCm: 180,
                agentRadiusCm: 30,
                obstacles ?? new NavObstacleSet(),
                maxLawsonFlipCount);
            return ExactCdtTriangleSurfaceBaker.Bake(in request);
        }

        private static NavTriangleSurfaceSnapshot BuildTwoQuadSurface(int[] triA, int[] triB, int[] triC, int[] triStableIds)
        {
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0, 100, 100, 200, 100, 200 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100, 100, 0, 0, 100, 100 },
                triA: triA,
                triB: triB,
                triC: triC,
                triAreaIds: new byte[] { 1, 1, 1, 1 },
                triStableIds: triStableIds,
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });
        }

        private static NavTriangleSurfaceSnapshot BuildFlipNeededQuadSurface()
        {
            // Fan diagonal (0,2) is non-Delaunay: vertex (0,30) lies inside the circumcircle of
            // (0,0),(100,0),(100,40) (center (50,20), r^2=2900; dist^2=2600), so LawsonFlipExisting
            // must flip once and exceed a zero budget.
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 100, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 40, 30 },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags });
        }

        private static NavTriangleSurfaceSnapshot BuildRasterSurface(int cellsX, int cellsZ, int cellSizeCm)
        {
            int quadCount = cellsX * cellsZ;
            var vx = new int[quadCount * 4];
            var vy = new int[quadCount * 4];
            var vz = new int[quadCount * 4];
            var a = new int[quadCount * 2];
            var b = new int[quadCount * 2];
            var c = new int[quadCount * 2];
            var areas = new byte[quadCount * 2];
            var stables = new int[quadCount * 2];
            var flags = new NavTriangleSurfaceFlags[quadCount * 2];
            int cell = 0;
            int stable = 1;
            for (int cz = 0; cz < cellsZ; cz++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int minX = cx * cellSizeCm;
                    int minZ = cz * cellSizeCm;
                    int maxX = minX + cellSizeCm;
                    int maxZ = minZ + cellSizeCm;
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

            int triCount = quadCount * 2;
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

        private static NavTriangleSurfaceSnapshot RasterTwoHoleFiveByThree()
        {
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

        private static void AssertHoleInteriorUncovered(NavTile tile, int xcm, int zcm)
        {
            Assert.That(
                PointInTileTriangles(tile, xcm, zcm),
                Is.False,
                $"Hole-interior sample ({xcm},{zcm}) must not be covered by any output triangle.");
        }

        private static void AssertValidChecksum(NavTile tile)
        {
            byte[] scratch = new byte[NavTileBinary.GetSerializedSize(tile)];
            Assert.That(
                NavTileBinary.ComputeChecksum(tile, scratch),
                Is.EqualTo(tile.Checksum),
                "Baked tile checksum must be the canonical FNV over its serialized payload.");
        }

        private static byte[] SerializeTile(NavTile tile)
        {
            byte[] bytes = new byte[NavTileBinary.GetSerializedSize(tile)];
            NavTileBinary.Write(bytes, tile);
            return bytes;
        }

        private static ulong ComputeChecksum(NavTile tile)
        {
            byte[] scratch = new byte[NavTileBinary.GetSerializedSize(tile)];
            return NavTileBinary.ComputeChecksum(tile, scratch);
        }

        private static void AssertNoCrossHeightAdjacency(NavTile tile)
        {
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int y = TriangleHeightY(tile, t);
                for (int e = 0; e < 3; e++)
                {
                    int n = e == 0 ? tile.N0[t] : (e == 1 ? tile.N1[t] : tile.N2[t]);
                    if (n < 0)
                    {
                        continue;
                    }

                    Assert.That(
                        TriangleHeightY(tile, n),
                        Is.EqualTo(y),
                        $"Neighbor {n} of triangle {t} must share its height (no false vertical adjacency).");
                }
            }
        }

        private static int TriangleHeightY(NavTile tile, int t)
        {
            int a = tile.VertexYcm[tile.TriA[t]];
            int b = tile.VertexYcm[tile.TriB[t]];
            int c = tile.VertexYcm[tile.TriC[t]];
            Assert.That(a, Is.EqualTo(b), "Sheet triangles must be flat in Y.");
            Assert.That(a, Is.EqualTo(c), "Sheet triangles must be flat in Y.");
            return a;
        }

        private static bool ContainsVertexY(NavTile tile, int y)
        {
            for (int i = 0; i < tile.VertexCount; i++)
            {
                if (tile.VertexYcm[i] == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertIndexInRange(int index, int count)
        {
            Assert.That(index, Is.InRange(0, count - 1));
        }

        private static string FormatPath(NavPathResult path)
        {
            if (path.PathXcm == null || path.PathXcm.Length == 0)
            {
                return "<empty>";
            }

            var parts = new string[path.PathXcm.Length];
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                parts[i] = $"({path.PathXcm[i]},{path.PathZcm[i]})";
            }

            return string.Join(" -> ", parts);
        }

        private static int CountInternalNeighborEdges(NavTile tile)
        {
            int count = 0;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                if (tile.N0[i] >= 0) count++;
                if (tile.N1[i] >= 0) count++;
                if (tile.N2[i] >= 0) count++;
            }

            return count;
        }

        private static int CountTriangleConnectedComponents(NavTile tile)
        {
            return SummarizeTriangleConnectedComponents(tile, out _);
        }

        private static string SummarizeTriangleConnectedComponents(NavTile tile)
        {
            SummarizeTriangleConnectedComponents(tile, out string summary);
            return summary;
        }

        private static int SummarizeTriangleConnectedComponents(NavTile tile, out string summary)
        {
            int n = tile.TriangleCount;
            if (n <= 0)
            {
                summary = "components=0";
                return 0;
            }

            var seen = new bool[n];
            int components = 0;
            var stack = new int[n];
            var sizeParts = new List<string>(8);
            for (int seed = 0; seed < n; seed++)
            {
                if (seen[seed])
                {
                    continue;
                }

                components++;
                int top = 0;
                stack[top++] = seed;
                seen[seed] = true;
                int size = 0;
                long sumX = 0;
                long sumZ = 0;
                while (top > 0)
                {
                    int t = stack[--top];
                    size++;
                    int a = tile.TriA[t];
                    int b = tile.TriB[t];
                    int c = tile.TriC[t];
                    sumX += tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c];
                    sumZ += tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c];
                    TryPushNeighbor(tile.N0[t], seen, stack, ref top);
                    TryPushNeighbor(tile.N1[t], seen, stack, ref top);
                    TryPushNeighbor(tile.N2[t], seen, stack, ref top);
                }

                int centroidX = (int)(sumX / (size * 3L));
                int centroidZ = (int)(sumZ / (size * 3L));
                sizeParts.Add($"n={size}@({centroidX},{centroidZ})");
            }

            summary = $"components={components} [{string.Join("; ", sizeParts)}] internalEdges={CountInternalNeighborEdges(tile)}";
            return components;
        }

        private static void TryPushNeighbor(int neighbor, bool[] seen, int[] stack, ref int top)
        {
            if (neighbor < 0 || neighbor >= seen.Length || seen[neighbor])
            {
                return;
            }

            seen[neighbor] = true;
            stack[top++] = neighbor;
        }

        private static bool PointInTileTriangles(NavTile tile, int xcm, int zcm)
        {
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int a = tile.TriA[t];
                int b = tile.TriB[t];
                int c = tile.TriC[t];
                if (PointInTriangleInclusive(
                        xcm,
                        zcm,
                        tile.VertexXcm[a],
                        tile.VertexZcm[a],
                        tile.VertexXcm[b],
                        tile.VertexZcm[b],
                        tile.VertexXcm[c],
                        tile.VertexZcm[c]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PortalSideCoversAlong(NavTile tile, NavPortalSide side, int alongCm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                int minAlong;
                int maxAlong;
                if (side is NavPortalSide.West or NavPortalSide.East)
                {
                    minAlong = Math.Min(portal.LeftZcm, portal.RightZcm);
                    maxAlong = Math.Max(portal.LeftZcm, portal.RightZcm);
                }
                else
                {
                    minAlong = Math.Min(portal.LeftXcm, portal.RightXcm);
                    maxAlong = Math.Max(portal.LeftXcm, portal.RightXcm);
                }

                if (alongCm >= minAlong && alongCm <= maxAlong)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInTriangleInclusive(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long area = ((long)bx - ax) * ((long)cz - az) - ((long)bz - az) * ((long)cx - ax);
            if (area == 0)
            {
                return false;
            }

            long sign = area > 0 ? 1 : -1;
            long ab = (((long)bx - ax) * ((long)pz - az) - ((long)bz - az) * ((long)px - ax)) * sign;
            long bc = (((long)cx - bx) * ((long)pz - bz) - ((long)cz - bz) * ((long)px - bx)) * sign;
            long ca = (((long)ax - cx) * ((long)pz - cz) - ((long)az - cz) * ((long)px - cx)) * sign;
            return ab >= 0 && bc >= 0 && ca >= 0;
        }

        private static int CountClockwiseTriangles(NavTile tile)
        {
            int clockwise = 0;
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int a = tile.TriA[t];
                int b = tile.TriB[t];
                int c = tile.TriC[t];
                long area2 =
                    ((long)tile.VertexXcm[b] - tile.VertexXcm[a]) * ((long)tile.VertexZcm[c] - tile.VertexZcm[a])
                    - ((long)tile.VertexZcm[b] - tile.VertexZcm[a]) * ((long)tile.VertexXcm[c] - tile.VertexXcm[a]);
                if (area2 < 0)
                {
                    clockwise++;
                }
            }

            return clockwise;
        }

    }
}
