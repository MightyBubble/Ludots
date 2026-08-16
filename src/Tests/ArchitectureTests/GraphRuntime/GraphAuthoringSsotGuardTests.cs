using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.GraphRuntime
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphAuthoringSsotGuardTests
    {
        [Test]
        public void FormalGraphsJson_MustNotUseNodesNext()
        {
            string root = FindRepoRoot();
            var offenders = new List<string>();
            foreach (string path in Directory.EnumerateFiles(root, "graphs.json", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement graph in doc.RootElement.EnumerateArray())
                {
                    if (!graph.TryGetProperty("nodes", out JsonElement nodes)) continue;
                    string id = graph.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? "?" : "?";
                    foreach (JsonElement node in nodes.EnumerateArray())
                    {
                        if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("next", out _))
                        {
                            offenders.Add($"{path} :: {id}");
                            break;
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Formal graphs.json must not use nodes[].next (issue #861). Offenders:\n" +
                string.Join("\n", offenders.Take(20)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                dir = dir.Parent;
            }

            Assert.That(dir, Is.Not.Null, "repo root not found");
            return dir!.FullName;
        }
    }
}
