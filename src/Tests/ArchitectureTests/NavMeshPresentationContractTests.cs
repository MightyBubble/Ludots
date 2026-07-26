using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshPresentationContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void Buffer_PublishesDeterministicColumns_AndOverflowNamesResidentCapacityOwner()
        {
            var buffer = new NavMeshPresentationBuffer(tileCapacity: 2, tileStateCapacity: 2);
            var style = CreateStyle();
            var tileSpace = new NavQueryTileSpace(0, 0, 400, 400);
            buffer.BeginFrame(
                layer: 3,
                profile: 1,
                in tileSpace,
                storeRevision: 9u,
                storeGeneration: 77UL,
                stateRevision: 4u,
                in style);

            NavTile first = CreateBankedTile(chunkX: 1, chunkY: 0, version: 2, checksum: 11UL);
            NavTile second = CreateBankedTile(chunkX: 0, chunkY: 1, version: 3, checksum: 22UL);
            buffer.AddTile(first);
            buffer.AddTile(second);

            Assert.That(buffer.TileCount, Is.EqualTo(2));
            Assert.That(buffer.Layer, Is.EqualTo(3));
            Assert.That(buffer.Profile, Is.EqualTo(1));
            Assert.That(buffer.StoreRevision, Is.EqualTo(9u));
            Assert.That(buffer.StoreGeneration, Is.EqualTo(77UL));
            Assert.That(buffer.StateRevision, Is.EqualTo(4u));
            Assert.That(buffer.TileSpace, Is.EqualTo(tileSpace));
            Assert.That(buffer.Tiles[0], Is.SameAs(first));
            Assert.That(buffer.Tiles[1], Is.SameAs(second));
            Assert.That(buffer.TileVersions[0], Is.EqualTo(2u));
            Assert.That(buffer.TileVersions[1], Is.EqualTo(3u));
            Assert.That(buffer.TileChecksums[0], Is.EqualTo(11UL));
            Assert.That(buffer.TileChecksums[1], Is.EqualTo(22UL));
            Assert.That(ReferenceEquals(buffer.Tiles[0].VertexYcm, first.VertexYcm), Is.True);

            InvalidOperationException? overflow = Assert.Throws<InvalidOperationException>(
                () => buffer.AddTile(CreateBankedTile(chunkX: 2, chunkY: 2, version: 1, checksum: 1UL)));
            Assert.That(overflow!.Message, Does.Contain("residentTileCapacity"));
            Assert.That(overflow.Message, Does.Contain("NavMeshBakeConfig.runtimeIncremental"));
        }

        [Test]
        public void Buffer_TileStateOverflow_NamesPresentationCapacityOwner()
        {
            var buffer = new NavMeshPresentationBuffer(tileCapacity: 1, tileStateCapacity: 1);
            var style = CreateStyle();
            var tileSpace = new NavQueryTileSpace(0, 0, 400, 400);
            buffer.BeginFrame(0, 0, in tileSpace, 1u, 1UL, 1u, in style);
            buffer.SetTileState(new NavBakeTileCoord(0, 0), NavMeshPresentationTileState.Pending);

            InvalidOperationException? overflow = Assert.Throws<InvalidOperationException>(
                () => buffer.SetTileState(new NavBakeTileCoord(1, 0), NavMeshPresentationTileState.Pending));
            Assert.That(overflow!.Message, Does.Contain("presentation.navMeshTileStateCapacity"));
        }

        [Test]
        public void Projector_CopiesStoreOrderGenerationAndPendingCoordsDeterministically()
        {
            PresentationHarness harness = CreateHarness(residentTileCapacity: 8, dirtyTileCapacity: 8);
            harness.Store.Replace(CreatePublishedTile(chunkX: 1, chunkY: 0, version: 1, checksum: 101UL));
            harness.Store.Replace(CreatePublishedTile(chunkX: 0, chunkY: 0, version: 2, checksum: 202UL));
            harness.Store.Replace(CreatePublishedTile(chunkX: 0, chunkY: 1, version: 3, checksum: 303UL));

            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);
            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 1)), Is.True);
            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);

            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());
            float dt = 0f;
            harness.System.Update(in dt);

            Assert.That(harness.Buffer.TileCount, Is.EqualTo(3));
            Assert.That(harness.Buffer.StoreRevision, Is.EqualTo(harness.Store.Revision));
            Assert.That(harness.Buffer.StoreGeneration, Is.EqualTo(harness.Store.Generation));
            Assert.That(harness.Buffer.Tiles[0].TileId.ChunkX, Is.EqualTo(0));
            Assert.That(harness.Buffer.Tiles[0].TileId.ChunkY, Is.EqualTo(0));
            Assert.That(harness.Buffer.Tiles[1].TileId.ChunkX, Is.EqualTo(1));
            Assert.That(harness.Buffer.Tiles[1].TileId.ChunkY, Is.EqualTo(0));
            Assert.That(harness.Buffer.Tiles[2].TileId.ChunkX, Is.EqualTo(0));
            Assert.That(harness.Buffer.Tiles[2].TileId.ChunkY, Is.EqualTo(1));

            Assert.That(harness.Buffer.TileStateCount, Is.EqualTo(3));
            Assert.That(harness.Buffer.TileStateCoords[0], Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(harness.Buffer.TileStateCoords[1], Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(harness.Buffer.TileStateCoords[2], Is.EqualTo(new NavBakeTileCoord(0, 1)));
            Assert.That(harness.Buffer.TileStates[0], Is.EqualTo(NavMeshPresentationTileState.Pending));
            Assert.That(harness.Buffer.TileStates[1], Is.EqualTo(NavMeshPresentationTileState.Pending));
            Assert.That(harness.Buffer.TileStates[2], Is.EqualTo(NavMeshPresentationTileState.Pending));

            uint revision = harness.Buffer.StoreRevision;
            ulong generation = harness.Buffer.StoreGeneration;
            harness.System.Update(in dt);
            Assert.That(harness.Buffer.StoreRevision, Is.EqualTo(revision));
            Assert.That(harness.Buffer.StoreGeneration, Is.EqualTo(generation));
        }

        [Test]
        public void Projector_PublishesPendingRebuildingAndCommitted_FromQueueLifecycle()
        {
            PresentationHarness harness = CreateHarness(residentTileCapacity: 8, dirtyTileCapacity: 8);
            var first = new NavBakeTileCoord(1, 0);
            var second = new NavBakeTileCoord(0, 1);
            var third = new NavBakeTileCoord(0, 0);
            Assert.That(harness.Queue.EnqueueDirtyTile(first), Is.True);
            Assert.That(harness.Queue.EnqueueDirtyTile(second), Is.True);
            Assert.That(harness.Queue.EnqueueDirtyTile(third), Is.True);
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            var published = new RuntimeNavMeshRebuildPublishedTile[8];
            var failures = new NavBakeResultEntry[8];
            RuntimeNavMeshRebuildBatchStats slice = harness.Queue.ProcessBudgetInto(1, published, failures);
            Assert.That(slice.Committed, Is.False);
            Assert.That(slice.SealedRemainingCount, Is.EqualTo(2));

            float dt = 0f;
            harness.System.Update(in dt);
            Assert.That(harness.Buffer.StoreGeneration, Is.Zero);
            Assert.That(harness.Buffer.TileStateCount, Is.EqualTo(3));
            Assert.That(FindTileState(harness.Buffer, first), Is.EqualTo(NavMeshPresentationTileState.Rebuilding));
            Assert.That(FindTileState(harness.Buffer, second), Is.EqualTo(NavMeshPresentationTileState.Pending));
            Assert.That(FindTileState(harness.Buffer, third), Is.EqualTo(NavMeshPresentationTileState.Pending));

            _ = harness.Queue.ProcessBudgetInto(1, published, failures);
            RuntimeNavMeshRebuildBatchStats committed = harness.Queue.ProcessBudgetInto(1, published, failures);
            Assert.That(committed.Committed, Is.True);
            Assert.That(committed.PublishedCount, Is.EqualTo(3));

            harness.System.Update(in dt);
            Assert.That(harness.Buffer.StoreGeneration, Is.EqualTo(committed.Generation));
            Assert.That(harness.Buffer.TileStateCount, Is.EqualTo(3));
            Assert.That(FindTileState(harness.Buffer, first), Is.EqualTo(NavMeshPresentationTileState.Committed));
            Assert.That(FindTileState(harness.Buffer, second), Is.EqualTo(NavMeshPresentationTileState.Committed));
            Assert.That(FindTileState(harness.Buffer, third), Is.EqualTo(NavMeshPresentationTileState.Committed));
        }

        [Test]
        public void Projector_SteadyStateUpdate_AllocatesZeroManagedBytesAfterWarmup()
        {
            PresentationHarness harness = CreateHarness(residentTileCapacity: 4, dirtyTileCapacity: 4);
            harness.Store.Replace(CreatePublishedTile(chunkX: 0, chunkY: 0, version: 1, checksum: 7UL));
            Assert.That(harness.Queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            float dt = 0f;
            for (int i = 0; i < 8; i++)
            {
                harness.System.Update(in dt);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                harness.System.Update(in dt);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Steady-state NavMesh presentation allocated {allocated} managed bytes.");
            Assert.That(harness.Buffer.TileCount, Is.EqualTo(1));
            Assert.That(harness.Buffer.TileStateCount, Is.EqualTo(1));
        }

        [Test]
        public void CapabilityValidator_RequiresExplicitNavMeshTileGeometry()
        {
            InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(
                () => NavMeshPresentationCapabilityValidator.Require(null));
            Assert.That(missing!.Message, Does.Contain("NavMeshTileGeometry"));

            var withoutFlag = new PresentationAdapterCapabilities(PresentationVisualCapabilities.Decal);
            InvalidOperationException? unsupported = Assert.Throws<InvalidOperationException>(
                () => NavMeshPresentationCapabilityValidator.Require(withoutFlag));
            Assert.That(unsupported!.Message, Does.Contain("NavMeshTileGeometry"));

            var withFlag = new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry);
            Assert.DoesNotThrow(() => NavMeshPresentationCapabilityValidator.Require(withFlag));
        }

        private static PresentationHarness CreateHarness(int residentTileCapacity, int dirtyTileCapacity)
        {
            var terrain = new FlatGridLogicTerrainField(8, 8, chunkSizeCells: 4);
            NavMeshBakeConfig config = CreateConfig(residentTileCapacity, dirtyTileCapacity);
            AgentProfileRegistry agentProfiles = CreateAgentProfiles();
            var navProfiles = new NavMeshProfileRegistry(config, agentProfiles);
            var obstacles = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                GroundLayerId);
            var store = new NavTileStore(
                _ => throw new InvalidOperationException("NavMesh presentation contract publishes before disk load."),
                config.RuntimeIncremental);
            NavBuildConfig build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, build);
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                },
                NavQueryTileSpace.FromGrid(surface.Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new INavBakeAlgorithm[] { new CdtNavBakeAlgorithm() }),
                new NavBakeContext
                {
                    MapId = "navmesh_presentation_contract",
                    SourceUri = "Core:Maps/navmesh_presentation_contract.runtime-navmesh",
                    TriangleSurface = surface,
                    Obstacles = obstacles,
                    Config = config,
                    AgentProfiles = agentProfiles,
                    Targets = new[] { new NavBakeTileCoord(0, 0) },
                    BuildConfig = build,
                    TileVersion = 1,
                    Mode = NavBakeMode.RuntimeIncremental,
                    Algorithm = NavBakeAlgorithmKind.Cdt,
                    Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
                },
                registry,
                navProfiles);

            var state = new NavMeshPresentationState();
            var buffer = new NavMeshPresentationBuffer(store.ResidentTileCapacity, dirtyTileCapacity);
            var engine = new Ludots.Core.Engine.GameEngine();
            engine.SetService(Ludots.Core.Scripting.CoreServiceKeys.NavQueryServices, registry);
            engine.SetService(Ludots.Core.Scripting.CoreServiceKeys.RuntimeNavMeshRebuildQueue, queue);
            var system = new NavMeshPresentationSystem(engine, state, buffer);
            return new PresentationHarness(store, queue, state, buffer, system);
        }

        private static NavMeshBakeConfig CreateConfig(int residentTileCapacity, int dirtyTileCapacity)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.Cdt),
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
                    TileBudgetPerFixedTick = 4,
                    IncludeNeighborTiles = false,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1,
                    TrackedStructuralEntityCapacity = 16,
                    ObstaclePrimitiveCapacity = 32,
                    PolygonVertexCapacity = 256,
                    DirtyTileCapacity = dirtyTileCapacity,
                    StagedEntryCapacity = dirtyTileCapacity,
                    PublishedTileCapacity = dirtyTileCapacity,
                    StoreGroupCapacity = 4,
                    ResidentTileCapacity = residentTileCapacity,
                    OutputVertexCapacity = 256,
                    OutputTriangleCapacity = 512,
                    OutputPortalCapacity = 64,
                    InitialResidentChunkX = 0,
                    InitialResidentChunkZ = 0,
                    InitialResidentWidthChunks = 1,
                    InitialResidentHeightChunks = 1
                },
                LayeredSpan = CreateMinimalLayeredSpan(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 100 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static NavLayeredSpanConfig CreateMinimalLayeredSpan()
        {
            return new NavLayeredSpanConfig
            {
                ScratchSlotCount = 2,
                RasterCellSizeCm = 100,
                RasterHaloCells = 1,
                SameSurfaceToleranceCm = 5,
                MaxSimplificationErrorCm = 0,
                HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                MaxLawsonFlipCount = 100_000,
                ColumnCapacity = 64,
                SpanCapacity = 128,
                ClassifiedSpanCapacity = 128,
                WalkableSpanCapacity = 128,
                LinkCapacity = 256,
                SheetCapacity = 128,
                PortalIntervalCapacity = 256,
                RegionCapacity = 64,
                ChartCapacity = 32,
                RingCapacity = 32,
                ContourVertexCapacity = 256,
                ContourEdgeCapacity = 256,
                SeamCapacity = 64,
                CanonicalLinkCapacity = 256,
                SplitPointCapacity = 64,
                TriangulationVertexCapacity = 256,
                TriangulationTriangleCapacity = 512,
                ConstrainedEdgeCapacity = 512,
                BorderPortalCapacity = 64,
                PolygonVertexCapacity = 256,
                AdjacencyEdgeCapacity = 1536,
                BridgeCandidateCapacity = 256,
                RingWorkCapacity = 64,
                TemporaryConstraintFlagCapacity = 512
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

        private static NavMeshPresentationStyle CreateStyle()
            => new NavMeshPresentationStyle(
                new NavMeshPresentationColor(0.1f, 0.2f, 0.3f, 0.4f),
                new NavMeshPresentationColor(0.2f, 0.3f, 0.4f, 0.5f),
                new NavMeshPresentationColor(0.3f, 0.4f, 0.5f, 0.6f),
                new NavMeshPresentationColor(0.9f, 0.8f, 0.1f, 0.7f),
                new NavMeshPresentationColor(0.9f, 0.4f, 0.1f, 0.7f),
                new NavMeshPresentationColor(0.1f, 0.9f, 0.4f, 0.7f),
                heightOffsetMeters: 0.05f,
                drawFill: true,
                drawEdges: true,
                drawTileBounds: true,
                drawTileStateIndication: true);

        private static NavMeshPresentationTileState FindTileState(
            NavMeshPresentationBuffer buffer,
            NavBakeTileCoord coord)
        {
            for (int i = 0; i < buffer.TileStateCount; i++)
            {
                if (buffer.TileStateCoords[i].Equals(coord))
                {
                    return buffer.TileStates[i];
                }
            }

            Assert.Fail($"NavMesh presentation buffer does not contain tile state for ({coord.ChunkX},{coord.ChunkY}).");
            return default;
        }

        private static NavTile CreateBankedTile(int chunkX, int chunkY, uint version, ulong checksum)
        {
            NavTile tile = NavTile.CreateBanked(8, 8, 4);
            tile.AssignHeader(new NavTileId(chunkX, chunkY, 0), version, buildConfigHash: 0UL, originXcm: 0, originZcm: 0);
            tile.VertexXcm[0] = 0;
            tile.VertexYcm[0] = 123;
            tile.VertexZcm[0] = 0;
            tile.SetCounts(vertexCount: 1, triangleCount: 0, portalCount: 0);
            tile.SetChecksum(checksum);
            return tile;
        }

        private static NavTile CreatePublishedTile(int chunkX, int chunkY, uint version, ulong checksum)
        {
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX,
                chunkY,
                layer: 0,
                tileVersion: version,
                tileWidthCm: 400,
                tileHeightCm: 400,
                tileWidthCells: 4,
                tileHeightCells: 4);
            // Preserve authored Y channel through zero-copy NavTile references.
            tile.VertexYcm[0] = 250;
            tile.SetChecksum(checksum);
            return tile;
        }

        private sealed class PresentationHarness
        {
            public PresentationHarness(
                NavTileStore store,
                RuntimeIncrementalNavMeshRebuildQueue queue,
                NavMeshPresentationState state,
                NavMeshPresentationBuffer buffer,
                NavMeshPresentationSystem system)
            {
                Store = store;
                Queue = queue;
                State = state;
                Buffer = buffer;
                System = system;
            }

            public NavTileStore Store { get; }
            public RuntimeIncrementalNavMeshRebuildQueue Queue { get; }
            public NavMeshPresentationState State { get; }
            public NavMeshPresentationBuffer Buffer { get; }
            public NavMeshPresentationSystem System { get; }
        }
    }
}
