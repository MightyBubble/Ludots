using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class TerminologyGovernanceTests
    {
        [Test]
        public void ProductionCode_DoesNotJuxtaposeClientWithMachineInIdentifiers()
        {
            string repoRoot = FindRepoRoot();
            var rules = new[]
            {
                new ForbiddenPattern("MachineClient* identifier", @"\bMachineClient\w*"),
                new ForbiddenPattern("ClientMachine* identifier", @"\bClientMachine\w*"),
            };
            var hits = new List<string>();

            foreach (string file in EnumerateTerminologyScanFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                for (int i = 0; i < rules.Length; i++)
                {
                    foreach (Match match in Regex.Matches(text, rules[i].Pattern, RegexOptions.CultureInvariant))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}: {match.Value} ({rules[i].Name})");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 1 (#902 3.5): 'client' must never denote a machine; identifiers must not juxtapose Client with Machine. See gitbook/architecture/terminology.md:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void Adapters_DoNotRegisterDeviceServicesIntoAppLevelContainer()
        {
            string repoRoot = FindRepoRoot();
            var deviceServiceRegistration = new Regex(
                @"SetService\s*\(\s*CoreServiceKeys\.(?<key>\w*Device\w*|SyntheticInput)\b",
                RegexOptions.CultureInvariant);
            var hits = new List<string>();

            foreach (string file in EnumerateAdapterCodeFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                string relativePath = ToRepoRelativePath(repoRoot, file);
                foreach (Match match in deviceServiceRegistration.Matches(text))
                {
                    string key = match.Groups["key"].Value;
                    DeviceServiceAllowance allowance = DeviceServiceAllowlist
                        .FirstOrDefault(entry => entry.Path == relativePath && entry.Key == key);
                    if (allowance.Path is null)
                    {
                        hits.Add($"{relativePath}: CoreServiceKeys.{key}");
                    }
                    else if (CountOccurrences(text, match.Value) > allowance.AllowedCount)
                    {
                        hits.Add($"{relativePath}: CoreServiceKeys.{key} registered {CountOccurrences(text, match.Value)}x (allowed {allowance.AllowedCount})");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 2 (#902 3.5): device instances are held by Seat; Adapters must not register device services into the App-level container. " +
                "The allowlist below is shrink-only (P3 collapses it to zero, see #1058); new device-service registrations are forbidden:\n" +
                string.Join("\n", hits));
        }

        private static readonly DeviceServiceAllowance[] DeviceServiceAllowlist =
        {
            new("src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs", "SyntheticInput", 1),
        };

        private static IEnumerable<string> EnumerateTerminologyScanFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "mods"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static IEnumerable<string> EnumerateAdapterCodeFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Adapters"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static IEnumerable<string> EnumerateFiles(IEnumerable<string> roots, string pattern)
        {
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(IsNotBuildArtifact))
                {
                    yield return file;
                }
            }
        }

        private static int CountOccurrences(string text, string value) =>
            Regex.Matches(text, Regex.Escape(value), RegexOptions.CultureInvariant).Count;

        private static bool IsNotBuildArtifact(string file)
        {
            string normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "gitbook")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static string ToRepoRelativePath(string repoRoot, string absolutePath) =>
            Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

        private readonly record struct ForbiddenPattern(string Name, string Pattern);

        private readonly record struct DeviceServiceAllowance(string Path, string Key, int AllowedCount);
    }
}
