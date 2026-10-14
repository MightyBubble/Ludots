using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// The engine root does not ship collection roles. A library mod declares a role it
    /// writes; a game declares a role it chooses. Missing files merge to an empty table.
    /// </summary>
    [TestFixture]
    public sealed class EngineRootCollectionRoleGuardTests
    {
        [Test]
        public void EngineRoot_DeclaresNoCollectionRoles()
        {
            string repoRoot = FindRepoRoot();
            string roleFile = Path.Combine(repoRoot, "assets", "Input", "collection_keys.json");
            if (File.Exists(roleFile))
            {
                using JsonDocument roles = JsonDocument.Parse(File.ReadAllText(roleFile));
                Assert.That(roles.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(roles.RootElement.EnumerateObject().MoveNext(), Is.False,
                    "assets/Input/collection_keys.json must not declare collection roles.");
            }

            string catalogPath = Path.Combine(repoRoot, "assets", "config_catalog.json");
            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            bool listed = false;
            foreach (JsonElement entry in catalog.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("Path", out JsonElement path) ||
                    !string.Equals(path.GetString(), "Input/collection_keys.json", StringComparison.Ordinal))
                {
                    continue;
                }

                listed = true;
                Assert.That(entry.GetProperty("Policy").GetString(), Is.EqualTo("DeepObject"));
            }

            Assert.That(listed, Is.True, "config_catalog.json must still list Input/collection_keys.json.");

            string coreRolesPath = Path.Combine(
                repoRoot,
                "mods",
                "LudotsCoreMod",
                "assets",
                "Input",
                "collection_keys.json");
            using JsonDocument coreRoles = JsonDocument.Parse(File.ReadAllText(coreRolesPath));
            Assert.That(coreRoles.RootElement.GetProperty("hover").GetString(), Is.EqualTo("collection.ui.command.hover"));
            Assert.That(coreRoles.RootElement.TryGetProperty("castRaw", out _), Is.False,
                "castRaw is chosen by the boot that calls CommitCast. LudotsCoreMod does not supply it.");
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
