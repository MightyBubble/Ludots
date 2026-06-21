using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshConfigContractTests
    {
        [Test]
        public void NavMeshBakeConfigPath_UsesRelativeConfigContract()
        {
            Assert.That(NavMeshConfigPaths.BakeConfigPath, Is.EqualTo("Navigation/navmesh.json"));
        }

        [Test]
        public void NavMeshBakeConfigLoader_LoadsThroughCoreConfigPipelineContract()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
            var config = new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog);

            Assert.That(config.Profiles, Is.Not.Null.And.Not.Empty);
            Assert.That(config.Layers, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AgentProfileRegistry_LoadsAsNavigationArrayByIdContract()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);

            Assert.That(catalog.TryGet(AgentProfileConfigLoader.RelativePath, out var entry), Is.True);
            Assert.That(entry.MergePolicy, Is.EqualTo(ConfigMergePolicy.ArrayById));
            Assert.That(entry.IdField, Is.EqualTo("id"));

            var registry = new AgentProfileConfigLoader(pipeline).Load(catalog);
            Assert.That(registry.TryGet("Small", out var small), Is.True);
            Assert.That(small.RadiusCm, Is.GreaterThan(0f));
            Assert.That(small.Mass, Is.GreaterThan(0f));
            Assert.That(registry.TryGet("small", out _), Is.False,
                "AgentProfile ids are case-sensitive and must not create lowercase aliases.");
        }

        [Test]
        public void NavigationProfileConfigs_DoNotOwnDuplicateGeometryFields()
        {
            string repoRoot = FindRepoRoot();
            JsonObject navmesh = ReadObject(Path.Combine(repoRoot, "assets", "Configs", "Navigation", "navmesh.json"));
            JsonArray navProfiles = navmesh["profiles"]?.AsArray()
                ?? throw new InvalidOperationException("Navigation/navmesh.json profiles missing.");
            foreach (JsonNode? node in navProfiles)
            {
                JsonObject profile = node?.AsObject()
                    ?? throw new InvalidOperationException("Navigation/navmesh.json profile entries must be objects.");
                Assert.That(profile.ContainsKey("radiusCm"), Is.False);
                Assert.That(profile.ContainsKey("heightCm"), Is.False);
            }

            JsonObject pathing = ReadObject(Path.Combine(repoRoot, "assets", "Configs", "Navigation", "pathing.json"));
            JsonArray agentTypes = pathing["agentTypes"]?.AsArray()
                ?? throw new InvalidOperationException("Navigation/pathing.json agentTypes missing.");
            foreach (JsonNode? node in agentTypes)
            {
                JsonObject agentType = node?.AsObject()
                    ?? throw new InvalidOperationException("Navigation/pathing.json agentTypes entries must be objects.");
                Assert.That(agentType.ContainsKey("layer"), Is.False);
            }
        }

        [Test]
        public void AgentProfileRegistry_RejectsUnknownFieldsStrictly()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-agent-profile-contract-" + Guid.NewGuid().ToString("N"));
            string coreConfigs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(coreConfigs, "Navigation"));
            File.WriteAllText(Path.Combine(coreConfigs, "config_catalog.json"),
                "[{ \"Path\": \"Navigation/agent_profiles.json\", \"Policy\": \"ArrayById\", \"IdField\": \"id\" }]");
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "agent_profiles.json"),
                """
                [
                  {
                    "id": "Small",
                    "radiusCm": 30,
                    "heightCm": 180,
                    "clearanceCm": 40,
                    "mass": 1,
                    "layer": 0,
                    "speedCmPerSecond": 800
                  }
                ]
                """);

            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => new AgentProfileConfigLoader(pipeline).Load(catalog))!;
                Assert.That(ex.Message, Does.Contain("speedCmPerSecond"));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void NavMeshBakeConfigLoader_LoadFromRepoRoot_UsesSameRelativeContract()
        {
            string repoRoot = FindRepoRoot();
            var config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);

            Assert.That(config.Profiles, Is.Not.Null.And.Not.Empty);
            Assert.That(config.Layers, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NavMeshAndPathing_RejectLegacyProfileFields()
        {
            var agentProfiles = new AgentProfileRegistry(new[]
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

            string navRoot = CreateTempNavigationConfig(
                navmeshJson:
                """
                {
                  "profiles": [
                    { "id": "Small", "radiusCm": 30, "maxClimbCm": 40, "maxSlopeDeg": 45 }
                  ],
                  "layers": [
                    { "id": "Ground", "layer": 0 }
                  ],
                  "areas": []
                }
                """,
                pathingJson:
                """
                {
                  "agentTypes": [
                    {
                      "id": "Humanoid",
                      "profileId": "Small",
                      "layer": 0,
                      "selection": { "mode": "PreferMesh", "graphBias": 0, "meshBias": 0, "graphCostWeight": 1, "meshCostWeight": 1 },
                      "navMesh": { "areaCosts": [] },
                      "nodeGraph": { "projectionMaxRadiusCm": 1, "forbiddenTagsAny": [], "requiredTagsAll": [], "tagCostRules": [] }
                    }
                  ]
                }
                """);

            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", navRoot);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                InvalidOperationException navmeshEx = Assert.Throws<InvalidOperationException>(
                    () => new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog))!;
                Assert.That(navmeshEx.Message, Does.Contain("radiusCm"));

                InvalidOperationException pathingEx = Assert.Throws<InvalidOperationException>(
                    () => new PathingConfigLoader(pipeline).Load(catalog))!;
                Assert.That(pathingEx.Message, Does.Contain("layer"));
            }
            finally
            {
                Directory.Delete(navRoot, recursive: true);
            }
        }

        private static string CreateTempNavigationConfig(string navmeshJson, string pathingJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-contract-" + Guid.NewGuid().ToString("N"));
            string coreConfigs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(coreConfigs, "Navigation"));
            File.WriteAllText(Path.Combine(coreConfigs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" },
                  { "Path": "Navigation/pathing.json", "Policy": "DeepObject" }
                ]
                """);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "navmesh.json"), navmeshJson);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "pathing.json"), pathingJson);
            return tempRoot;
        }

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
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

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
