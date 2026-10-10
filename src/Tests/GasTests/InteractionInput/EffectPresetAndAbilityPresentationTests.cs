using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.GAS.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EffectPresetAndAbilityPresentationTests
    {
        [Test]
        public void MobaEffects_CoversSharedCombatPresetTypes()
        {
            string repoRoot = FindRepoRoot();
            string effectsPath = Path.Combine(repoRoot, "mods", "showcases", "moba_demo", "MobaDemoMod", "assets", "GAS", "effects.json");
            Assert.That(File.Exists(effectsPath), Is.True, "MobaDemoMod effects.json is missing.");

            using var stream = File.OpenRead(effectsPath);
            using var doc = JsonDocument.Parse(stream);

            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("presetType", out var presetNode))
                {
                    var preset = presetNode.GetString();
                    if (!string.IsNullOrWhiteSpace(preset))
                    {
                        found.Add(preset);
                    }
                }
            }

            string[] expected =
            {
                "InstantDamage", "Heal", "DoT", "HoT", "Buff",
                "ApplyForce2D", "Search", "PeriodicSearch",
                "LaunchProjectile", "CreateUnit", "Displacement"
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(found.Contains(expected[i]), Is.True, $"Missing presetType '{expected[i]}' in MobaDemoMod effects.");
            }
        }




        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesPresentationMetadata()
        {
            var obj = JsonNode.Parse(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "End", "tick": 0 }
                    ]
                  },
                  "presentation": {
                    "displayName": "Mystic Shot",
                    "iconGlyph": "Q",
                    "accentColor": "#2AA7FF",
                    "hintText": "Quick skill shot",
                    "modeIconGlyphs": {
                      "PressReleaseAimCast": "QA"
                    },
                    "modeHints": {
                      "PressReleaseAimCast": "Release then confirm"
                    }
                  }
                }
                """) as JsonObject;

            Assert.That(obj, Is.Not.Null);

            var ability = AbilityExecLoader.CompileAbility(obj!, "ez.q", "test://abilities.json");
            Assert.That(ability.HasPresentation, Is.True);
            Assert.That(ability.Presentation, Is.Not.Null);
            Assert.That(ability.Presentation!.ResolveDisplayName("fallback"), Is.EqualTo("Mystic Shot"));
            Assert.That(ability.Presentation.ResolveIconGlyph("SmartCast", "?"), Is.EqualTo("Q"));
            Assert.That(ability.Presentation.ResolveIconGlyph("PressReleaseAimCast", "?"), Is.EqualTo("QA"));
            Assert.That(ability.Presentation.ResolveHintText("PressReleaseAimCast", "fallback"), Is.EqualTo("Release then confirm"));
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
