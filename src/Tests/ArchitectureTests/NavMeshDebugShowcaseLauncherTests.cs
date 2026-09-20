using System;
using System.IO;
using System.Linq;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshDebugShowcaseLauncherTests
    {
        [Test]
        public void Launcher_ResolvesNavMeshDirtyUpdateShowcases_AsMapSpecificEntrypoints()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-navmesh-debug-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                AssertNavMeshDebugPlan(
                    launcher.Resolve(
                        new[] { "$navmesh_debug_hex_launch", "$navmesh_debug" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    "NavMeshDebugHexLaunchMod",
                    "navmesh_debug_openworld");

                AssertNavMeshDebugPlan(
                    launcher.Resolve(
                        new[] { "$navmesh_debug_grid_launch", "$navmesh_debug" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    "NavMeshDebugGridLaunchMod",
                    "navmesh_debug_grid");

                AssertNavMeshDebugPlan(
                    launcher.Resolve(
                        new[] { "$navmesh_debug_vhtm_launch", "$navmesh_debug" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    "NavMeshDebugVhtmLaunchMod",
                    "navmesh_debug_vhtm");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static void AssertNavMeshDebugPlan(
            LauncherLaunchPlan plan,
            string expectedMapSelectorModId,
            string expectedStartupMapId)
        {
            Assert.That(plan.RootModIds, Does.Contain("NavMeshDebugLaunchMod"));
            Assert.That(plan.RootModIds, Does.Contain(expectedMapSelectorModId));
            Assert.That(plan.OrderedModIds, Does.Contain("LudotsCoreMod"));
            Assert.That(plan.OrderedModIds, Does.Contain("CoreInputMod"));
            Assert.That(plan.OrderedModIds, Does.Contain("CameraProfilesMod"));
            Assert.That(plan.OrderedModIds, Does.Contain("NavMeshDebugLaunchMod"));
            Assert.That(plan.OrderedModIds, Does.Contain(expectedMapSelectorModId));

            string[] ordered = plan.OrderedModIds.ToArray();
            Assert.That(Array.IndexOf(ordered, "NavMeshDebugLaunchMod"), Is.LessThan(Array.IndexOf(ordered, expectedMapSelectorModId)));

            var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(expectedStartupMapId));
            Assert.That(startupMapSetting.EffectiveSource, Does.Contain(expectedMapSelectorModId));
        }

        [Test]
        public void NavMeshDebugMaps_SuppressHostDebugGuides()
        {
            string repoRoot = FindRepoRoot();
            string mapsRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod", "assets", "Maps");
            string[] mapIds = { "navmesh_debug_openworld", "navmesh_debug_grid", "navmesh_debug_vhtm" };

            foreach (string mapId in mapIds)
            {
                string mapPath = Path.Combine(mapsRoot, $"{mapId}.json");
                Assert.That(File.Exists(mapPath), Is.True, $"navmesh debug map '{mapId}' must exist at {mapPath}");
                string mapJson = File.ReadAllText(mapPath);
                Assert.That(
                    mapJson,
                    Does.Contain("\"Raylib.DebugGuides:Off\""),
                    $"navmesh debug map '{mapId}' must suppress the camera-anchored host debug grid; without the tag the infinite grid renders as a gray slab that reads as terrain.");
            }
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                var candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }
    }
}
