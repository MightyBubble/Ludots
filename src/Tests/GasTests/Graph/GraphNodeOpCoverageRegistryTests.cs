using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        [Test]
        public void CoveredEntries_RequireRegisteredGalleryAndGalleryTestFilters()
        {
            string repoRoot = FindRepoRoot();
            HashSet<string> showcaseIds = LoadActiveOrExperimentalShowcaseIds(repoRoot);
            HashSet<string> testMethods = LoadGasTestMethodNames(repoRoot);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, RegistryRelativePath)));
            var failures = new List<string>();
            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string op = entry.GetProperty("op").GetString() ?? string.Empty;
                string status = entry.GetProperty("status").GetString() ?? string.Empty;
                string showcaseId = entry.GetProperty("showcaseId").GetString() ?? string.Empty;
                string filter = entry.GetProperty("unitTestFilter").GetString() ?? string.Empty;
                if (!string.Equals(status, "covered", StringComparison.Ordinal))
                {
                    failures.Add($"{op}: status is '{status}', expected covered.");
                    continue;
                }

                if (!showcaseIds.Contains(showcaseId))
                {
                    failures.Add($"{op}: showcaseId '{showcaseId}' is missing from showcase.registry.json.");
                }

                string[] tokens = filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool hasGalleryTest = false;
                foreach (string token in tokens)
                {
                    int dot = token.IndexOf('.');
                    if (dot <= 0 || dot >= token.Length - 1)
                    {
                        failures.Add($"{op}: unitTestFilter token '{token}' is not Class.Method.");
                        continue;
                    }

                    string method = token[(dot + 1)..];
                    if (!testMethods.Contains(method))
                    {
                        failures.Add($"{op}: unitTestFilter '{token}' does not match a GasTests method.");
                    }

                    if (token.StartsWith("GraphOpsNodeGallery", StringComparison.Ordinal))
                    {
                        hasGalleryTest = true;
                    }
                }

                if (!hasGalleryTest)
                {
                    failures.Add($"{op}: covered filter has no GraphOpsNodeGallery* test.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        private static HashSet<string> LoadActiveOrExperimentalShowcaseIds(string repoRoot)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement showcase in doc.RootElement.GetProperty("showcases").EnumerateArray())
            {
                string id = showcase.GetProperty("id").GetString() ?? string.Empty;
                string status = showcase.TryGetProperty("status", out JsonElement statusEl)
                    ? statusEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.Equals(status, "active", StringComparison.Ordinal) ||
                    string.Equals(status, "experimental", StringComparison.Ordinal))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static HashSet<string> LoadGasTestMethodNames(string repoRoot)
        {
            var methods = new HashSet<string>(StringComparer.Ordinal);
            string testsRoot = Path.Combine(repoRoot, "src", "Tests", "GasTests");
            var methodPattern = new Regex(@"public void ([A-Za-z0-9_]+)\s*\(", RegexOptions.Compiled);
            foreach (string file in Directory.EnumerateFiles(testsRoot, "*Tests.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in methodPattern.Matches(File.ReadAllText(file)))
                {
                    methods.Add(match.Groups[1].Value);
                }
            }

            return methods;
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
