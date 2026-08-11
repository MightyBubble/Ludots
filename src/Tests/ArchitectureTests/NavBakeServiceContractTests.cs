using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavBakeServiceContractTests
    {
        private const string GroundLayerId = "Ground";

        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void NavBakeService_RunsSingleContextForHeadlessAndBridgeAdapters()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmExactCdt);
            var profiles = CreateAgentProfiles();
            var context = new NavBakeContext
            {
                MapId = "nav_bake_contract",
                SourceUri = "Core:Maps/nav_bake_contract.vtxm",
                TriangleSurface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 0),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = profiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 7,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var service = new NavBakeService(new ExactCdtNavBakeAlgorithm());
            NavBakeResult headless = service.Bake(context);
            NavBakeResult bridge = service.Bake(context);

            Assert.That(headless.FailureCount, Is.EqualTo(0));
            Assert.That(bridge.FailureCount, Is.EqualTo(0));
            Assert.That(headless.Entries.Count, Is.EqualTo(bridge.Entries.Count));
            Assert.That(headless.Entries[0].ToTileBytes(), Is.EqualTo(bridge.Entries[0].ToTileBytes()));
        }

        [Test]
        public void RecastBake_FlatGridProducesNonEmptyTile()
        {
            var terrain = new FlatGridLogicTerrainField(16, 16, chunkSizeCells: 16);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_recast_grid_contract",
                SourceUri = "Core:Maps/nav_recast_grid_contract.tris",
                TriangleSurface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            NavBakeResult result = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);

            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.Entries[0].Tile.VertexCount, Is.GreaterThan(0));
            Assert.That(result.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(result.Entries[0].DetourTileBytes.Length, Is.GreaterThan(0));
        }

        [Test]
        public void RecastBake_OpenGridCrossTileQuery_ReturnsStraightCorePath()
        {
            const int chunkSizeCells = 4;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            var terrain = new FlatGridLogicTerrainField(12, 4, chunkSizeCells: chunkSizeCells);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_recast_open_grid_query_contract",
                SourceUri = "Core:Maps/nav_recast_open_grid_query_contract.tris",
                TriangleSurface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, haloPaddingCm: 100),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(1, 0),
                    new NavBakeTileCoord(2, 0)
                },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourTileBytes(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 150,
                goalXcm: 1050,
                goalZcm: 150,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            TestContext.WriteLine("Default baseline path: " + string.Join(" -> ", FormatPathPoints(path)));
            Assert.That(path.PathXcm, Is.EqualTo(new[] { 50, 1050 }));
            Assert.That(path.PathZcm, Is.EqualTo(new[] { 150, 150 }));
        }

        [Test]
        public void DefaultGridFlatBaseline_CrossTileQuery_ReturnsStraightCorePath()
        {
            const int chunkSizeCells = 4;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            var detourTiles = new List<byte[]>(3);

            for (int cx = 0; cx < 3; cx++)
            {
                NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                    cx,
                    chunkY: 0,
                    layer: 0,
                    tileVersion: 1,
                    chunkSizeCells,
                    SpatialScaleDefaults.CellCm);

                Assert.That(tile.VertexCount, Is.EqualTo(4));
                Assert.That(tile.TriangleCount, Is.EqualTo(2));
                Assert.That(tile.Portals.Length, Is.EqualTo(4));

                byte[] detourBytes = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, tileSizeCm, tileSizeCm);
                Assert.That(detourBytes.Length, Is.GreaterThan(0));
                detourTiles.Add(detourBytes);
            }

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                detourTiles,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 150,
                goalXcm: 1050,
                goalZcm: 150,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            string pathPoints = string.Join(" -> ", FormatPathPoints(path));
            Assert.That(path.PathXcm, Is.EqualTo(new[] { 50, 1050 }), pathPoints);
            Assert.That(path.PathZcm, Is.EqualTo(new[] { 150, 150 }), pathPoints);
        }

        [Test]
        public void DefaultGridFlatBaseline_LongOpenDiagonalQuery_ReturnsDirectCorePath()
        {
            const int chunkSizeCells = 64;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            var detourTiles = new List<byte[]>(168);

            for (int cy = 16; cy <= 29; cy++)
            {
                for (int cx = 16; cx <= 27; cx++)
                {
                    NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                        cx,
                        cy,
                        layer: 0,
                        tileVersion: 1,
                        chunkSizeCells,
                        SpatialScaleDefaults.CellCm);

                    detourTiles.Add(DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, tileSizeCm, tileSizeCm));
                }
            }

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                detourTiles,
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 175700,
                startZcm: 185800,
                goalXcm: 108100,
                goalZcm: 103000,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            string pathPoints = string.Join(" -> ", FormatPathPoints(path));
            Assert.That(path.PathXcm, Is.EqualTo(new[] { 175700, 108100 }), pathPoints);
            Assert.That(path.PathZcm, Is.EqualTo(new[] { 185800, 103000 }), pathPoints);
        }

        [Test]
        public void RecastBake_QueryPathDoesNotCutThroughBlockedGridHole()
        {
            const int chunkSizeCells = 9;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            const int obstacleMinXcm = 300;
            const int obstacleMinZcm = 300;
            const int obstacleMaxXcm = 600;
            const int obstacleMaxZcm = 600;

            var context = new NavBakeContext
            {
                MapId = "nav_recast_blocked_hole_query_contract",
                SourceUri = "Core:Maps/nav_recast_blocked_hole_query_contract.tris",
                TriangleSurface = LogicTerrainTriangleSurfaceCompiler.Compile(
                    new FlatGridLogicTerrainField(9, 9, chunkSizeCells: chunkSizeCells),
                    new NavBuildConfig(1f, 0.6f, 1),
                    haloPaddingCm: 100),
                Obstacles = new NavObstacleSet
                {
                    Obstacles =
                    {
                        new NavObstacle
                        {
                            Id = "center-hole",
                            Enabled = true,
                            Kind = NavObstacleKind.Polygon,
                            LayerId = GroundLayerId,
                            MinYcm = 0,
                            MaxYcm = 1000,
                            Points =
                            {
                                new NavPointCm(obstacleMinXcm, obstacleMinZcm),
                                new NavPointCm(obstacleMaxXcm, obstacleMinZcm),
                                new NavPointCm(obstacleMaxXcm, obstacleMaxZcm),
                                new NavPointCm(obstacleMinXcm, obstacleMaxZcm)
                            }
                        }
                    }
                },
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourTileBytes(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 750,
                goalZcm: 750,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            AssertPathSegmentsDoNotEnterAabb(path, obstacleMinXcm, obstacleMinZcm, obstacleMaxXcm, obstacleMaxZcm);
            var store = new NavTileStore(_ => throw new InvalidOperationException("Blocked-hole query test publishes tiles before disk load."));
            foreach (NavBakeResultEntry entry in bake.Entries)
            {
                store.Replace(entry.Tile);
            }
            AssertPathSegmentsStayInsideNavMesh(path, store, tileSizeCm, tileSizeCm);
        }

        [Test]
        public void RuntimeIncremental_RejectsAlgorithmThatDoesNotDeclareSupport()
        {
            // Recast declares runtime-incremental support over triangle surface only; a
            // LogicTerrain runtime-incremental context must hard-fail with the capability diagnostic.
            var context = CreateRuntimeIncrementalContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.Recast);

            var service = new NavBakeService(new RecastNavBakeAlgorithm());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("recast"));
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
        }

        [Test]
        public void AlgorithmAdapters_DeclareExplicitCapabilityMatrix()
        {
            var exactCdt = new ExactCdtNavBakeAlgorithm();
            Assert.That(exactCdt.SupportsMode(NavBakeMode.Offline), Is.True);
            Assert.That(exactCdt.SupportsMode(NavBakeMode.RuntimeIncremental), Is.True);
            Assert.That(exactCdt.GuaranteesBitwiseDeterminism, Is.True);
            Assert.That(exactCdt.Supports3DMultiLayer, Is.True);
            Assert.That(exactCdt.IsZeroAllocationHotPath, Is.False);

            var recast = new RecastNavBakeAlgorithm();
            Assert.That(recast.SupportsMode(NavBakeMode.Offline), Is.True);
            Assert.That(recast.SupportsMode(NavBakeMode.RuntimeIncremental), Is.True);
            Assert.That(recast.GuaranteesBitwiseDeterminism, Is.False);
            Assert.That(recast.Supports3DMultiLayer, Is.True);
            Assert.That(recast.IsZeroAllocationHotPath, Is.False);
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_RejectsUnsupportedOrMissingAdapterAtConstruction()
        {
            var context = CreateRuntimeIncrementalContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.Recast);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>());

            InvalidOperationException noSupport = Assert.Throws<InvalidOperationException>(() =>
                new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new RecastNavBakeAlgorithm()),
                    context,
                    queryServices,
                    navProfiles))!;
            Assert.That(noSupport.Message, Does.Contain("recast"));
            Assert.That(noSupport.Message, Does.Contain("runtime-incremental"));

            InvalidOperationException missingAdapter = Assert.Throws<InvalidOperationException>(() =>
                new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                    context,
                    queryServices,
                    navProfiles))!;
            Assert.That(missingAdapter.Message, Does.Contain("recast"));
            Assert.That(missingAdapter.Message, Does.Contain("runtime-incremental"));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_RejectsUnsupportedInputAtConstruction()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var context = CreateRuntimeIncrementalContext(terrain, algorithm: NavBakeAlgorithmKind.ExactCdt);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>());

            // ExactCdt now declares triangle-surface capabilities only; LogicTerrain runtime-incremental
            // input must hard-fail with the capability diagnostic (no fake conversion).
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                    context,
                    queryServices,
                    navProfiles))!;

            Assert.That(ex.Message, Does.Contain("exact-cdt"));
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
            Assert.That(ex.Message, Does.Contain("logic-terrain"));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_PreservesBaseContextAlgorithmThroughRebuild()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                new NavBuildConfig(1f, 0.6f, 1),
                haloPaddingCm: 0);
            var context = CreateRuntimeIncrementalSurfaceContext(surface, algorithm: NavBakeAlgorithmKind.Recast);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var probe = new RecordingBakeAlgorithm();
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(probe),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(1);

            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(probe.LastBakedAlgorithm, Is.EqualTo(context.Algorithm));
        }

        [Test]
        public void NavBakeNames_RejectsUnknownEnumValuesInsteadOfFallingBack()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NavBakeNames.FormatAlgorithm((NavBakeAlgorithmKind)127));
            Assert.Throws<ArgumentOutOfRangeException>(() => NavBakeNames.FormatMode((NavBakeMode)127));
        }

        [Test]
        public void NavBakeEstimator_RejectsUnknownAlgorithmKindInsteadOfFallingBack()
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            var context = new NavBakeContext
            {
                MapId = "nav_estimate_unknown_kind",
                SourceUri = "Core:Maps/nav_estimate_unknown_kind.vtxm",
                Terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = (NavBakeAlgorithmKind)127,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => NavBakeEstimator.Estimate(context));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_ProcessesDirtyTilesByBudgetAndPublishesRevision()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            var context = CreateRuntimeIncrementalSurfaceContext(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(50, 50, 20, 20), includeNeighbors: false), Is.EqualTo(1));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(450, 50, 20, 20), includeNeighbors: false), Is.EqualTo(1));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(450, 50, 20, 20), includeNeighbors: false), Is.EqualTo(0));
            Assert.That(queue.PendingTileCount, Is.EqualTo(2));

            RuntimeNavMeshRebuildBatch first = queue.ProcessBudget(1);
            Assert.That(first.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(first.FailedEntryCount, Is.EqualTo(0));
            Assert.That(first.PendingTileCount, Is.EqualTo(1));
            Assert.That(first.SealedRemainingCount, Is.EqualTo(1));
            Assert.That(first.Committed, Is.False);
            Assert.That(first.Aborted, Is.False);
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(0u));
            Assert.That(store.Generation, Is.EqualTo(0UL));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out _), Is.False);

            RuntimeNavMeshRebuildBatch second = queue.ProcessBudget(1);
            Assert.That(second.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(second.FailedEntryCount, Is.EqualTo(0));
            Assert.That(second.PendingTileCount, Is.EqualTo(0));
            Assert.That(second.SealedRemainingCount, Is.EqualTo(0));
            Assert.That(second.Committed, Is.True);
            Assert.That(second.Aborted, Is.False);
            Assert.That(second.PublishedTiles.Count, Is.EqualTo(2));
            Assert.That(second.Generation, Is.EqualTo(1UL));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile firstTile), Is.True);
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out NavTile secondTile), Is.True);
            Assert.That(firstTile.TileVersion, Is.EqualTo(context.TileVersion + 1u));
            Assert.That(secondTile.TileVersion, Is.EqualTo(context.TileVersion + 1u));
            Assert.That(second.PublishedTiles[0].Generation, Is.EqualTo(1UL));
            Assert.That(second.PublishedTiles[1].Generation, Is.EqualTo(1UL));
            Assert.That(second.PublishedTiles[0].StoreRevision, Is.EqualTo(1u));
            Assert.That(second.PublishedTiles[1].StoreRevision, Is.EqualTo(1u));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_DirtyAabbMapsToNeighborTilesAndIgnoresOutOfWorld()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 2);
            var context = CreateRuntimeIncrementalSurfaceContext(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new ExactCdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(-500, -500, 20, 20), includeNeighbors: true), Is.EqualTo(0));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(405, 405, 10, 10), includeNeighbors: true), Is.EqualTo(4));

            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(4);
            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PendingTileCount, Is.EqualTo(0));
            Assert.That(batch.Committed, Is.True);
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(4));
            Assert.That(batch.Generation, Is.EqualTo(1UL));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(batch.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(batch.PublishedTiles[1].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(batch.PublishedTiles[2].Target, Is.EqualTo(new NavBakeTileCoord(0, 1)));
            Assert.That(batch.PublishedTiles[3].Target, Is.EqualTo(new NavBakeTileCoord(1, 1)));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_FailedBakeKeepsReadablePreviousTile()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            var context = CreateRuntimeIncrementalSurfaceContext(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var baseline = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalSurfaceContext(CreateFlatGridTriangleSurfaceIndex(tileCountX: 1, tileCountZ: 1)));
            Assert.That(baseline.FailureCount, Is.EqualTo(0));

            var store = CreateRuntimeTestStore(context.Config);
            uint baselineRevision = store.Replace(baseline.Entries[0].Tile);
            ulong baselineGeneration = store.Generation;
            Assert.That(baselineRevision, Is.EqualTo(1u));
            Assert.That(baselineGeneration, Is.EqualTo(1UL));

            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new SelectiveFailNavBakeAlgorithm(failTarget: new NavBakeTileCoord(1, 0))),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);

            RuntimeNavMeshRebuildBatch first = queue.ProcessBudget(1);
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(first.Committed, Is.False);
            Assert.That(store.Revision, Is.EqualTo(baselineRevision));
            Assert.That(store.Generation, Is.EqualTo(baselineGeneration));

            RuntimeNavMeshRebuildBatch failed = queue.ProcessBudget(1);
            Assert.That(failed.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(failed.FailedEntryCount, Is.EqualTo(1));
            Assert.That(failed.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(failed.Committed, Is.False);
            Assert.That(failed.Aborted, Is.True);
            Assert.That(failed.FailedEntries[0].Success, Is.False);
            Assert.That(failed.FailedEntries[0].Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.TriangulationFailed));
            Assert.That(store.Revision, Is.EqualTo(baselineRevision));
            Assert.That(store.Generation, Is.EqualTo(baselineGeneration));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile current), Is.True);
            Assert.That(current.Checksum, Is.EqualTo(baseline.Entries[0].Tile.Checksum));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.False);
        }

        [Test]
        public void ExactCdtBake_ConsumesObstacleSetWithStrictLayerId()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                new NavBuildConfig(1f, 0.6f, 1),
                haloPaddingCm: 0);
            NavBakeContext clearContext = CreateRuntimeIncrementalSurfaceContext(surface);
            NavBakeResult clear = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(clearContext);
            Assert.That(clear.FailureCount, Is.EqualTo(0));

            // Fully blocked tile is a legitimate valid-empty success under the triangle-surface
            // contract (zero walkable triangles, real checksum), never a fallback.
            NavBakeContext blockedContext = CreateRuntimeIncrementalSurfaceContext(
                surface,
                obstacles: new NavObstacleSet
                {
                    Obstacles =
                    {
                        new NavObstacle
                        {
                            Id = "center-blocker",
                            Enabled = true,
                            Kind = NavObstacleKind.Circle,
                            LayerId = GroundLayerId,
                            Center = new NavPointCm(200, 200),
                            RadiusCm = 500,
                            MinYcm = 0,
                            MaxYcm = 1000
                        }
                    }
                });
            NavBakeResult blocked = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(blockedContext);

            Assert.That(blocked.FailureCount, Is.EqualTo(0));
            Assert.That(blocked.Entries[0].Tile.TriangleCount, Is.EqualTo(0), "Fully blocked tile bakes to a valid empty tile.");
            Assert.That(blocked.Entries[0].Tile.Checksum, Is.Not.EqualTo(0UL));

            NavBakeContext wrongCaseLayerContext = CreateRuntimeIncrementalSurfaceContext(
                surface,
                obstacles: new NavObstacleSet
                {
                    Obstacles =
                    {
                        new NavObstacle
                        {
                            Id = "wrong-case-blocker",
                            Enabled = true,
                            Kind = NavObstacleKind.Circle,
                            LayerId = "ground",
                            Center = new NavPointCm(150, 150),
                            RadiusCm = 45,
                            MinYcm = 0,
                            MaxYcm = 1000
                        }
                    }
                });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(wrongCaseLayerContext))!;
            Assert.That(ex.Message, Does.Contain("unknown nav layer"));
        }

        [Test]
        public void NavTileStore_StableReadRejectsMixedRevision()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                new NavBuildConfig(1f, 0.6f, 1),
                haloPaddingCm: 0);
            NavBakeResult baseline = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(
                CreateOfflineContext(triangleSurface: surface, algorithm: NavBakeAlgorithmKind.ExactCdt));
            Assert.That(baseline.FailureCount, Is.EqualTo(0));

            var store = new NavTileStore(_ => throw new InvalidOperationException("Stable read test publishes tiles before disk load."));
            store.Replace(baseline.Entries[0].Tile);
            int attempts = 0;

            bool stable = store.TryRunStableRead(
                () =>
                {
                    attempts++;
                    store.Replace(baseline.Entries[0].Tile);
                    return attempts;
                },
                out int _,
                maxAttempts: 2);

            Assert.That(stable, Is.False);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void NavBakeEstimator_UsesRealContextAndReportsBudgetFromTargetsLayersProfiles()
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            config.Profiles.Add(new NavMeshAgentProfileConfig { Id = "Large", MaxClimbCm = 75, MaxSlopeDeg = 30 });
            config.Layers.Add(new NavLayerConfig { Id = "Bridge", Layer = 1 });
            var context = new NavBakeContext
            {
                MapId = "nav_estimate_contract",
                SourceUri = "Core:Maps/nav_estimate_contract.vtxm",
                Terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet
                {
                    Obstacles =
                    {
                        new NavObstacle
                        {
                            Id = "estimate-blocker",
                            Enabled = true,
                            Kind = NavObstacleKind.Circle,
                            LayerId = GroundLayerId,
                            Center = new NavPointCm(150, 150),
                            RadiusCm = 35,
                            MinYcm = 0,
                            MaxYcm = 1000
                        }
                    }
                },
                Config = config,
                AgentProfiles = CreateAgentProfiles(
                    new AgentProfileConfig
                    {
                        Id = "Large",
                        RadiusCm = 90,
                        HeightCm = 240,
                        ClearanceCm = 60,
                        Mass = 3,
                        Layer = 0
                    }),
                Targets = new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = true, MaxDegreeOfParallelism = 4 }
            };

            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);

            Assert.That(estimate.TargetTileCount, Is.EqualTo(2));
            Assert.That(estimate.LayerCount, Is.EqualTo(2));
            Assert.That(estimate.ProfileCount, Is.EqualTo(2));
            Assert.That(estimate.BakeOperationCount, Is.EqualTo(8));
            Assert.That(estimate.ObstacleCount, Is.EqualTo(1));
            Assert.That(estimate.EffectiveWorkers, Is.EqualTo(4));
            Assert.That(estimate.EstimateHash, Is.Not.Empty);
            Assert.That(estimate.TerrainContentHash, Is.Not.Empty);
            Assert.That(estimate.CellCm, Is.EqualTo(100));
            Assert.That(estimate.TileWorldWidthCm, Is.EqualTo(400));
            Assert.That(estimate.TileWorldHeightCm, Is.EqualTo(400));
            Assert.That(estimate.TerrainCellSampleCount, Is.EqualTo(32));
            // Raster resolution is data-driven from NavMeshBakeConfig.recast, so every profile shares the
            // configured 10cm/5cm cells instead of deriving them from agent radius. Both profiles therefore
            // rasterize 40 columns per axis: 2 tiles * 2 layers * (1600 + 1600).
            Assert.That(estimate.RecastColumnBudgetTotal, Is.EqualTo(12800));
            Assert.That(estimate.BudgetWorkUnitCount, Is.EqualTo(12800));
            Assert.That(estimate.EstimatedTileBytesLow, Is.EqualTo(8L * NavBakeEstimator.EstimatedBytesPerOperationLow));
            Assert.That(estimate.EstimatedTileBytesHigh, Is.EqualTo(8L * NavBakeEstimator.EstimatedBytesPerOperationHigh));
            Assert.That(estimate.EstimatedSerialSecondsLow, Is.EqualTo(0.64d).Within(0.0001d));
            Assert.That(estimate.EstimatedSecondsLow, Is.EqualTo(0.16d).Within(0.0001d));
            Assert.That(estimate.BudgetStatus, Is.EqualTo(NavBakeBudgetStatus.Ok));
            Assert.That(estimate.RequiresExplicitLargeBakeApproval, Is.False);
            Assert.DoesNotThrow(() => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: false, acceptedEstimateHash: null));

            NavBakeProfileEstimate small = estimate.Profiles[0];
            Assert.That(small.ProfileId, Is.EqualTo("Small"));
            Assert.That(small.RecastCellSizeCm, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(small.RecastCellHeightCm, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(small.RecastColumnsPerAxis, Is.EqualTo(40));
            Assert.That(small.WalkableHeightVoxels, Is.EqualTo(36));
            Assert.That(small.WalkableClimbVoxels, Is.EqualTo(8));
            Assert.That(small.MinWalkableUpDot, Is.EqualTo(MathF.Cos(45f * MathF.PI / 180f)).Within(0.0001f));

            NavBakeProfileEstimate large = estimate.Profiles[1];
            Assert.That(large.ProfileId, Is.EqualTo("Large"));
            Assert.That(large.RecastCellSizeCm, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(large.RecastCellHeightCm, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(large.RecastColumnsPerAxis, Is.EqualTo(40));
            Assert.That(large.WalkableHeightVoxels, Is.EqualTo(48));
            Assert.That(large.WalkableClimbVoxels, Is.EqualTo(15));
        }

        [Test]
        public void NavBakeEstimator_RejectsInvalidProfileSlopeInsteadOfClamping()
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            config.Profiles[0].MaxSlopeDeg = 90f;
            var context = new NavBakeContext
            {
                MapId = "nav_estimate_invalid_slope",
                SourceUri = "Core:Maps/nav_estimate_invalid_slope.vtxm",
                Terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 8 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavBakeEstimator.Estimate(context))!;
            Assert.That(ex.Message, Does.Contain("maxSlopeDeg"));
        }

        [Test]
        public void NavBakeEstimator_HashChangesWhenTargetTerrainContentChanges()
        {
            var terrain = new MutableGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            NavBakeContext first = CreateEstimateContext(terrain, new[] { new NavBakeTileCoord(0, 0) });
            NavBakeEstimateReport firstEstimate = NavBakeEstimator.Estimate(first);

            terrain.SetCell(1, 1, new LogicTerrainCell(3, 0, LogicTerrainSurfaceFlags.Ramp, areaId: 7));
            NavBakeContext second = CreateEstimateContext(terrain, new[] { new NavBakeTileCoord(0, 0) });
            NavBakeEstimateReport secondEstimate = NavBakeEstimator.Estimate(second);

            Assert.That(secondEstimate.TerrainContentHash, Is.Not.EqualTo(firstEstimate.TerrainContentHash));
            Assert.That(secondEstimate.EstimateHash, Is.Not.EqualTo(firstEstimate.EstimateHash));
        }

        [Test]
        public void NavBakeEstimator_LargeBakeRequiresExplicitApprovalAndMatchingHash()
        {
            NavBakeContext context = CreateEstimateBudgetContext(widthCells: 128, heightCells: 128, chunkSizeCells: 4, layerCount: 2);

            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);

            Assert.That(estimate.BakeOperationCount, Is.EqualTo(2048));
            Assert.That(estimate.BudgetWorkUnitCount, Is.EqualTo(3_276_800));
            Assert.That(estimate.BudgetStatus, Is.EqualTo(NavBakeBudgetStatus.Large));
            Assert.That(estimate.RequiresExplicitLargeBakeApproval, Is.True);
            Assert.Throws<InvalidOperationException>(
                () => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: false, acceptedEstimateHash: null));
            Assert.Throws<InvalidOperationException>(
                () => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: true, acceptedEstimateHash: "wrong"));
            Assert.DoesNotThrow(
                () => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: true, acceptedEstimateHash: estimate.EstimateHash));
        }

        [Test]
        public void NavBakeEstimator_RejectsOversizedBakeEvenWithApproval()
        {
            NavBakeContext context = CreateEstimateBudgetContext(widthCells: 128, heightCells: 128, chunkSizeCells: 4, layerCount: 123);

            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);

            Assert.That(estimate.BakeOperationCount, Is.EqualTo(125952));
            Assert.That(estimate.BudgetWorkUnitCount, Is.EqualTo(201_523_200));
            Assert.That(estimate.BudgetStatus, Is.EqualTo(NavBakeBudgetStatus.Reject));
            Assert.That(estimate.RequiresExplicitLargeBakeApproval, Is.False);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: true, acceptedEstimateHash: estimate.EstimateHash))!;
            Assert.That(ex.Message, Does.Contain("reject"));
        }

        [Test]
        public void NavMeshBakeConfig_RequiresExplicitAlgorithmAndStrictCase()
        {
            var profiles = CreateAgentProfiles();

            string missingAlgorithmRoot = CreateTempNavConfig(WithRemovedProperty(ValidNavmeshJson(), "algorithm"));

            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(missingAlgorithmRoot, profiles))!;
                Assert.That(ex.Message, Does.Contain("algorithm"));
            }
            finally
            {
                Directory.Delete(missingAlgorithmRoot, recursive: true);
            }

            string wrongCaseRoot = CreateTempNavConfig(WithSetProperty(ValidNavmeshJson(), "algorithm", "Recast"));

            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(wrongCaseRoot, profiles))!;
                Assert.That(ex.Message, Does.Contain("recast"));
            }
            finally
            {
                Directory.Delete(wrongCaseRoot, recursive: true);
            }
        }

        [Test]
        public void NavMeshBakeConfig_RequiresExplicitRuntimeIncrementalConfig()
        {
            string missingRuntimeRoot = CreateTempNavConfig(WithRemovedProperty(ValidNavmeshJson(), "runtimeIncremental"));

            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LoadTempConfig(missingRuntimeRoot, CreateAgentProfiles()))!;
                Assert.That(ex.Message, Does.Contain("runtimeIncremental"));
            }
            finally
            {
                Directory.Delete(missingRuntimeRoot, recursive: true);
            }
        }

        [Test]
        public void NavMeshBakeConfigLoader_ModContextMergesNavigationConfigsThroughOfficialPipeline()
        {
            string repoRoot = CreateTempRepoNavigationConfig();
            try
            {
                string depRoot = Path.Combine(repoRoot, "mods", "DepNavMod");
                string targetRoot = Path.Combine(repoRoot, "mods", "TargetNavMod");
                WriteModManifest(depRoot, "DepNavMod", dependenciesJson: "{}");
                WriteModManifest(targetRoot, "TargetNavMod", dependenciesJson: """{ "DepNavMod": "*" }""");

                WriteNavigationAgentProfiles(depRoot,
                    """
                    [
                      { "id": "Small", "radiusCm": 30, "heightCm": 180, "clearanceCm": 40, "draftCm": 0, "beamCm": 0, "mass": 1, "layer": 0 },
                      { "id": "DepScout", "radiusCm": 22, "heightCm": 140, "clearanceCm": 25, "draftCm": 0, "beamCm": 0, "mass": 0.7, "layer": 0 }
                    ]
                    """);
                WriteNavigationAgentProfiles(targetRoot,
                    """
                    [
                      { "id": "Small", "radiusCm": 42, "heightCm": 190, "clearanceCm": 45, "draftCm": 0, "beamCm": 0, "mass": 1.25, "layer": 1 },
                      { "id": "ModHeavy", "radiusCm": 80, "heightCm": 240, "clearanceCm": 70, "draftCm": 0, "beamCm": 0, "mass": 3.5, "layer": 1 }
                    ]
                    """);
                WriteNavigationNavmesh(targetRoot,
                    """
                    {
                      "profiles": [
                        { "id": "Small", "maxClimbCm": 55, "maxSlopeDeg": 38 },
                        { "id": "ModHeavy", "maxClimbCm": 65, "maxSlopeDeg": 30 }
                      ],
                      "layers": [
                        { "id": "Ground", "layer": 0 },
                        { "id": "Air", "layer": 1 }
                      ],
                      "areas": [
                        { "id": "Road", "areaId": 1, "cost": 0.75 }
                      ]
                    }
                    """);

                NavMeshBakeConfigContext context = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, "TargetNavMod");

                Assert.That(context.AgentProfiles.Count, Is.EqualTo(3));
                Assert.That(context.AgentProfiles.Require("Small", "test").RadiusCm, Is.EqualTo(42f).Within(0.0001f));
                Assert.That(context.AgentProfiles.Require("Small", "test").Layer, Is.EqualTo(1));
                Assert.That(context.AgentProfiles.Require("DepScout", "test").RadiusCm, Is.EqualTo(22f).Within(0.0001f));
                Assert.That(context.AgentProfiles.Require("ModHeavy", "test").Mass, Is.EqualTo(3.5f).Within(0.0001f));
                Assert.That(context.Config.Algorithm, Is.EqualTo(NavBakeNames.AlgorithmRecast));
                Assert.That(context.Config.Profiles.Count, Is.EqualTo(2));
                Assert.That(context.Config.Profiles[0].Id, Is.EqualTo("Small"));
                Assert.That(context.Config.Profiles[0].MaxClimbCm, Is.EqualTo(55));
                Assert.That(context.Config.Layers.Count, Is.EqualTo(2));
                Assert.That(context.Config.Areas.Count, Is.EqualTo(1));
                Assert.That(context.Config.RuntimeIncremental.TileBudgetPerFixedTick, Is.EqualTo(4));
            }
            finally
            {
                Directory.Delete(repoRoot, recursive: true);
            }
        }

        [Test]
        public void NavMeshBakeConfigLoader_UnknownModFailsFast()
        {
            string repoRoot = CreateTempRepoNavigationConfig();
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, "missing_mod"))!;
                Assert.That(ex.Message, Does.Contain("Unknown mod"));
            }
            finally
            {
                Directory.Delete(repoRoot, recursive: true);
            }
        }

        [Test]
        public void NavMeshBakeConfigLoader_LoadFromRepoRootPreservesRelativePathParameter()
        {
            string repoRoot = CreateTempRepoNavigationConfig();
            try
            {
                string navigationDir = Path.Combine(repoRoot, "assets", "Configs", "Navigation");
                File.WriteAllText(Path.Combine(navigationDir, "alt_navmesh.json"),
                    """
                    {
                      "mode": "offline",
                      "algorithm": "exact-cdt",
                      "profiles": [
                        { "id": "Small", "maxClimbCm": 12, "maxSlopeDeg": 20 }
                      ],
                      "layers": [
                        { "id": "Ground", "layer": 0 }
                      ],
                      "areas": [],
                      "runtimeIncremental": {
                        "tileBudgetPerFixedTick": 2,
                        "includeNeighborTiles": false,
                        "heightScaleMeters": 1,
                        "minWalkableUpDot": 0.5,
                        "cliffHeightThreshold": 1,
                        "trackedStructuralEntityCapacity": 256,
                        "obstaclePrimitiveCapacity": 512,
                        "polygonVertexCapacity": 4096,
                        "dirtyTileCapacity": 64,
                        "stagedEntryCapacity": 64,
                        "publishedTileCapacity": 64,
                        "storeGroupCapacity": 8,
                        "residentTileCapacity": 128,
                        "outputVertexCapacity": 256,
                        "outputTriangleCapacity": 512,
                        "outputPortalCapacity": 64,
                        "initialResidentChunkX": 0,
                        "initialResidentChunkZ": 0,
                        "initialResidentWidthChunks": 1,
                        "initialResidentHeightChunks": 1
                      },
                      "layeredSpan": {
                        "scratchSlotCount": 2,
                        "rasterCellSizeCm": 100,
                        "rasterHaloCells": 1,
                        "sameSurfaceToleranceCm": 5,
                        "maxSimplificationErrorCm": 0,
                        "heightRounding": "roundHalfAwayFromZero",
                        "maxLawsonFlipCount": 100000,
                        "columnCapacity": 64,
                        "spanCapacity": 128,
                        "classifiedSpanCapacity": 128,
                        "walkableSpanCapacity": 128,
                        "linkCapacity": 256,
                        "sheetCapacity": 128,
                        "portalIntervalCapacity": 256,
                        "regionCapacity": 64,
                        "chartCapacity": 32,
                        "ringCapacity": 32,
                        "contourVertexCapacity": 256,
                        "contourEdgeCapacity": 256,
                        "seamCapacity": 64,
                        "canonicalLinkCapacity": 256,
                        "splitPointCapacity": 64,
                        "triangulationVertexCapacity": 256,
                        "triangulationTriangleCapacity": 512,
                        "constrainedEdgeCapacity": 512,
                        "borderPortalCapacity": 64,
                        "polygonVertexCapacity": 256,
                        "adjacencyEdgeCapacity": 1536,
                        "bridgeCandidateCapacity": 256,
                        "ringWorkCapacity": 64,
                        "temporaryConstraintFlagCapacity": 512
                      },
                      "triangleSurface": {
                        "haloPaddingCm": 100
                      },
                      "recast": {
                        "rasterCellSizeCm": 10,
                        "rasterCellHeightCm": 5
                      }
                    }
                    """);
                string catalogPath = Path.Combine(repoRoot, "assets", "Configs", "config_catalog.json");
                string catalog = File.ReadAllText(catalogPath).TrimEnd();
                catalog = catalog.Substring(0, catalog.Length - 1) +
                    "," + Environment.NewLine +
                    """  { "Path": "Navigation/alt_navmesh.json", "Policy": "DeepObject" }""" +
                    Environment.NewLine +
                    "]";
                File.WriteAllText(catalogPath, catalog);

                NavMeshBakeConfig config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot, "Navigation/alt_navmesh.json");

                Assert.That(config.Algorithm, Is.EqualTo(NavBakeNames.AlgorithmExactCdt));
                Assert.That(config.Profiles[0].MaxClimbCm, Is.EqualTo(12));
                Assert.That(config.RuntimeIncremental.TileBudgetPerFixedTick, Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(repoRoot, recursive: true);
            }
        }

        [Test]
        public void NavBakeContext_RejectsAbsoluteFilesystemSourceUri()
        {
            var context = new NavBakeContext
            {
                MapId = "nav_bake_contract",
                SourceUri = "Core:C:/absolute/map_data.bin",
                Terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmExactCdt),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.Validate())!;
            Assert.That(ex.Message, Does.Contain("VFS-relative"));
        }

        [Test]
        public void ExactCdtBake_DoesNotFallbackToGridMesh()
        {
            var terrain = new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            terrain.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                new NavBuildConfig(1f, 0.6f, 1),
                haloPaddingCm: 0);

            NavBakeResult result = new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(
                CreateOfflineContext(triangleSurface: surface, algorithm: NavBakeAlgorithmKind.ExactCdt));

            Assert.That(result.FailureCount, Is.EqualTo(0));
            Assert.That(result.Entries[0].Tile.TriangleCount, Is.EqualTo(0), "Fully blocked tile bakes to a valid empty tile, never a fallback.");
            Assert.That(result.Entries[0].Tile.Checksum, Is.Not.EqualTo(0UL));
            string artifact = JsonSerializer.Serialize(result.Entries[0].Artifact, new JsonSerializerOptions { IncludeFields = true });
            Assert.That(artifact, Does.Not.Contain("Grid mesh fallback"));
        }

        [Test]
        public void ExactCdtBake_RequiresTriangleSurfaceInputAndStrictObstacleLayer()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);

            InvalidOperationException unsupportedInput = Assert.Throws<InvalidOperationException>(() =>
                new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(
                    CreateOfflineContext(terrain, algorithm: NavBakeAlgorithmKind.ExactCdt)))!;
            Assert.That(unsupportedInput.Message, Does.Contain("does not support offline/logic-terrain"));

            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                new NavBuildConfig(1f, 0.6f, 1),
                haloPaddingCm: 0);
            NavBakeContext wrongCaseLayerContext = CreateOfflineContext(
                triangleSurface: surface,
                algorithm: NavBakeAlgorithmKind.ExactCdt,
                obstacles: new NavObstacleSet
                {
                    Obstacles =
                    {
                        new NavObstacle
                        {
                            Id = "wrong-case-blocker",
                            Enabled = true,
                            Kind = NavObstacleKind.Circle,
                            LayerId = "ground",
                            Center = new NavPointCm(150, 150),
                            RadiusCm = 45,
                            MinYcm = 0,
                            MaxYcm = 1000
                        }
                    }
                });
            InvalidOperationException missingLayer = Assert.Throws<InvalidOperationException>(
                () => new NavBakeService(new ExactCdtNavBakeAlgorithm()).Bake(wrongCaseLayerContext))!;
            Assert.That(missingLayer.Message, Does.Contain("unknown nav layer"));
        }

        [Test]
        public void NavBakeContext_RequiresExactlyOneInput()
        {
            InvalidOperationException neither = Assert.Throws<InvalidOperationException>(
                () => CreateOfflineContext(terrain: null, triangleSurface: null).Validate())!;
            Assert.That(neither.Message, Does.Contain("exactly one"));
            Assert.That(neither.Message, Does.Contain("neither"));

            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavTriangleSurfaceTileIndex surface = CreateTinyTriangleSurfaceIndex();
            InvalidOperationException both = Assert.Throws<InvalidOperationException>(
                () => new NavBakeContext
                {
                    MapId = "nav_bake_input_union_contract",
                    SourceUri = "Core:Maps/nav_bake_input_union_contract.vtxm",
                    Terrain = terrain,
                    TriangleSurface = surface,
                    Obstacles = new NavObstacleSet(),
                    Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                    AgentProfiles = CreateAgentProfiles(),
                    Targets = new[] { new NavBakeTileCoord(0, 0) },
                    BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                    TileVersion = 1,
                    Mode = NavBakeMode.Offline,
                    Algorithm = NavBakeAlgorithmKind.Recast,
                    Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
                }.Validate())!;
            Assert.That(both.Message, Does.Contain("exactly one"));
            Assert.That(both.Message, Does.Contain("both"));
        }

        [Test]
        public void NavBakeContext_TriangleSurfaceTargetBounds_UseGridTileCounts()
        {
            NavTriangleSurfaceTileIndex surface = CreateTinyTriangleSurfaceIndex(
                tileCountX: 2,
                tileCountZ: 1);
            NavBakeContext ok = CreateOfflineContext(
                terrain: null,
                triangleSurface: surface,
                targets: new[] { new NavBakeTileCoord(1, 0) });
            Assert.DoesNotThrow(() => ok.Validate());
            Assert.That(ok.InputKind, Is.EqualTo(NavBakeInputKind.TriangleSurface));
            Assert.That(ok.RequireTriangleSurface(), Is.SameAs(surface));

            NavBakeContext outOfRange = CreateOfflineContext(
                terrain: null,
                triangleSurface: surface,
                targets: new[] { new NavBakeTileCoord(0, 1) });
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => outOfRange.Validate())!;
            Assert.That(ex.Message, Does.Contain("triangleSurface.grid"));
            Assert.That(ex.Message, Does.Contain("0,1"));
        }

        [Test]
        public void NavBakeService_RejectsUnsupportedCapabilityWithoutFallback()
        {
            NavTriangleSurfaceTileIndex surface = CreateTinyTriangleSurfaceIndex();
            var context = CreateOfflineContext(
                terrain: null,
                triangleSurface: surface,
                algorithm: NavBakeAlgorithmKind.ExactCdt);
            var exactCdt = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.ExactCdt,
                NavBakeAdapterCapabilities.OfflineLogicTerrain |
                NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain);
            var recast = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.Recast,
                NavBakeAdapterCapabilities.OfflineLogicTerrain);
            var service = new NavBakeService(exactCdt, recast);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("does not support"));
            Assert.That(ex.Message, Does.Contain("triangle-surface"));
            Assert.That(ex.Message, Does.Contain("exact-cdt"));
            Assert.That(exactCdt.InvokeCount, Is.EqualTo(0));
            Assert.That(recast.InvokeCount, Is.EqualTo(0));
        }

        [Test]
        public void NavBakeService_MissingAdapterFailsFast()
        {
            var context = CreateOfflineContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.LayeredSpan);
            var service = new NavBakeService(new ExactCdtNavBakeAlgorithm());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("no adapter"));
            Assert.That(ex.Message, Does.Contain(NavBakeNames.AlgorithmLayeredSpan));
        }

        [Test]
        public void RecastAndExactCdt_DeclareOnlyTriangleSurfaceCapabilities()
        {
            var recast = new RecastNavBakeAlgorithm();
            var exactCdt = new ExactCdtNavBakeAlgorithm();

            Assert.That(
                recast.Capabilities,
                Is.EqualTo(
                    NavBakeAdapterCapabilities.OfflineTriangleSurface |
                    NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface));
            Assert.That(
                exactCdt.Capabilities,
                Is.EqualTo(
                    NavBakeAdapterCapabilities.OfflineTriangleSurface |
                    NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface));
            Assert.That(recast.Capabilities.HasFlag(NavBakeAdapterCapabilities.OfflineLogicTerrain), Is.False);
            Assert.That(recast.Capabilities.HasFlag(NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain), Is.False);
            Assert.That(exactCdt.Capabilities.HasFlag(NavBakeAdapterCapabilities.OfflineLogicTerrain), Is.False);
            Assert.That(exactCdt.Capabilities.HasFlag(NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain), Is.False);
        }

        [Test]
        public void NavBakeAdapterCapabilities_ValidateConsistency_RejectsContradictoryDeclarations()
        {
            NavBakeAdapterCapabilities flags = NavBakeAdapterCapabilities.OfflineLogicTerrain;

            Assert.DoesNotThrow(
                () => NavBakeAdapterCapability.ValidateConsistency(
                    NavBakeAlgorithmKind.Recast,
                    flags,
                    mode => mode == NavBakeMode.Offline));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavBakeAdapterCapability.ValidateConsistency(
                    NavBakeAlgorithmKind.Recast,
                    flags,
                    mode => true))!;
            Assert.That(ex.Message, Does.Contain("inconsistent"));
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
        }

        [Test]
        public void NavBakeNames_ParseAndFormatLayeredSpanExplicitly()
        {
            Assert.That(
                NavBakeNames.ParseAlgorithm(NavBakeNames.AlgorithmLayeredSpan, "test.algorithm"),
                Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.LayeredSpan), Is.EqualTo(NavBakeNames.AlgorithmLayeredSpan));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.Recast), Is.EqualTo(NavBakeNames.AlgorithmRecast));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.ExactCdt), Is.EqualTo(NavBakeNames.AlgorithmExactCdt));

            Assert.Throws<InvalidOperationException>(
                () => NavBakeNames.ParseAlgorithm("bogus", "test.algorithm"));
            ArgumentOutOfRangeException unknown = Assert.Throws<ArgumentOutOfRangeException>(
                () => NavBakeNames.FormatAlgorithm((NavBakeAlgorithmKind)255))!;
            Assert.That(unknown.Message, Does.Contain("Unknown nav bake algorithm kind"));
            Assert.That(unknown.Message, Does.Not.Contain(NavBakeNames.AlgorithmExactCdt));
            Assert.That(unknown.Message, Does.Not.Contain(NavBakeNames.AlgorithmRecast));
        }

        [Test]
        public void NavMeshBakeConfigLoader_AcceptsLayeredSpanWithoutAdapterClaim()
        {
            string root = CreateTempNavConfig(WithSetProperty(ValidNavmeshJson(), "algorithm", NavBakeNames.AlgorithmLayeredSpan));

            try
            {
                NavMeshBakeConfig config = LoadTempConfig(root, CreateAgentProfiles());
                Assert.That(config.Algorithm, Is.EqualTo(NavBakeNames.AlgorithmLayeredSpan));
                Assert.That(config.ParsedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void NavBakeEstimator_LayeredSpanFailsFastWithoutInheritingOtherMetrics()
        {
            var context = CreateOfflineContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.LayeredSpan);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => NavBakeEstimator.Estimate(context))!;
            Assert.That(ex.Message, Does.Contain("layered-span"));
            Assert.That(ex.Message, Does.Contain("does not support"));
            Assert.That(ex.Message, Does.Contain("not implemented"));
        }

        private static void AssertPathSegmentsDoNotEnterAabb(
            NavPathResult path,
            int minXcm,
            int minZcm,
            int maxXcm,
            int maxZcm)
        {
            Assert.That(path.PathXcm.Length, Is.EqualTo(path.PathZcm.Length));
            for (int i = 0; i + 1 < path.PathXcm.Length; i++)
            {
                int ax = path.PathXcm[i];
                int az = path.PathZcm[i];
                int bx = path.PathXcm[i + 1];
                int bz = path.PathZcm[i + 1];
                int dx = bx - ax;
                int dz = bz - az;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dz * dz) / 25d));

                for (int s = 0; s <= steps; s++)
                {
                    double t = s / (double)steps;
                    double x = ax + dx * t;
                    double z = az + dz * t;
                    bool inside = x > minXcm && x < maxXcm && z > minZcm && z < maxZcm;
                    Assert.That(
                        inside,
                        Is.False,
                        $"Path segment {i} enters the blocked obstacle interior near ({x:0.##},{z:0.##}).");
                }
            }
        }

        private static void AssertPathSegmentsStayInsideNavMesh(
            NavPathResult path,
            NavTileStore store,
            int tileWidthCm,
            int tileHeightCm)
        {
            Assert.That(path.PathXcm.Length, Is.EqualTo(path.PathZcm.Length));
            for (int i = 0; i + 1 < path.PathXcm.Length; i++)
            {
                int ax = path.PathXcm[i];
                int az = path.PathZcm[i];
                int bx = path.PathXcm[i + 1];
                int bz = path.PathZcm[i + 1];
                int dx = bx - ax;
                int dz = bz - az;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dz * dz) / 25d));

                for (int s = 0; s <= steps; s++)
                {
                    double t = s / (double)steps;
                    double x = ax + dx * t;
                    double z = az + dz * t;
                    Assert.That(
                        IsPointInsideAnyNavTriangle(store, tileWidthCm, tileHeightCm, x, z),
                        Is.True,
                        $"Path segment {i} leaves the baked navmesh near ({x:0.##},{z:0.##}).");
                }
            }
        }

        private static bool IsPointInsideAnyNavTriangle(
            NavTileStore store,
            int tileWidthCm,
            int tileHeightCm,
            double worldXcm,
            double worldZcm)
        {
            int tileX = (int)Math.Floor(worldXcm / tileWidthCm);
            int tileZ = (int)Math.Floor(worldZcm / tileHeightCm);
            if (!store.TryGet(new NavTileId(tileX, tileZ, 0), out NavTile tile))
            {
                return false;
            }

            double localX = worldXcm - tile.OriginXcm;
            double localZ = worldZcm - tile.OriginZcm;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                if (PointInTriangle2D(
                    localX,
                    localZ,
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

        private static bool PointInTriangle2D(
            double px,
            double pz,
            double ax,
            double az,
            double bx,
            double bz,
            double cx,
            double cz)
        {
            double v0x = cx - ax;
            double v0z = cz - az;
            double v1x = bx - ax;
            double v1z = bz - az;
            double v2x = px - ax;
            double v2z = pz - az;

            double dot00 = v0x * v0x + v0z * v0z;
            double dot01 = v0x * v1x + v0z * v1z;
            double dot02 = v0x * v2x + v0z * v2z;
            double dot11 = v1x * v1x + v1z * v1z;
            double dot12 = v1x * v2x + v1z * v2z;

            double denom = dot00 * dot11 - dot01 * dot01;
            if (Math.Abs(denom) <= 0.000001d) return false;

            double invDenom = 1d / denom;
            double u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            double v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            const double epsilon = 0.001d;
            return u >= -epsilon && v >= -epsilon && u + v <= 1d + epsilon;
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
                Recast = new NavRecastConfig
                {
                    RasterCellSizeCm = 10,
                    RasterCellHeightCm = 5
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 100 },
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
                    ResidentTileCapacity = 64,
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

        private static AgentProfileRegistry CreateAgentProfiles(params AgentProfileConfig[] additionalProfiles)
        {
            var profiles = new List<AgentProfileConfig>
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
            };
            profiles.AddRange(additionalProfiles);
            return new AgentProfileRegistry(profiles);
        }

        private static NavBakeContext CreateOfflineContext(
            LogicTerrainField? terrain = null,
            NavTriangleSurfaceTileIndex? triangleSurface = null,
            IReadOnlyList<NavBakeTileCoord>? targets = null,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.Recast,
            NavObstacleSet? obstacles = null)
        {
            return new NavBakeContext
            {
                MapId = "nav_bake_input_union_contract",
                SourceUri = "Core:Maps/nav_bake_input_union_contract.vtxm",
                Terrain = terrain,
                TriangleSurface = triangleSurface,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.FormatAlgorithm(algorithm)),
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets ?? new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavTriangleSurfaceTileIndex CreateTinyTriangleSurfaceIndex(
            int originXcm = 0,
            int originZcm = 0,
            int tileWidthCm = 100,
            int tileHeightCm = 100,
            int tileCountX = 1,
            int tileCountZ = 1,
            int haloPaddingCm = 0)
        {
            var snapshot = new NavTriangleSurfaceSnapshot(
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<byte>(),
                Array.Empty<int>(),
                Array.Empty<NavTriangleSurfaceFlags>());
            return NavTriangleSurfaceTileIndex.Build(
                snapshot,
                new NavTriangleSurfaceTileGrid(
                    originXcm,
                    originZcm,
                    tileWidthCm,
                    tileHeightCm,
                    tileCountX,
                    tileCountZ,
                    haloPaddingCm));
        }

        private static NavBakeContext CreateRuntimeIncrementalContext(
            LogicTerrainField terrain,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.ExactCdt,
            NavObstacleSet obstacles = null)
        {
            return new NavBakeContext
            {
                MapId = "nav_runtime_incremental_contract",
                SourceUri = "Core:Maps/nav_runtime_incremental_contract.vtxm",
                Terrain = terrain,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.FormatAlgorithm(algorithm)),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 11,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavBakeContext CreateRuntimeIncrementalSurfaceContext(
            NavTriangleSurfaceTileIndex surface,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.ExactCdt,
            NavObstacleSet obstacles = null,
            IReadOnlyList<NavBakeTileCoord> targets = null,
            uint tileVersion = 11)
        {
            return new NavBakeContext
            {
                MapId = "nav_runtime_incremental_surface_contract",
                SourceUri = "Core:Maps/nav_runtime_incremental_surface_contract.tris",
                TriangleSurface = surface,
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.FormatAlgorithm(algorithm)),
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets ?? new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = tileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavTriangleSurfaceTileIndex CreateFlatGridTriangleSurfaceIndex(
            int tileCountX,
            int tileCountZ,
            int tileWidthCm = 400,
            int tileHeightCm = 400,
            int yCm = 0,
            int haloPaddingCm = 100)
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

        private static NavTileStore CreateRuntimeTestStore(NavMeshBakeConfig config)
        {
            return new NavTileStore(
                _ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."),
                config.RuntimeIncremental);
        }

        private static NavBakeContext CreateEstimateBudgetContext(
            int widthCells,
            int heightCells,
            int chunkSizeCells,
            int layerCount)
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            config.Layers.Clear();
            for (int i = 0; i < layerCount; i++)
            {
                config.Layers.Add(new NavLayerConfig { Id = $"Layer{i}", Layer = i });
            }

            var terrain = new FlatGridLogicTerrainField(widthCells, heightCells, chunkSizeCells: chunkSizeCells);
            var targets = new List<NavBakeTileCoord>(terrain.WidthChunks * terrain.HeightChunks);
            for (int y = 0; y < terrain.HeightChunks; y++)
            {
                for (int x = 0; x < terrain.WidthChunks; x++)
                {
                    targets.Add(new NavBakeTileCoord(x, y));
                }
            }

            return new NavBakeContext
            {
                MapId = "nav_estimate_budget_contract",
                SourceUri = "Core:Maps/nav_estimate_budget_contract.vtxm",
                Terrain = terrain,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = true, MaxDegreeOfParallelism = 8 }
            };
        }

        private static NavBakeContext CreateEstimateContext(
            LogicTerrainField terrain,
            IReadOnlyList<NavBakeTileCoord> targets)
        {
            return new NavBakeContext
            {
                MapId = "nav_estimate_hash_contract",
                SourceUri = "Core:Maps/nav_estimate_hash_contract.vtxm",
                Terrain = terrain,
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavMeshBakeConfig LoadTempConfig(string root, AgentProfileRegistry profiles)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog);
        }

        private static IReadOnlyList<byte[]> CollectDetourTileBytes(NavBakeResult bake)
        {
            var tiles = new List<byte[]>(bake.Entries.Count);
            for (int i = 0; i < bake.Entries.Count; i++)
            {
                NavBakeResultEntry entry = bake.Entries[i];
                if (entry.Success && entry.DetourTileBytes.Length > 0)
                {
                    tiles.Add(entry.DetourTileBytes);
                }
            }

            return tiles;
        }

        private static IEnumerable<string> FormatPathPoints(NavPathResult path)
        {
            int count = Math.Min(path.PathXcm.Length, path.PathZcm.Length);
            for (int i = 0; i < count; i++)
            {
                yield return $"({path.PathXcm[i]},{path.PathZcm[i]})";
            }
        }

        private static string CreateTempNavConfig(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-bake-service-" + Guid.NewGuid().ToString("N"));
            string configs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(configs, "Navigation"));
            File.WriteAllText(Path.Combine(configs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(configs, "Navigation", "navmesh.json"), navmeshJson);
            return tempRoot;
        }

        private static string ValidNavmeshJson()
        {
            return """
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": [
                { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
              ],
              "layers": [
                { "id": "Ground", "layer": 0 }
              ],
              "areas": [],
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 4,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1,
                "trackedStructuralEntityCapacity": 256,
                "obstaclePrimitiveCapacity": 512,
                "polygonVertexCapacity": 4096,
                "dirtyTileCapacity": 64,
                "stagedEntryCapacity": 64,
                "publishedTileCapacity": 64,
                "storeGroupCapacity": 8,
                "residentTileCapacity": 128,
                "outputVertexCapacity": 256,
                "outputTriangleCapacity": 512,
                "outputPortalCapacity": 64,
                "initialResidentChunkX": 0,
                "initialResidentChunkZ": 0,
                "initialResidentWidthChunks": 1,
                "initialResidentHeightChunks": 1
              },
              "layeredSpan": {
                "scratchSlotCount": 2,
                "rasterCellSizeCm": 100,
                "rasterHaloCells": 1,
                "sameSurfaceToleranceCm": 5,
                "maxSimplificationErrorCm": 0,
                "heightRounding": "roundHalfAwayFromZero",
                "maxLawsonFlipCount": 100000,
                "columnCapacity": 64,
                "spanCapacity": 128,
                "classifiedSpanCapacity": 128,
                "walkableSpanCapacity": 128,
                "linkCapacity": 256,
                "sheetCapacity": 128,
                "portalIntervalCapacity": 256,
                "regionCapacity": 64,
                "chartCapacity": 32,
                "ringCapacity": 32,
                "contourVertexCapacity": 256,
                "contourEdgeCapacity": 256,
                "seamCapacity": 64,
                "canonicalLinkCapacity": 256,
                "splitPointCapacity": 64,
                "triangulationVertexCapacity": 256,
                "triangulationTriangleCapacity": 512,
                "constrainedEdgeCapacity": 512,
                "borderPortalCapacity": 64,
                "polygonVertexCapacity": 256,
                "adjacencyEdgeCapacity": 1536,
                "bridgeCandidateCapacity": 256,
                "ringWorkCapacity": 64,
                "temporaryConstraintFlagCapacity": 512
              },
              "triangleSurface": {
                "haloPaddingCm": 100
              },
              "recast": {
                "rasterCellSizeCm": 10,
                "rasterCellHeightCm": 5
              }
            }
            """;
        }

        private static string WithRemovedProperty(string json, string propertyName)
        {
            System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root.Remove(propertyName);
            return root.ToJsonString();
        }

        private static string WithSetProperty(string json, string propertyName, string value)
        {
            System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root[propertyName] = value;
            return root.ToJsonString();
        }

        private static string CreateTempRepoNavigationConfig()
        {
            string repoRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-config-repo-" + Guid.NewGuid().ToString("N"));
            string navigationDir = Path.Combine(repoRoot, "assets", "Configs", "Navigation");
            Directory.CreateDirectory(navigationDir);
            Directory.CreateDirectory(Path.Combine(repoRoot, "mods"));
            File.WriteAllText(Path.Combine(repoRoot, "assets", "Configs", "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" },
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(navigationDir, "agent_profiles.json"),
                """
                [
                  { "id": "Small", "radiusCm": 30, "heightCm": 180, "clearanceCm": 40, "draftCm": 0, "beamCm": 0, "mass": 1, "layer": 0 }
                ]
                """);
            File.WriteAllText(Path.Combine(navigationDir, "navmesh.json"), ValidNavmeshJson());
            return repoRoot;
        }

        private static void WriteModManifest(string modRoot, string id, string dependenciesJson)
        {
            Directory.CreateDirectory(modRoot);
            File.WriteAllText(Path.Combine(modRoot, "mod.json"),
                $$"""
                {
                  "name": "{{id}}",
                  "version": "1.0.0",
                  "dependencies": {{dependenciesJson}}
                }
                """);
        }

        private static void WriteNavigationAgentProfiles(string modRoot, string json)
        {
            string dir = Path.Combine(modRoot, "assets", "Configs", "Navigation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "agent_profiles.json"), json);
        }

        private static void WriteNavigationNavmesh(string modRoot, string json)
        {
            string dir = Path.Combine(modRoot, "assets", "Configs", "Navigation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "navmesh.json"), json);
        }

        private sealed class RecordingBakeAlgorithm : INavBakeAlgorithm
        {
            public NavBakeAlgorithmKind LastBakedAlgorithm { get; private set; } = (NavBakeAlgorithmKind)127;

            // Test fake: claims Recast identity while explicitly declaring the triangle-surface
            // runtime contract so the rebuild-context algorithm contract is observable without
            // changing production Recast capability.
            public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Recast;

            public NavBakeAdapterCapabilities Capabilities =>
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            public bool SupportsMode(NavBakeMode mode)
            {
                return mode switch
                {
                    NavBakeMode.Offline => true,
                    NavBakeMode.RuntimeIncremental => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
                };
            }

            public bool GuaranteesBitwiseDeterminism => false;

            public bool Supports3DMultiLayer => false;

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
                LastBakedAlgorithm = context.Algorithm;
                tile = NavValidEmptyTile.Create(
                    new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                    context.TileVersion,
                    buildConfigHash: 1,
                    originXcm: 0,
                    originZcm: 0);
                detourTileBytes = Array.Empty<byte>();
                artifact = NavValidEmptyTile.CreateSuccessArtifact(tile, "recording-fake-ok");
                return true;
            }
        }

        private sealed class SelectiveFailNavBakeAlgorithm : INavBakeAlgorithm
        {
            private readonly NavBakeTileCoord _failTarget;
            private readonly ExactCdtNavBakeAlgorithm _ok = new ExactCdtNavBakeAlgorithm();

            public SelectiveFailNavBakeAlgorithm(NavBakeTileCoord failTarget)
            {
                _failTarget = failTarget;
            }

            public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.ExactCdt;

            public NavBakeAdapterCapabilities Capabilities =>
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            public bool SupportsMode(NavBakeMode mode)
            {
                return mode switch
                {
                    NavBakeMode.Offline => true,
                    NavBakeMode.RuntimeIncremental => true,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
                };
            }

            public bool GuaranteesBitwiseDeterminism => false;

            public bool Supports3DMultiLayer => false;

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
                if (target.Equals(_failTarget))
                {
                    tile = null!;
                    detourTileBytes = Array.Empty<byte>();
                    artifact = new NavBakeArtifact(
                        new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                        context.TileVersion,
                        NavBakeStage.Triangulate,
                        NavBakeErrorCode.TriangulationFailed,
                        "selective-fail",
                        walkableTriangleCount: 0,
                        vertexCount: 0,
                        triangleCount: 0,
                        portalCount: 0);
                    return false;
                }

                return _ok.TryBake(context, target, layer, navProfile, agentProfile, out tile, out detourTileBytes, out artifact);
            }
        }

        private sealed class RecordingFakeNavBakeAlgorithm : INavBakeAlgorithm
        {
            private readonly NavBakeAlgorithmKind _kind;
            private readonly NavBakeAdapterCapabilities _capabilities;

            public RecordingFakeNavBakeAlgorithm(NavBakeAlgorithmKind kind, NavBakeAdapterCapabilities capabilities)
            {
                _kind = kind;
                _capabilities = capabilities;
            }

            public int InvokeCount { get; private set; }

            public NavBakeAlgorithmKind Kind => _kind;

            public NavBakeAdapterCapabilities Capabilities => _capabilities;

            public bool SupportsMode(NavBakeMode mode)
            {
                return NavBakeAdapterCapability.SupportsMode(_capabilities, mode);
            }

            public bool GuaranteesBitwiseDeterminism => false;

            public bool Supports3DMultiLayer => false;

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
                InvokeCount++;
                throw new InvalidOperationException("RecordingFakeNavBakeAlgorithm must never be invoked for unsupported capability checks.");
            }
        }
    }
}
