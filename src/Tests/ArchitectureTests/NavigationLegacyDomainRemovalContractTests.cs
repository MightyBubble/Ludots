using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavigationLegacyDomainRemovalContractTests
    {
        [Test]
        public void Repository_DoesNotReferenceRemovedNavigationExecutionDomain()
        {
            string repoRoot = FindRepoRoot();
            string[] tokens =
            {
                Join("Navigation", "2D"),
                Join("Crowd", "Flow", "2D"),
                Join("Nav", "Goal", "2D"),
                Join("Nav", "Agent", "2D"),
                Join("Nav", "Kinematics", "2D"),
                Join("Nav", "Desired", "Velocity", "2D"),
                Join("Navigation", "2D", "Steering", "System", "2D"),
                Join("Navigation", "2D", "Simulation", "System", "2D"),
                "navigation" + "2d",
                "nav" + "2d",
            };

            var hits = Scan(
                repoRoot,
                repositoryRoots: ActiveImplementationRoots,
                tokens,
                ignoreExternalReferenceLines: true);

            Assert.That(
                hits,
                Is.Empty,
                "Removed navigation execution domain tokens must not appear in active source or mod assets:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void Repository_DoesNotInjectDefaultNavLayerOrDirtyBakeFallback()
        {
            string repoRoot = FindRepoRoot();
            string[] tokens =
            {
                "fallback" + "To" + "Full" + "When" + "No" + "Targets",
                "new NavLayerConfig { Id = " + Quote("Ground"),
            };

            var hits = Scan(
                repoRoot,
                repositoryRoots: ActiveImplementationRoots,
                tokens,
                ignoreExternalReferenceLines: true);

            Assert.That(
                hits,
                Is.Empty,
                "Nav bake/editor source and mod asset paths must fail fast instead of injecting default layers or full-bake fallbacks:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void Repository_MassNavigationDomainSources_DoNotReintroduceRemovedPrefixIdentifiers()
        {
            string repoRoot = FindRepoRoot();
            string[] tokens =
            {
                Join("Mass", "Crowd"),
                Join("Mass", "Flow"),
                Join("Mass", "Move"),
            };

            var hits = Scan(
                repoRoot,
                new[] { "src", "mods" },
                tokens,
                ignoreExternalReferenceLines: true);

            Assert.That(
                hits,
                Is.Empty,
                "Removed mass-navigation prefix identifiers must not appear in source or mod assets:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        private static readonly string[] ActiveImplementationRoots =
        {
            "src",
            "mods"
        };

        private static List<string> Scan(
            string repoRoot,
            IReadOnlyList<string>? repositoryRoots,
            IReadOnlyList<string> tokens,
            bool ignoreExternalReferenceLines)
        {
            var hits = new List<string>();
            foreach (string file in EnumerateRepositoryFiles(repoRoot, repositoryRoots))
            {
                foreach ((int lineNumber, string line) in SourceTextScanner.ReadCodeLines(file))
                {
                    if (ignoreExternalReferenceLines && IsExternalReferenceLine(line))
                    {
                        continue;
                    }

                    for (int i = 0; i < tokens.Count; i++)
                    {
                        string token = tokens[i];
                        if (line.Contains(token, StringComparison.Ordinal))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: {token}");
                        }
                    }
                }
            }

            return hits;
        }

        private static IEnumerable<string> EnumerateRepositoryFiles(string repoRoot, IReadOnlyList<string>? repositoryRoots)
        {
            var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                ".tmp",
                "artifacts",
                "bin",
                "obj"
            };

            return Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    string relative = ToRepoRelativePath(repoRoot, file);
                    if (repositoryRoots != null &&
                        !repositoryRoots.Any(root => IsUnderRepositoryRoot(relative, root)))
                    {
                        return false;
                    }

                    if (relative.Equals("src/Tests/ArchitectureTests/NavigationLegacyDomainRemovalContractTests.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (relative.StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    string[] segments = relative.Split('/');
                    return !segments.Any(segment => excludedDirectories.Contains(segment));
                });
        }

        private static bool IsUnderRepositoryRoot(string relative, string root)
        {
            return relative.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExternalReferenceLine(string line)
        {
            return line.Contains("://", StringComparison.Ordinal) &&
                   line.Contains("wiki", StringComparison.OrdinalIgnoreCase);
        }

        private static string Join(params string[] parts)
        {
            return string.Concat(parts);
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        }
    }
}
