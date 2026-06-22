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
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

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
        public void NavBakeService_RuntimeIncremental_RequiresCdtAlgorithm()
        {
            var context = CreateRuntimeIncrementalContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.Recast);

            var service = new NavBakeService(new CdtNavBakeAlgorithm());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
            Assert.That(ex.Message, Does.Contain("cdt"));
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
            });
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
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
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(first.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile firstTile), Is.True);
            Assert.That(firstTile.TileVersion, Is.EqualTo(context.TileVersion + 1u));

            RuntimeNavMeshRebuildBatch second = queue.ProcessBudget(1);
            Assert.That(second.RebuiltTileCount, Is.EqualTo(1));
            Assert.That(second.FailedEntryCount, Is.EqualTo(0));
            Assert.That(second.PendingTileCount, Is.EqualTo(0));
            Assert.That(second.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(second.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(store.Revision, Is.EqualTo(2u));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);
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
            });
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(-500, -500, 20, 20), includeNeighbors: true), Is.EqualTo(0));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(405, 405, 10, 10), includeNeighbors: true), Is.EqualTo(4));

            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(4);
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
            });
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            RuntimeNavMeshRebuildBatch failed = queue.ProcessBudget(1);

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

        private static NavMeshBakeConfig LoadTempConfig(string root, AgentProfileRegistry profiles)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog);
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
    }
}
