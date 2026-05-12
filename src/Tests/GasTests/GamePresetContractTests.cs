using System;
using System.IO;
using System.Linq;
using Ludots.Core.Modding.Workspace;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public class GamePresetContractTests
    {
        [Test]
        public void DiscoverPresets_RequiresExactFileNameAndJsonContract()
        {
            var root = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(root, "game.alpha.json"), """
                {
                  "WindowTitle": "Alpha",
                  "ModPaths": [ "mods/Alpha" ]
                }
                """);

                File.WriteAllText(Path.Combine(root, "Game.beta.json"), """
                {
                  "WindowTitle": "Beta",
                  "ModPaths": [ "mods/Beta" ]
                }
                """);

                var presets = GamePreset.DiscoverPresets(root);

                Assert.That(presets.Select(preset => preset.Id), Is.EqualTo(new[] { "alpha" }));
                Assert.That(presets[0].WindowTitle, Is.EqualTo("Alpha"));
                Assert.That(presets[0].ModPaths, Is.EqualTo(new[] { "mods/Alpha" }));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Test]
        public void DiscoverPresets_RejectsCaseAliasesAndUnknownFields()
        {
            var root = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(root, "game.bad.json"), """
                {
                  "windowTitle": "Bad",
                  "modPaths": [ "mods/Bad" ]
                }
                """);

                Assert.That(() => GamePreset.DiscoverPresets(root), Throws.TypeOf<System.Text.Json.JsonException>());
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Test]
        public void DiscoverPresets_RequiresModPaths()
        {
            var root = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(root, "game.bad.json"), """
                {
                  "WindowTitle": "Bad"
                }
                """);

                Assert.That(() => GamePreset.DiscoverPresets(root), Throws.TypeOf<InvalidDataException>());
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_preset_" + Guid.NewGuid().ToString("N"));
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
