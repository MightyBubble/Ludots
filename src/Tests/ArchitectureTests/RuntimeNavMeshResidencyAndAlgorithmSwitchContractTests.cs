using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Engine;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Stage F runtime composition contracts: generation-sealed incremental-vs-full terminal
    /// equivalence, algorithm-switch generation fence, triangle-surface replacement fence,
    /// fixed resident/output capacity hard-fails, Core+external adapter composition through the
    /// service registry, and query tile-space origin ownership.
    /// </summary>
    [TestFixture]
    public sealed class RuntimeNavMeshResidencyAndAlgorithmSwitchContractTests
    {
        private const string GroundLayerId = "Ground";

        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void Queue_RuntimeIncrementalVsFullBake_EquivalentTerminalState()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            NavMeshBakeConfig config = CreateRuntimeConfig();
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            NavBuildConfig build = new NavBuildConfig(1f, 0.6f, 1);

            // Full offline bake over the same surface = terminal reference state (tile version 1).
            var fullContext = new NavBakeContext
            {
                MapId = "nav_fvsi_full",
                SourceUri = "Core:Maps/nav_fvsi_full.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = agentProfiles,
                Targets = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            NavBakeResult full = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(fullContext);
            Assert.That(full.FailureCount, Is.EqualTo(0));
            var fullChecksums = new Dictionary<NavBakeTileCoord, ulong>();
            foreach (NavBakeResultEntry entry in full.Entries)
            {
                fullChecksums[entry.Target] = entry.Tile.Checksum;
            }

            // Incremental queue: same surface, same adapter, resident window = both tiles.
            // Base tile version 0 so the first sealed batch bakes at version 1 (matches the full
            // bake) and the serialized terminal states are byte-equivalent.
            var context = new NavBakeContext
            {
                MapId = "nav_fvsi_inc",
                SourceUri = "Core:Maps/nav_fvsi_inc.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = agentProfiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 0,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            NavTileStore store = CreateTestStore(config);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));

            queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) });
            RuntimeNavMeshRebuildBatch batch = Drain(queue);

            Assert.That(batch.Committed, Is.True);
            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(2));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile inc0), Is.True);
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out NavTile inc1), Is.True);
            Assert.That(inc0.Checksum, Is.EqualTo(fullChecksums[new NavBakeTileCoord(0, 0)]),
                "Incremental terminal tile checksum must equal the full-bake terminal state.");
            Assert.That(inc1.Checksum, Is.EqualTo(fullChecksums[new NavBakeTileCoord(1, 0)]));
        }

        [Test]
        public void Queue_SwitchAlgorithm_KeepsOldVisibleThenCommitsOneGeneration()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 1, tileCountZ: 1);
            NavMeshBakeConfig config = CreateRuntimeConfig();
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var layeredPool = new LayeredSpanScratchPool(CreateLayeredConfig());
            var exactCdt = new ExactCdtNavBakeAlgorithm();
            var layeredSpan = new LayeredSpanNavBakeAlgorithm(layeredPool);
            var context = CreateSurfaceContext(surface, config, agentProfiles);

            NavTileStore store = CreateTestStore(config);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(exactCdt, layeredSpan),
                context,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));

            queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0) });
            RuntimeNavMeshRebuildBatch boot = Drain(queue);
            Assert.That(boot.Committed, Is.True);
            Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.ExactCdt));
            ulong committedChecksum = store.TryGet(new NavTileId(0, 0, 0), out NavTile visible) ? visible.Checksum : 0UL;

            // Switch is a cold-path generation fence: outstanding request, bake under the new
            // adapter, commit only when the sealed generation is complete.
            queue.SwitchAlgorithm(NavBakeAlgorithmKind.LayeredSpan, new[] { new NavBakeTileCoord(0, 0) });
            Assert.That(queue.HasRequestedAlgorithm, Is.True);
            Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.ExactCdt), "Old algorithm stays visible until atomic commit.");

            RuntimeNavMeshRebuildBatch switched = Drain(queue);
            Assert.That(switched.Committed, Is.True);
            Assert.That(switched.FailedEntryCount, Is.EqualTo(0));
            Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(queue.HasRequestedAlgorithm, Is.False);
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile after), Is.True);
            Assert.That(after.Checksum, Is.Not.EqualTo(committedChecksum), "LayeredSpan output must differ from CDT output.");
        }

        [Test]
        public void Queue_SwitchAlgorithm_UnregisteredKind_DoesNotMutateStore()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 1, tileCountZ: 1);
            NavMeshBakeConfig config = CreateRuntimeConfig();
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var context = CreateSurfaceContext(surface, config, agentProfiles);
            NavTileStore store = CreateTestStore(config);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));
            queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0) });
            _ = Drain(queue);
            ulong revisionBefore = store.Revision;
            ulong generationBefore = store.Generation;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                queue.SwitchAlgorithm(NavBakeAlgorithmKind.LayeredSpan, new[] { new NavBakeTileCoord(0, 0) }))!;
            Assert.That(ex.Message, Does.Contain("not registered"));
            Assert.That(store.Revision, Is.EqualTo(revisionBefore));
            Assert.That(store.Generation, Is.EqualTo(generationBefore));
            Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.ExactCdt));
        }

        [Test]
        public void Queue_ReplaceTriangleSurface_RejectedWhileSealed()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            NavMeshBakeConfig config = CreateRuntimeConfig();
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var context = CreateSurfaceContext(surface, config, agentProfiles);
            NavTileStore store = CreateTestStore(config);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));
            queue.RequestResidentWindowTransition(new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) });
            _ = Drain(queue);

            NavTriangleSurfaceTileIndex edited = NavTriangleSurfaceTerrainBrush.Apply(
                queue.CurrentTriangleSurface,
                new NavTriangleSurfaceTerrainBrushSpec(
                    centerXcm: 200,
                    centerZcm: 200,
                    halfExtentCm: 50,
                    kind: NavTriangleSurfaceTerrainBrushKind.Raise,
                    cellSizeCm: 100,
                    heightScaleMeters: 1f,
                    baseHeightLevel: 0,
                    raiseHeightLevel: 2,
                    targetMinYcm: -10,
                    targetMaxYcm: 10),
                out _);

            Assert.DoesNotThrow(() => queue.ReplaceTriangleSurface(edited));
            Assert.That(queue.CurrentTriangleSurface, Is.SameAs(edited));

            // A sealed generation must reject replacement (no mixed surface generation).
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);
            var published = new RuntimeNavMeshRebuildPublishedTile[16];
            var failures = new NavBakeResultEntry[16];
            _ = queue.ProcessBudgetInto(1, published.AsSpan(), failures.AsSpan());
            Assert.That(queue.SealedRemainingCount, Is.GreaterThan(0));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => queue.ReplaceTriangleSurface(surface))!;
            Assert.That(ex.Message, Does.Contain("sealed/baking"));
            Assert.That(queue.CurrentTriangleSurface, Is.SameAs(edited));
        }

        [Test]
        public void Queue_DirtyCapacityExhaustion_HardFailsNamingOwnerAndRequired()
        {
            NavMeshBakeConfig config = CreateRuntimeConfig();
            config.RuntimeIncremental.DirtyTileCapacity = 1;
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var context = CreateSurfaceContext(surface, config, agentProfiles);
            NavTileStore store = CreateTestStore(config);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                registry,
                new NavMeshProfileRegistry(config, agentProfiles));

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)))!;
            Assert.That(ex.Message, Does.Contain("dirtyTileCapacity"));
            Assert.That(ex.Message, Does.Contain("required"));
        }

        [Test]
        public void Queue_StoreOutputCapacityMismatch_HardFailsAtConstruction()
        {
            NavMeshBakeConfig config = CreateRuntimeConfig();
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 1, tileCountZ: 1);
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var context = CreateSurfaceContext(surface, config, agentProfiles);
            var undersized = new NavTileStore(
                _ => throw new InvalidOperationException("no disk"),
                residentTileCapacity: config.RuntimeIncremental.ResidentTileCapacity,
                outputVertexCapacity: 1,
                outputTriangleCapacity: 1,
                outputPortalCapacity: 1);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = undersized },
                NavQueryTileSpace.FromGrid(surface.Grid));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                    context,
                    registry,
                    new NavMeshProfileRegistry(config, agentProfiles)))!;
            Assert.That(ex.Message, Does.Contain("outputVertexCapacity"));
            Assert.That(ex.Message, Does.Contain(config.RuntimeIncremental.OutputVertexCapacity.ToString()));
        }

        [Test]
        public void NavQueryServiceRegistry_NonZeroOriginTileSpace_LocatesTilesByFloorDivision()
        {
            // Origin (1000,2000), 250x250 tiles, 2x2 grid. World (1250,2250) -> tile (1,1).
            NavQueryTileSpace tileSpace = new NavQueryTileSpace(
                originXcm: 1000,
                originZcm: 2000,
                tileWidthCm: 250,
                tileHeightCm: 250);
            var store = new NavTileStore(_ => throw new InvalidOperationException("tile space contract has no disk tiles"));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                tileSpace);

            Assert.That(registry.TileSpace, Is.EqualTo(tileSpace));
            Assert.That(registry.StoreCount, Is.EqualTo(1));
            var snapshots = new NavQueryServiceStoreSnapshot[4];
            Assert.That(registry.CopyStoreSnapshots(snapshots.AsSpan()), Is.EqualTo(1));
            Assert.That(registry.TryCreateQuery(0, 0, NavAreaCostTable.CreateDefault(), out NavQueryService query), Is.True);
            Assert.That(query.TileSpace, Is.EqualTo(tileSpace));
        }

        [Test]
        public void GameEngine_RegisterExternalNavBakeAdapters_ComposesCoreAndExternalWithoutCatalog()
        {
            var engine = new GameEngine();

            // Core-owned kinds cannot be injected; registrations lock after the first call.
            InvalidOperationException coreOwned = Assert.Throws<InvalidOperationException>(
                () => engine.RegisterExternalNavBakeAdapters(new FakeAdapter(NavBakeAlgorithmKind.ExactCdt)))!;
            Assert.That(coreOwned.Message, Does.Contain("owned by Core"));

            engine.RegisterExternalNavBakeAdapters(new RecastFakeAdapter());
            Assert.That(engine.ExternalNavBakeAdapters.Count, Is.EqualTo(1));
            Assert.That(engine.ExternalNavBakeAdapters[0].Kind, Is.EqualTo(NavBakeAlgorithmKind.Recast));

            InvalidOperationException locked = Assert.Throws<InvalidOperationException>(
                () => engine.RegisterExternalNavBakeAdapters(new FakeAdapter(NavBakeAlgorithmKind.Recast)))!;
            Assert.That(locked.Message, Does.Contain("locked"));

            // The composed service is the only registry: Core CDT + LayeredSpan + injected Recast.
            var layeredPool = new LayeredSpanScratchPool(CreateLayeredConfig());
            var exactCdt = new ExactCdtNavBakeAlgorithm();
            var layeredSpan = new LayeredSpanNavBakeAlgorithm(layeredPool);
            var adapters = new List<INavBakeAlgorithm>(3) { exactCdt, layeredSpan };
            adapters.AddRange(engine.ExternalNavBakeAdapters);
            INavBakeAlgorithm[] sorted = adapters.ToArray();
            Array.Sort(sorted, static (a, b) => ((byte)a.Kind).CompareTo((byte)b.Kind));
            var service = new NavBakeService(sorted);

            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.Recast), Is.True);
            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.ExactCdt), Is.True);
            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.LayeredSpan), Is.True);
            Assert.That(service.RegisteredKinds, Is.EqualTo(new[]
            {
                NavBakeAlgorithmKind.Recast,
                NavBakeAlgorithmKind.ExactCdt,
                NavBakeAlgorithmKind.LayeredSpan
            }));
        }

        private static RuntimeNavMeshRebuildBatch Drain(RuntimeIncrementalNavMeshRebuildQueue queue)
        {
            var published = new RuntimeNavMeshRebuildPublishedTile[16];
            var failures = new NavBakeResultEntry[16];
            RuntimeNavMeshRebuildBatchStats last = default;
            int guard = 0;
            while (queue.PendingTileCount > 0 || queue.SealedRemainingCount > 0)
            {
                last = queue.ProcessBudgetInto(4, published.AsSpan(), failures.AsSpan());
                if (++guard > 32)
                {
                    throw new InvalidOperationException("Drain guard exceeded: queue did not settle.");
                }
            }

            var publishedList = new RuntimeNavMeshRebuildPublishedTile[last.PublishedCount];
            for (int i = 0; i < last.PublishedCount; i++)
            {
                publishedList[i] = published[i];
            }

            var failureList = new NavBakeResultEntry[last.FailedEntryCount];
            for (int i = 0; i < last.FailedEntryCount; i++)
            {
                failureList[i] = failures[i];
            }

            return new RuntimeNavMeshRebuildBatch(
                last.RequestedTileBudget,
                last.RebuiltTileCount,
                last.FailedEntryCount,
                last.PendingTileCount,
                last.SealedRemainingCount,
                last.Committed,
                last.Aborted,
                last.Generation,
                publishedList,
                failureList);
        }

        private static NavBakeContext CreateSurfaceContext(
            NavTriangleSurfaceTileIndex surface,
            NavMeshBakeConfig config,
            AgentProfileRegistry agentProfiles)
        {
            return new NavBakeContext
            {
                MapId = "nav_residency_switch_contract",
                SourceUri = "Core:Maps/nav_residency_switch_contract.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = agentProfiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavMeshBakeConfig CreateRuntimeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.AlgorithmExactCdt,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 0 },
                LayeredSpan = CreateLayeredConfig(),
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
                    DirtyTileCapacity = 8,
                    StagedEntryCapacity = 8,
                    PublishedTileCapacity = 8,
                    StoreGroupCapacity = 4,
                    ResidentTileCapacity = 8,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                }
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
                MaxLawsonFlipCount = 100000,
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
            };
        }

        private static NavTriangleSurfaceTileIndex CreateFlatGridTriangleSurfaceIndex(
            int tileCountX,
            int tileCountZ,
            int tileWidthCm = 400,
            int tileHeightCm = 400,
            int yCm = 0,
            int haloPaddingCm = 200)
        {
            int widthCm = checked(tileCountX * tileWidthCm);
            int heightCm = checked(tileCountZ * tileHeightCm);
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, widthCm, 0, widthCm },
                vertexYcm: new[] { yCm, yCm, yCm, yCm },
                vertexZcm: new[] { 0, 0, heightCm, heightCm },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 3 },
                triC: new[] { 2, 2 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            var grid = new NavTriangleSurfaceTileGrid(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm,
                tileHeightCm,
                tileCountX,
                tileCountZ,
                haloPaddingCm);
            return NavTriangleSurfaceTileIndex.Build(surface, grid);
        }

        private static NavTileStore CreateTestStore(NavMeshBakeConfig config)
        {
            return new NavTileStore(
                _ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."),
                config.RuntimeIncremental);
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

        private sealed class RecastFakeAdapter : INavBakeAlgorithm
        {
            public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Recast;
            public NavBakeAdapterCapabilities Capabilities => NavBakeAdapterCapabilities.OfflineLogicTerrain;

            public bool SupportsMode(NavBakeMode mode)
                => mode == NavBakeMode.Offline;

            public bool GuaranteesBitwiseDeterminism => false;
            public bool Supports3DMultiLayer => true;
            public bool IsZeroAllocationHotPath => false;

            public bool TryBake(
                NavBakeContext context,
                NavBakeTileCoord target,
                NavLayerConfig layer,
                NavMeshAgentProfileConfig navProfile,
                AgentProfileConfig agentProfile,
                out NavTile tile,
                out byte[] detourTileBytes,
                out NavBakeArtifact artifact)
            {
                throw new NotSupportedException("Composition contract does not bake.");
            }
        }

        private sealed class FakeAdapter : INavBakeAlgorithm
        {
            public FakeAdapter(NavBakeAlgorithmKind kind)
            {
                Kind = kind;
            }

            public NavBakeAlgorithmKind Kind { get; }
            public NavBakeAdapterCapabilities Capabilities => NavBakeAdapterCapabilities.OfflineLogicTerrain;

            public bool SupportsMode(NavBakeMode mode) => mode == NavBakeMode.Offline;
            public bool GuaranteesBitwiseDeterminism => false;
            public bool Supports3DMultiLayer => true;
            public bool IsZeroAllocationHotPath => false;

            public bool TryBake(
                NavBakeContext context,
                NavBakeTileCoord target,
                NavLayerConfig layer,
                NavMeshAgentProfileConfig navProfile,
                AgentProfileConfig agentProfile,
                out NavTile tile,
                out byte[] detourTileBytes,
                out NavBakeArtifact artifact)
            {
                throw new NotSupportedException("Composition contract does not bake.");
            }
        }
    }
}
