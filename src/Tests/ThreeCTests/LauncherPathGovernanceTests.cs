using System;
using System.IO;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace ThreeCTests
{
    [TestFixture]
    public sealed class LauncherPathGovernanceTests
    {
        [Test]
        public void DiscoverMods_WhenNestedModRootsExist_ThrowsExplicitGovernanceError()
        {
            string repoRoot = CreateTempRepoRoot();
            try
            {
                string outer = Path.Combine(repoRoot, "mods", "showcases", "chunk_streaming");
                string inner = Path.Combine(outer, "ChunkStreamingShowcaseMod");
                CreateModRoot(outer, "ChunkStreamingShowcaseMod");
                CreateModRoot(inner, "ChunkStreamingShowcaseMod");

                string preferencesPath = Path.Combine(repoRoot, "preferences.json");
                string userConfigPath = Path.Combine(repoRoot, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var service = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var ex = Assert.Throws<InvalidOperationException>(() => service.DiscoverMods());
                Assert.That(ex!.Message, Does.Contain("Nested mod roots are not allowed"));
            }
            finally
            {
                TryDelete(repoRoot);
            }
        }

        [Test]
        public void GetState_WhenBindingTargetsNestedMirrorRoot_ThrowsCanonicalRootError()
        {
            string repoRoot = CreateTempRepoRoot();
            try
            {
                string outer = Path.Combine(repoRoot, "mods", "showcases", "chunk_streaming");
                CreateModRoot(outer, "ChunkStreamingShowcaseMod");

                File.WriteAllText(
                    Path.Combine(repoRoot, "launcher.config.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "scanRoots": [
                        {
                          "id": "repo_mods",
                          "path": "mods",
                          "scanMode": "recursive",
                          "enabled": true
                        }
                      ],
                      "bindings": [
                        {
                          "name": "chunk_streaming_showcase",
                          "target": {
                            "type": "path",
                            "value": "mods/showcases/chunk_streaming/ChunkStreamingShowcaseMod",
                            "projectPath": "ChunkStreamingShowcaseMod.csproj"
                          }
                        }
                      ],
                      "adapters": {
                        "default": "raylib"
                      },
                      "projectHints": []
                    }
                    """);

                string preferencesPath = Path.Combine(repoRoot, "preferences.json");
                string userConfigPath = Path.Combine(repoRoot, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var service = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var ex = Assert.Throws<InvalidOperationException>(() => service.GetState());
                Assert.That(ex!.Message, Does.Contain("points inside discovered mod root"));
                Assert.That(ex.Message, Does.Contain("chunk_streaming/ChunkStreamingShowcaseMod"));
                Assert.That(ex.Message, Does.Contain("mods/showcases/chunk_streaming"));
            }
            finally
            {
                TryDelete(repoRoot);
            }
        }

        private static string CreateTempRepoRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ludots_launcher_path_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            File.WriteAllText(Path.Combine(root, "launcher.config.json"), "{ \"schemaVersion\": 1 }");
            File.WriteAllText(Path.Combine(root, "launcher.presets.json"), "{ \"presets\": [] }");
            return root;
        }

        private static void CreateModRoot(string root, string modName)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "mod.json"),
                $$"""
                {
                  "name": "{{modName}}",
                  "version": "1.0.0",
                  "description": "test",
                  "main": "",
                  "priority": 0,
                  "dependencies": {}
                }
                """);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
