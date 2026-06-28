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
        public void CoreModLoadContext_DoesNotHardcodeBrowserProviderAssemblyNames()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Modding", "ModLoadContext.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Not.Contain("Ludots.UI.Browser"));
            Assert.That(source, Does.Not.Contain("CefSharp"));
        }

        [Test]
        public void BrowserCefRuntimeMod_DeclaresProcessSharedAssembliesInManifest()
        {
            string repoRoot = FindRepoRoot();
            string manifestPath = Path.Combine(repoRoot, "mods", "browser", "BrowserCefRuntimeMod", "mod.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

            string[] processSharedAssemblies = document.RootElement
                .GetProperty("processSharedAssemblies")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();

            Assert.That(processSharedAssemblies, Does.Contain("Ludots.UI.Browser"));
            Assert.That(processSharedAssemblies, Does.Contain("Ludots.UI.Browser.Cef"));
            Assert.That(processSharedAssemblies, Does.Contain("CefSharp"));
            Assert.That(processSharedAssemblies, Does.Contain("CefSharp.Core"));
            Assert.That(processSharedAssemblies, Does.Contain("CefSharp.Core.Runtime"));
            Assert.That(processSharedAssemblies, Does.Contain("CefSharp.OffScreen"));
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
