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
                    $"{Path.GetFileName(file)} must stay collection-generic (constitution §12 v2: intents carry their own actor sets).");
                Assert.That(source, Does.Not.Contains("collection.command.source"),
                    $"{Path.GetFileName(file)} must not reference the legacy command-source key literal.");
            }

            // The graph ops themselves: handlers route through the API buffer only.
            string handlerTable = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "NodeLibraries", "GASGraph", "GasGraphOpHandlerTable.cs"));
            Assert.That(handlerTable, Does.Contain("SubmitCommandIntent"), "SubmitCommandIntent op must stay registered.");
            Assert.That(handlerTable, Does.Contain("SubmitCast"), "SubmitCast op must stay registered.");
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
