using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Geometry;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RecastBorderPortalContractTests
    {
        private const string GroundLayerId = "Ground";
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
        private static readonly NavTriangleSurfaceFlags SolidOnly =
            NavTriangleSurfaceFlags.Solid;

        [Test]
        public void Recast_FlatCrossTile_EmitsPortalAndAllowsDetourPath()
        {
            NavTriangleSurfaceTileIndex surface = CreateContinuousFloor(tileCountX: 2, tileWidthCm: 400, yCm: 0);
            NavBakeResult bake = BakeRecast(surface, new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) });
            Assert.That(bake.FailureCount, Is.EqualTo(0));
            Assert.That(
                bake.Entries[0].Tile.PortalCount,
                Is.GreaterThan(0),
                $"tris={bake.Entries[0].Tile.TriangleCount} detour={bake.Entries[0].DetourTileBytes.Length} idx0={surface.GetTriangleIndices(0,0).Length} msg={bake.Entries[0].Artifact.Message}");
            Assert.That(HasSidePortal(bake.Entries[0].Tile, NavPortalSide.East), Is.True);
            Assert.That(HasSidePortal(bake.Entries[1].Tile, NavPortalSide.West), Is.True);

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetour(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 200,
                goalXcm: 750,
                goalZcm: 200,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void Recast_SlopedCrossTile_EmitsPortal()
        {
            // Continuous ramp across tile boundary at X=400.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0, 400, 800, 800, 400 },
                vertexYcm: new[] { 0, 0, 40, 40, 0, 0, 40, 40 },
                vertexZcm: new[] { 0, 0, 400, 400, 0, 0, 400, 400 },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 200));

            NavBakeResult bake = BakeRecast(index, new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) });
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            Assert.That(HasSidePortal(bake.Entries[0].Tile, NavPortalSide.East), Is.True);
        }

        [Test]
        public void Recast_WrongWorldBoundaryLine_SameAlongAndY_NoPortal()
        {
            // Tile0 floor ends at X=400. Halo contains a walkable strip on a parallel line X=500..600
            // with matching Z/Y �?old along+Y-only gate would falsely accept this as portal evidence.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 400, 400, 0,
                    500, 600, 600, 500
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    0, 0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 400, 400,
                    0, 0, 400, 400
                },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 200));

            NavTile left = BakeRecastTile(index, new NavBakeTileCoord(0, 0));
            Assert.That(HasSidePortal(left, NavPortalSide.East), Is.False);
        }

        [Test]
        public void Recast_WrongStackedFloor_NoPortal()
        {
            // Lower continuous floor across tiles. Upper floor only on left with east edge at Y=300.
            // Neighbor lower floor must not prove the upper portal.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 400, 400, 0, 400, 800, 800, 400,
                    0, 400, 400, 0
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0, 0, 0, 0, 0,
                    300, 300, 300, 300
                },
                vertexZcm: new[]
                {
                    0, 0, 400, 400, 0, 0, 400, 400,
                    0, 0, 400, 400
                },
                triA: new[] { 0, 0, 4, 4, 8, 8 },
                triB: new[] { 1, 2, 5, 6, 9, 10 },
                triC: new[] { 2, 3, 6, 7, 10, 11 },
                triAreaIds: new byte[] { 0, 0, 0, 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags, FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 200));

            NavTile left = BakeRecastTile(index, new NavBakeTileCoord(0, 0));
            Assert.That(HasEastPortalNearY(left, ycm: 300, toleranceCm: 20), Is.False);
        }

        [Test]
        public void Recast_PointOnlyCornerContact_NoPortal()
        {
            // Neighbor triangle touches the shared boundary only at a single corner (point contact).
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 400, 400, 0,
                    400, 500, 500
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    0, 0, 0
                },
                vertexZcm: new[]
                {
                    0, 0, 400, 400,
                    0, 0, 100
                },
                triA: new[] { 0, 0, 4 },
                triB: new[] { 1, 2, 5 },
                triC: new[] { 2, 3, 6 },
                triAreaIds: new byte[] { 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 200));

            NavTile left = BakeRecastTile(index, new NavBakeTileCoord(0, 0));
            Assert.That(HasSidePortal(left, NavPortalSide.East), Is.False);
        }

        [Test]
        public void Recast_BlockedNeighborTriangle_NoPortal()
        {
            NavTriangleSurfaceTileIndex surface = CreateContinuousFloor(tileCountX: 2, tileWidthCm: 400, yCm: 0);
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "block-neighbor",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(450, 200),
                        RadiusCm = 180,
                        MinYcm = -10,
                        MaxYcm = 200
                    }
                }
            };

            NavBakeResult bake = BakeRecast(
                surface,
                new[] { new NavBakeTileCoord(0, 0) },
                obstacles);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            Assert.That(HasSidePortal(bake.Entries[0].Tile, NavPortalSide.East), Is.False);
        }

        [Test]
        public void BoundaryAlongInterval_MoreThan64ChainedObstacles_AcceptsCoverageAndRejectsGap()
        {
            // Regression: old stackalloc[64] interval scratch threw once more than 64 boundary-overlapping
            // obstacles contributed clipped intervals. Repeated-scan coverage must accept a fully chained
            // seal and still reject a real free gap — without heap allocation or a fixed capacity.
            const int segmentCount = 80;
            const int stepCm = 10;
            const int overlapCm = 1;
            const int alongMinCm = 0;
            const int alongMaxCm = segmentCount * stepCm;
            const int boundaryXcm = 400;
            const int surfaceMinYcm = 0;
            const int agentHeightCm = 180;
            const int agentRadiusCm = 0;

            NavObstacleSet chained = BuildBoundaryPolygonStrip(
                segmentCount,
                stepCm,
                overlapCm,
                skipSegmentIndex: -1,
                boundaryXcm);
            Assert.That(chained.ObstacleCount, Is.EqualTo(segmentCount));
            Assert.That(segmentCount, Is.GreaterThan(64));
            Assert.That(
                NavTriangleObstaclePredicate.IsBoundaryAlongIntervalFullyBlocked(
                    NavPortalSide.East,
                    boundaryXcm,
                    alongMinCm,
                    alongMaxCm,
                    surfaceMinYcm,
                    chained,
                    GroundLayerId,
                    agentHeightCm,
                    agentRadiusCm),
                Is.True,
                "80 chained boundary polygons must fully seal the along interval.");

            NavObstacleSet withGap = BuildBoundaryPolygonStrip(
                segmentCount,
                stepCm,
                overlapCm,
                skipSegmentIndex: 40,
                boundaryXcm);
            Assert.That(withGap.ObstacleCount, Is.EqualTo(segmentCount - 1));
            Assert.That(
                NavTriangleObstaclePredicate.IsBoundaryAlongIntervalFullyBlocked(
                    NavPortalSide.East,
                    boundaryXcm,
                    alongMinCm,
                    alongMaxCm,
                    surfaceMinYcm,
                    withGap,
                    GroundLayerId,
                    agentHeightCm,
                    agentRadiusCm),
                Is.False,
                "Removing the mid-chain segment must leave a free along gap.");
        }

        [Test]
        public void BoundaryAlongInterval_CompletedCoverage_StillValidatesLaterInvalidVerticalRange()
        {
            // Regression: once the coverage frontier already seals alongMaxCm mid-pass, the scan must
            // still finish and reject later enabled matching-layer obstacles with invalid half-open
            // vertical authoring — never early-return true and silently bypass source validation.
            const int alongMinCm = 0;
            const int alongMaxCm = 100;
            const int boundaryXcm = 400;
            const int surfaceMinYcm = 0;
            const int agentHeightCm = 180;
            const int agentRadiusCm = 0;
            const int halfThicknessCm = 10;

            var sealOnly = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "seal-full-interval",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        MinYcm = -10,
                        MaxYcm = 200,
                        Points =
                        {
                            new NavPointCm(boundaryXcm - halfThicknessCm, alongMinCm),
                            new NavPointCm(boundaryXcm + halfThicknessCm, alongMinCm),
                            new NavPointCm(boundaryXcm + halfThicknessCm, alongMaxCm),
                            new NavPointCm(boundaryXcm - halfThicknessCm, alongMaxCm)
                        }
                    }
                }
            };
            Assert.That(
                NavTriangleObstaclePredicate.IsBoundaryAlongIntervalFullyBlocked(
                    NavPortalSide.East,
                    boundaryXcm,
                    alongMinCm,
                    alongMaxCm,
                    surfaceMinYcm,
                    sealOnly,
                    GroundLayerId,
                    agentHeightCm,
                    agentRadiusCm),
                Is.True,
                "First obstacle alone must fully seal the requested along interval.");

            var sealThenInvalid = new NavObstacleSet
            {
                Obstacles =
                {
                    sealOnly.Obstacles[0],
                    new NavObstacle
                    {
                        Id = "later-invalid-vertical",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(boundaryXcm, alongMaxCm / 2),
                        RadiusCm = 10,
                        MinYcm = 50,
                        MaxYcm = 50
                    }
                }
            };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                NavTriangleObstaclePredicate.IsBoundaryAlongIntervalFullyBlocked(
                    NavPortalSide.East,
                    boundaryXcm,
                    alongMinCm,
                    alongMaxCm,
                    surfaceMinYcm,
                    sealThenInvalid,
                    GroundLayerId,
                    agentHeightCm,
                    agentRadiusCm))!;
            Assert.That(ex.Message, Does.Contain("minYcm").IgnoreCase);
            Assert.That(ex.Message, Does.Contain("maxYcm").IgnoreCase);
        }

        [Test]
        public void Recast_CornerObstacleOnCoarseTileFloor_KeepsFreeSideRoutePortals()
        {
            // Mirrors the RTS fortress seam: one quad per 6400cm tile, circle obstacle exactly at the
            // four-tile corner. Whole-triangle obstacle rejection would erase the entire z=0 border;
            // interval-aware evidence must keep free side-route portals far from the sealed gate.
            const int tileSizeCm = 6400;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    -tileSizeCm, 0, 0, -tileSizeCm,
                    0, tileSizeCm, tileSizeCm, 0,
                    -tileSizeCm, 0, 0, -tileSizeCm,
                    0, tileSizeCm, tileSizeCm, 0
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0
                },
                vertexZcm: new[]
                {
                    -tileSizeCm, -tileSizeCm, 0, 0,
                    -tileSizeCm, -tileSizeCm, 0, 0,
                    0, 0, tileSizeCm, tileSizeCm,
                    0, 0, tileSizeCm, tileSizeCm
                },
                triA: new[] { 0, 0, 4, 4, 8, 8, 12, 12 },
                triB: new[] { 1, 2, 5, 6, 9, 10, 13, 14 },
                triC: new[] { 2, 3, 6, 7, 10, 11, 14, 15 },
                triAreaIds: new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                triFlags: new[]
                {
                    FloorFlags, FloorFlags, FloorFlags, FloorFlags,
                    FloorFlags, FloorFlags, FloorFlags, FloorFlags
                });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(
                    originXcm: -tileSizeCm,
                    originZcm: -tileSizeCm,
                    tileWidthCm: tileSizeCm,
                    tileHeightCm: tileSizeCm,
                    tileCountX: 2,
                    tileCountZ: 2,
                    haloPaddingCm: 200));

            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "corner-gate",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(0, 0),
                        RadiusCm = 150,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            NavBakeResult bake = BakeRecast(
                index,
                new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(1, 0),
                    new NavBakeTileCoord(0, 1),
                    new NavBakeTileCoord(1, 1)
                },
                obstacles);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);

            NavTile sw = bake.Entries[0].Tile;
            NavTile se = bake.Entries[1].Tile;
            NavTile nw = bake.Entries[2].Tile;
            NavTile ne = bake.Entries[3].Tile;
            Assert.That(HasSidePortalCoveringAlong(sw, NavPortalSide.South, alongLocalCm: 1600), Is.True);
            Assert.That(HasSidePortalCoveringAlong(nw, NavPortalSide.North, alongLocalCm: 1600), Is.True);
            Assert.That(HasSidePortalCoveringAlong(se, NavPortalSide.South, alongLocalCm: 4800), Is.True);
            Assert.That(HasSidePortalCoveringAlong(ne, NavPortalSide.North, alongLocalCm: 4800), Is.True);

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetour(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: -3200,
                startZcm: -3200,
                goalXcm: -3200,
                goalZcm: 3200,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), "Free western side route must remain after corner gate seal.");
        }

        [Test]
        public void Recast_SteepLowWallNeighbor_NoPortal()
        {
            // Vertical wall on the neighbor side of the boundary �?not a walkable surface for portal proof.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[]
                {
                    0, 400, 400, 0,
                    400, 400, 400, 400
                },
                vertexYcm: new[]
                {
                    0, 0, 0, 0,
                    0, 0, 180, 180
                },
                vertexZcm: new[]
                {
                    0, 0, 400, 400,
                    0, 400, 400, 0
                },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 1, 2, 3, 4 },
                triFlags: new[] { FloorFlags, FloorFlags, SolidOnly, SolidOnly });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 2, 1, haloPaddingCm: 200));

            NavTile left = BakeRecastTile(index, new NavBakeTileCoord(0, 0));
            Assert.That(HasSidePortal(left, NavPortalSide.East), Is.False);
        }

        [Test]
        public void Detour_NoNavTilePortal_NoExternalLinkAndNoCrossTilePath()
        {
            const int tileSizeCm = 400;
            NavTile left = DefaultGridNavTileFactory.CreateFlatTile(0, 0, 0, 1, 4, 100);
            NavTile right = DefaultGridNavTileFactory.CreateFlatTile(1, 0, 0, 1, 4, 100);
            NavTile leftClosed = StripPortals(left);
            NavTile rightClosed = StripPortals(right);

            byte[] leftBytes = DetourNavQueryEngine.BuildDetourTileBytes(leftClosed, tileSizeCm, tileSizeCm);
            byte[] rightBytes = DetourNavQueryEngine.BuildDetourTileBytes(rightClosed, tileSizeCm, tileSizeCm);
            Assert.That(leftBytes.Length, Is.GreaterThan(0));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                new[] { leftBytes, rightBytes },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 150,
                goalXcm: 450,
                goalZcm: 150,
                maxPortals: 64);
            Assert.That(path.Status, Is.Not.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void SegmentLength_LargeLocalDeltas_DoNotOverflow()
        {
            int len = NavSegmentMetrics.RoundEuclideanLengthCm(
                0, 0, 0,
                100_000, 0, 100_000);
            Assert.That(len, Is.EqualTo((int)Math.Round(Math.Sqrt(2d * 100_000d * 100_000d))));

            // Values that overflow int squared arithmetic must still succeed via Int128.
            int wide = NavSegmentMetrics.RoundEuclideanLengthCm(
                -40_000, 0, 0,
                40_000, 0, 0);
            Assert.That(wide, Is.EqualTo(80_000));

            // Length beyond int.MaxValue fails explicitly rather than wrapping.
            Assert.Throws<OverflowException>(() =>
                NavSegmentMetrics.RoundEuclideanLengthCm(-1_500_000_000, 0, 0, 1_500_000_000, 0, 0));
        }

        [Test]
        public void PortalCoordinateCapacity_BeyondShort_FailsExplicitly()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                    short.MaxValue + 1,
                    400,
                    "RecastBorderPortalContractTests"))!;
            Assert.That(ex.Message, Does.Contain("short").IgnoreCase);

            Assert.Throws<InvalidOperationException>(() =>
                NavBorderPortalCoordinateContract.RequirePortalCoordinate(short.MaxValue + 1, "u"));

            Assert.Throws<InvalidOperationException>(() =>
                DetourNavQueryEngine.BuildDetourTileBytes(
                    DefaultGridNavTileFactory.CreateFlatTile(0, 0, 0, 1, 4, 100),
                    short.MaxValue + 1,
                    400));
        }

        private static NavObstacleSet BuildBoundaryPolygonStrip(
            int segmentCount,
            int stepCm,
            int overlapCm,
            int skipSegmentIndex,
            int boundaryXcm)
        {
            var obstacles = new NavObstacleSet();
            int halfThicknessCm = 10;
            for (int i = 0; i < segmentCount; i++)
            {
                if (i == skipSegmentIndex)
                {
                    continue;
                }

                int z0 = i * stepCm;
                int z1 = z0 + stepCm + overlapCm;
                obstacles.Obstacles.Add(new NavObstacle
                {
                    Id = $"boundary-strip-{i}",
                    Enabled = true,
                    Kind = NavObstacleKind.Polygon,
                    LayerId = GroundLayerId,
                    MinYcm = -10,
                    MaxYcm = 200,
                    Points =
                    {
                        new NavPointCm(boundaryXcm - halfThicknessCm, z0),
                        new NavPointCm(boundaryXcm + halfThicknessCm, z0),
                        new NavPointCm(boundaryXcm + halfThicknessCm, z1),
                        new NavPointCm(boundaryXcm - halfThicknessCm, z1)
                    }
                });
            }

            return obstacles;
        }

        private static bool HasSidePortal(NavTile tile, NavPortalSide side)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i].Side == side)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSidePortalCoveringAlong(NavTile tile, NavPortalSide side, int alongLocalCm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                int a;
                int b;
                if (side is NavPortalSide.West or NavPortalSide.East)
                {
                    a = portal.LeftZcm;
                    b = portal.RightZcm;
                }
                else
                {
                    a = portal.LeftXcm;
                    b = portal.RightXcm;
                }

                int min = a < b ? a : b;
                int max = a > b ? a : b;
                if (min <= alongLocalCm && alongLocalCm <= max)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEastPortalNearY(NavTile tile, int ycm, int toleranceCm)
        {
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal p = portals[i];
                if (p.Side != NavPortalSide.East)
                {
                    continue;
                }

                if (Math.Abs(p.LeftYcm - ycm) <= toleranceCm || Math.Abs(p.RightYcm - ycm) <= toleranceCm)
                {
                    return true;
                }
            }

            return false;
        }

        private static NavTriangleSurfaceTileIndex CreateContinuousFloor(int tileCountX, int tileWidthCm, int yCm)
        {
            int widthCm = checked(tileCountX * tileWidthCm);
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, widthCm, widthCm, 0 },
                vertexYcm: new[] { yCm, yCm, yCm, yCm },
                vertexZcm: new[] { 0, 0, tileWidthCm, tileWidthCm },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            return NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, tileWidthCm, tileWidthCm, tileCountX, 1, haloPaddingCm: 200));
        }

        private static NavTile BakeRecastTile(NavTriangleSurfaceTileIndex surface, NavBakeTileCoord target)
        {
            NavBakeResult bake = BakeRecast(surface, new[] { target });
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            return bake.Entries[0].Tile;
        }

        private static NavBakeResult BakeRecast(
            NavTriangleSurfaceTileIndex surface,
            IReadOnlyList<NavBakeTileCoord> targets,
            INavObstacleSource? obstacles = null)
        {
            NavMeshBakeConfig config = CreateBakeConfig();
            var context = new NavBakeContext
            {
                MapId = "nav_recast_border_portal",
                SourceUri = "Core:Maps/nav_recast_border_portal.vtxm",
                TriangleSurface = surface,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            return new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
        }

        private static List<byte[]> CollectDetour(NavBakeResult bake)
        {
            var bytes = new List<byte[]>(bake.Entries.Count);
            for (int i = 0; i < bake.Entries.Count; i++)
            {
                Assert.That(bake.Entries[i].Success, Is.True, bake.Entries[i].Artifact.Message);
                Assert.That(bake.Entries[i].DetourTileBytes.Length, Is.GreaterThan(0));
                bytes.Add(bake.Entries[i].DetourTileBytes);
            }

            return bytes;
        }

        private static NavTile StripPortals(NavTile tile)
        {
            var stripped = new NavTile(
                tile.TileId,
                tile.TileVersion,
                tile.BuildConfigHash,
                tile.Checksum,
                tile.OriginXcm,
                tile.OriginZcm,
                tile.VertexXcm,
                tile.VertexYcm,
                tile.VertexZcm,
                tile.TriA,
                tile.TriB,
                tile.TriC,
                tile.N0,
                tile.N1,
                tile.N2,
                tile.TriAreaIds,
                Array.Empty<NavBorderPortal>());
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, stripped);
            ms.Position = 0;
            return NavTileBinary.Read(ms);
        }

        private static NavMeshBakeConfig CreateBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmRecast,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1,
                    TrackedStructuralEntityCapacity = 32,
                    ObstaclePrimitiveCapacity = 64,
                    PolygonVertexCapacity = 512,
                    DirtyTileCapacity = 64,
                    StagedEntryCapacity = 64,
                    PublishedTileCapacity = 64,
                    StoreGroupCapacity = 8,
                    ResidentTileCapacity = 128,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 200 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }
    }
}
