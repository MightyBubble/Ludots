using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavBakeServiceContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void NavBakeService_RunsSingleContextForHeadlessAndBridgeAdapters()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var profiles = CreateAgentProfiles();
            var context = new NavBakeContext
            {
                MapId = "nav_bake_contract",
                SourceUri = "Core:Maps/nav_bake_contract.vtxm",
                Terrain = terrain,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = profiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 7,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Cdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            var service = new NavBakeService(new CdtNavBakeAlgorithm());
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
            var context = new NavBakeContext
            {
                MapId = "nav_recast_grid_contract",
                SourceUri = "Core:Maps/nav_recast_grid_contract.bin",
                Terrain = new FlatGridLogicTerrainField(16, 16, chunkSizeCells: 16),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
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
            const int chunkSizeCells = 32;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            var context = new NavBakeContext
            {
                MapId = "nav_recast_open_grid_query_contract",
                SourceUri = "Core:Maps/nav_recast_open_grid_query_contract.bin",
                Terrain = new FlatGridLogicTerrainField(96, 32, chunkSizeCells: chunkSizeCells),
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(1, 0),
                    new NavBakeTileCoord(2, 0)
                },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
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
                startXcm: 400,
                startZcm: 1200,
                goalXcm: 6800,
                goalZcm: 1200,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            TestContext.WriteLine("Default baseline path: " + string.Join(" -> ", FormatPathPoints(path)));
            Assert.That(path.PathXcm, Is.EqualTo(new[] { 400, 6800 }));
            Assert.That(path.PathZcm, Is.EqualTo(new[] { 1200, 1200 }));
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
            const int chunkSizeCells = 64;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            const int obstacleMinXcm = 2400;
            const int obstacleMinZcm = 2400;
            const int obstacleMaxXcm = 3600;
            const int obstacleMaxZcm = 3600;

            var context = new NavBakeContext
            {
                MapId = "nav_recast_blocked_hole_query_contract",
                SourceUri = "Core:Maps/nav_recast_blocked_hole_query_contract.bin",
                Terrain = new FlatGridLogicTerrainField(64, 64, chunkSizeCells: chunkSizeCells),
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
                startXcm: 400,
                startZcm: 400,
                goalXcm: 6000,
                goalZcm: 6000,
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
        public void NavBakeService_RuntimeIncremental_AcceptsCdtAndRecastOnly()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var service = new NavBakeService(new RecastNavBakeAlgorithm(), new CdtNavBakeAlgorithm());

            Assert.DoesNotThrow(() => _ = service.Bake(CreateRuntimeIncrementalContext(terrain, algorithm: NavBakeAlgorithmKind.Recast)),
                "runtime-incremental + recast 是受纳组合（vhtm 起伏地形的运行时重烤口径）");
            Assert.DoesNotThrow(() => _ = service.Bake(CreateRuntimeIncrementalContext(terrain, algorithm: NavBakeAlgorithmKind.Cdt)),
                "runtime-incremental + cdt 是受纳组合");

            var context = CreateRuntimeIncrementalContext(terrain, algorithm: (NavBakeAlgorithmKind)99);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
            Assert.That(ex.Message, Does.Contain("cdt' or 'recast'"));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_ProcessesDirtyTilesByBudgetAndPublishesRevision()
        {
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            var context = CreateRuntimeIncrementalContext(terrain);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = new NavTileStore(_ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."));
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, tileWidthCm: 400, tileHeightCm: 400);
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Platform.Abstractions.WorldAabbCm(50, 50, 20, 20), includeNeighbors: false), Is.EqualTo(1));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Platform.Abstractions.WorldAabbCm(450, 50, 20, 20), includeNeighbors: false), Is.EqualTo(1));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Platform.Abstractions.WorldAabbCm(450, 50, 20, 20), includeNeighbors: false), Is.EqualTo(0));
            Assert.That(queue.PendingTileCount, Is.EqualTo(2));

            RuntimeNavMeshRebuildBatch first = PumpUntilNPublished(queue, submitBudget: 1, expectedPublished: 1);
            Assert.That(first.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(first.FailedEntryCount, Is.EqualTo(0));
            Assert.That(first.PendingTileCount, Is.EqualTo(1));
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(first.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile firstTile), Is.True);
            Assert.That(firstTile.TileVersion, Is.EqualTo(context.TileVersion + 1u));

            RuntimeNavMeshRebuildBatch second = PumpUntilNPublished(queue, submitBudget: 1, expectedPublished: 1);
            Assert.That(second.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(second.FailedEntryCount, Is.EqualTo(0));
            Assert.That(second.PendingTileCount, Is.EqualTo(0));
            Assert.That(second.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(second.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(store.Revision, Is.EqualTo(2u));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);
        }

        /// <summary>
        /// 泵到累计发布 expectedPublished 个瓦片为止：首轮提交 submitBudget 个，后续轮次只发布不提交。
        /// 返回累计发布清单构成的批次视图（PublishedTiles 为累计，其余字段取最后一次泵）。
        /// </summary>
        private static RuntimeNavMeshRebuildBatch PumpUntilNPublished(
            RuntimeIncrementalNavMeshRebuildQueue queue,
            int submitBudget,
            int expectedPublished)
        {
            var published = new List<RuntimeNavMeshRebuildPublishedTile>();
            var failures = new List<NavBakeResultEntry>();
            RuntimeNavMeshRebuildBatch last = queue.ProcessBudget(submitBudget);
            published.AddRange(last.PublishedTiles);
            failures.AddRange(last.FailedEntries);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (published.Count + failures.Count < expectedPublished && stopwatch.ElapsedMilliseconds < 10000)
            {
                Thread.Sleep(1);
                last = queue.ProcessBudget(0);
                published.AddRange(last.PublishedTiles);
                failures.AddRange(last.FailedEntries);
            }

            Assert.That(published.Count + failures.Count, Is.EqualTo(expectedPublished), "Runtime rebake did not publish expected tiles in time.");
            return new RuntimeNavMeshRebuildBatch(last.RequestedTileBudget, last.RebuiltTileCount, failures.Count, last.PendingTileCount, published, failures);
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_DirtyAabbMapsToNeighborTilesAndIgnoresOutOfWorld()
        {
            var terrain = new FlatGridLogicTerrainField(8, 8, chunkSizeCells: 4);
            var context = CreateRuntimeIncrementalContext(terrain);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = new NavTileStore(_ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."));
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, tileWidthCm: 400, tileHeightCm: 400);
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Platform.Abstractions.WorldAabbCm(-500, -500, 20, 20), includeNeighbors: true), Is.EqualTo(0));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Platform.Abstractions.WorldAabbCm(405, 405, 10, 10), includeNeighbors: true), Is.EqualTo(4));

            RuntimeNavMeshRebuildBatch batch = PumpUntilNPublished(queue, submitBudget: 4, expectedPublished: 4);
            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PendingTileCount, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(4));
            Assert.That(batch.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(batch.PublishedTiles[1].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(batch.PublishedTiles[2].Target, Is.EqualTo(new NavBakeTileCoord(0, 1)));
            Assert.That(batch.PublishedTiles[3].Target, Is.EqualTo(new NavBakeTileCoord(1, 1)));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_FailedBakeKeepsReadablePreviousTile()
        {
            var context = CreateRuntimeIncrementalContext(new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4));
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var baseline = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(CreateRuntimeIncrementalContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4)));
            Assert.That(baseline.FailureCount, Is.EqualTo(0));

            var store = new NavTileStore(_ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."));
            uint baselineRevision = store.Replace(baseline.Entries[0].Tile);
            Assert.That(baselineRevision, Is.EqualTo(1u));

            ((MutableGridLogicTerrainField)context.Terrain).Fill(
                new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, tileWidthCm: 400, tileHeightCm: 400);
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            RuntimeNavMeshRebuildBatch failed = PumpUntilNPublished(queue, submitBudget: 1, expectedPublished: 1);

            Assert.That(failed.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(failed.FailedEntryCount, Is.EqualTo(1));
            Assert.That(failed.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(baselineRevision));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile current), Is.True);
            Assert.That(current.Checksum, Is.EqualTo(baseline.Entries[0].Tile.Checksum));
        }

        [Test]
        public void CdtBake_ConsumesObstacleSetWithStrictLayerId()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBakeContext clearContext = CreateRuntimeIncrementalContext(terrain);
            NavBakeResult clear = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(clearContext);
            Assert.That(clear.FailureCount, Is.EqualTo(0));

            NavBakeContext blockedContext = CreateRuntimeIncrementalContext(
                terrain,
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
                            RadiusCm = 500
                        }
                    }
                });
            NavBakeResult blocked = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(blockedContext);

            Assert.That(blocked.FailureCount, Is.EqualTo(1));
            Assert.That(blocked.Entries[0].Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.NoWalkableDomain));

            NavBakeContext wrongCaseLayerContext = CreateRuntimeIncrementalContext(
                terrain,
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
                            RadiusCm = 45
                        }
                    }
                });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new NavBakeService(new CdtNavBakeAlgorithm()).Bake(wrongCaseLayerContext))!;
            Assert.That(ex.Message, Does.Contain("unknown nav layer"));
        }

        [Test]
        public void NavTileStore_StableReadRejectsMixedRevision()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBakeResult baseline = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(CreateRuntimeIncrementalContext(terrain));
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
                            RadiusCm = 35
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
            Assert.That(estimate.RecastColumnBudgetTotal, Is.EqualTo(128));
            Assert.That(estimate.BudgetWorkUnitCount, Is.EqualTo(128));
            Assert.That(estimate.EstimatedTileBytesLow, Is.EqualTo(8L * NavBakeEstimator.EstimatedBytesPerOperationLow));
            Assert.That(estimate.EstimatedTileBytesHigh, Is.EqualTo(8L * NavBakeEstimator.EstimatedBytesPerOperationHigh));
            Assert.That(estimate.EstimatedSerialSecondsLow, Is.EqualTo(0.64d).Within(0.0001d));
            Assert.That(estimate.EstimatedSecondsLow, Is.EqualTo(0.16d).Within(0.0001d));
            Assert.That(estimate.BudgetStatus, Is.EqualTo(NavBakeBudgetStatus.Ok));
            Assert.That(estimate.RequiresExplicitLargeBakeApproval, Is.False);
            Assert.DoesNotThrow(() => NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: false, acceptedEstimateHash: null));

            NavBakeProfileEstimate small = estimate.Profiles[0];
            Assert.That(small.ProfileId, Is.EqualTo("Small"));
            Assert.That(small.RecastCellSizeCm, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(small.RecastCellHeightCm, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(small.RecastColumnsPerAxis, Is.EqualTo(4));
            Assert.That(small.WalkableHeightVoxels, Is.EqualTo(4));
            Assert.That(small.WalkableClimbVoxels, Is.EqualTo(0));
            Assert.That(small.MinWalkableUpDot, Is.EqualTo(MathF.Cos(45f * MathF.PI / 180f)).Within(0.0001f));
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
            NavBakeContext context = CreateEstimateBudgetContext(widthCells: 1280, heightCells: 1280, chunkSizeCells: 4, layerCount: 2);

            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);

            Assert.That(estimate.BakeOperationCount, Is.EqualTo(204_800));
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
            NavBakeContext context = CreateEstimateBudgetContext(widthCells: 1280, heightCells: 1280, chunkSizeCells: 4, layerCount: 123);

            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);

            Assert.That(estimate.BakeOperationCount, Is.EqualTo(12_595_200));
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

            string missingAlgorithmRoot = CreateTempNavConfig(
                """
                {
                  "mode": "offline",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1
                  }
                }
                """);

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

            string wrongCaseRoot = CreateTempNavConfig(
                """
                {
                  "mode": "offline",
                  "algorithm": "Recast",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": [],
                  "runtimeIncremental": {
                    "tileBudgetPerFixedTick": 1,
                    "includeNeighborTiles": true,
                    "heightScaleMeters": 1,
                    "minWalkableUpDot": 0.6,
                    "cliffHeightThreshold": 1
                  }
                }
                """);

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
            string missingRuntimeRoot = CreateTempNavConfig(
                """
                {
                  "mode": "offline",
                  "algorithm": "recast",
                  "profiles": [
                    { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": []
                }
                """);

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
                string navigationDir = Path.Combine(repoRoot, "assets", "Navigation");
                File.WriteAllText(Path.Combine(navigationDir, "alt_navmesh.json"),
                    """
                    {
                      "mode": "offline",
                      "algorithm": "cdt",
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
                        "cliffHeightThreshold": 1
                      }
                    }
                    """);
                string catalogPath = Path.Combine(repoRoot, "assets", "config_catalog.json");
                string catalog = File.ReadAllText(catalogPath).TrimEnd();
                catalog = catalog.Substring(0, catalog.Length - 1) +
                    "," + Environment.NewLine +
                    """  { "Path": "Navigation/alt_navmesh.json", "Policy": "DeepObject" }""" +
                    Environment.NewLine +
                    "]";
                File.WriteAllText(catalogPath, catalog);

                NavMeshBakeConfig config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot, "Navigation/alt_navmesh.json");

                Assert.That(config.Algorithm, Is.EqualTo(NavBakeNames.AlgorithmCdt));
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
                Config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.Cdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.Validate())!;
            Assert.That(ex.Message, Does.Contain("VFS-relative"));
        }

        [Test]
        public void CdtBakePipeline_DoesNotFallbackToGridMesh()
        {
            var terrain = new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            terrain.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));

            BakePipelineResult result = BakePipeline.Execute(
                terrain,
                chunkX: 0,
                chunkY: 0,
                tileVersion: 1,
                new NavBuildConfig(1f, 0.6f, 1),
                new NavObstacleSet(),
                GroundLayerId);

            Assert.That(result.Success, Is.False);
            string artifact = JsonSerializer.Serialize(result.Artifact, new JsonSerializerOptions { IncludeFields = true });
            Assert.That(artifact, Does.Not.Contain("Grid mesh fallback"));
        }

        [Test]
        public void CdtBakePipeline_RequiresExplicitObstacleSetAndLayer()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);

            InvalidOperationException missingObstacles = Assert.Throws<InvalidOperationException>(() =>
                BakePipeline.Execute(
                    terrain,
                    chunkX: 0,
                    chunkY: 0,
                    tileVersion: 1,
                    new NavBuildConfig(1f, 0.6f, 1),
                    obstacles: null!,
                    GroundLayerId))!;
            Assert.That(missingObstacles.Message, Does.Contain("NavObstacleSet"));

            InvalidOperationException missingLayer = Assert.Throws<InvalidOperationException>(() =>
                BakePipeline.Execute(
                    terrain,
                    chunkX: 0,
                    chunkY: 0,
                    tileVersion: 1,
                    new NavBuildConfig(1f, 0.6f, 1),
                    new NavObstacleSet(),
                    layerId: ""))!;
            Assert.That(missingLayer.Message, Does.Contain("layer id"));
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
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1
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

        private static NavBakeContext CreateRuntimeIncrementalContext(
            LogicTerrainField terrain,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.Cdt,
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
            string configs = tempRoot;
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

        private static string CreateTempRepoNavigationConfig()
        {
            string repoRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-config-repo-" + Guid.NewGuid().ToString("N"));
            string navigationDir = Path.Combine(repoRoot, "assets", "Navigation");
            Directory.CreateDirectory(navigationDir);
            Directory.CreateDirectory(Path.Combine(repoRoot, "mods"));
            File.WriteAllText(Path.Combine(repoRoot, "assets", "config_catalog.json"),
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
            File.WriteAllText(Path.Combine(navigationDir, "navmesh.json"),
                """
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
                    "cliffHeightThreshold": 1
                  }
                }
                """);
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
            string dir = Path.Combine(modRoot, "assets", "Navigation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "agent_profiles.json"), json);
        }

        private static void WriteNavigationNavmesh(string modRoot, string json)
        {
            string dir = Path.Combine(modRoot, "assets", "Navigation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "navmesh.json"), json);
        }
    }
}
