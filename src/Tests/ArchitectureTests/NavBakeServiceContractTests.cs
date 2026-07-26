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
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
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
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmCdt);
            var profiles = CreateAgentProfiles();
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_bake_contract",
                SourceUri = "Core:Maps/nav_bake_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = profiles,
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
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
            var terrain = new FlatGridLogicTerrainField(16, 16, chunkSizeCells: 16);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_grid_contract",
                "Core:Maps/nav_recast_grid_contract.bin",
                terrain,
                config,
                new[] { new NavBakeTileCoord(0, 0) },
                NavBakeAlgorithmKind.Recast);

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
            var terrain = new FlatGridLogicTerrainField(12, 4, chunkSizeCells: chunkSizeCells);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_open_grid_query_contract",
                "Core:Maps/nav_recast_open_grid_query_contract.bin",
                terrain,
                config,
                new[]
                {
                    new NavBakeTileCoord(0, 0),
                    new NavBakeTileCoord(1, 0),
                    new NavBakeTileCoord(2, 0)
                },
                NavBakeAlgorithmKind.Recast);

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
                Assert.That(tile.PortalCount, Is.EqualTo(4));
                Assert.That(tile.ActivePortals.Length, Is.EqualTo(4));

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

            var terrain = new FlatGridLogicTerrainField(9, 9, chunkSizeCells: chunkSizeCells);
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            var obstacles = new NavObstacleSet
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
                        },
                        MinYcm = 0,
                        MaxYcm = 1000
                    }
                }
            };
            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_blocked_hole_query_contract",
                "Core:Maps/nav_recast_blocked_hole_query_contract.bin",
                terrain,
                config,
                new[] { new NavBakeTileCoord(0, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

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
            var store = CreateRuntimeTestStore(context.Config);
            foreach (NavBakeResultEntry entry in bake.Entries)
            {
                store.Replace(entry.Tile);
            }
            AssertPathSegmentsStayInsideNavMesh(path, store, tileSizeCm, tileSizeCm);
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
                targets: new[] { new NavBakeTileCoord(1, 0) },
                algorithm: NavBakeAlgorithmKind.Cdt);
            Assert.DoesNotThrow(() => ok.Validate());
            Assert.That(ok.InputKind, Is.EqualTo(NavBakeInputKind.TriangleSurface));
            Assert.That(ok.RequireTriangleSurface(), Is.SameAs(surface));

            NavBakeContext outOfRange = CreateOfflineContext(
                terrain: null,
                triangleSurface: surface,
                targets: new[] { new NavBakeTileCoord(0, 1) },
                algorithm: NavBakeAlgorithmKind.Cdt);
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
                algorithm: NavBakeAlgorithmKind.Cdt);
            var cdt = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.Cdt,
                NavBakeAdapterCapabilities.OfflineLogicTerrain |
                NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain);
            var recast = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.Recast,
                NavBakeAdapterCapabilities.OfflineLogicTerrain |
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface);
            var service = new NavBakeService(cdt, recast);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("does not support"));
            Assert.That(ex.Message, Does.Contain("triangle-surface"));
            Assert.That(ex.Message, Does.Contain("cdt"));
            Assert.That(cdt.InvokeCount, Is.EqualTo(0));
            Assert.That(recast.InvokeCount, Is.EqualTo(0));
        }

        [Test]
        public void NavBakeService_MissingAdapterFailsFast()
        {
            var context = CreateOfflineContext(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                algorithm: NavBakeAlgorithmKind.LayeredSpan);
            var service = new NavBakeService(new CdtNavBakeAlgorithm());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Bake(context))!;
            Assert.That(ex.Message, Does.Contain("no adapter"));
            Assert.That(ex.Message, Does.Contain(NavBakeNames.AlgorithmLayeredSpan));
        }

        [Test]
        public void RecastAndCdt_DeclareOnlyTriangleSurfaceCapabilities()
        {
            var recast = new RecastNavBakeAlgorithm();
            var cdt = new CdtNavBakeAlgorithm();
            var layeredSpan = new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(CreateDefaultLayeredSpanConfig()));
            NavBakeAdapterCapabilities expected =
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

            Assert.That(recast.Capabilities, Is.EqualTo(expected));
            Assert.That(cdt.Capabilities, Is.EqualTo(expected));
            Assert.That(layeredSpan.Capabilities, Is.EqualTo(expected));
            Assert.That(recast.Capabilities.HasFlag(NavBakeAdapterCapabilities.OfflineLogicTerrain), Is.False);
            Assert.That(cdt.Capabilities.HasFlag(NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain), Is.False);
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_PreservesSelectedAlgorithm()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var context = CreateRuntimeIncrementalContext(terrain, algorithm: NavBakeAlgorithmKind.Recast);
            var fake = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.Recast,
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(fake),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(1);

            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(fake.InvokeCount, Is.EqualTo(1));
            Assert.That(fake.LastInvokedKind, Is.EqualTo(NavBakeAlgorithmKind.Recast));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_RejectsMutableOfflineObstacleSource()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBakeContext context = CreateRuntimeIncrementalContext(
                terrain,
                obstacles: new NavObstacleSet());
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new CdtNavBakeAlgorithm()),
                    context,
                    queryServices,
                    navProfiles))!;

            Assert.That(ex.Message, Does.Contain(nameof(RuntimeNavObstacleSnapshot)));
            Assert.That(ex.Message, Does.Contain("pin immutable obstacle input"));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_TriangleGridDirtyAabbHonorsOriginAndTileSize()
        {
            NavTriangleSurfaceTileIndex surface = CreateTinyTriangleSurfaceIndex(
                originXcm: 1000,
                originZcm: 2000,
                tileWidthCm: 250,
                tileHeightCm: 250,
                tileCountX: 2,
                tileCountZ: 2);
            NavMeshBakeConfig config = CreateBakeConfig(
                NavBakeNames.ModeRuntimeIncremental,
                NavBakeNames.AlgorithmLayeredSpan);
            var runtimeObstacles = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                GroundLayerId);
            var context = new NavBakeContext
            {
                MapId = "nav_runtime_triangle_grid",
                SourceUri = "Core:Maps/nav_runtime_triangle_grid.tris",
                TriangleSurface = surface,
                Obstacles = runtimeObstacles,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 3,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.LayeredSpan,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
            var fake = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.LayeredSpan,
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(fake),
                context,
                queryServices,
                navProfiles);

            // World (1250,2250) is tile-local (250,250) -> tile (1,1); neighbors expand to 2x2.
            Assert.That(
                queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(1250, 2250, 10, 10), includeNeighbors: true),
                Is.EqualTo(4));
            Assert.That(queue.EnqueueDirtyAabb(new Ludots.Core.Mathematics.WorldAabbCm(0, 0, 10, 10), includeNeighbors: true), Is.EqualTo(0));

            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(4);
            Assert.That(batch.FailedEntryCount, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(4));
            Assert.That(batch.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(batch.PublishedTiles[1].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(batch.PublishedTiles[2].Target, Is.EqualTo(new NavBakeTileCoord(0, 1)));
            Assert.That(batch.PublishedTiles[3].Target, Is.EqualTo(new NavBakeTileCoord(1, 1)));
            Assert.That(fake.LastInvokedKind, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(fake.InvokeCount, Is.EqualTo(4));
        }

        [Test]
        public void NavBakeNames_ParseAndFormatLayeredSpanExplicitly()
        {
            Assert.That(
                NavBakeNames.ParseAlgorithm(NavBakeNames.AlgorithmLayeredSpan, "test.algorithm"),
                Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.LayeredSpan), Is.EqualTo(NavBakeNames.AlgorithmLayeredSpan));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.Recast), Is.EqualTo(NavBakeNames.AlgorithmRecast));
            Assert.That(NavBakeNames.FormatAlgorithm(NavBakeAlgorithmKind.Cdt), Is.EqualTo(NavBakeNames.AlgorithmCdt));

            InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(
                () => NavBakeNames.FormatAlgorithm((NavBakeAlgorithmKind)255))!;
            Assert.That(unknown.Message, Does.Contain("Unknown NavBakeAlgorithmKind"));
            Assert.That(unknown.Message, Does.Not.Contain(NavBakeNames.AlgorithmCdt));
            Assert.That(unknown.Message, Does.Not.Contain(NavBakeNames.AlgorithmRecast));
        }

        [Test]
        public void NavMeshBakeConfigLoader_AcceptsLayeredSpanWithoutAdapterClaim()
        {
            string root = CreateTempNavConfig(
                """
                {
                  "mode": "offline",
                  "algorithm": "layered-span",
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

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_ProcessesDirtyTilesByBudgetAndPublishesRevision()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            var context = CreateRuntimeIncrementalContextFromSurface(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
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
            var context = CreateRuntimeIncrementalContextFromSurface(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
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
        public void RuntimeIncrementalNavMeshRebuildQueue_FailedBakeAbortsGenerationWithoutPublish()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            var context = CreateRuntimeIncrementalContextFromSurface(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var baseline = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContextFromSurface(CreateFlatGridTriangleSurfaceIndex(tileCountX: 1, tileCountZ: 1)));
            Assert.That(baseline.FailureCount, Is.EqualTo(0));

            var store = CreateRuntimeTestStore(context.Config);
            uint baselineRevision = store.Replace(baseline.Entries[0].Tile);
            ulong baselineGeneration = store.Generation;
            Assert.That(baselineRevision, Is.EqualTo(1u));
            Assert.That(baselineGeneration, Is.EqualTo(1UL));

            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
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
        public void RuntimeIncrementalNavMeshRebuildQueue_DirtyDuringActiveBatchBecomesLaterGeneration()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 3, tileCountZ: 1);
            var context = CreateRuntimeIncrementalContextFromSurface(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);

            RuntimeNavMeshRebuildBatch first = queue.ProcessBudget(1);
            Assert.That(first.Committed, Is.False);
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(2, 0)), Is.True);
            Assert.That(queue.PendingTileCount, Is.EqualTo(2));
            Assert.That(queue.SealedRemainingCount, Is.EqualTo(1));

            RuntimeNavMeshRebuildBatch second = queue.ProcessBudget(1);
            Assert.That(second.Committed, Is.True);
            Assert.That(second.PublishedTiles.Count, Is.EqualTo(2));
            Assert.That(second.Generation, Is.EqualTo(1UL));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(queue.PendingTileCount, Is.EqualTo(1));
            Assert.That(store.TryGet(new NavTileId(2, 0, 0), out _), Is.False);

            RuntimeNavMeshRebuildBatch third = queue.ProcessBudget(1);
            Assert.That(third.Committed, Is.True);
            Assert.That(third.PublishedTiles.Count, Is.EqualTo(1));
            Assert.That(third.Generation, Is.EqualTo(2UL));
            Assert.That(third.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(2, 0)));
            Assert.That(store.Revision, Is.EqualTo(2u));
            Assert.That(store.Generation, Is.EqualTo(2UL));
            Assert.That(store.TryGet(new NavTileId(2, 0, 0), out NavTile later), Is.True);
            Assert.That(later.TileVersion, Is.EqualTo(context.TileVersion + 2u));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_PinsObstacleSnapshotAcrossTicks()
        {
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            NavMeshBakeConfig config = CreateBakeConfig(
                NavBakeNames.ModeRuntimeIncremental,
                NavBakeNames.AlgorithmCdt);
            var liveObstacles = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                GroundLayerId);
            liveObstacles.BeginCapture();
            int initial = liveObstacles.BeginPrimitive(7, 0, NavObstacleKind.Circle, minYcm: 0, maxYcm: 200);
            liveObstacles.SetCircle(initial, centerXcm: 10, centerZcm: 20, radiusCm: 5);
            liveObstacles.EndCaptureAndSort();

            NavBakeContext context = CreateRuntimeIncrementalContext(terrain, obstacles: liveObstacles);
            var fake = new RecordingFakeNavBakeAlgorithm(
                NavBakeAlgorithmKind.Cdt,
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(fake),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);
            RuntimeNavMeshRebuildBatch first = queue.ProcessBudget(1);
            Assert.That(first.Committed, Is.False);
            Assert.That(fake.ObservedCircleCenterXcm, Is.EqualTo(new[] { 10 }));

            liveObstacles.BeginCapture();
            int changed = liveObstacles.BeginPrimitive(7, 0, NavObstacleKind.Circle, minYcm: 0, maxYcm: 200);
            liveObstacles.SetCircle(changed, centerXcm: 99, centerZcm: 20, radiusCm: 5);
            liveObstacles.EndCaptureAndSort();
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);

            RuntimeNavMeshRebuildBatch second = queue.ProcessBudget(1);
            Assert.That(second.Committed, Is.True);
            Assert.That(fake.ObservedCircleCenterXcm, Is.EqualTo(new[] { 10, 10 }));

            RuntimeNavMeshRebuildBatch third = queue.ProcessBudget(1);
            Assert.That(third.Committed, Is.True);
            Assert.That(fake.ObservedCircleCenterXcm, Is.EqualTo(new[] { 10, 10, 99 }));
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
                            RadiusCm = 500,
                            MinYcm = 0,
                            MaxYcm = 1000
                        }
                    }
                });
            NavBakeResult blocked = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(blockedContext);

            Assert.That(blocked.FailureCount, Is.EqualTo(0));
            Assert.That(blocked.SuccessCount, Is.EqualTo(1));
            Assert.That(blocked.Entries[0].Success, Is.True);
            Assert.That(blocked.Entries[0].Tile, Is.Not.Null);
            Assert.That(blocked.Entries[0].Tile.TriangleCount, Is.EqualTo(0));
            Assert.That(blocked.Entries[0].Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
            Assert.That(blocked.Entries[0].Artifact.Message, Does.Contain("walkable").IgnoreCase);

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
                            RadiusCm = 45,
                            MinYcm = 0,
                            MaxYcm = 1000
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

            var store = CreateRuntimeTestStore(CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt));
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
        public void CdtBake_AllBlockedTerrainAndObstacle_ReturnsValidEmptyTile()
        {
            var blockedTerrain = new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            blockedTerrain.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            NavBakeResult terrainBlocked = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(blockedTerrain));

            AssertValidEmptyBakeEntry(terrainBlocked.Entries[0]);

            NavBakeResult obstacleBlocked = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(
                    new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                    obstacles: new NavObstacleSet
                    {
                        Obstacles =
                        {
                            new NavObstacle
                            {
                                Id = "full-blocker",
                                Enabled = true,
                                Kind = NavObstacleKind.Circle,
                                LayerId = GroundLayerId,
                                Center = new NavPointCm(200, 200),
                                RadiusCm = 500,
                            MinYcm = 0,
                            MaxYcm = 1000
                            }
                        }
                    }));

            AssertValidEmptyBakeEntry(obstacleBlocked.Entries[0]);
        }

        [Test]
        public void RecastBake_AllBlockedTerrainAndObstacle_ReturnsValidEmptyTile()
        {
            var blockedTerrain = new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            blockedTerrain.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            NavBakeResult terrainBlocked = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(
                CreateOfflineContext(blockedTerrain, algorithm: NavBakeAlgorithmKind.Recast));

            AssertValidEmptyBakeEntry(terrainBlocked.Entries[0]);
            Assert.That(terrainBlocked.Entries[0].DetourTileBytes, Is.Empty);

            NavBakeResult obstacleBlocked = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(
                CreateOfflineBakeContextFromTerrain(
                    "nav_recast_empty_obstacle_contract",
                    "Core:Maps/nav_recast_empty_obstacle_contract.bin",
                    new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                    CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                    new[] { new NavBakeTileCoord(0, 0) },
                    NavBakeAlgorithmKind.Recast,
                    new NavObstacleSet
                    {
                        Obstacles =
                        {
                            new NavObstacle
                            {
                                Id = "full-blocker",
                                Enabled = true,
                                Kind = NavObstacleKind.Circle,
                                LayerId = GroundLayerId,
                                Center = new NavPointCm(200, 200),
                                RadiusCm = 500,
                                MinYcm = 0,
                                MaxYcm = 1000
                            }
                        }
                    }));

            AssertValidEmptyBakeEntry(obstacleBlocked.Entries[0]);
            Assert.That(obstacleBlocked.Entries[0].DetourTileBytes, Is.Empty);
        }

        [Test]
        public void ValidEmptyTile_ReplacesPriorTopologyAndBlocksPreciseQuery()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBakeResult open = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(CreateRuntimeIncrementalContext(terrain));
            Assert.That(open.FailureCount, Is.EqualTo(0));
            Assert.That(open.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));

            var store = CreateRuntimeTestStore(CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt));
            store.Replace(open.Entries[0].Tile);
            var query = new NavQueryService(store, layer: 0, areaCosts: null, new NavQueryTileSpace(0, 0, 400, 400));
            Assert.That(query.TryProject(150, 150, out _), Is.True);

            var blockedTerrain = new MutableGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            blockedTerrain.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));
            NavBakeResult empty = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(blockedTerrain));
            AssertValidEmptyBakeEntry(empty.Entries[0]);

            store.Replace(empty.Entries[0].Tile);
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out NavTile current), Is.True);
            Assert.That(current.TriangleCount, Is.EqualTo(0));
            Assert.That(current.VertexCount, Is.EqualTo(0));
            Assert.That(query.TryProject(150, 150, out _), Is.False);

            NavPathResult path = query.TryFindPath(50, 50, 300, 300);
            Assert.That(path.Status, Is.Not.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void NavTileStore_GenerationBatchIsAtomicAndRejectsInvalidBatchesWithoutMutation()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            NavBakeResult bake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContextFromSurface(surface));
            Assert.That(bake.FailureCount, Is.EqualTo(0));
            Assert.That(bake.Entries.Count, Is.EqualTo(1));

            NavBakeResult secondTileBake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContextFromSurface(
                    surface,
                    targets: new[] { new NavBakeTileCoord(1, 0) },
                    tileVersion: 2));
            Assert.That(secondTileBake.FailureCount, Is.EqualTo(0));

            var store = CreateRuntimeTestStore(CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt));
            Assert.That(store.Revision, Is.EqualTo(0u));
            Assert.That(store.Generation, Is.EqualTo(0UL));

            InvalidOperationException zeroGen = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(0UL, new[] { bake.Entries[0].Tile }))!;
            Assert.That(zeroGen.Message, Does.Contain("non-zero"));
            Assert.That(store.Revision, Is.EqualTo(0u));
            Assert.That(store.Generation, Is.EqualTo(0UL));

            uint firstRevision = store.ReplaceGenerationBatch(
                1UL,
                new[] { bake.Entries[0].Tile, secondTileBake.Entries[0].Tile });
            Assert.That(firstRevision, Is.EqualTo(1u));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(store.TryGet(new NavTileId(0, 0, 0), out _), Is.True);
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.True);

            InvalidOperationException stale = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(1UL, new[] { bake.Entries[0].Tile }))!;
            Assert.That(stale.Message, Does.Contain("strictly greater"));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));

            InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(
                    2UL,
                    new[] { bake.Entries[0].Tile, bake.Entries[0].Tile }))!;
            Assert.That(duplicate.Message, Does.Contain("duplicate"));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));

            InvalidOperationException empty = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(2UL, Array.Empty<NavTile>()))!;
            Assert.That(empty.Message, Does.Contain("non-empty"));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
        }

        [Test]
        public void NavTileStore_GenerationOverflowPreflightLeavesTopologyUnchanged()
        {
            NavBakeResult bake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4)));
            Assert.That(bake.FailureCount, Is.EqualTo(0));
            NavTile tile = bake.Entries[0].Tile;

            var store = CreateRuntimeTestStore(CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt));
            store.ReplaceGenerationBatch(ulong.MaxValue, new[] { tile });
            Assert.That(store.Generation, Is.EqualTo(ulong.MaxValue));
            uint revisionAtMax = store.Revision;
            Assert.That(store.TryGet(tile.TileId, out NavTile loaded), Is.True);
            Assert.That(loaded.TriangleCount, Is.EqualTo(tile.TriangleCount));

            InvalidOperationException replaceOverflow = Assert.Throws<InvalidOperationException>(
                () => store.Replace(tile))!;
            Assert.That(replaceOverflow.Message, Does.Contain("Generation overflow"));
            Assert.That(store.Generation, Is.EqualTo(ulong.MaxValue));
            Assert.That(store.Revision, Is.EqualTo(revisionAtMax));
            Assert.That(store.TryGet(tile.TileId, out NavTile afterReplace), Is.True);
            Assert.That(afterReplace.TriangleCount, Is.EqualTo(tile.TriangleCount));

            InvalidOperationException unloadOverflow = Assert.Throws<InvalidOperationException>(
                () => store.Unload(tile.TileId))!;
            Assert.That(unloadOverflow.Message, Does.Contain("Generation overflow"));
            Assert.That(store.Generation, Is.EqualTo(ulong.MaxValue));
            Assert.That(store.Revision, Is.EqualTo(revisionAtMax));
            Assert.That(store.TryGet(tile.TileId, out _), Is.True);

            InvalidOperationException clearOverflow = Assert.Throws<InvalidOperationException>(
                () => store.Clear())!;
            Assert.That(clearOverflow.Message, Does.Contain("Generation overflow"));
            Assert.That(store.Generation, Is.EqualTo(ulong.MaxValue));
            Assert.That(store.Revision, Is.EqualTo(revisionAtMax));
            Assert.That(store.TryGet(tile.TileId, out _), Is.True);
        }

        [Test]
        public void NavTileStore_AtomicMultiStoreCommitUpdatesBothOrNeither()
        {
            NavBakeResult layer0Bake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4)));
            Assert.That(layer0Bake.FailureCount, Is.EqualTo(0));

            NavBakeResult layer1Bake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContextWithLayers(
                    new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 },
                    new NavLayerConfig { Id = "Bridge", Layer = 1 }));
            NavBakeResultEntry layer1Entry = null;
            for (int i = 0; i < layer1Bake.Entries.Count; i++)
            {
                if (layer1Bake.Entries[i].Layer == 1)
                {
                    layer1Entry = layer1Bake.Entries[i];
                    break;
                }
            }

            Assert.That(layer1Entry, Is.Not.Null);
            Assert.That(layer1Entry.Tile.TileId.Layer, Is.EqualTo(1));

            NavMeshBakeConfig storeConfig = CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt);
            var storeA = CreateRuntimeTestStore(storeConfig);
            var storeB = CreateRuntimeTestStore(storeConfig);

            uint[] revisions = NavTileStore.ReplaceGenerationBatchesAtomically(
                new[] { storeA, storeB },
                new IReadOnlyList<NavTile>[]
                {
                    new[] { layer0Bake.Entries[0].Tile },
                    new[] { layer1Entry.Tile }
                },
                out ulong generation);

            Assert.That(generation, Is.EqualTo(1UL));
            Assert.That(revisions, Is.EqualTo(new uint[] { 1u, 1u }));
            Assert.That(storeA.Generation, Is.EqualTo(1UL));
            Assert.That(storeB.Generation, Is.EqualTo(1UL));
            Assert.That(storeA.TryGet(new NavTileId(0, 0, 0), out _), Is.True);
            Assert.That(storeB.TryGet(new NavTileId(0, 0, 1), out _), Is.True);

            NavTile priorA = storeA.SnapshotLoadedTiles()[0];
            NavTile priorB = storeB.SnapshotLoadedTiles()[0];
            uint revisionA = storeA.Revision;
            uint revisionB = storeB.Revision;

            InvalidOperationException invalidSecond = Assert.Throws<InvalidOperationException>(
                () => NavTileStore.ReplaceGenerationBatchesAtomically(
                    new[] { storeA, storeB },
                    new IReadOnlyList<NavTile>[]
                    {
                        new[] { layer0Bake.Entries[0].Tile },
                        Array.Empty<NavTile>()
                    },
                    out _))!;
            Assert.That(invalidSecond.Message, Does.Contain("non-empty"));
            Assert.That(storeA.Generation, Is.EqualTo(1UL));
            Assert.That(storeB.Generation, Is.EqualTo(1UL));
            Assert.That(storeA.Revision, Is.EqualTo(revisionA));
            Assert.That(storeB.Revision, Is.EqualTo(revisionB));
            Assert.That(storeA.TryGet(priorA.TileId, out NavTile stillA), Is.True);
            Assert.That(ReferenceEquals(stillA, priorA), Is.True);
            Assert.That(storeB.TryGet(priorB.TileId, out NavTile stillB), Is.True);
            Assert.That(ReferenceEquals(stillB, priorB), Is.True);
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_TwoStoreCommitSharesGenerationAndOrdering()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            NavBakeContext context = CreateRuntimeIncrementalContextFromSurface(
                surface,
                layers: new[]
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 },
                    new NavLayerConfig { Id = "Bridge", Layer = 1 }
                });
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var groundStore = CreateRuntimeTestStore(context.Config);
            var bridgeStore = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = groundStore,
                [new NavQueryServiceKey(layer: 1, profile: 0)] = bridgeStore
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);

            RuntimeNavMeshRebuildBatch batch = queue.ProcessBudget(2);
            Assert.That(batch.Committed, Is.True);
            Assert.That(batch.Generation, Is.EqualTo(1UL));
            Assert.That(batch.PublishedTiles.Count, Is.EqualTo(4));
            Assert.That(groundStore.Generation, Is.EqualTo(1UL));
            Assert.That(bridgeStore.Generation, Is.EqualTo(1UL));
            Assert.That(batch.PublishedTiles[0].Layer, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles[0].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(batch.PublishedTiles[1].Layer, Is.EqualTo(0));
            Assert.That(batch.PublishedTiles[1].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(batch.PublishedTiles[2].Layer, Is.EqualTo(1));
            Assert.That(batch.PublishedTiles[2].Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(batch.PublishedTiles[3].Layer, Is.EqualTo(1));
            Assert.That(batch.PublishedTiles[3].Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_RejectsDuplicateStoreInstanceAcrossKeys()
        {
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBakeContext context = CreateRuntimeIncrementalContextWithLayers(
                terrain,
                new NavLayerConfig { Id = GroundLayerId, Layer = 0 },
                new NavLayerConfig { Id = "Bridge", Layer = 1 });
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var shared = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = shared,
                [new NavQueryServiceKey(layer: 1, profile: 0)] = shared
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new CdtNavBakeAlgorithm()),
                    context,
                    queryServices,
                    navProfiles))!;
            Assert.That(ex.Message, Does.Contain("duplicate NavTileStore"));
        }

        [Test]
        public void NavTileLayerRewriter_NonZeroLayerRecomputesCanonicalChecksum()
        {
            NavBakeResult bake = new NavBakeService(new CdtNavBakeAlgorithm()).Bake(
                CreateRuntimeIncrementalContext(new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4)));
            Assert.That(bake.FailureCount, Is.EqualTo(0));
            Assert.That(bake.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));

            NavTile rewritten = NavTileLayerRewriter.WithLayer(bake.Entries[0].Tile, layer: 3);
            Assert.That(rewritten.TileId.Layer, Is.EqualTo(3));
            Assert.That(rewritten.Checksum, Is.Not.EqualTo(0UL));
            Assert.That(rewritten.Checksum, Is.Not.EqualTo(bake.Entries[0].Tile.Checksum));

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, rewritten);
            ms.Position = 0;
            NavTile roundTrip = NavTileBinary.Read(ms);
            Assert.That(roundTrip.Checksum, Is.EqualTo(rewritten.Checksum));
            Assert.That(roundTrip.TileId.Layer, Is.EqualTo(3));

            NavTile empty = NavValidEmptyTile.Create(
                new NavTileId(0, 0, 0),
                tileVersion: 1,
                buildConfigHash: 9,
                originXcm: 0,
                originZcm: 0);
            NavTile emptyRewritten = NavTileLayerRewriter.WithLayer(empty, layer: 2);
            Assert.That(emptyRewritten.TileId.Layer, Is.EqualTo(2));
            using var emptyMs = new MemoryStream();
            NavTileBinary.Write(emptyMs, emptyRewritten);
            emptyMs.Position = 0;
            NavTile emptyRoundTrip = NavTileBinary.Read(emptyMs);
            Assert.That(emptyRoundTrip.Checksum, Is.EqualTo(emptyRewritten.Checksum));
            Assert.That(emptyRoundTrip.TriangleCount, Is.EqualTo(0));
        }

        [Test]
        public void RecastBake_TargetFullyBlockedWithOpenNeighbor_ReturnsValidEmptyTarget()
        {
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "cover-tile-0",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        Points =
                        {
                            new NavPointCm(-50, -50),
                            new NavPointCm(450, -50),
                            new NavPointCm(450, 450),
                            new NavPointCm(-50, 450)
                        },
                        MinYcm = 0,
                        MaxYcm = 1000
                    }
                }
            };

            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_empty_neighbor_contract",
                "Core:Maps/nav_recast_empty_neighbor_contract.bin",
                new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4),
                CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

            NavBakeResult result = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(result.Entries.Count, Is.EqualTo(2));

            NavBakeResultEntry blocked = result.Entries[0];
            NavBakeResultEntry open = result.Entries[1];
            Assert.That(blocked.Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(open.Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));

            AssertValidEmptyBakeEntry(blocked);
            Assert.That(blocked.DetourTileBytes, Is.Empty);
            Assert.That(open.Success, Is.True);
            Assert.That(open.Tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(open.DetourTileBytes, Is.Not.Empty);
        }

        [Test]
        public void RecastBake_CircleObstacle_CreatesDetourHoleWithoutErasingTile()
        {
            const int chunkSizeCells = 9;
            const int tileSizeCm = chunkSizeCells * SpatialScaleDefaults.CellCm;
            const int centerXcm = 450;
            const int centerZcm = 450;
            const int radiusCm = 120;

            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "center-circle",
                        Enabled = true,
                        Kind = NavObstacleKind.Circle,
                        LayerId = GroundLayerId,
                        Center = new NavPointCm(centerXcm, centerZcm),
                        RadiusCm = radiusCm,
                        MinYcm = 0,
                        MaxYcm = 1000
                    }
                }
            };

            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_circle_hole_contract",
                "Core:Maps/nav_recast_circle_hole_contract.bin",
                new FlatGridLogicTerrainField(9, 9, chunkSizeCells: chunkSizeCells),
                CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                new[] { new NavBakeTileCoord(0, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            Assert.That(bake.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));
            Assert.That(bake.Entries[0].DetourTileBytes, Is.Not.Empty);

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourTileBytes(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 850,
                goalZcm: 850,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            AssertPathSegmentsDoNotEnterCircle(path, centerXcm, centerZcm, radiusCm);
            var store = CreateRuntimeTestStore(context.Config);
            store.Replace(bake.Entries[0].Tile);
            AssertPathSegmentsStayInsideNavMesh(path, store, tileSizeCm, tileSizeCm);
        }

        [Test]
        public void RecastBake_ConcavePolygonObstacle_DecomposesAndCreatesDetourHole()
        {
            // Concave L-shape: outer box 300..600 with a bite taken from the +X/+Z corner.
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "concave-l",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        Points =
                        {
                            new NavPointCm(300, 300),
                            new NavPointCm(600, 300),
                            new NavPointCm(600, 450),
                            new NavPointCm(450, 450),
                            new NavPointCm(450, 600),
                            new NavPointCm(300, 600)
                        },
                        MinYcm = 0,
                        MaxYcm = 1000
                    }
                }
            };

            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_concave_poly_hole_contract",
                "Core:Maps/nav_recast_concave_poly_hole_contract.bin",
                new FlatGridLogicTerrainField(9, 9, chunkSizeCells: 9),
                CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                new[] { new NavBakeTileCoord(0, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            Assert.That(bake.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourTileBytes(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 850,
                goalZcm: 850,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            AssertPathSegmentsDoNotEnterAabb(path, 300, 300, 450, 600);
            AssertPathSegmentsDoNotEnterAabb(path, 300, 300, 600, 450);
        }

        [Test]
        public void RecastBake_VerticalNonOverlapObstacle_LeavesWalkableDeck()
        {
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "high-deck-blocker",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        Points =
                        {
                            new NavPointCm(200, 200),
                            new NavPointCm(700, 200),
                            new NavPointCm(700, 700),
                            new NavPointCm(200, 700)
                        },
                        // Agent height is 180cm on ground at Y=0; [500,1000) does not overlap [0,180).
                        MinYcm = 500,
                        MaxYcm = 1000
                    }
                }
            };

            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_vertical_nonoverlap_contract",
                "Core:Maps/nav_recast_vertical_nonoverlap_contract.bin",
                new FlatGridLogicTerrainField(9, 9, chunkSizeCells: 9),
                CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                new[] { new NavBakeTileCoord(0, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

            NavBakeResult bake = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(bake.FailureCount, Is.EqualTo(0), bake.Entries[0].Artifact.Message);
            Assert.That(bake.Entries[0].Tile.TriangleCount, Is.GreaterThan(0));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                CollectDetourTileBytes(bake),
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 850,
                goalZcm: 850,
                maxPortals: 256);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void RecastBake_PartialBoxObstacle_KeepsOpenNeighborWalkable()
        {
            // Box overlaps both tiles but leaves a physically valid corridor around it for the
            // default 30 cm-radius agent; neither source tile may be erased wholesale.
            var obstacles = new NavObstacleSet
            {
                Obstacles =
                {
                    new NavObstacle
                    {
                        Id = "partial-overhang",
                        Enabled = true,
                        Kind = NavObstacleKind.Polygon,
                        LayerId = GroundLayerId,
                        Points =
                        {
                            new NavPointCm(100, 100),
                            new NavPointCm(450, 100),
                            new NavPointCm(450, 300),
                            new NavPointCm(100, 300)
                        },
                        MinYcm = 0,
                        MaxYcm = 1000
                    }
                }
            };

            var context = CreateOfflineBakeContextFromTerrain(
                "nav_recast_partial_neighbor_contract",
                "Core:Maps/nav_recast_partial_neighbor_contract.bin",
                new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4),
                CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast),
                new[] { new NavBakeTileCoord(0, 0), new NavBakeTileCoord(1, 0) },
                NavBakeAlgorithmKind.Recast,
                obstacles);

            NavBakeResult result = new NavBakeService(new RecastNavBakeAlgorithm()).Bake(context);
            Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries[0].Artifact.Message);
            Assert.That(result.Entries.Count, Is.EqualTo(2));

            NavBakeResultEntry left = result.Entries[0];
            NavBakeResultEntry right = result.Entries[1];
            Assert.That(left.Target, Is.EqualTo(new NavBakeTileCoord(0, 0)));
            Assert.That(right.Target, Is.EqualTo(new NavBakeTileCoord(1, 0)));
            Assert.That(left.Tile.TriangleCount, Is.GreaterThan(0), "Partial cover must leave a hole, not erase the whole target tile.");
            Assert.That(right.Tile.TriangleCount, Is.GreaterThan(0), "Open neighbor must remain non-empty.");
            Assert.That(right.DetourTileBytes, Is.Not.Empty);
        }

        [Test]
        public void RuntimeIncrementalNavMeshRebuildQueue_BudgetSplitGenerationIsQueryStable()
        {
            NavTriangleSurfaceTileIndex surface = CreateFlatGridTriangleSurfaceIndex(tileCountX: 2, tileCountZ: 1);
            var context = CreateRuntimeIncrementalContextFromSurface(surface);
            var navProfiles = new NavMeshProfileRegistry(context.Config, context.AgentProfiles);
            var store = CreateRuntimeTestStore(context.Config);
            var queryServices = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>{
                [new NavQueryServiceKey(layer: 0, profile: 0)] = store
            }, NavQueryTileSpace.FromGrid(context.RequireTriangleSurface().Grid));
            var queue = new RuntimeIncrementalNavMeshRebuildQueue(
                new NavBakeService(new CdtNavBakeAlgorithm()),
                context,
                queryServices,
                navProfiles);
            var query = new NavQueryService(store, layer: 0, areaCosts: null, new NavQueryTileSpace(0, 0, 400, 400));

            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(0, 0)), Is.True);
            Assert.That(queue.EnqueueDirtyTile(new NavBakeTileCoord(1, 0)), Is.True);

            RuntimeNavMeshRebuildBatch first = queue.ProcessBudget(1);
            Assert.That(first.PublishedTiles.Count, Is.EqualTo(0));
            Assert.That(store.Revision, Is.EqualTo(0u));
            Assert.That(store.Generation, Is.EqualTo(0UL));
            Assert.That(store.SnapshotLoadedTiles(), Is.Empty);
            Assert.That(query.TryProject(50, 50, out _), Is.False);

            RuntimeNavMeshRebuildBatch second = queue.ProcessBudget(1);
            Assert.That(second.Committed, Is.True);
            Assert.That(second.PublishedTiles.Count, Is.EqualTo(2));
            Assert.That(store.Revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(store.SnapshotLoadedTiles().Length, Is.EqualTo(2));
            Assert.That(query.TryProject(50, 50, out _), Is.True);
            Assert.That(query.TryProject(450, 50, out _), Is.True);
        }

        [Test]
        public void NavBakeEstimator_UsesRealContextAndReportsBudgetFromTargetsLayersProfiles()
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            config.Profiles.Add(new NavMeshAgentProfileConfig { Id = "Large", MaxClimbCm = 75, MaxSlopeDeg = 30 });
            config.Layers.Add(new NavLayerConfig { Id = "Bridge", Layer = 1 });
            var terrain = new FlatGridLogicTerrainField(8, 4, chunkSizeCells: 4);
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_estimate_contract",
                SourceUri = "Core:Maps/nav_estimate_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
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
                BuildConfig = buildConfig,
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
            Assert.That(large.RecastCellSizeCm, Is.EqualTo(10f).Within(0.0001f), "Recast raster is config-owned, not radius-derived.");
            Assert.That(large.RecastCellHeightCm, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(large.RecastColumnsPerAxis, Is.EqualTo(40));
        }

        [Test]
        public void NavBakeEstimator_RejectsInvalidProfileSlopeInsteadOfClamping()
        {
            var config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            config.Profiles[0].MaxSlopeDeg = 90f;
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            var context = new NavBakeContext
            {
                MapId = "nav_estimate_invalid_slope",
                SourceUri = "Core:Maps/nav_estimate_invalid_slope.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
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
                string navigationDir = Path.Combine(repoRoot, "assets", "Configs", "Navigation");
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
                TriangleSurface = CreateTinyTriangleSurfaceIndex(),
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

            Assert.That(result.Success, Is.True);
            Assert.That(result.Tile, Is.Not.Null);
            Assert.That(result.Tile.TriangleCount, Is.EqualTo(0));
            Assert.That(result.Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
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
            Assert.That(missingObstacles.Message, Does.Contain("INavObstacleSource"));

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

        private static void AssertPathSegmentsDoNotEnterCircle(
            NavPathResult path,
            int centerXcm,
            int centerZcm,
            int radiusCm)
        {
            Assert.That(radiusCm, Is.GreaterThan(0));
            long radiusSq = (long)radiusCm * radiusCm;
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
                    long ox = (long)Math.Round(x) - centerXcm;
                    long oz = (long)Math.Round(z) - centerZcm;
                    bool inside = (ox * ox) + (oz * oz) < radiusSq;
                    Assert.That(
                        inside,
                        Is.False,
                        $"Path segment {i} enters the blocked circle interior near ({x:0.##},{z:0.##}).");
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

        private static NavBakeContext CreateOfflineContext(
            LogicTerrainField? terrain = null,
            NavTriangleSurfaceTileIndex? triangleSurface = null,
            IReadOnlyList<NavBakeTileCoord>? targets = null,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.Recast)
        {
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.FormatAlgorithm(algorithm));
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            if (terrain != null && triangleSurface == null)
            {
                triangleSurface = LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, buildConfig);
            }

            return new NavBakeContext
            {
                MapId = "nav_bake_input_union_contract",
                SourceUri = "Core:Maps/nav_bake_input_union_contract.vtxm",
                TriangleSurface = triangleSurface,
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets ?? new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavBakeContext CreateOfflineBakeContextFromTerrain(
            string mapId,
            string sourceUri,
            LogicTerrainField terrain,
            NavMeshBakeConfig config,
            IReadOnlyList<NavBakeTileCoord> targets,
            NavBakeAlgorithmKind algorithm,
            INavObstacleSource? obstacles = null,
            uint tileVersion = 1,
            NavBakeExecutionOptions? execution = null)
        {
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            return new NavBakeContext
            {
                MapId = mapId,
                SourceUri = sourceUri,
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = obstacles ?? new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = buildConfig,
                TileVersion = tileVersion,
                Mode = NavBakeMode.Offline,
                Algorithm = algorithm,
                Execution = execution ?? new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavTriangleSurfaceTileIndex CompileTriangleSurface(
            LogicTerrainField terrain,
            NavMeshBakeConfig config,
            NavBuildConfig buildConfig)
            => LogicTerrainTriangleSurfaceCompiler.Compile(terrain, config, buildConfig);

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

        private static NavBakeContext CreateRuntimeIncrementalContextFromSurface(
            NavTriangleSurfaceTileIndex surface,
            NavBakeAlgorithmKind algorithm = NavBakeAlgorithmKind.Cdt,
            INavObstacleSource? obstacles = null,
            IReadOnlyList<NavBakeTileCoord>? targets = null,
            uint tileVersion = 11,
            IReadOnlyList<NavLayerConfig>? layers = null)
        {
            NavMeshBakeConfig config = CreateBakeConfig(
                NavBakeNames.ModeRuntimeIncremental,
                NavBakeNames.FormatAlgorithm(algorithm));
            if (layers != null)
            {
                config.Layers.Clear();
                for (int i = 0; i < layers.Count; i++)
                {
                    config.Layers.Add(layers[i] ?? throw new ArgumentNullException(nameof(layers)));
                }
            }

            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            INavObstacleSource obstacleSource = obstacles ?? new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                config.Layers[0].Id);
            return new NavBakeContext
            {
                MapId = "nav_runtime_incremental_surface_contract",
                SourceUri = "Core:Maps/nav_runtime_incremental_surface_contract.tris",
                TriangleSurface = surface,
                Obstacles = obstacleSource,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets ?? new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
                TileVersion = tileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
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
            int maxX = checked(originXcm + tileWidthCm * tileCountX - 1);
            int maxZ = checked(originZcm + tileHeightCm * tileCountZ - 1);
            int x0 = originXcm + 1;
            int z0 = originZcm + 1;
            int x1 = Math.Min(originXcm + 10, maxX);
            int z1 = Math.Min(originZcm + 10, maxZ);
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { x0, x1, x0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { z0, z0, z1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate });
            var grid = new NavTriangleSurfaceTileGrid(
                originXcm,
                originZcm,
                tileWidthCm,
                tileHeightCm,
                tileCountX,
                tileCountZ,
                haloPaddingCm);
            return NavTriangleSurfaceTileIndex.Build(surface, grid);
        }

        private sealed class RecordingFakeNavBakeAlgorithm : INavBakeAlgorithm
        {
            public RecordingFakeNavBakeAlgorithm(NavBakeAlgorithmKind kind, NavBakeAdapterCapabilities capabilities)
            {
                Kind = kind;
                Capabilities = capabilities;
            }

            public NavBakeAlgorithmKind Kind { get; }

            public NavBakeAdapterCapabilities Capabilities { get; }

            public NavBakeAlgorithmKind? LastInvokedKind { get; private set; }

            public int InvokeCount { get; private set; }

            public List<int> ObservedCircleCenterXcm { get; } = new List<int>();

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
                LastInvokedKind = Kind;
                InvokeCount++;
                int observedCenterXcm = int.MinValue;
                for (int i = 0; i < context.Obstacles.ObstacleCount; i++)
                {
                    if (!context.Obstacles.IsEnabled(i) ||
                        context.Obstacles.GetKind(i) != NavObstacleKind.Circle)
                    {
                        continue;
                    }

                    context.Obstacles.GetCircle(i, out observedCenterXcm, out _, out _);
                    break;
                }

                ObservedCircleCenterXcm.Add(observedCenterXcm);
                tile = NavValidEmptyTile.Create(
                    new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                    context.TileVersion,
                    buildConfigHash: 1,
                    originXcm: 0,
                    originZcm: 0);
                detourTileBytes = Array.Empty<byte>();
                artifact = NavValidEmptyTile.CreateSuccessArtifact(tile, "fake-ok");
                return true;
            }
        }

        private sealed class SelectiveFailNavBakeAlgorithm : INavBakeAlgorithm
        {
            private readonly NavBakeTileCoord _failTarget;
            private readonly CdtNavBakeAlgorithm _ok = new CdtNavBakeAlgorithm();

            public SelectiveFailNavBakeAlgorithm(NavBakeTileCoord failTarget)
            {
                _failTarget = failTarget;
            }

            public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Cdt;

            public NavBakeAdapterCapabilities Capabilities =>
                NavBakeAdapterCapabilities.OfflineTriangleSurface |
                NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

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

        private static void AssertValidEmptyBakeEntry(NavBakeResultEntry entry)
        {
            Assert.That(entry.Success, Is.True);
            Assert.That(entry.Tile, Is.Not.Null);
            Assert.That(entry.Tile.VertexCount, Is.EqualTo(0));
            Assert.That(entry.Tile.TriangleCount, Is.EqualTo(0));
            Assert.That(entry.Tile.TriAreaIds, Is.Empty);
            Assert.That(entry.Tile.N0, Is.Empty);
            Assert.That(entry.Tile.N1, Is.Empty);
            Assert.That(entry.Tile.N2, Is.Empty);
            Assert.That(entry.Tile.Portals, Is.Empty);
            Assert.That(entry.Artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
            Assert.That(entry.Artifact.VertexCount, Is.EqualTo(0));
            Assert.That(entry.Artifact.TriangleCount, Is.EqualTo(0));
            Assert.That(entry.Artifact.PortalCount, Is.EqualTo(0));
            Assert.That(entry.Tile.Checksum, Is.Not.EqualTo(0UL));

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, entry.Tile);
            ms.Position = 0;
            NavTile roundTrip = NavTileBinary.Read(ms);
            Assert.That(roundTrip.Checksum, Is.EqualTo(entry.Tile.Checksum));
            Assert.That(roundTrip.TriangleCount, Is.EqualTo(0));
        }

        private static NavTileStore CreateRuntimeTestStore(NavMeshBakeConfig config)
        {
            return new NavTileStore(
                _ => throw new InvalidOperationException("Runtime incremental test publishes tiles before disk load."),
                config.RuntimeIncremental);
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
                LayeredSpan = CreateDefaultLayeredSpanConfig(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 100 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }

        private static NavLayeredSpanConfig CreateDefaultLayeredSpanConfig()
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
            INavObstacleSource? obstacles = null,
            IReadOnlyList<NavBakeTileCoord>? targets = null,
            uint tileVersion = 11)
        {
            NavMeshBakeConfig config = CreateBakeConfig(
                NavBakeNames.ModeRuntimeIncremental,
                NavBakeNames.FormatAlgorithm(algorithm));
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            INavObstacleSource obstacleSource = obstacles ?? new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                GroundLayerId);
            return new NavBakeContext
            {
                MapId = "nav_runtime_incremental_contract",
                SourceUri = "Core:Maps/nav_runtime_incremental_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = obstacleSource,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets ?? new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
                TileVersion = tileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavBakeContext CreateRuntimeIncrementalContextWithLayers(
            LogicTerrainField terrain,
            params NavLayerConfig[] layers)
        {
            if (layers == null || layers.Length == 0)
            {
                throw new ArgumentException("At least one layer is required.", nameof(layers));
            }

            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeRuntimeIncremental, NavBakeNames.AlgorithmCdt);
            config.Layers.Clear();
            for (int i = 0; i < layers.Length; i++)
            {
                config.Layers.Add(layers[i] ?? throw new ArgumentNullException(nameof(layers)));
            }

            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            var runtimeObstacles = new RuntimeNavObstacleSnapshot(
                config.RuntimeIncremental.ObstaclePrimitiveCapacity,
                config.RuntimeIncremental.PolygonVertexCapacity,
                config.Layers[0].Id);

            return new NavBakeContext
            {
                MapId = "nav_runtime_incremental_layers_contract",
                SourceUri = "Core:Maps/nav_runtime_incremental_layers_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = runtimeObstacles,
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = buildConfig,
                TileVersion = 11,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.Cdt,
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

            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            return new NavBakeContext
            {
                MapId = "nav_estimate_budget_contract",
                SourceUri = "Core:Maps/nav_estimate_budget_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = buildConfig,
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
            NavMeshBakeConfig config = CreateBakeConfig(NavBakeNames.ModeOffline, NavBakeNames.AlgorithmRecast);
            NavBuildConfig buildConfig = new NavBuildConfig(1f, 0.6f, 1);
            return new NavBakeContext
            {
                MapId = "nav_estimate_hash_contract",
                SourceUri = "Core:Maps/nav_estimate_hash_contract.vtxm",
                TriangleSurface = CompileTriangleSurface(terrain, config, buildConfig),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = targets,
                BuildConfig = buildConfig,
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
    }
}
