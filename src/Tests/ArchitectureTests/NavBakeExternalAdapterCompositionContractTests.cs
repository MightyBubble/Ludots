using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavBakeExternalAdapterCompositionContractTests
    {
        [Test]
        public void Catalog_ComposesCoreThenExternalInDeterministicKindOrder()
        {
            NavMeshBakeConfig config = CreateLayeredConfig();
            INavBakeAlgorithm[] composed = NavBakeAlgorithmCatalog.Compose(
                new CdtNavBakeAlgorithm(),
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                new INavBakeAlgorithm[] { new RecastNavBakeAlgorithm() });

            Assert.That(
                NavBakeAlgorithmCatalog.ToOrderedKinds(composed),
                Is.EqualTo(new[]
                {
                    NavBakeAlgorithmKind.Cdt,
                    NavBakeAlgorithmKind.LayeredSpan,
                    NavBakeAlgorithmKind.Recast
                }));
        }

        [Test]
        public void Catalog_DuplicateExternalKind_FailsFast()
        {
            NavMeshBakeConfig config = CreateLayeredConfig();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                NavBakeAlgorithmCatalog.Compose(
                    new CdtNavBakeAlgorithm(),
                    new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                    new INavBakeAlgorithm[]
                    {
                        new RecastNavBakeAlgorithm(),
                        new RecastNavBakeAlgorithm()
                    }))!;

            Assert.That(ex.Message, Does.Contain("duplicate").IgnoreCase);
            Assert.That(ex.Message, Does.Contain("Recast"));
        }

        [Test]
        public void Catalog_ExternalConflictingWithCoreKind_FailsFast()
        {
            NavMeshBakeConfig config = CreateLayeredConfig();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                NavBakeAlgorithmCatalog.Compose(
                    new CdtNavBakeAlgorithm(),
                    new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                    new INavBakeAlgorithm[] { new CdtNavBakeAlgorithm() }))!;

            Assert.That(ex.Message, Does.Contain("duplicate").IgnoreCase);
        }

        [Test]
        public void GameEngine_RegisterExternalRecast_AllowsSelectingAllThreeAdapters()
        {
            NavMeshBakeConfig config = CreateLayeredConfig();
            var engine = new GameEngine();
            engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());

            INavBakeAlgorithm[] composed = NavBakeAlgorithmCatalog.Compose(
                new CdtNavBakeAlgorithm(),
                new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                engine.ExternalNavBakeAdapters);
            var service = new NavBakeService(composed);

            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.Recast), Is.True);
            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.Cdt), Is.True);
            Assert.That(service.HasAdapter(NavBakeAlgorithmKind.LayeredSpan), Is.True);
            Assert.That(
                service.RegisteredKinds,
                Is.EqualTo(new[]
                {
                    NavBakeAlgorithmKind.Recast,
                    NavBakeAlgorithmKind.Cdt,
                    NavBakeAlgorithmKind.LayeredSpan
                }));
        }

        [Test]
        public void GameEngine_MissingRecast_SelectedAlgorithmFailsExplicitly()
        {
            NavMeshBakeConfig config = CreateLayeredConfig();
            var service = new NavBakeService(
                NavBakeAlgorithmCatalog.Compose(
                    new CdtNavBakeAlgorithm(),
                    new LayeredSpanNavBakeAlgorithm(new LayeredSpanScratchPool(config.LayeredSpan)),
                    externalAdapters: null));

            var context = new NavBakeContext
            {
                MapId = "nav_missing_recast",
                SourceUri = "Core:Maps/nav_missing_recast.vtxm",
                TriangleSurface = CreateTinySurfaceIndex(),
                Obstacles = new NavObstacleSet(),
                Config = config,
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
                TileVersion = 1,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.EnsureSupports(context))!;
            Assert.That(ex.Message, Does.Contain("no adapter").IgnoreCase);
            Assert.That(ex.Message, Does.Contain("recast").IgnoreCase);
        }

        [Test]
        public void GameEngine_DuplicateExternalRegistration_FailsFast()
        {
            var engine = new GameEngine();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                engine.RegisterExternalNavBakeAdapters(
                    new RecastNavBakeAlgorithm(),
                    new RecastNavBakeAlgorithm()))!;
            Assert.That(ex.Message, Does.Contain("duplicate").IgnoreCase);
        }

        [Test]
        public void GameEngine_SecondRegistration_FailsFast()
        {
            var engine = new GameEngine();
            engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm()))!;
            Assert.That(ex.Message, Does.Contain("locked").IgnoreCase);
        }

        [Test]
        public void StartupOrder_RegisterRecastBeforeInitialize_AllowsRuntimeIncrementalRecast()
        {
            // Mirrors GameBootstrapper: new GameEngine -> RegisterExternal -> InitializeWithConfigPipeline
            // -> LoadNav with algorithm=recast / mode=runtime-incremental.
            string repoRoot = FindRepoRoot();
            string mapId = "nav_bootstrap_external_recast_startup_order";
            string tempAssetsRoot = CreateTempAssetsRootWithNavTiles(repoRoot, mapId);

            try
            {
                RewriteTempNavmeshMode(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmRecast);

                var engine = new GameEngine();
                engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);

                var vfs = (VirtualFileSystem)engine.VFS;
                vfs.Unmount("Core");
                vfs.Mount("Core", tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(engine, new FlatGridLogicTerrainField(
                        SpatialScaleDefaults.TerrainChunkCells,
                        SpatialScaleDefaults.TerrainChunkCells,
                        chunkSizeCells: SpatialScaleDefaults.TerrainChunkCells));

                engine.LoadNavForMapForTests(
                    mapId,
                    new MapConfig
                    {
                        Id = mapId,
                        Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                    });

                NavBakeService bakeService = engine.GetService(CoreServiceKeys.NavBakeService);
                Assert.That(bakeService, Is.Not.Null);
                Assert.That(bakeService.HasAdapter(NavBakeAlgorithmKind.Recast), Is.True);
                Assert.That(engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue), Is.Not.Null);
                Assert.That(
                    engine.GetService(CoreServiceKeys.NavMeshBakeConfig).ParsedAlgorithm,
                    Is.EqualTo(NavBakeAlgorithmKind.Recast));
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        [Test]
        public void StartupOrder_MissingRecast_RuntimeIncrementalRecastFailsFast()
        {
            string repoRoot = FindRepoRoot();
            string mapId = "nav_bootstrap_missing_recast_startup_order";
            string tempAssetsRoot = CreateTempAssetsRootWithNavTiles(repoRoot, mapId);

            try
            {
                RewriteTempNavmeshMode(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmRecast);

                var engine = new GameEngine();
                // Intentionally no RegisterExternalNavBakeAdapters - Core owns only CDT+LayeredSpan.
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);

                var vfs = (VirtualFileSystem)engine.VFS;
                vfs.Unmount("Core");
                vfs.Mount("Core", tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(engine, new FlatGridLogicTerrainField(
                        SpatialScaleDefaults.TerrainChunkCells,
                        SpatialScaleDefaults.TerrainChunkCells,
                        chunkSizeCells: SpatialScaleDefaults.TerrainChunkCells));

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    engine.LoadNavForMapForTests(
                        mapId,
                        new MapConfig
                        {
                            Id = mapId,
                            Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                        }))!;
                Assert.That(ex.Message, Does.Contain("no adapter").IgnoreCase);
                Assert.That(ex.Message, Does.Contain("recast").IgnoreCase);
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        [Test]
        public void StartupOrder_RegisterAfterRuntimeNavComposition_FailsFast()
        {
            string repoRoot = FindRepoRoot();
            string mapId = "nav_bootstrap_late_recast_registration";
            string tempAssetsRoot = CreateTempAssetsRootWithNavTiles(repoRoot, mapId);

            try
            {
                RewriteTempNavmeshMode(
                    tempAssetsRoot,
                    NavBakeNames.ModeRuntimeIncremental,
                    NavBakeNames.AlgorithmCdt);

                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                    tempAssetsRoot);

                var vfs = (VirtualFileSystem)engine.VFS;
                vfs.Unmount("Core");
                vfs.Mount("Core", tempAssetsRoot);

                typeof(GameEngine)
                    .GetProperty(nameof(GameEngine.LogicTerrain), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(engine, new FlatGridLogicTerrainField(
                        SpatialScaleDefaults.TerrainChunkCells,
                        SpatialScaleDefaults.TerrainChunkCells,
                        chunkSizeCells: SpatialScaleDefaults.TerrainChunkCells));

                engine.LoadNavForMapForTests(
                    mapId,
                    new MapConfig
                    {
                        Id = mapId,
                        Tags = new List<string> { MapTags.FeatureNavMeshOn.Name }
                    });

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm()))!;
                Assert.That(
                    ex.Message,
                    Does.Contain("locked").IgnoreCase
                        .Or.Contain("before runtime nav composition").IgnoreCase);
            }
            finally
            {
                Directory.Delete(tempAssetsRoot, recursive: true);
            }
        }

        private static NavTriangleSurfaceTileIndex CreateTinySurfaceIndex()
        {
            const NavTriangleSurfaceFlags floor =
                NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { floor });
            var grid = new NavTriangleSurfaceTileGrid(
                0, 0, 100, 100, 1, 1, haloPaddingCm: 50);
            return NavTriangleSurfaceTileIndex.Build(surface, grid);
        }

        private static string CreateTempAssetsRootWithNavTiles(string repoRoot, string mapId)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-external-startup-" + Guid.NewGuid().ToString("N"));
            string configSource = Path.Combine(repoRoot, "assets", "Configs");
            string configTarget = Path.Combine(tempRoot, "Configs");
            CopyDirectory(configSource, configTarget);

            var config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);
            for (int layerIndex = 0; layerIndex < config.Layers.Count; layerIndex++)
            {
                int layer = config.Layers[layerIndex].Layer;
                for (int profileIndex = 0; profileIndex < config.Profiles.Count; profileIndex++)
                {
                    string profileId = config.Profiles[profileIndex].Id;
                    string rel = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, 0, 0);
                    string tilePath = Path.Combine(tempRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
                    File.WriteAllBytes(tilePath, Array.Empty<byte>());
                }
            }

            return tempRoot;
        }

        private static void RewriteTempNavmeshMode(string tempAssetsRoot, string mode, string algorithm)
        {
            string path = Path.Combine(tempAssetsRoot, "Configs", "Navigation", "navmesh.json");
            JsonObject navmesh = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
            navmesh["mode"] = mode;
            navmesh["algorithm"] = algorithm;
            File.WriteAllText(
                path,
                navmesh.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }

            foreach (string dir in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Ludots repo root from test BaseDirectory.");
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

        private static NavMeshBakeConfig CreateLayeredConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeRuntimeIncremental,
                Algorithm = NavBakeNames.AlgorithmCdt,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
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
                    ScratchSlotCount = 1,
                    RasterCellSizeCm = 100,
                    RasterHaloCells = 2,
                    SameSurfaceToleranceCm = 5,
                    MaxSimplificationErrorCm = 0,
                    HeightRounding = NavLayeredSpanConfig.HeightRoundingRoundHalfAwayFromZero,
                    MaxLawsonFlipCount = 1000,
                    ColumnCapacity = 256,
                    SpanCapacity = 1024,
                    ClassifiedSpanCapacity = 1024,
                    WalkableSpanCapacity = 1024,
                    LinkCapacity = 4096,
                    SheetCapacity = 1024,
                    PortalIntervalCapacity = 4096,
                    RegionCapacity = 256,
                    ChartCapacity = 64,
                    RingCapacity = 128,
                    ContourVertexCapacity = 1024,
                    ContourEdgeCapacity = 1024,
                    SeamCapacity = 256,
                    CanonicalLinkCapacity = 4096,
                    SplitPointCapacity = 256,
                    TriangulationVertexCapacity = 1024,
                    TriangulationTriangleCapacity = 2048,
                    ConstrainedEdgeCapacity = 2048,
                    BorderPortalCapacity = 256,
                    PolygonVertexCapacity = 1024,
                    AdjacencyEdgeCapacity = 4096,
                    BridgeCandidateCapacity = 1024,
                    RingWorkCapacity = 128,
                    TemporaryConstraintFlagCapacity = 2048
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 200 },
                Recast = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 5 }
            };
        }
    }
}
