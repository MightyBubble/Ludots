using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Graph order chain migration guards (constitution §12, branch graph-order-migration):
    /// local order mapping installs through exactly one config-driven composition root, the
    /// graph order bridge (SubmitCommandIntent/SubmitCast → buffer → drain) references no
    /// engine-reserved business collection key, and migrated showcases declare their active
    /// collection per battle context instead of relying on any steady-state fallback.
    /// </summary>
    [TestFixture]
    public sealed class GraphOrderChainContractTests
    {
        [Test]
        public void Mods_Carry_NoPerModLocalOrderSourceInstallers()
        {
            string repoRoot = FindRepoRoot();
            string modsRoot = Path.Combine(repoRoot, "mods");
            Assert.That(Directory.Exists(modsRoot), Is.True, "mods/ root missing");

            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(modsRoot, "*LocalOrderSourceSystem*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
                {
                    continue;
                }

                // The single shared config-installed order source is the composition root's.
                if (normalized.EndsWith("CoreInputMod/Systems/AutoInstalledLocalOrderSourceSystem.cs"))
                {
                    continue;
                }

                offenders.Add(normalized);
            }

            Assert.That(offenders, Is.Empty,
                "Per-mod local order source installers are retired (migration slice 2): the only installer is CoreInputMod's AutoInstalledLocalOrderSourceSystem. Offenders:\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void GraphOrderBridge_ReferencesNoBusinessCollectionKeys()
        {
            string repoRoot = FindRepoRoot();
            string[] bridgeFiles =
            {
                Path.Combine(repoRoot, "src", "Core", "Input", "Orders", "CommandIntentBufferDrainSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "GAS", "Orders", "CommandIntentSubmissionBuffer.cs"),
                Path.Combine(repoRoot, "src", "Core", "NodeLibraries", "GASGraph", "GasGraphOpHandlerTable.cs"),
            };

            foreach (string file in bridgeFiles)
            {
                Assert.That(File.Exists(file), Is.True, $"Missing bridge file {file}");
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("EntityCollectionKeys"),
                    $"{Path.GetFileName(file)} must stay collection-generic (constitution §12: routing reads only the rep's active-context-declared activeCollectionKey).");
                Assert.That(source, Does.Not.Contains("collection.command.source"),
                    $"{Path.GetFileName(file)} must not reference the legacy command-source key literal.");
            }

            // The graph ops themselves: handlers route through the API buffer only.
            string handlerTable = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "NodeLibraries", "GASGraph", "GasGraphOpHandlerTable.cs"));
            Assert.That(handlerTable, Does.Contain("SubmitCommandIntent"), "SubmitCommandIntent op must stay registered.");
            Assert.That(handlerTable, Does.Contain("SubmitCast"), "SubmitCast op must stay registered.");
        }

        [Test]
        public void MigratedShowcases_DeclareActiveCollectionKeyOnBattleContexts()
        {
            string repoRoot = FindRepoRoot();
            string[] profileFiles =
            {
                Path.Combine(repoRoot, "mods", "showcases", "case_e_selection", "CaseESelectionMod", "assets", "Input", "interaction_context_profiles.json"),
                Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "Input", "interaction_context_profiles.json"),
                Path.Combine(repoRoot, "mods", "showcases", "arpg_demo", "ArpgDemoMod", "assets", "Input", "interaction_context_profiles.json"),
            };

            foreach (string file in profileFiles)
            {
                Assert.That(File.Exists(file), Is.True, $"Missing migrated showcase profile {file}");
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement profiles = doc.RootElement.GetProperty("profiles");
                Assert.That(profiles.GetArrayLength(), Is.GreaterThan(0), $"{file} must declare at least the battle context");
                bool battleDeclared = false;
                foreach (JsonElement profile in profiles.EnumerateArray())
                {
                    string id = profile.GetProperty("id").GetString() ?? string.Empty;
                    if (!id.EndsWith(".battle", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Assert.That(profile.TryGetProperty("activeCollectionKey", out JsonElement key), Is.True,
                        $"{file}: battle context must declare activeCollectionKey (the order pipeline's read interface, constitution §12).");
                    Assert.That(key.GetString(), Is.Not.Null.And.Not.Empty,
                        $"{file}: activeCollectionKey must be a non-empty declared key name.");
                    battleDeclared = true;
                }

                Assert.That(battleDeclared, Is.True, $"{file} must contain a battle context profile.");
            }
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
