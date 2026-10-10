using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Graph order chain guards (constitution §12): orders come only from interaction-context
    /// trigger graphs through SubmitCommandIntent/SubmitCast → buffer → drain, and that bridge
    /// references no engine-reserved business collection key.
    /// </summary>
    [TestFixture]
    public sealed class GraphOrderChainContractTests
    {
        private static readonly string[] RetiredOrderSourceFileNames =
        {
            "LocalOrderSource",
            "InputOrderMapping",
            "input_order_mappings.json",
            "local_order_source.json",
        };

        [Test]
        public void Repository_Carries_NoLegacyInputOrderMappingOrLocalOrderSource()
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();
            foreach (string root in new[] { Path.Combine(repoRoot, "mods"), Path.Combine(repoRoot, "src"), Path.Combine(repoRoot, "assets") })
            {
                Assert.That(Directory.Exists(root), Is.True, $"{root} missing");
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
                    {
                        continue;
                    }

                    string name = Path.GetFileName(normalized);
                    for (int i = 0; i < RetiredOrderSourceFileNames.Length; i++)
                    {
                        if (name.Contains(RetiredOrderSourceFileNames[i], StringComparison.Ordinal))
                        {
                            offenders.Add(Path.GetRelativePath(repoRoot, normalized));
                            break;
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Orders come from interaction-context trigger graphs; the input order mapping and local order source are retired. Offenders:\n" +
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
