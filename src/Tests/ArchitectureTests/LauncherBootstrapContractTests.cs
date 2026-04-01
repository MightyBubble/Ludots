using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ludots.Core.Hosting;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class LauncherBootstrapContractTests
    {
        [Test]
        public void GameBootstrapper_PrefersLaunchGraphMetadata_WhenPresent()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "test-fingerprint",
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "test-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");

                Assert.That(result.Engine, Is.Not.Null);
                Assert.That(result.Config, Is.Not.Null);
                Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsLaunchGraph_WhenDependencyOrderIsInvalid()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var coreInputRoot = Path.Combine(repoRoot, "mods", "CoreInputMod");
            var coreRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "invalid-order-fingerprint",
                      "orderedModIds": [
                        "CoreInputMod",
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "CoreInputMod",
                          "rootPath": "{{coreInputRoot.Replace("\\", "\\\\")}}"
                        },
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{coreRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "invalid-order-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_UsesGraphPlanOrder_AsRuntimeLoadOrder()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var lowPriorityMod = CreateTestMod(tempDirectory, "LowPriorityMod", priority: 0);
                var highPriorityMod = CreateTestMod(tempDirectory, "HighPriorityMod", priority: 100);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "LowPriorityMod",
                        "rootPath": "{{lowPriorityMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "HighPriorityMod",
                        "rootPath": "{{highPriorityMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-order-fingerprint");

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");

                Assert.That(result.Engine.ModLoader.LoadedModIds, Is.EqualTo(new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" }),
                    "Graph-planned order should remain the runtime load order even when priority would have reordered an ad-hoc resolve path.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsBootstrapWithoutLaunchGraphMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-missing-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDirectory, "launcher.runtime.json"),
                    """
                    {
                      "PlanFingerprint": "graph-required-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("missing launch graph metadata"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsGraphDependencyOrderViolations()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var baseMod = CreateTestMod(tempDirectory, "BaseMod", priority: 0);
                var featureMod = CreateTestMod(
                    tempDirectory,
                    "FeatureMod",
                    priority: 0,
                    dependenciesJson: """
                    {
                      "BaseMod": "^1.0.0"
                    }
                    """);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-invalid-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "FeatureMod", "BaseMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "FeatureMod",
                        "rootPath": "{{featureMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "BaseMod",
                        "rootPath": "{{baseMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-invalid-order-fingerprint");

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static string CreateTestMod(string root, string modName, int priority, string? dependenciesJson = null)
        {
            var modDir = Path.Combine(root, modName);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(
                Path.Combine(modDir, "mod.json"),
                $$"""
                {
                  "name": "{{modName}}",
                  "version": "1.0.0",
                  "description": "test",
                  "main": "",
                  "priority": {{priority}},
                  "dependencies": {{dependenciesJson ?? "{}"}}
                }
                """);
            return modDir;
        }

        private static void WriteLaunchGraph(string graphPath, string planFingerprint, string[] orderedModIds, string plannedModsJson)
        {
            var orderedIdsJson = string.Join(
                "," + Environment.NewLine + "    ",
                orderedModIds.Select(id => $"\"{id}\""));
            File.WriteAllText(
                graphPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                  "planFingerprint": "{{planFingerprint}}",
                  "orderedModIds": [
                    {{orderedIdsJson}}
                  ],
                  "plannedMods": {{plannedModsJson}}
                }
                """);
        }

        private static void WriteBootstrap(string bootstrapPath, string fingerprint)
        {
            File.WriteAllText(
                bootstrapPath,
                $$"""
                {
                  "LaunchGraphPath": "raylib.launch.graph.json",
                  "PlanFingerprint": "{{fingerprint}}",
                  "PlanSchemaVersion": 1
                }
                """);
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
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
