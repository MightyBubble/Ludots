using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Case E collection keys are graph node <c>collectionKey</c> declarations.
    /// Interaction profiles do not grow a parallel collection field.
    /// </summary>
    [TestFixture]
    public sealed class CaseECollectionKeyContractTests
    {
        private static readonly HashSet<string> CollectionOps = new(StringComparer.Ordinal)
        {
            "WriteCollection",
            "QueryFromCollection",
            "QueryScreenRegionCollection",
            "BindQueryCollection",
            "SnapToNearestInCollection",
            "SubmitCommandIntent",
            "SubmitCast",
            "SubmitEngageBatch",
        };

        private static readonly HashSet<string> SubmitOps = new(StringComparer.Ordinal)
        {
            "SubmitCommandIntent",
            "SubmitCast",
            "SubmitEngageBatch",
        };

        [Test]
        public void CaseEProfiles_DeclareNoCollectionFields()
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();
            foreach (string file in CaseEJsonFiles(repoRoot))
            {
                if (!file.EndsWith("interaction_context_profiles.json", StringComparison.Ordinal))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("profiles", out JsonElement profiles) ||
                    profiles.ValueKind != JsonValueKind.Array)
                {
                    offenders.Add($"{file}: missing profiles array");
                    continue;
                }

                int index = 0;
                foreach (JsonElement profile in profiles.EnumerateArray())
                {
                    foreach (JsonProperty property in profile.EnumerateObject())
                    {
                        if (property.Name.Contains("collection", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(property.Name, "activeEntityViewKey", StringComparison.OrdinalIgnoreCase))
                        {
                            offenders.Add($"{file} profiles[{index}].{property.Name}");
                        }
                    }

                    index++;
                }
            }

            Assert.That(offenders, Is.Empty,
                "Case E interaction profiles must not declare collection fields. Declare collectionKey on the graph node; install persists it on the entity interaction instance.\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void CaseEGraphs_DeclareCollectionKeysOnlyOnCollectionOps()
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();
            var writtenKeys = new HashSet<string>(StringComparer.Ordinal);
            var submitKeys = new List<string>();
            foreach (string file in CaseEJsonFiles(repoRoot))
            {
                if (!file.Contains($"{Path.DirectorySeparatorChar}GAS{Path.DirectorySeparatorChar}graphs{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !file.Contains("/GAS/graphs/", StringComparison.Ordinal))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (JsonElement graph in EnumerateGraphs(doc.RootElement))
                {
                    if (!graph.TryGetProperty("nodes", out JsonElement nodes) || nodes.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement node in nodes.EnumerateArray())
                    {
                        string op = node.TryGetProperty("op", out JsonElement opElement)
                            ? opElement.GetString() ?? string.Empty
                            : string.Empty;
                        string nodeId = node.TryGetProperty("id", out JsonElement idElement)
                            ? idElement.GetString() ?? string.Empty
                            : string.Empty;
                        bool hasCollectionKey = false;
                        foreach (JsonProperty property in node.EnumerateObject())
                        {
                            if (!property.Name.Contains("collection", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!string.Equals(property.Name, "collectionKey", StringComparison.Ordinal))
                            {
                                offenders.Add($"{Path.GetFileName(file)} node '{nodeId}' field '{property.Name}'");
                                continue;
                            }

                            hasCollectionKey = true;
                            string key = property.Value.GetString() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(key))
                            {
                                offenders.Add($"{Path.GetFileName(file)} node '{nodeId}' has an empty collectionKey");
                                continue;
                            }

                            if (!CollectionOps.Contains(op))
                            {
                                offenders.Add($"{Path.GetFileName(file)} node '{nodeId}' op '{op}' must not declare collectionKey");
                                continue;
                            }

                            if (SubmitOps.Contains(op))
                            {
                                submitKeys.Add($"{Path.GetFileName(file)}:{nodeId}:{key}");
                            }
                            else
                            {
                                writtenKeys.Add(key);
                            }
                        }

                        if (SubmitOps.Contains(op) && !hasCollectionKey)
                        {
                            offenders.Add($"{Path.GetFileName(file)} node '{nodeId}' op '{op}' must declare collectionKey");
                        }
                    }
                }
            }

            foreach (string submit in submitKeys)
            {
                string key = submit[(submit.LastIndexOf(':') + 1)..];
                if (!writtenKeys.Contains(key))
                {
                    offenders.Add($"{submit} is not declared on a Case E collection op");
                }
            }

            Assert.That(offenders, Is.Empty,
                "Case E collection keys belong on collection graph ops. Submit ops name the key the entity interaction instance persists; some other Case E graph must declare the same key.\n" +
                string.Join("\n", offenders));
        }

        private static IEnumerable<string> CaseEJsonFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "mods", "capabilities", "input", "SelectionInteractionMod"),
                Path.Combine(repoRoot, "mods", "showcases", "case_e_selection"),
            };
            foreach (string root in roots)
            {
                Assert.That(Directory.Exists(root), Is.True, root);
                foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    string normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
                    {
                        continue;
                    }

                    yield return file;
                }
            }
        }

        private static IEnumerable<JsonElement> EnumerateGraphs(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement graph in root.EnumerateArray())
                {
                    yield return graph;
                }

                yield break;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                yield return root;
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
