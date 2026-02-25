using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

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
    }
}
