using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class MassFlowNavLauncherPresetContractTests
    {
        [Test]
        public void LauncherConfig_ContainsMassFlowNavPlaygroundBinding_AndPresets()
        {
            string repoRoot = FindRepoRoot();
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
            using JsonDocument presets = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));

            JsonElement bindings = config.RootElement.GetProperty("bindings");
            JsonElement presetArray = presets.RootElement.GetProperty("presets");

            Assert.That(
                bindings.EnumerateArray().Any(binding =>
                    binding.GetProperty("name").GetString() == "mass_flow_nav_playground" &&
                    binding.GetProperty("target").GetProperty("value").GetString() == "mods/showcases/navigation/MassFlowNavPlaygroundMod"),
                Is.True,
                "Launcher config should expose the new mass-flow navigation playground binding.");

            Assert.That(
                presetArray.EnumerateArray().Any(preset => preset.GetProperty("id").GetString() == "mass_flow_nav_playground_raylib"),
                Is.True,
                "Launcher presets should include a raylib preset for the new playground.");

            Assert.That(
                presetArray.EnumerateArray().Any(preset => preset.GetProperty("id").GetString() == "mass_flow_nav_playground_web"),
                Is.True,
                "Launcher presets should include a web preset for the new playground.");
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
