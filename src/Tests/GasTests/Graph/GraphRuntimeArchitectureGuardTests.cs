using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    public class GraphRuntimeArchitectureGuardTests
    {
        [Test]
        public void GraphRuntime_Code_MustNotReference_GAS()
        {
            var repoRoot = FindRepoRoot();
            var dir = Path.Combine(repoRoot, "src", "Core", "GraphRuntime");

            if (!Directory.Exists(dir))
            {
                Assert.Fail("Missing src/Core/GraphRuntime directory. Phase 0 requires creating it.");
            }

            var forbidden = new[]
            {
                "Ludots.Core.Gameplay.GAS",
                "Gameplay.GAS"
            };

            var hits = new List<string>();

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    for (int f = 0; f < forbidden.Length; f++)
                    {
                        if (line.Contains(forbidden[f], StringComparison.Ordinal))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{i + 1}: {line.Trim()}");
                            break;
                        }
                    }
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail("GraphRuntime references GAS:\n" + string.Join("\n", hits));
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static string ToRepoRelativePath(string repoRoot, string absolutePath)
        {
            var relative = Path.GetRelativePath(repoRoot, absolutePath);
            return relative.Replace('\\', '/');
        }
    }
}

