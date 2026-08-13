using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphNodeOpCoverageRegistryTests
    {
        private const string RegistryRelativePath = "assets/Configs/GAS/graph_node_op_coverage.registry.json";

        [Test]
        public void Registry_ShowcaseId_MustBeUniquePerOp()
        {
            string repoRoot = FindRepoRoot();
            string registryPath = Path.Combine(repoRoot, RegistryRelativePath);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(registryPath));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string op = entry.GetProperty("op").GetString() ?? string.Empty;
                string showcaseId = entry.GetProperty("showcaseId").GetString() ?? string.Empty;
                Assert.That(showcaseId, Is.EqualTo("capability_standard_graph_op_" + op),
                    $"Coverage showcaseId for {op} must be the per-op gallery, not a family aggregate.");
                Assert.That(seen.Add(showcaseId), Is.True, $"Duplicate coverage showcaseId '{showcaseId}'.");
            }
        }

        [Test]
        public void Registry_Entries_MustMatch_GraphNodeOpEnum_ExcludingNone()
        {
            string repoRoot = FindRepoRoot();
            string registryPath = Path.Combine(repoRoot, RegistryRelativePath);
            Assert.That(File.Exists(registryPath), Is.True, $"Missing coverage registry: {RegistryRelativePath}");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(registryPath));
            JsonElement root = doc.RootElement;
            Assert.That(root.TryGetProperty("entries", out JsonElement entriesEl), Is.True);
            Assert.That(entriesEl.ValueKind, Is.EqualTo(JsonValueKind.Array));

            var registryOps = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in entriesEl.EnumerateArray())
            {
                Assert.That(entry.TryGetProperty("op", out JsonElement opEl), Is.True);
                string op = opEl.GetString() ?? string.Empty;
                Assert.That(registryOps.Add(op), Is.True, $"Duplicate registry entry for op '{op}'.");
            }

            var enumOps = Enum.GetValues(typeof(GraphNodeOp))
                .Cast<GraphNodeOp>()
                .Where(v => v != GraphNodeOp.None)
                .Select(v => v.ToString())
                .ToHashSet(StringComparer.Ordinal);

            var missingFromRegistry = enumOps.Except(registryOps).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            var extraInRegistry = registryOps.Except(enumOps).OrderBy(v => v, StringComparer.Ordinal).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(missingFromRegistry, Is.Empty,
                    "GraphNodeOp values missing from coverage registry:\n" + string.Join("\n", missingFromRegistry));
                Assert.That(extraInRegistry, Is.Empty,
                    "Coverage registry entries not present in GraphNodeOp enum:\n" + string.Join("\n", extraInRegistry));
            });
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                dir = dir.Parent;
            }

            Assert.That(dir, Is.Not.Null, "Repository root not found from test output directory.");
            return dir!.FullName;
        }
    }
}
