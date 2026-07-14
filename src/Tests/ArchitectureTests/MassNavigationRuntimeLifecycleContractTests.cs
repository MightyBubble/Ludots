using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class MassNavigationRuntimeLifecycleContractTests
    {
        [Test]
        public void MassNavigationSystems_DoNotRetainSimulationRuntimeInstances()
        {
            string repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationAgentMetadataSyncSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationAuthoredAgentBindingSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationEnvironmentBindingSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationLocomotionAnimatorParamSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationOrderIngestionSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Systems", "MassNavigationSimulationStepSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Runtime", "MassNavigationRuntime.cs"),
            };

            string[] forbiddenTokens =
            {
                "private readonly MassNavigationSimulationRuntime _simulation",
            };

            List<string> hits = files
                .SelectMany(file => FindTokenHits(repoRoot, file, forbiddenTokens))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "MassNavigation fixed-step and binding systems must resolve the current prepared runtime through RuntimeBinding; constructor-captured simulations can execute stale map state after unload/reload:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void Showcases_DoNotPrivatelyResetMassNavigationRuntimeOnUnload()
        {
            string repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "mods", "showcases", "formation_capability", "FormationCapabilityShowcaseMod", "Runtime", "FormationCapabilityShowcaseRuntime.cs"),
            };

            string[] forbiddenTokens =
            {
                ".ResetRuntimeState(",
            };

            List<string> hits = files
                .SelectMany(file => FindTokenHits(repoRoot, file, forbiddenTokens))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Map-scoped MassNavigation unload must be owned by MassNavigationRuntime; Showcase unload handlers may clear their own presentation/adapter state, but must not privately reset the shared execution runtime:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void MassNavigationRuntime_DoesNotPublishPreparedDuringMapFocus()
        {
            string repoRoot = FindRepoRoot();
            string runtime = Path.Combine(repoRoot, "src", "Core", "MassNavigation", "Runtime", "MassNavigationRuntime.cs");
            List<string> hits = FindTokenHits(
                    repoRoot,
                    runtime,
                    new[] { "binding.MarkPrepared(mapId, simulation)" })
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "MassNavigation runtime must not become Prepared during MapFocused. Authored agents, environment, relationship projection and capacities are bound in RuntimeEntityBinding, so preparation must be published only after those binding passes complete:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        private static IEnumerable<string> FindTokenHits(string repoRoot, string file, IReadOnlyList<string> tokens)
        {
            Assert.That(File.Exists(file), Is.True, $"Missing lifecycle contract source file: {file}");
            string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        yield return $"{relative}:{lineNumber}: {token}";
                    }
                }
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
