using System.Text.Json;
using Ludots.Core.Gameplay.GAS.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kGasWhitelistTests
    {
        [Test]
        public void Y5kAbilityPack_UsesWhitelistedExecKindsAndPresets()
        {
            string abilitiesPath = System.IO.Path.Combine(
                FindRepoRoot(),
                "mods/showcases/y5k_grand_strategy/Y5kGrandStrategyMod/assets/GAS/abilities.json");
            string effectsPath = System.IO.Path.Combine(
                FindRepoRoot(),
                "mods/showcases/y5k_grand_strategy/Y5kGrandStrategyMod/assets/GAS/effects.json");

            using JsonDocument abilities = JsonDocument.Parse(System.IO.File.ReadAllText(abilitiesPath));
            foreach (JsonElement ability in abilities.RootElement.EnumerateArray())
            {
                string id = ability.GetProperty("id").GetString() ?? string.Empty;
                if (!ability.TryGetProperty("exec", out JsonElement exec) ||
                    !exec.TryGetProperty("items", out JsonElement items))
                {
                    continue;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    string kind = item.GetProperty("kind").GetString() ?? string.Empty;
                    Assert.DoesNotThrow(() => GasOperatorWhitelist.ValidateExecItemKind(kind, id));
                }
            }

            using JsonDocument effects = JsonDocument.Parse(System.IO.File.ReadAllText(effectsPath));
            foreach (JsonElement effect in effects.RootElement.EnumerateArray())
            {
                string id = effect.GetProperty("id").GetString() ?? string.Empty;
                string preset = effect.TryGetProperty("presetType", out JsonElement presetEl)
                    ? presetEl.GetString() ?? string.Empty
                    : string.Empty;
                Assert.DoesNotThrow(() => GasOperatorWhitelist.ValidateEffectPresetType(preset, id));
            }
        }

        [Test]
        public void UnknownExecKind_FailsWithName()
        {
            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                GasOperatorWhitelist.ValidateExecItemKind("TotallyFakeOp", "Ability.Test"));
            Assert.That(ex!.Message, Does.Contain("TotallyFakeOp"));
            Assert.That(ex.Message, Does.Contain("Ability.Test"));
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }

            throw new System.InvalidOperationException("repo root not found");
        }
    }
}
