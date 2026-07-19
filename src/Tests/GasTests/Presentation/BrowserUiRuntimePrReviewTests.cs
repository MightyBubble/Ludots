using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Launcher.Backend;
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
            string loaderSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Ludots.UI.Browser",
                "BrowserRuntimeProviderLoader.cs"));
            string loadContextSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Ludots.UI.Browser",
                "BrowserRuntimeProviderAssemblyLoadContext.cs"));

            Assert.That(installerSource, Does.Contain("BrowserRuntimeProviderLoader.Install"));
            Assert.That(installerSource, Does.Not.Contain("AssemblyLoadContext.Default.LoadFromAssemblyPath"));
            Assert.That(loaderSource, Does.Contain("ShadowCopy"));
            Assert.That(loaderSource, Does.Contain("SHA256"));
            Assert.That(loaderSource, Does.Contain("Unload()"));
            Assert.That(loaderSource, Does.Contain("UseCollectibleLoadContext"));
            Assert.That(installerSource, Does.Contain("UseCollectibleLoadContext = useCollectibleLoadContext"));
            Assert.That(installerSource, Does.Not.Contain("CefSharp"));
            Assert.That(loadContextSource, Does.Contain("AssemblyDependencyResolver"));
            Assert.That(loadContextSource, Does.Contain("bool isCollectible = true"));
            Assert.That(loaderSource, Does.Not.Contain("BrowserCefRuntimeMod"));
            Assert.That(loadContextSource, Does.Not.Contain("BrowserCefRuntimeMod"));
            Assert.That(loadContextSource, Does.Not.Contain("ResolvedModLoadPlan"));
            Assert.That(loadContextSource, Does.Not.Contain("CefSharp"));
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
        public void BrowserShowcaseGameConfigs_DoNotAdvertiseProviderImplementation()
        {
            string repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "mods", "showcases", "browser_react_flow", "BrowserReactFlowShowcaseMod", "Assets", "game.json"),
                Path.Combine(repoRoot, "mods", "showcases", "browser_rts_production", "BrowserRtsProductionShowcaseMod", "Assets", "game.json"),
                Path.Combine(repoRoot, "mods", "showcases", "control_plane_projection", "ControlPlaneProjectionShowcaseMod", "assets", "game.json"),
                Path.Combine(repoRoot, "mods", "showcases", "entity_command_panel", "EntityCommandPanelShowcaseMod", "assets", "game.json")
            };

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("providerAssemblyPath"), file);
                Assert.That(source, Does.Not.Contain("providerHostTypeName"), file);
                Assert.That(source, Does.Not.Contain("processSharedAssemblyNamePrefixes"), file);
                Assert.That(source, Does.Not.Contain("Ludots.UI.Browser.Cef"), file);
                Assert.That(source, Does.Not.Contain("CefSharp"), file);
            }
        }

        [Test]
        public void BrowserShowcaseWebApps_UseLudotsFacadesOnly()
        {
            string repoRoot = FindRepoRoot();
            string showcasesRoot = Path.Combine(repoRoot, "mods", "showcases");
            string[] webAppSourceRoots = Directory
                .EnumerateDirectories(showcasesRoot, "WebApp", SearchOption.AllDirectories)
                .Select(dir => Path.Combine(dir, "src"))
                .Where(Directory.Exists)
                .ToArray();
            string[] packagedAppRoots = Directory
                .EnumerateDirectories(showcasesRoot, "*app", SearchOption.AllDirectories)
                .Where(dir =>
                    IsPackagedBrowserAppDirectory(dir) &&
                    !dir.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] files = webAppSourceRoots
                .Concat(packagedAppRoots)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(file => file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(files, Is.Not.Empty, "Browser showcase WebApp facade guard did not discover any JS/HTML files.");

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("CefSharp"), file);
                Assert.That(source, Does.Not.Contain("cefsharp"), file);
                Assert.That(source, Does.Not.Contain("window.cefSharp"), file);
            }
        }

        private static bool IsPackagedBrowserAppDirectory(string directory)
        {
            string name = Path.GetFileName(directory);
            if (!name.EndsWith("-app", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "browser-app", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string parentName = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);
            return string.Equals(parentName, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(parentName, "assets", StringComparison.OrdinalIgnoreCase);
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
            Assert.That(provider.GetProperty("packageRootPath").GetString(), Is.EqualTo("BrowserRuntime/cef"));
            Assert.That(provider.GetProperty("assemblyPath").GetString(), Is.EqualTo("BrowserRuntime/cef/Ludots.UI.Browser.Cef.dll"));
            Assert.That(provider.GetProperty("hostTypeName").GetString(), Is.EqualTo("Ludots.UI.Browser.Cef.CefBrowserRuntimeHost"));
            Assert.That(provider.GetProperty("useCollectibleLoadContext").GetBoolean(), Is.False);
            Assert.That(provider.GetProperty("processSharedAssemblyNamePrefixes").EnumerateArray().Select(item => item.GetString()).ToArray(),
                Is.EqualTo(new[] { "CefSharp" }));
            Assert.That(provider.GetProperty("projectPath").GetString(), Does.Not.Contain("mods/"));
            Assert.That(provider.GetProperty("assemblyPath").GetString(), Does.Not.Contain("mods/"));
        }

        [Test]
        public void LauncherCefPreset_CompletesHostProviderDescriptorFromRegistry()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"browser-provider-registry-{Guid.NewGuid():N}");
            string graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            byte[]? originalGraph = File.Exists(graphPath) ? File.ReadAllBytes(graphPath) : null;
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                LauncherLaunchPlan plan = launcher.Resolve(
                    new[] { "preset:browser_react_flow_cef_raylib" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.BrowserRuntime, Is.Not.Null);
                Assert.That(plan.BrowserRuntime!.Provider, Is.EqualTo("cef"));
                Assert.That(plan.BrowserRuntime.ProviderAssemblyPath, Does.EndWith("Ludots.UI.Browser.Cef.dll"));
                Assert.That(Path.IsPathRooted(plan.BrowserRuntime.ProviderAssemblyPath), Is.True);
                Assert.That(plan.BrowserRuntime.ProviderHostTypeName, Is.EqualTo("Ludots.UI.Browser.Cef.CefBrowserRuntimeHost"));
                Assert.That(plan.BrowserRuntime.UseCollectibleLoadContext, Is.False);
                Assert.That(plan.BrowserRuntime.ProcessSharedAssemblyNamePrefixes, Is.EqualTo(new[] { "CefSharp" }));
            }
            finally
            {
                if (originalGraph == null)
                {
                    File.Delete(graphPath);
                }
                else
                {
                    File.WriteAllBytes(graphPath, originalGraph);
                }

                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
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
