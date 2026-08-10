using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kContentPackTests
    {
        [Test]
        public void ActivityAndTaskPacks_MeetPlanThresholds_AndFixturesExist()
        {
            string root = Path.Combine(
                FindRepoRoot(),
                "mods/showcases/y5k_grand_strategy/Y5kGrandStrategyMod/assets");

            using JsonDocument activities = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "Activities/activities.json")));
            using JsonDocument tasks = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "Tasks/tasks.json")));

            Assert.That(activities.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(12));
            Assert.That(tasks.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(6));

            string[] fixtures =
            {
                "y5k_supply_strain_v1.json",
                "y5k_siege_two_paths_v1.json",
                "y5k_takeover_transfer_v1.json",
                "y5k_captive_disposal_v1.json",
                "y5k_governor_appoint_v1.json",
                "y5k_covert_exposure_v1.json",
                "y5k_hero_skill_cast_v1.json",
            };

            foreach (string fixture in fixtures)
            {
                string path = Path.Combine(root, "Fixtures", fixture);
                Assert.That(File.Exists(path), Is.True, fixture);
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                Assert.That(doc.RootElement.GetProperty("fixture_id").GetString(), Does.EndWith("_v1"));
                Assert.That(doc.RootElement.TryGetProperty("fields", out _), Is.True);
            }
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("repo root not found");
        }
    }
}
