using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Physics2D.Navigation;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavigationObstacleAuthoringContractTests
    {
        private static readonly string[] ForbiddenTokens =
        {
            "MassNavigationObstacleConfig",
            "WorldConfig.Obstacles",
            "\"obstacles\"",
            ".obstacles.json",
            "GetObstacleRelativePath",
            "EnqueueConfiguredObstacleBlockers",
            "ObstacleGeometryProfile2D"
        };

        [Test]
        public void ProductionNavigationCode_DoesNotReintroduceLegacyObstacleSidecars()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory
                .GetFiles(repoRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => IsProductionSource(repoRoot, path))
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(files, Is.Not.Empty, "Navigation obstacle contract scanned no production files.");

            var hits = new List<string>();
            foreach (string file in files)
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;
                    foreach (string token in ForbiddenTokens)
                    {
                        if (line.Contains(token, StringComparison.Ordinal))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: {token}");
                        }
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Obstacle authoring must use ManifestationObstacleIntent2D + ShapeDataStorage2D + CompoundObstacle2DState, not MassNavigationConfig/world.obstacles or obstacle sidecars.\n" +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void MassNavigationMap_AuthorsObstacleEntitiesThroughSharedComponents()
        {
            string repoRoot = FindRepoRoot();
            string modRoot = Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod");

            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world missing.");
            Assert.That(world.ContainsKey("obstacles"), Is.False,
                "MassNavigationConfig.world.obstacles[] is obsolete and must stay removed.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject blockerTemplate = FindObjectById(templates, "mass_navigation_blocker");
            AssertObstacleAuthoring(
                blockerTemplate["components"]?.AsObject()
                    ?? throw new InvalidOperationException("mass_navigation_blocker must author components."),
                "mass_navigation_blocker template");

            JsonObject map = ReadObject(Path.Combine(modRoot, "assets", "Maps", "mass_navigation.json"));
            JsonArray entities = map["Entities"]?.AsArray()
                ?? throw new InvalidOperationException("mass_navigation map must author entities.");
            JsonObject[] blockers = entities
                .Select(node => node?.AsObject() ?? throw new InvalidOperationException("Map entity entries must be objects."))
                .Where(entity => string.Equals(entity["Template"]?.GetValue<string>(), "mass_navigation_blocker", StringComparison.Ordinal))
                .ToArray();

            Assert.That(blockers.Length, Is.GreaterThan(0),
                "MassNavigation map must author blocker entities through ManifestationObstacleIntent2D overrides.");
            foreach (JsonObject blocker in blockers)
            {
                JsonObject overrides = blocker["Overrides"]?.AsObject()
                    ?? throw new InvalidOperationException("MassNavigation blocker entity must author component overrides.");
                AssertObstacleAuthoring(overrides, blocker["InstanceId"]?.GetValue<string>() ?? "mass_navigation_blocker entity");
            }
        }

        private static void AssertObstacleAuthoring(JsonObject components, string owner)
        {
            JsonObject obstacle = components["ManifestationObstacleIntent2D"]?.AsObject()
                ?? throw new InvalidOperationException($"{owner} must author ManifestationObstacleIntent2D.");
            Assert.That(obstacle["sinkNavigationObstacle"]?.GetValue<bool>(), Is.True);
            Assert.That(obstacle["radiusCm"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(obstacle["navRadiusCm"]?.GetValue<float>(), Is.GreaterThan(0f));
        }

        private static JsonObject FindObjectById(JsonArray array, string id)
        {
            return array
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => string.Equals(obj?["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing object with id '{id}'.");
        }

        private static JsonArray ReadArray(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsArray()
                ?? throw new InvalidOperationException($"Expected JSON array at '{path}'.");
        }

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
        }

        private static bool IsProductionSource(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(".tmp/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);
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

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        }

        [Test]
        public void Physics2DAuthoringAdapter_ProducesValidVerticalIntervals()
        {
            var map = new MapConfig
            {
                Id = "test-map",
                Entities = new List<EntitySpawnData>
                {
                    new EntitySpawnData
                    {
                        Template = "box-obstacle",
                        InstanceId = "box1",
                        Overrides = new Dictionary<string, JsonNode>
                        {
                            ["WorldPositionCm"] = JsonNode.Parse("{\"Value\":{\"X\":100,\"Y\":100}}"),
                            ["ManifestationObstacleIntent2D"] = JsonNode.Parse("{\"shape\":\"Box\",\"sinkPhysicsCollider\":false,\"sinkNavigationObstacle\":true,\"navRadiusCm\":0,\"halfWidthCm\":50,\"halfHeightCm\":50,\"localOffsetXCm\":0,\"localOffsetYCm\":0}")
                        }
                    }
                }
            };

            var templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal)
            {
                ["box-obstacle"] = new EntityTemplate
                {
                    Id = "box-obstacle",
                    Components = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
                    {
                        ["WorldPositionCm"] = JsonNode.Parse("{\"Value\":{\"X\":0,\"Y\":0}}"),
                        ["ManifestationObstacleIntent2D"] = JsonNode.Parse("{\"shape\":\"Box\",\"halfWidthCm\":50,\"halfHeightCm\":50,\"sinkPhysicsCollider\":false,\"sinkNavigationObstacle\":true,\"navRadiusCm\":0,\"localOffsetXCm\":0,\"localOffsetYCm\":0}")
                    }
                }
            };

            NavObstacleSet set = NavObstacleAuthoringAdapter.BuildFromMapAuthoring(map, templates, "Ground");

            Assert.That(set.Obstacles.Count, Is.EqualTo(1), "Expected exactly one obstacle from authoring.");
            NavObstacle obstacle = set.Obstacles[0];
            Assert.That(obstacle.MinYcm, Is.EqualTo(0), "Physics2D authoring must populate MinYcm.");
            Assert.That(obstacle.MaxYcm, Is.EqualTo(NavObstacle.DefaultPhysics2DVerticalExtentCm), "Physics2D authoring must populate MaxYcm with the default vertical extent.");
            Assert.That(obstacle.MinYcm, Is.LessThan(obstacle.MaxYcm), "MinYcm must be less than MaxYcm to pass ValidateForBake.");
        }
    }
}
