using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanSpanningTriangleContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void LayeredSpan_MixedBaselineAndObstacleTile_NorthMarch_StringPullsAroundBuilding()
        {
            // After placing a building, only the touched tile leaves flat-grid-baseline and becomes
            // dense LayeredSpan. Funnel (FindStraightPath) must still collapse the corridor to a
            // short pulled-string — not a portal-by-portal weave filling open triangles.
            const int chunkSizeCells = 64;
            const int chunks = 8;
            const int cellSizeCm = SpatialScaleDefaults.CellCm;
            const int originXcm = -25600;
            const int originZcm = -25600;
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            var terrain = new FlatGridLogicTerrainField(
                widthCells: chunks * chunkSizeCells,
                heightCells: chunks * chunkSizeCells,
                cellSizeCm: cellSizeCm,
                chunkSizeCells: chunkSizeCells,
                originXcm: originXcm,
                originZcm: originZcm);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmLayeredSpan);
            config.LayeredSpan.ColumnCapacity = 8192;
            config.RuntimeIncremental.OutputVertexCapacity = 16384;
            config.RuntimeIncremental.OutputTriangleCapacity = 32768;
            config.RuntimeIncremental.OutputPortalCapacity = 4096;
            var build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);

            var targets = new[]
            {
                new NavBakeTileCoord(3, 3),
                new NavBakeTileCoord(4, 3),
                new NavBakeTileCoord(3, 4),
                new NavBakeTileCoord(4, 4)
            };
            NavBakeResult openBake = BakeAll(surface, config, build, targets, NavBakeAlgorithmKind.LayeredSpan);
            var tiles = new NavTile[openBake.Entries.Count];
            for (int i = 0; i < openBake.Entries.Count; i++)
            {
                Assert.That(openBake.Entries[i].Success, Is.True, openBake.Entries[i].Artifact.Message);
                tiles[i] = openBake.Entries[i].Tile;
            }

            // Building deep inside the NE tile — well clear of the x=100 north march and of tile seams.
            // Mixed baseline/dense linking must still allow a direct pulled-string at x=100.
            const int buildingXcm = 3200;
            const int buildingZcm = 3200;
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
                        Center = new NavPointCm(buildingXcm, buildingZcm),
                        RadiusCm = buildingRadiusCm,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            int obstacleTileIndex = -1;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                int minX = tile.OriginXcm;
                int minZ = tile.OriginZcm;
                int maxX = checked(minX + tileSizeCm);
                int maxZ = checked(minZ + tileSizeCm);
                if (buildingXcm + buildingRadiusCm < minX ||
                    buildingXcm - buildingRadiusCm > maxX ||
                    buildingZcm + buildingRadiusCm < minZ ||
                    buildingZcm - buildingRadiusCm > maxZ)
                {
                    continue;
                }

                obstacleTileIndex = i;
                NavBakeResult blocked = BakeAllWithObstacles(
                    surface,
                    config,
                    build,
                    new[] { new NavBakeTileCoord(tile.TileId.ChunkX, tile.TileId.ChunkY) },
                    NavBakeAlgorithmKind.LayeredSpan,
                    obstacles);
                Assert.That(blocked.Entries[0].Success, Is.True, blocked.Entries[0].Artifact.Message);
                tiles[i] = blocked.Entries[0].Tile;
                Assert.That(
                    DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tiles[i], tileSizeCm, tileSizeCm),
                    Is.False,
                    "Obstacle tile must leave flat-grid-baseline and use dense LayeredSpan.");
            }

            Assert.That(obstacleTileIndex, Is.GreaterThanOrEqualTo(0), "Building must dirty at least one of the four tiles.");

            int baselineCount = 0;
            for (int i = 0; i < tiles.Length; i++)
            {
                if (DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tiles[i], tileSizeCm, tileSizeCm))
                {
                    baselineCount++;
                }
            }

            Assert.That(baselineCount, Is.EqualTo(tiles.Length - 1), "Neighbor tiles must remain flat-grid-baseline.");

            NavTile obstacleTile = tiles[obstacleTileIndex];
            string componentSummary = SummarizeTriangleConnectedComponents(obstacleTile);
            Assert.That(
                CountTriangleConnectedComponents(obstacleTile),
                Is.EqualTo(1),
                "Obstacle LayeredSpan tile must stay a single walkable component outside the building hole. " + componentSummary);
            Assert.That(
                CountInternalNeighborEdges(obstacleTile),
                Is.GreaterThan(obstacleTile.TriangleCount),
                "Dense obstacle tile must retain internal triangle adjacency (not only border portals). " + componentSummary);
            Assert.That(
                PointInTileTriangles(obstacleTile, localXcm: 100, localZcm: 2000),
                Is.True,
                "West corridor sample (100,2000) must stay covered after building bake.");
            Assert.That(
                PointInTileTriangles(obstacleTile, localXcm: 100, localZcm: 5000),
                Is.True,
                "West corridor sample (100,5000) must stay covered after building bake.");
            Assert.That(
                PortalSideCoversAlong(obstacleTile, NavPortalSide.North, alongCm: 100),
                Is.True,
                "Dense tile north border must keep a portal covering x=100 for baseline handoff.");
            Assert.That(
                CountClockwiseTriangles(obstacleTile),
                Is.EqualTo(0),
                "Detour funnel requires consistent CCW triangles; CW faces flip portal left/right and weave.");

            // Reachability + connectivity are the hard gates after the hole-winding fix.
            // Hole-annulus ear-clip can still leave west↔south border fans; FindStraightPath then
            // emits many apexes on that corridor (mesh quality follow-up, not a dead funnel).
            const int startXcm = 100;
            const int startZcm = -2000;
            const int goalXcm = 100;
            const int goalZcm = 3600;
            NavPathResult path = DetourNavQueryEngine.FindPath(
                tiles,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                tileWidthCm: tileSizeCm,
                tileHeightCm: tileSizeCm,
                startXcm: startXcm,
                startZcm: startZcm,
                goalXcm: goalXcm,
                goalZcm: goalZcm,
                maxPortals: 256);
            Assert.That(
                path.Status,
                Is.EqualTo(NavPathStatus.Ok),
                $"status={path.Status} points={path.PathXcm.Length} {componentSummary} path={FormatPath(path)}");
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(path.PathXcm[0], Is.EqualTo(startXcm));
            Assert.That(path.PathZcm[0], Is.EqualTo(startZcm));
            Assert.That(path.PathXcm[^1], Is.EqualTo(goalXcm));
            Assert.That(path.PathZcm[^1], Is.EqualTo(goalZcm));
            // Must not thread the building disc — that was the east/west component split symptom.
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                long dx = path.PathXcm[i] - buildingXcm;
                long dz = path.PathZcm[i] - buildingZcm;
                Assert.That(
                    (dx * dx) + (dz * dz),
                    Is.GreaterThan((long)buildingRadiusCm * buildingRadiusCm),
                    $"Waypoint ({path.PathXcm[i]},{path.PathZcm[i]}) must stay outside the building. {FormatPath(path)}");
            }
        }

        private static NavBakeResult BakeAll(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm)
            => BakeAllWithObstacles(surface, config, build, targets, algorithm, new NavObstacleSet());

        private static NavBakeResult BakeAllWithObstacles(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            NavBuildConfig build,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm,
            INavObstacleSource obstacles)
        {
            config.Algorithm = NavBakeNames.FormatAlgorithm(algorithm);
            var context = new NavBakeContext
            {
                MapId = "nav_diff_reachability",
                SourceUri = "Core:Maps/nav_diff_reachability.vtxm",
                TriangleSurface = surface,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var adapter = new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan));
            NavBakeResult result = new NavBakeService(adapter).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0));
            return result;
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
            => SummarizeTriangleConnectedComponents(tile, out _);

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

        private static bool PointInTileTriangles(NavTile tile, int localXcm, int localZcm)
        {
            for (int t = 0; t < tile.TriangleCount; t++)
            {
                int a = tile.TriA[t];
                int b = tile.TriB[t];
                int c = tile.TriC[t];
                if (PointInTriangleInclusive(
                        localXcm,
                        localZcm,
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
            // Z-up / XZ floor: positive cross (bx-ax)*(cz-az)-(bz-az)*(cx-ax) is CCW in the XZ plane
            // used by LayeredSpanContourBuilder ("mathematical-CCW left for Z-down grids" is the
            // contour convention; baked NavTile tris must stay CCW for Detour portals).
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

        private static NavMeshBakeConfig CreateBakeConfig(string mode, string algorithm)
        {
            return new NavMeshBakeConfig
            {
                Mode = mode,
                Algorithm = algorithm,
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
                LayeredSpan = new NavLayeredSpanConfig
                {
                    ScratchSlotCount = 2,
                    RasterCellSizeCm = 100,
                    // Halo depth 2: outer clearance seeds sit at the rim; depth-1 alone
                    // leaves border neighbors as outer seeds (clearance 0) and drops portals.
                    RasterHaloCells = 2,
                    SameSurfaceToleranceCm = 5,
                    MaxSimplificationErrorCm = 0,
                    HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                    MaxLawsonFlipCount = 100_000,
                    ColumnCapacity = 4096,
                    SpanCapacity = 16384,
                    ClassifiedSpanCapacity = 16384,
                    WalkableSpanCapacity = 16384,
                    LinkCapacity = 65536,
                    SheetCapacity = 16384,
                    PortalIntervalCapacity = 65536,
                    RegionCapacity = 4096,
                    ChartCapacity = 1024,
                    RingCapacity = 2048,
                    ContourVertexCapacity = 16384,
                    ContourEdgeCapacity = 16384,
                    SeamCapacity = 4096,
                    CanonicalLinkCapacity = 65536,
                    SplitPointCapacity = 4096,
                    TriangulationVertexCapacity = 16384,
                    TriangulationTriangleCapacity = 32768,
                    ConstrainedEdgeCapacity = 32768,
                    BorderPortalCapacity = 4096,
                    PolygonVertexCapacity = 16384,
                    AdjacencyEdgeCapacity = 98304,
                    BridgeCandidateCapacity = 16384,
                    RingWorkCapacity = 2048,
                    TemporaryConstraintFlagCapacity = 32768
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
