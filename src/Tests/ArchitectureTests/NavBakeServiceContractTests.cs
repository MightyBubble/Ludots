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
                  "areas": []
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
                  "areas": []
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
                new NavBuildConfig(1f, 0.6f, 1));

            Assert.That(result.Success, Is.False);
            string artifact = JsonSerializer.Serialize(result.Artifact, new JsonSerializerOptions { IncludeFields = true });
            Assert.That(artifact, Does.Not.Contain("Grid mesh fallback"));
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
                Areas = new List<NavAreaCostConfig>()
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
