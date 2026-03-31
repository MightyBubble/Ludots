using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ExampleMod;

namespace GasTests
{
    [TestFixture]
    public class ModLoaderLifecycleTests
    {
        [Test]
        public void LoadMods_WhenCalledAgain_ReplacesLoadedModIdsAndUnmountsStale()
        {
            var tempRoot = CreateTempDir();
            try
            {
                var modA = CreateModDir(tempRoot, "ModA");
                var modB = CreateModDir(tempRoot, "ModB");

                var vfs = new VirtualFileSystem();
                var loader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());

                loader.LoadMods(new[] { modA, modB });
                Assert.That(loader.LoadedModIds.Count, Is.EqualTo(2));
                Assert.That(loader.LoadedModIds, Does.Contain("ModA"));
                Assert.That(loader.LoadedModIds, Does.Contain("ModB"));
                Assert.That(vfs.TryResolveFullPath("ModB:mod.json", out _), Is.True);

                loader.LoadMods(new[] { modA });

                Assert.That(loader.LoadedModIds.Count, Is.EqualTo(1));
                Assert.That(loader.LoadedModIds.Single(), Is.EqualTo("ModA"));
                Assert.That(vfs.TryResolveFullPath("ModB:mod.json", out _), Is.False);
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMods_ReusesMainAssembly_FromDefaultContextOnly()
        {
            _ = typeof(ExampleModEntry).Assembly;

            string repoRoot = FindRepoRoot();
            string modRoot = Path.Combine(repoRoot, "mods", "ExampleMod");

            var vfs = new VirtualFileSystem();
            var loader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());

            Assert.DoesNotThrow(() => loader.LoadMods(new[] { modRoot }));
            Assert.That(loader.LoadedModIds, Does.Contain("ExampleMod"));
        }

        [Test]
        public void LoadMods_WhenAssemblyAlreadyLoadedInCollectibleContext_ThrowsUnsafeReuseError()
        {
            string tempRoot = CreateTempDir();
            try
            {
                string modDir = CreateModDir(tempRoot, "UnsafeReuseMod");
                string outputDir = Path.Combine(modDir, "bin", "Release", "net8.0");
                Directory.CreateDirectory(outputDir);

                string sourceAssemblyPath = typeof(ModLoaderLifecycleTests).Assembly.Location;
                string copiedAssemblyPath = Path.Combine(outputDir, "UnsafeReuseMod.dll");
                File.Copy(sourceAssemblyPath, copiedAssemblyPath, overwrite: true);

                using var collectibleContext = new TempCollectibleLoadContext(copiedAssemblyPath);
                _ = collectibleContext.LoadFromAssemblyPath(copiedAssemblyPath);

                var vfs = new VirtualFileSystem();
                var loader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());

                var ex = Assert.Throws<InvalidOperationException>(() => loader.LoadMods(new[] { modDir }));
                Assert.That(ex!.Message, Does.Contain("Unsafe mod assembly reuse detected"));
                Assert.That(ex.Message, Does.Contain("Only AssemblyLoadContext.Default assemblies may be reused"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static string CreateModDir(string root, string modName)
        {
            var modDir = Path.Combine(root, modName);
            Directory.CreateDirectory(modDir);
            var json = $$"""
            {
              "name": "{{modName}}",
              "version": "1.0.0",
              "description": "test",
              "main": "bin/Release/net8.0/{{modName}}.dll",
              "priority": 0,
              "dependencies": {}
            }
            """;
            File.WriteAllText(Path.Combine(modDir, "mod.json"), json);
            return modDir;
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_modloader_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(current.FullName, "mods")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }

        private sealed class TempCollectibleLoadContext : AssemblyLoadContext, IDisposable
        {
            private readonly AssemblyDependencyResolver _resolver;

            public TempCollectibleLoadContext(string mainAssemblyPath)
                : base(name: "TempCollectibleLoadContext", isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                string? path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path == null ? null : LoadFromAssemblyPath(path);
            }

            public void Dispose()
            {
                Unload();
            }
        }
    }
}
