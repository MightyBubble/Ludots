using System;
using System.Collections.Generic;
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
    /// Focused Stage D zero-allocation and bank-capacity contracts for the LayeredSpan adapter
    /// (TryBakeInto / NavBakeService.BakeInto). Stage F queue/GameEngine/telemetry harnesses are
    /// not required here; everything runs against the pool, the surface index and a banked tile.
    /// </summary>
    [TestFixture]
    public sealed class LayeredSpanRuntimeZeroAllocContractTests
    {
        private const string GroundLayerId = "Ground";

        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void LayeredSpan_BakeInto_WarmedDensePath_AllocatesZeroManagedBytes()
        {
            (LayeredSpanNavBakeAlgorithm algorithm, NavBakeService service, NavBakeContext context,
             NavLayerConfig layer, NavMeshAgentProfileConfig navProfile, AgentProfileConfig agentProfile) = CreateHarness();

            NavTile destination = NavTile.CreateBanked(
                vertexCapacity: 1024,
                triangleCapacity: 2048,
                portalCapacity: 256);
            Span<byte> checksumScratch = stackalloc byte[NavTileBinary.GetSerializedSize(
                FullProbe(destination.VertexCapacity, destination.TriangleCapacity, destination.PortalCapacity))];

            for (int i = 0; i < 64; i++)
            {
                bool warm = service.BakeInto(
                    context,
                    new NavBakeTileCoord(0, 0),
                    layer,
                    navProfile,
                    agentProfile,
                    destination,
                    checksumScratch,
                    out NavBakeArtifact warmArtifact);
                if (!warm)
                {
                    throw new InvalidOperationException(warmArtifact.Message);
                }
            }

            Assert.That(destination.TriangleCount, Is.GreaterThan(0), "Dense obstacle tile must produce walkable triangles.");
            Assert.That(destination.Checksum, Is.Not.EqualTo(0UL));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                bool ok = service.BakeInto(
                    context,
                    new NavBakeTileCoord(0, 0),
                    layer,
                    navProfile,
                    agentProfile,
                    destination,
                    checksumScratch,
                    out NavBakeArtifact artifact);
                if (!ok)
                {
                    throw new InvalidOperationException(artifact.Message);
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0L),
                $"Warmed NavBakeService.BakeInto dense layered-span path allocated {allocated} bytes.");
            Assert.That(destination.Checksum, Is.Not.EqualTo(0UL));

            long beforeDirect = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                bool ok = algorithm.TryBakeInto(
                    context,
                    new NavBakeTileCoord(0, 0),
                    layer,
                    navProfile,
                    agentProfile,
                    destination,
                    checksumScratch,
                    out NavBakeArtifact artifact);
                if (!ok)
                {
                    throw new InvalidOperationException(artifact.Message);
                }
            }

            long allocatedDirect = GC.GetAllocatedBytesForCurrentThread() - beforeDirect;
            Assert.That(
                allocatedDirect,
                Is.EqualTo(0L),
                $"Warmed LayeredSpanNavBakeAlgorithm.TryBakeInto dense path allocated {allocatedDirect} bytes.");
        }

        [Test]
        public void LayeredSpan_BakeInto_BankCapacityExhaustion_NamesOwnerAndRequired()
        {
            (_, NavBakeService service, NavBakeContext context, NavLayerConfig layer,
             NavMeshAgentProfileConfig navProfile, AgentProfileConfig agentProfile) = CreateHarness();

            var tinyVertexTile = NavTile.CreateBanked(
                vertexCapacity: 1,
                triangleCapacity: 2048,
                portalCapacity: 256);
            var vertexScratch = new byte[NavTileBinary.GetSerializedSize(
                FullProbe(tinyVertexTile.VertexCapacity, tinyVertexTile.TriangleCapacity, tinyVertexTile.PortalCapacity))];
            InvalidOperationException vertexEx = Assert.Throws<InvalidOperationException>(
                () => service.BakeInto(
                    context,
                    new NavBakeTileCoord(0, 0),
                    layer,
                    navProfile,
                    agentProfile,
                    tinyVertexTile,
                    vertexScratch,
                    out _))!;
            Assert.That(vertexEx.Message, Does.Contain("outputVertexCapacity"));
            Assert.That(vertexEx.Message, Does.Contain("required"));

            var tinyTriangleTile = NavTile.CreateBanked(
                vertexCapacity: 1024,
                triangleCapacity: 1,
                portalCapacity: 256);
            var triangleScratch = new byte[NavTileBinary.GetSerializedSize(
                FullProbe(tinyTriangleTile.VertexCapacity, tinyTriangleTile.TriangleCapacity, tinyTriangleTile.PortalCapacity))];
            InvalidOperationException triangleEx = Assert.Throws<InvalidOperationException>(
                () => service.BakeInto(
                    context,
                    new NavBakeTileCoord(0, 0),
                    layer,
                    navProfile,
                    agentProfile,
                    tinyTriangleTile,
                    triangleScratch,
                    out _))!;
            Assert.That(triangleEx.Message, Does.Contain("outputTriangleCapacity"));
            Assert.That(triangleEx.Message, Does.Contain("required"));
        }

        private static NavTile FullProbe(int vertexCapacity, int triangleCapacity, int portalCapacity)
        {
            NavTile probe = NavTile.CreateBanked(vertexCapacity, triangleCapacity, portalCapacity);
            probe.SetCounts(vertexCapacity, triangleCapacity, portalCapacity);
            return probe;
        }

        private static (
            LayeredSpanNavBakeAlgorithm Algorithm,
            NavBakeService Service,
            NavBakeContext Context,
            NavLayerConfig Layer,
            NavMeshAgentProfileConfig NavProfile,
            AgentProfileConfig AgentProfile) CreateHarness()
        {
            NavLayeredSpanConfig layered = CreateLayeredConfig();
            var pool = new LayeredSpanScratchPool(layered);
            var algorithm = new LayeredSpanNavBakeAlgorithm(pool);
            var service = new NavBakeService(algorithm);

            NavTriangleSurfaceSnapshot surface = QuadFloor(0, 0, 400, 400, y: 0, area: 1, stable: 1);
            var grid = new NavTriangleSurfaceTileGrid(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm: 400,
                tileHeightCm: 400,
                tileCountX: 1,
                tileCountZ: 1,
                haloPaddingCm: 200);
            NavTriangleSurfaceTileIndex index = NavTriangleSurfaceTileIndex.Build(surface, grid);

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

            var layer = new NavLayerConfig { Id = GroundLayerId, Layer = 0 };
            var navProfile = new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 };
            var agentProfile = new AgentProfileConfig
            {
                Id = "Small",
                RadiusCm = 30,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0
            };

            var context = new NavBakeContext
            {
                MapId = "layered_span_stage_d_zero_alloc",
                SourceUri = "Core:Maps/layered_span_stage_d_zero_alloc.tris",
                TriangleSurface = index,
                Obstacles = obstacles,
                Config = CreateBakeConfig(layered),
                AgentProfiles = new AgentProfileRegistry(new[] { agentProfile }),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            return (algorithm, service, context, layer, navProfile, agentProfile);
        }

        private static NavTriangleSurfaceSnapshot QuadFloor(
            int minX,
            int minZ,
            int maxX,
            int maxZ,
            int y,
            byte area,
            int stable)
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
    }
}
