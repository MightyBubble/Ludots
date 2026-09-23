using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class CoreBoundaryTests
    {
        [Test]
        public void LudotsCore_DoesNotReference_Raylib_Client_OrAdapter()
        {
            var repoRoot = FindRepoRoot();
            var coreCsprojPath = Path.Combine(repoRoot, "src", "Core", "Ludots.Core.csproj");
            Assert.That(File.Exists(coreCsprojPath), Is.True, $"Missing: {coreCsprojPath}");

            var doc = XDocument.Load(coreCsprojPath);
            var includes =
                doc.Descendants()
                    .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .OfType<string>()
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

            var forbidden = new[]
            {
                "Raylib",
                "Ludots.Client.Raylib",
                "Ludots.Adapter.Raylib",
                "WebGpu",
                "WebGPU",
                "Silk.NET",
                "Ludots.Client.WebGpu",
                "Ludots.Adapter.WebGpu"
            };

            var offenders =
                includes.Where(i => forbidden.Any(f => i.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            Assert.That(offenders, Is.Empty, $"Core should not reference platform SDK/impl projects. Offenders: {string.Join(", ", offenders)}");
        }

        [Test]
        public void Launcher_ListsWebGpuAsOfficialAdapter()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-webgpu-platform-{Guid.NewGuid():N}");
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

                var state = launcher.GetState();
                Assert.That(
                    state.Platforms.Any(p => string.Equals(p.Id, LauncherPlatformIds.WebGpu, StringComparison.OrdinalIgnoreCase)),
                    Is.True);

                var plan = launcher.Resolve(
                    new[] { "mod:LudotsCoreMod" },
                    LauncherPlatformIds.WebGpu,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.AdapterId, Is.EqualTo(LauncherPlatformIds.WebGpu));
                Assert.That(plan.Adapter.HostKind, Is.EqualTo("web"));
                Assert.That(plan.Adapter.BuildPipeline, Is.EqualTo("dotnet+npm"));
                Assert.That(plan.AppAssemblyPath, Does.Contain("Ludots.App.Web"));
                Assert.That(plan.LaunchUrl, Is.EqualTo("http://localhost:5201"));
                Assert.That(plan.Adapter.ClientProjectDirectory.Replace('\\', '/'), Does.Contain("Client/WebGpu"));
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
        public void Mods_DoNotReference_SkiaSharp_UnlessWhitelisted()
        {
            var repoRoot = FindRepoRoot();
            var modsDir = Path.Combine(repoRoot, "mods");
            Assert.That(Directory.Exists(modsDir), Is.True, $"Missing mods directory: {modsDir}");

            var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UiShowcaseCoreMod",
                "PerformanceVisualizationMod",
                "UxPrototypeMod"
            };

            var csprojFiles = Directory.GetFiles(modsDir, "*.csproj", SearchOption.AllDirectories);
            var violations = new List<string>();

            foreach (var csprojPath in csprojFiles)
            {
                var modName = Path.GetFileNameWithoutExtension(csprojPath);
                if (whitelist.Contains(modName))
                {
                    continue;
                }

                var doc = XDocument.Load(csprojPath);
                var includes = doc.Descendants()
                    .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .OfType<string>()
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

                foreach (var include in includes)
                {
                    if (include.Contains("Ludots.UI.Skia", StringComparison.OrdinalIgnoreCase) ||
                        include.Contains("SkiaSharp", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{modName}: {include}");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                $"Mods must not reference Ludots.UI.Skia or SkiaSharp directly (use engine-injected services). Whitelisted: {string.Join(", ", whitelist)}. Violations:\n{string.Join("\n", violations)}");
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
