using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavRecastConfigContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        [Test]
        public void AssetsNavmesh_LoadsExplicitRecastSmallProfileRaster()
        {
            string repoRoot = FindRepoRoot();
            NavMeshBakeConfig config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);
            Assert.That(config.Recast, Is.Not.Null);
            Assert.That(config.Recast.RasterCellSizeCm, Is.EqualTo(10));
            Assert.That(config.Recast.RasterCellHeightCm, Is.EqualTo(5));
        }

        [Test]
        public void ShowcaseNavmesh_LoadsSharedTwentyCmRasterForScaleComparison()
        {
            string repoRoot = FindRepoRoot();
            JsonObject rts = ReadObject(Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "nav_bake",
                "NavBakeDynamicRtsShowcaseMod",
                "assets",
                "Configs",
                "Navigation",
                "navmesh.json"));
            JsonObject openWorld = ReadObject(Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "nav_bake",
                "NavBakeOpenWorld64x64ShowcaseMod",
                "assets",
                "Configs",
                "Navigation",
                "navmesh.json"));

            Assert.That(rts["recast"]?["rasterCellSizeCm"]?.GetValue<int>(), Is.EqualTo(20));
            Assert.That(rts["recast"]?["rasterCellHeightCm"]?.GetValue<int>(), Is.EqualTo(10));
            Assert.That(openWorld["recast"]?["rasterCellSizeCm"]?.GetValue<int>(), Is.EqualTo(20));
            Assert.That(openWorld["recast"]?["rasterCellHeightCm"]?.GetValue<int>(), Is.EqualTo(10));
        }

        [Test]
        public void Loader_RequiresExplicitRecastObject()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(OmitRecast(ValidNavmeshJson())))!;
            Assert.That(ex.Message, Does.Contain("recast"));
        }

        [Test]
        public void Loader_RejectsUnknownRecastProperty()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!.AsObject()["unknown"] = 1;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("unknown"));
            Assert.That(ex.Message, Does.Contain("NavMeshBakeConfig.recast"));
        }

        [Test]
        public void Loader_RejectsMissingRecastField()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!.AsObject().Remove("rasterCellHeightCm");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("rasterCellHeightCm"));
        }

        [Test]
        public void Loader_RejectsNonPositiveRecastValues()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!["rasterCellSizeCm"] = 0;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("rasterCellSizeCm"));
            Assert.That(ex.Message, Does.Contain("> 0"));
        }

        [Test]
        public void Validate_RejectsNonPositiveFieldsWithPath()
        {
            var config = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 0 };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => config.Validate())!;
            Assert.That(ex.Message, Does.Contain("NavMeshBakeConfig.recast.RasterCellHeightCm"));
        }

        [Test]
        public void RecastTileBuildHash_IncludesRasterSettings()
        {
            NavTriangleSurfaceTileIndex surface = CreateTinyFloor();
            ulong hashA = BakeHash(surface, cellSizeCm: 10, cellHeightCm: 5);
            ulong hashB = BakeHash(surface, cellSizeCm: 20, cellHeightCm: 10);
            Assert.That(hashB, Is.Not.EqualTo(hashA));
        }

        private static ulong BakeHash(NavTriangleSurfaceTileIndex surface, int cellSizeCm, int cellHeightCm)
        {
            var config = CreateBakeConfig(cellSizeCm, cellHeightCm);
            var context = new NavBakeContext
            {
                MapId = "nav_recast_hash",
                SourceUri = "Core:Maps/nav_recast_hash.vtxm",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = config,
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
            return bake.Entries[0].Tile.BuildConfigHash;
        }

        private static NavTriangleSurfaceTileIndex CreateTinyFloor()
        {
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 400, 400 },
                triA: new[] { 0, 0 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 3 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            return NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, haloPaddingCm: 200));
        }

        private static NavMeshBakeConfig CreateBakeConfig(int cellSizeCm, int cellHeightCm)
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmRecast,
                Profiles = new System.Collections.Generic.List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new System.Collections.Generic.List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
                },
                Areas = new System.Collections.Generic.List<NavAreaCostConfig>(),
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
                    RasterHaloCells = 2,
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
                },
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 200 },
                Recast = new NavRecastConfig
                {
                    RasterCellSizeCm = cellSizeCm,
                    RasterCellHeightCm = cellHeightCm
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

        private static string OmitRecast(string json)
        {
            JsonObject root = JsonNode.Parse(json)!.AsObject();
            root.Remove("recast");
            return root.ToJsonString();
        }

        private static NavMeshBakeConfig LoadTemp(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-recast-config-" + Guid.NewGuid().ToString("N"));
            string coreConfigs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(coreConfigs, "Navigation"));
            File.WriteAllText(Path.Combine(coreConfigs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" },
                  { "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "navmesh.json"), navmeshJson);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "agent_profiles.json"),
                """
                [
                  {
                    "id": "Small",
                    "radiusCm": 30,
                    "heightCm": 180,
                    "clearanceCm": 40,
                    "draftCm": 0,
                    "beamCm": 0,
                    "mass": 1,
                    "layer": 0
                  }
                ]
                """);
            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                var catalog = ConfigCatalogLoader.Load(pipeline);
                var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
                return new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
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
                """;
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Ludots.sln")) ||
                    File.Exists(Path.Combine(dir, "assets", "Configs", "Navigation", "navmesh.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Unable to locate repo root from test base directory.");
        }

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
        }
    }
}
