using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class MassNavigationBoundaryContractTests
    {
        [Test]
        public void CoreMassNavigation_DoesNotReadInputSelectionOrSubmitOrdersDirectly()
        {
            string repoRoot = FindRepoRoot();
            string massNavigationRoot = Path.Combine(repoRoot, "src", "Core", "MassNavigation");
            Assert.That(Directory.Exists(massNavigationRoot), Is.True, $"Missing MassNavigation root: {massNavigationRoot}");

            string[] forbiddenTokens =
            {
                "CoreServiceKeys.AuthoritativeInput",
                "SelectionRuntime",
                "SelectionContextRuntime",
                "MassNavigationLocalCommandInputSystem",
                "SubmitOrder(",
            };

            var hits = new List<string>();
            foreach (string file in Directory.EnumerateFiles(massNavigationRoot, "*.cs", SearchOption.AllDirectories))
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;
                    for (int i = 0; i < forbiddenTokens.Length; i++)
                    {
                        string token = forbiddenTokens[i];
                        if (line.Contains(token, StringComparison.Ordinal))
                        {
                            hits.Add($"{Path.GetRelativePath(repoRoot, file).Replace('\\', '/')}:{lineNumber}: {token}");
                        }
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Core MassNavigation must consume command-source collections and OrderBuffer ingestion; input reading, SelectionRuntime reads, and direct OrderBuffer SubmitOrder calls belong outside this core runtime:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void ProductionOrderProducers_DoNotBypassOrderQueueWithDirectOrderBufferSubmit()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "src", "Tools"),
                Path.Combine(repoRoot, "mods"),
            };

            string allowedDefinition = Path.Combine(
                repoRoot,
                "src",
                "Core",
                "Gameplay",
                "GAS",
                "Systems",
                "OrderBufferSystem.cs");

            var hits = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsGeneratedBuildOutput(file) ||
                        Path.GetFullPath(file).Equals(allowedDefinition, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int lineNumber = 0;
                    foreach (string line in File.ReadLines(file))
                    {
                        lineNumber++;
                        if (line.Contains(".SubmitOrder(", StringComparison.Ordinal))
                        {
                            hits.Add($"{Path.GetRelativePath(repoRoot, file).Replace('\\', '/')}:{lineNumber}: {line.Trim()}");
                        }
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Production order producers must enqueue through OrderQueue; direct OrderBufferSystem.SubmitOrder calls bypass the unified intake:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
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

        private static bool IsGeneratedBuildOutput(string file)
        {
            string fullPath = Path.GetFullPath(file);
            return fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                   fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
    }
}
