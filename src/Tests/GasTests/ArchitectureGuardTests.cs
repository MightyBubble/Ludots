using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ludots.Core.Engine;
using NUnit.Framework;

namespace GasTests
{
    public class ArchitectureGuardTests
    {
        [Test]
        public void SystemGroup_MustMatchDesignDocument()
        {
            var expected = new[]
            {
                nameof(SystemGroup.SchemaUpdate),
                nameof(SystemGroup.InputCollection),
                nameof(SystemGroup.PostMovement),
                nameof(SystemGroup.AbilityActivation),
                nameof(SystemGroup.EffectProcessing),
                nameof(SystemGroup.AttributeCalculation),
                nameof(SystemGroup.DeferredTriggerCollection),
                nameof(SystemGroup.Cleanup),
                nameof(SystemGroup.EventDispatch),
                nameof(SystemGroup.ClearPresentationFlags)
            };

            Assert.That(Enum.GetNames<SystemGroup>(), Is.EquivalentTo(expected));
        }

        [Test]
        public void Codebase_MustNotContainCompatibilityOrFallbackMarkers()
        {
            var repoRoot = FindRepoRoot();
            var directories = new[]
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "src", "Mods"),
                Path.Combine(repoRoot, "src", "Platforms")
            };

            var patterns = new[]
            {
                new Regex("向后兼容", RegexOptions.Compiled | RegexOptions.CultureInvariant),
                new Regex("backward\\s+compatibility", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new Regex("keep\\s+compatibility", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new Regex("legacy\\s+support", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new Regex("legacy\\s+alias", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new Regex("\\[Obsolete\\(\"Merged\\s+into", RegexOptions.Compiled | RegexOptions.CultureInvariant),
                new Regex("\\[Obsolete\\(\"Removed\\s+in\\s+favor", RegexOptions.Compiled | RegexOptions.CultureInvariant)
            };

            var hits = new List<string>();

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        for (int p = 0; p < patterns.Length; p++)
                        {
                            if (patterns[p].IsMatch(line))
                            {
                                hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{i + 1}: {line.Trim()}");
                                break;
                            }
                        }
                    }
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail("Found forbidden compatibility/fallback markers:\n" + string.Join("\n", hits));
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
