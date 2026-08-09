using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Proves LayeredSpan bitwise determinism through the production adapter: the same input baked
    /// N&gt;=3 times must serialize to byte-for-byte identical NavTileBinary payloads and checksums.
    /// The adapter declares GuaranteesBitwiseDeterminism only because this contract stays green.
    /// </summary>
    [TestFixture]
    public sealed class LayeredSpanBitwiseDeterminismContractTests
    {
        private const string GroundLayerId = "Ground";

        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void RepeatedBake_StackedFloors_SameBytesAndChecksumAcrossFourRuns()
        {
            AssertDeterministic(BuildStackedFloorSurface());
        }

        [Test]
        public void RepeatedBake_ObstacleTile_SameBytesAndChecksumAcrossFourRuns()
        {
            AssertDeterministic(BuildObstacleFloorSurface());
        }

        [Test]
        public void Adapter_DeclaresBitwiseDeterminism()
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig();
            var algorithm = new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(layered));
            Assert.That(algorithm.GuaranteesBitwiseDeterminism, Is.True);
        }

        private static void AssertDeterministic(NavTriangleSurfaceSnapshot surface)
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig();
            var pool = new LayeredSpanScratchPool(layered);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);
            NavTriangleSurfaceTileIndex index = BuildIndex(surface, layered);

            const int runs = 4;
            var serialized = new byte[runs][];
            var checksums = new ulong[runs];
            for (int i = 0; i < runs; i++)
            {
                NavBakeContext context = CreateContext(index, layered);
                NavBakeResult result = service.Bake(context);
                Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
                Assert.That(result.Entries[0].Tile.TriangleCount, Is.GreaterThan(0), result.Entries[0].Artifact.Message);
                checksums[i] = result.Entries[0].Tile.Checksum;
                using var ms = new MemoryStream();
                NavTileBinary.Write(ms, result.Entries[0].Tile);
                serialized[i] = ms.ToArray();
            }

            for (int i = 1; i < runs; i++)
            {
                Assert.That(
                    serialized[i],
                    Is.EqualTo(serialized[0]),
                    $"Bake run {i} serialized bytes differ from run 0 ({serialized[0].Length} bytes).");
                Assert.That(
                    checksums[i],
                    Is.EqualTo(checksums[0]),
                    $"Bake run {i} checksum {checksums[i]} differs from run 0 {checksums[0]}.");
            }
        }

        private static NavTriangleSurfaceSnapshot BuildStackedFloorSurface()
        {
            // Two full floors at y=0 and y=500: dense path with stacked same-XZ different-Y charts.
            int cell = 100;
            const int cells = 4;
            int triCount = cells * cells * 4;
            int vertCount = cells * cells * 8;
            var vx = new int[vertCount];
            var vy = new int[vertCount];
            var vz = new int[vertCount];
            var ta = new int[triCount];
            var tb = new int[triCount];
            var tc = new int[triCount];
            var areas = new byte[triCount];
            var stables = new int[triCount];
            var flags = new NavTriangleSurfaceFlags[triCount];

            int v = 0;
            int t = 0;
            int stable = 1;
            for (int layer = 0; layer < 2; layer++)
            {
                int y = layer == 0 ? 0 : 500;
                for (int cz = 0; cz < cells; cz++)
                {
                    for (int cx = 0; cx < cells; cx++)
                    {
                        int minX = cx * cell;
                        int maxX = minX + cell;
                        int minZ = cz * cell;
                        int maxZ = minZ + cell;
                        int v0 = v++;
                        int v1 = v++;
                        int v2 = v++;
                        int v3 = v++;
                        vx[v0] = minX; vy[v0] = y; vz[v0] = minZ;
                        vx[v1] = maxX; vy[v1] = y; vz[v1] = minZ;
                        vx[v2] = minX; vy[v2] = y; vz[v2] = maxZ;
                        vx[v3] = maxX; vy[v3] = y; vz[v3] = maxZ;

                        ta[t] = v0; tb[t] = v1; tc[t] = v2;
                        areas[t] = 1; stables[t] = stable++; flags[t] = FloorFlags; t++;
                        ta[t] = v1; tb[t] = v3; tc[t] = v2;
                        areas[t] = 1; stables[t] = stable++; flags[t] = FloorFlags; t++;
                    }
                }
            }

            return new NavTriangleSurfaceSnapshot(vx, vy, vz, ta, tb, tc, areas, stables, flags);
        }

        private static NavTriangleSurfaceSnapshot BuildObstacleFloorSurface()
        {
            return new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 0, 400 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 1, 1 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
        }

        private static NavTriangleSurfaceTileIndex BuildIndex(NavTriangleSurfaceSnapshot surface, NavLayeredSpanConfig layered)
        {
            var grid = new NavTriangleSurfaceTileGrid(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm: 400,
                tileHeightCm: 400,
                tileCountX: 1,
                tileCountZ: 1,
                haloPaddingCm: checked(layered.RasterHaloCells * layered.RasterCellSizeCm));
            return NavTriangleSurfaceTileIndex.Build(surface, grid);
        }

        private static NavBakeContext CreateContext(NavTriangleSurfaceTileIndex index, NavLayeredSpanConfig layered)
        {
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "pillar",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(80, 80),
                        RadiusCm = 40,
                        MinYcm = 0,
                        MaxYcm = 300
                    }
                }
            };

            return new NavBakeContext
            {
                MapId = "layered_span_determinism",
                SourceUri = "Core:Maps/layered_span_determinism.tris",
                TriangleSurface = index,
                Obstacles = obstacles,
                Config = CreateBakeConfig(layered),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 3,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavLayeredSpanConfig CreateLayeredConfig()
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = 2,
                RasterCellSizeCm = 100,
                RasterHaloCells = 2,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = 512,
                SpanCapacity = 1024,
                ClassifiedSpanCapacity = 1024,
                WalkableSpanCapacity = 1024,
                LinkCapacity = 4096,
                SheetCapacity = 1024,
                PortalIntervalCapacity = 4096,
                RegionCapacity = 128,
                ChartCapacity = 64,
                RingCapacity = 64,
                ContourVertexCapacity = 2048,
                ContourEdgeCapacity = 2048,
                SeamCapacity = 512,
                CanonicalLinkCapacity = 4096,
                SplitPointCapacity = 256,
                TriangulationVertexCapacity = 2048,
                TriangulationTriangleCapacity = 4096,
                ConstrainedEdgeCapacity = 4096,
                BorderPortalCapacity = 128,
                PolygonVertexCapacity = 2048,
                AdjacencyEdgeCapacity = 12288,
                BridgeCandidateCapacity = 2048,
                RingWorkCapacity = 128,
                TemporaryConstraintFlagCapacity = 4096
            };
        }

        private static NavMeshBakeConfig CreateBakeConfig(NavLayeredSpanConfig layered)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmLayeredSpan,
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
                LayeredSpan = layered,
                TriangleSurface = new NavTriangleSurfaceConfig
                {
                    HaloPaddingCm = checked(layered.RasterHaloCells * layered.RasterCellSizeCm)
                },
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
