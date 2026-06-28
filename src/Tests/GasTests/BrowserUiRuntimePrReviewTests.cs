using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public sealed class BrowserUiRuntimePrReviewTests
    {
        [Test]
        public void DefaultRaylibHub_DoesNotLoadBrowserUiShowcaseMod()
        {
            string repoRoot = FindRepoRoot();
            string hubPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.Raylib",
                "game.hub.json");

            using var document = JsonDocument.Parse(File.ReadAllText(hubPath));
            string[] modPaths = document.RootElement
                .GetProperty("ModPaths")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();

            Assert.That(
                modPaths.Any(path => path.Contains("BrowserUiShowcaseMod", StringComparison.Ordinal)),
                Is.False,
                "Browser UI showcase mods must be launched through explicit presets instead of the default Raylib hub.");
        }

        [Test]
        public void CefRuntime_DoesNotReturnHardcodedVersionWhenCefSharpVersionIsMissing()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Ludots.UI.Browser.Cef",
                "CefBrowserRuntime.cs");

            string source = File.ReadAllText(sourcePath);

            Assert.That(
                source,
                Does.Not.Contain("? \"148.0.90\""),
                "CEF runtime info must not fake a real-looking engine version when CefSharp does not report one.");
            Assert.That(
                source,
                Does.Contain("? \"unknown\""),
                "CEF runtime info should explicitly report an unknown engine version instead of a hardcoded fallback.");
        }

        [Test]
        public void CefRuntime_DisposeDoesNotShutdownProcessCef()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Ludots.UI.Browser.Cef",
                "CefBrowserRuntime.cs");

            string source = File.ReadAllText(sourcePath);

            Assert.That(
                source,
                Does.Not.Contain("Cef.Shutdown"),
                "CefSharp shutdown is process-scoped; runtime disposal must only release surfaces so repeated UE PIE can create a new runtime in the same process.");
        }

        [Test]
        public void RaylibHost_OwnsTerminalBrowserRuntimeShutdown()
        {
            string repoRoot = FindRepoRoot();
            string hostSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibGameHost.cs"));

            Assert.That(hostSource, Does.Contain("ShutdownBrowserRuntimeForHostExit"));
            Assert.That(hostSource, Does.Contain("ShutdownBrowserRuntimeProcessForHostExit"));
            Assert.That(hostSource, Does.Contain("BrowserRuntimeServiceNames.HostLifecycle"));
            Assert.That(hostSource, Does.Contain("ShutdownProcessForHostExit"));
            Assert.That(hostSource, Does.Contain("finally"));
            Assert.That(hostSource, Does.Not.Contain("Cef.Shutdown"));
            Assert.That(hostSource, Does.Not.Contain("BrowserCefRuntimeMod"));
        }

        [Test]
        public void CoreModLoadContext_DoesNotHardcodeBrowserProviderAssemblyNames()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Modding", "ModLoadContext.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Not.Contain("Ludots.UI.Browser"));
            Assert.That(source, Does.Not.Contain("CefSharp"));
        }

        [Test]
        public void BrowserCefRuntime_IsNotProvidedByModLoading()
        {
            string repoRoot = FindRepoRoot();
            string runtimeModManifest = Path.Combine(repoRoot, "mods", "browser", "BrowserCefRuntimeMod", "mod.json");
            string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
            string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
            string rtsManifest = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "browser_rts_production",
                "BrowserRtsProductionShowcaseMod",
                "mod.json"));

            Assert.That(File.Exists(runtimeModManifest), Is.False);
            Assert.That(launcherConfig, Does.Not.Contain("BrowserCefRuntimeMod"));
            Assert.That(launcherConfig, Does.Not.Contain("browser_cef_runtime"));
            Assert.That(launcherPresets, Does.Not.Contain("BrowserCefRuntimeMod"));
            Assert.That(launcherPresets, Does.Not.Contain("browser_cef_runtime"));
            Assert.That(rtsManifest, Does.Not.Contain("BrowserCefRuntimeMod"));
        }

        [Test]
        public void RaylibBrowserRuntimeInstaller_DoesNotResolveCefFromModLoadPlan()
        {
            string repoRoot = FindRepoRoot();
            string source = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibBrowserRuntimeInstaller.cs"));

            Assert.That(source, Does.Not.Contain("ResolvedModLoadPlan"));
            Assert.That(source, Does.Not.Contain("ModLoadPlan"));
            Assert.That(source, Does.Not.Contain("RuntimePackageModId"));
            Assert.That(source, Does.Not.Contain("RuntimeRootRelativePath"));
            Assert.That(source, Does.Not.Contain("BrowserCefRuntimeMod"));
        }

        [Test]
        public void RaylibBrowserRuntimeInstaller_ResolvesProviderDependenciesThroughProviderPackage()
        {
            string repoRoot = FindRepoRoot();
            string installerSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibBrowserRuntimeInstaller.cs"));
            string resolverSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibBrowserRuntimeProviderAssemblyResolver.cs"));

            Assert.That(installerSource, Does.Contain("EnsureProviderAssemblyResolver(fullAssemblyPath)"));
            Assert.That(installerSource, Does.Contain("AssemblyLoadContext.Default.LoadFromAssemblyPath(fullAssemblyPath)"));
            Assert.That(resolverSource, Does.Contain("AssemblyDependencyResolver"));
            Assert.That(resolverSource, Does.Not.Contain("BrowserCefRuntimeMod"));
            Assert.That(resolverSource, Does.Not.Contain("ResolvedModLoadPlan"));
            Assert.That(resolverSource, Does.Not.Contain("CefSharp"));
        }

        [Test]
        public void BrowserShowcaseMods_ConsumeBrowserPortOnly()
        {
            string repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "mods", "showcases", "browser_ui", "BrowserUiShowcaseMod", "BrowserUiShowcaseMod.csproj"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_ui", "BrowserUiShowcaseMod", "BrowserUiShowcaseModEntry.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_react_flow", "BrowserReactFlowShowcaseMod", "BrowserReactFlowShowcaseMod.csproj"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_react_flow", "BrowserReactFlowShowcaseMod", "BrowserReactFlowShowcaseModEntry.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "BrowserRtsProductionShowcaseMod.csproj"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "BrowserRtsProductionShowcaseModEntry.cs")
            };

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("Ludots.UI.Browser.Cef"), file);
                Assert.That(source, Does.Not.Contain("CefSharp"), file);
            }
        }

        [Test]
        public void BrowserShowcaseModManifests_DoNotAdvertiseProviderIdentity()
        {
            string repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "mods", "showcases", "browser_ui", "BrowserUiShowcaseMod", "mod.json"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_react_flow", "BrowserReactFlowShowcaseMod", "mod.json"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "mod.json")
            };

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("CEF"), file);
                Assert.That(source, Does.Not.Contain("Cef"), file);
                Assert.That(source, Does.Not.Contain("\"cef\""), file);
            }
        }

        [Test]
        public void BrowserShowcaseWebApps_UseLudotsFacadesOnly()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory
                .EnumerateFiles(Path.Combine(repoRoot, "mods", "showcases", "browser_ui", "BrowserUiShowcaseMod", "Assets", "browser-app"), "*.*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "WebApp", "src"), "*.*", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "Assets", "rts-production-app"), "*.*", SearchOption.AllDirectories))
                .Where(file => file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("CefSharp"), file);
                Assert.That(source, Does.Not.Contain("cefsharp"), file);
                Assert.That(source, Does.Not.Contain("window.cefSharp"), file);
            }
        }

        [Test]
        public void LauncherCefPresets_RequestHostBrowserRuntime()
        {
            string repoRoot = FindRepoRoot();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));
            JsonElement browserUiPreset = FindPreset(document, "browser_ui_cef_raylib");
            JsonElement reactFlowPreset = FindPreset(document, "browser_react_flow_cef_raylib");

            AssertHostCefPreset(browserUiPreset);
            AssertHostCefPreset(reactFlowPreset);
        }

        [Test]
        public void LauncherConfig_RegistersCefAsHostRuntimeProvider()
        {
            string repoRoot = FindRepoRoot();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
            JsonElement provider = document.RootElement
                .GetProperty("browserRuntimeProviders")
                .EnumerateArray()
                .Single(item => string.Equals(item.GetProperty("id").GetString(), "cef", StringComparison.Ordinal));

            Assert.That(provider.GetProperty("projectPath").GetString(), Does.StartWith("src/Libraries/Ludots.UI.Browser.Cef"));
            Assert.That(provider.GetProperty("assemblyPath").GetString(), Does.StartWith("src/Libraries/Ludots.UI.Browser.Cef"));
            Assert.That(provider.GetProperty("projectPath").GetString(), Does.Not.Contain("mods/"));
            Assert.That(provider.GetProperty("assemblyPath").GetString(), Does.Not.Contain("mods/"));
        }

        private static JsonElement FindPreset(JsonDocument document, string id)
        {
            return document.RootElement
                .GetProperty("presets")
                .EnumerateArray()
                .Single(item => string.Equals(item.GetProperty("id").GetString(), id, StringComparison.Ordinal));
        }

        private static void AssertHostCefPreset(JsonElement preset)
        {
            string[] selectors = preset
                .GetProperty("selectors")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();

            Assert.That(selectors, Does.Not.Contain("$browser_cef_runtime"));
            JsonElement browserRuntime = preset.GetProperty("browserRuntime");
            Assert.That(browserRuntime.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("required").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("provider").GetString(), Is.EqualTo("cef"));
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "mods")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }
    }
}
