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
        private const string RegistryRelativePath = "assets/GAS/graph_node_op_coverage.registry.json";
        private const string GalleryMapsRelative =
            "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Maps";
        private const string EntryRootRelative =
            "mods/showcases/capability_standard/graph_op_entries";

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
                Assert.That(showcaseId, Is.EqualTo(GraphOpTestAttribution.PerOpShowcasePrefix + op),
                    $"Coverage showcaseId for {op} must be the per-op gallery, not a family aggregate.");
                Assert.That(seen.Add(showcaseId), Is.True, $"Duplicate coverage showcaseId '{showcaseId}'.");
            }
        }

        [Test]
        public void Registry_AuthorableKinds_MustMatchDescriptorProjection()
        {
            string repoRoot = FindRepoRoot();
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, RegistryRelativePath)));
            var failures = new List<string>();
            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string opName = entry.GetProperty("op").GetString() ?? string.Empty;
                if (!Enum.TryParse(opName, ignoreCase: false, out GraphNodeOp op) ||
                    op == GraphNodeOp.None ||
                    !Enum.IsDefined(typeof(GraphNodeOp), op))
                {
                    failures.Add($"{opName}: not a GraphNodeOp.");
                    continue;
                }

                string[] expected = GraphOpDescriptorTable.ProjectCoverageAuthorableKinds(op);
                string[] actual = entry.GetProperty("authorableKinds")
                    .EnumerateArray()
                    .Select(v => v.GetString() ?? string.Empty)
                    .ToArray();
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    failures.Add($"{opName}: expected [{string.Join(",", expected)}] but registry has [{string.Join(",", actual)}].");
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
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
        public void CoveredEntries_RequireMeasuredGalleryRefsThatExecuteTheOp()
        {
            string repoRoot = FindRepoRoot();
            HashSet<string> showcaseIds = LoadActiveOrExperimentalShowcaseIds(repoRoot);
            List<CoverageEntry> entries = LoadCoverageEntries(repoRoot);
            GraphOpTestAttribution attribution = GraphOpTestAttribution.Load(
                repoRoot,
                entries.Select(e => e.Op));
            var failures = new List<string>();
            foreach (CoverageEntry entry in entries)
            {
                if (!string.Equals(entry.Status, "covered", StringComparison.Ordinal))
                {
                    failures.Add($"{entry.Op}: status is '{entry.Status}', expected covered.");
                    continue;
                }

                if (!showcaseIds.Contains(entry.ShowcaseId))
                {
                    failures.Add($"{entry.Op}: showcaseId '{entry.ShowcaseId}' is missing from showcase.registry.json.");
                }

                if (entry.UnitTestRefs.Count == 0)
                {
                    failures.Add($"{entry.Op}: unitTestRefs is empty.");
                    continue;
                }

                bool hasSpecificGallery = false;
                foreach (string token in entry.UnitTestRefs)
                {
                    int dot = token.IndexOf('.');
                    if (dot <= 0 || dot != token.LastIndexOf('.') || dot >= token.Length - 1)
                    {
                        failures.Add($"{entry.Op}: unitTestRefs token '{token}' is not Class.Method.");
                        continue;
                    }

                    string className = token[..dot];
                    string method = token[(dot + 1)..];
                    if (!attribution.HasMethod(className, method))
                    {
                        failures.Add($"{entry.Op}: unitTestRefs '{token}' is not a GasTests (Class, Method) pair.");
                        continue;
                    }

                    if (!attribution.Executes(className, method, entry.Op))
                    {
                        failures.Add($"{entry.Op}: unitTestRefs '{token}' does not execute this op.");
                    }

                    if (GraphOpTestAttribution.IsGallerySpecific(className, method))
                    {
                        hasSpecificGallery = true;
                    }
                }

                if (!hasSpecificGallery)
                {
                    failures.Add($"{entry.Op}: covered refs have no op-specific GraphOpsNodeGallery test.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void Attribution_RejectsTheKnownFamilyMispointers()
        {
            string repoRoot = FindRepoRoot();
            List<CoverageEntry> entries = LoadCoverageEntries(repoRoot);
            GraphOpTestAttribution attribution = GraphOpTestAttribution.Load(
                repoRoot,
                entries.Select(e => e.Op));

            Assert.Multiple(() =>
            {
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryFloatAcceptanceTests",
                        "FloatFamilyOp_RendersPlayerCaption",
                        "ConstFloat"),
                    Is.False);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryFloatAcceptanceTests",
                        "FloatFamilyOp_RendersPlayerCaption",
                        "AddFloat"),
                    Is.False);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryEventAcceptanceTests",
                        "SnapToNearestInCollection_SucceedsWithPlayerCaption",
                        "SendEvent"),
                    Is.False);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryAcceptanceTests",
                        "ConstFloat_SetsTargetHealthToAuthoredConstant",
                        "ConstFloat"),
                    Is.True);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryEventAcceptanceTests",
                        "SendEvent_BroadcastsPlayerReadableHit",
                        "SendEvent"),
                    Is.True);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryAcceptanceTests",
                        "ExistingVignettes_CompileWithFeaturedOp",
                        "ConstFloat"),
                    Is.True);
                Assert.That(
                    attribution.Executes(
                        "GraphOpsNodeGalleryAcceptanceTests",
                        "GeneratedMaps_SpawnEveryVignetteActor",
                        "AddFloat"),
                    Is.True);
            });
        }

        [Test]
        public void GeneratedPerOpArtifacts_HaveNoOrphans()
        {
            string repoRoot = FindRepoRoot();
            var liveOps = new HashSet<string>(
                LoadCoverageEntries(repoRoot).Select(e => e.Op),
                StringComparer.Ordinal);
            var failures = new List<string>();

            string mapsDir = Path.Combine(repoRoot, GalleryMapsRelative);
            if (Directory.Exists(mapsDir))
            {
                foreach (string map in Directory.GetFiles(mapsDir, "*.json"))
                {
                    string sid = Path.GetFileNameWithoutExtension(map);
                    if (!GraphOpTestAttribution.IsPerOpShowcaseId(sid))
                    {
                        continue;
                    }

                    string op = sid[GraphOpTestAttribution.PerOpShowcasePrefix.Length..];
                    if (!liveOps.Contains(op))
                    {
                        failures.Add($"orphan map {sid}");
                    }
                }
            }

            string entryRoot = Path.Combine(repoRoot, EntryRootRelative);
            if (Directory.Exists(entryRoot))
            {
                foreach (string folder in Directory.GetDirectories(entryRoot))
                {
                    string name = Path.GetFileName(folder);
                    const string prefix = "CapabilityStandardGraphOp";
                    const string suffix = "EntryMod";
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                        !name.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string op = name[prefix.Length..^suffix.Length];
                    if (!liveOps.Contains(op))
                    {
                        failures.Add($"orphan entry mod {name}");
                    }
                }
            }

            using JsonDocument registry = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
            foreach (JsonElement showcase in registry.RootElement.GetProperty("showcases").EnumerateArray())
            {
                string id = showcase.GetProperty("id").GetString() ?? string.Empty;
                if (!GraphOpTestAttribution.IsPerOpShowcaseId(id))
                {
                    continue;
                }

                string op = id[GraphOpTestAttribution.PerOpShowcasePrefix.Length..];
                if (!liveOps.Contains(op))
                {
                    failures.Add($"orphan showcase {id}");
                }
            }

            using JsonDocument launcher = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
            foreach (JsonElement binding in launcher.RootElement.GetProperty("bindings").EnumerateArray())
            {
                string name = binding.GetProperty("name").GetString() ?? string.Empty;
                if (!GraphOpTestAttribution.IsPerOpShowcaseId(name))
                {
                    continue;
                }

                string op = name[GraphOpTestAttribution.PerOpShowcasePrefix.Length..];
                if (!liveOps.Contains(op))
                {
                    failures.Add($"orphan binding {name}");
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        private static List<CoverageEntry> LoadCoverageEntries(string repoRoot)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, RegistryRelativePath)));
            var entries = new List<CoverageEntry>();
            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var refs = new List<string>();
                if (entry.TryGetProperty("unitTestRefs", out JsonElement refsEl) &&
                    refsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in refsEl.EnumerateArray())
                    {
                        string? token = item.GetString();
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            refs.Add(token);
                        }
                    }
                }

                entries.Add(new CoverageEntry(
                    entry.GetProperty("op").GetString() ?? string.Empty,
                    entry.GetProperty("status").GetString() ?? string.Empty,
                    entry.GetProperty("showcaseId").GetString() ?? string.Empty,
                    refs));
            }

            return entries;
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

        private readonly record struct CoverageEntry(
            string Op,
            string Status,
            string ShowcaseId,
            List<string> UnitTestRefs);
    }
}
