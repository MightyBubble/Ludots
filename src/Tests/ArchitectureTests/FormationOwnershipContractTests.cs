using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class FormationOwnershipContractTests
    {
        private const string FormationShowcaseRoot = "mods/showcases/formation_capability/FormationCapabilityShowcaseMod";

        private static readonly string[] FormationOrderKeys =
        {
            "formationMove",
            "formationRotate",
        };

        private static readonly string[] FormationStateTokens =
        {
            "MassNavigationFormation",
            "FormationCapabilityShowcaseFormationAgent",
            "FormationCapabilityShowcaseFormationSoldier",
            "FormationCapabilityShowcaseCommandState",
            "FormationCapabilityShowcaseFormationState",
            "FormationCapabilityShowcaseFormationOutline",
        };

        [Test]
        public void CoreMassNavigation_DoesNotOwnFormationBusinessVocabulary()
        {
            string repoRoot = FindRepoRoot();
            string massNavigationRoot = Path.Combine(repoRoot, "src", "Core", "MassNavigation");

            List<string> hits = EnumerateFiles(massNavigationRoot, ".cs", ".json")
                .SelectMany(file => FindFormationVocabularyHits(repoRoot, file))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Core MassNavigation must execute navigation without owning Formation capability vocabulary:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void FormationMoveAndRotateOrderKeys_AreOwnedOnlyByFormationShowcase()
        {
            string repoRoot = FindRepoRoot();
            var hitsByKey = FormationOrderKeys.ToDictionary(
                key => key,
                _ => new List<string>(),
                StringComparer.Ordinal);

            foreach (string file in EnumerateRepositorySourceAndConfigFiles(repoRoot))
            {
                string relative = ToRepoRelativePath(repoRoot, file);
                if (IsTestPath(relative))
                {
                    continue;
                }

                string text = File.ReadAllText(file);
                foreach (string key in FormationOrderKeys)
                {
                    if (ContainsIdentifierToken(text, key))
                    {
                        hitsByKey[key].Add(relative);
                    }
                }
            }

            foreach (string key in FormationOrderKeys)
            {
                Assert.That(
                    hitsByKey[key],
                    Is.Not.Empty,
                    $"Formation showcase must explicitly author its '{key}' order contract.");

                string[] offenders = hitsByKey[key]
                    .Where(path => !IsUnderRepositoryRoot(path, FormationShowcaseRoot))
                    .ToArray();
                Assert.That(
                    offenders,
                    Is.Empty,
                    $"'{key}' is Formation showcase business vocabulary and must not leak into Core, capabilities, or other Mods:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, offenders));
            }
        }

        [Test]
        public void NonFormationConfigs_DoNotAuthorFormationStateOrOrders()
        {
            string repoRoot = FindRepoRoot();
            var forbiddenTokens = FormationStateTokens.Concat(FormationOrderKeys).ToArray();
            var hits = new List<string>();

            foreach (string root in new[] { "assets", "mods" })
            {
                string absoluteRoot = Path.Combine(repoRoot, root);
                foreach (string file in EnumerateFiles(absoluteRoot, ".json"))
                {
                    string relative = ToRepoRelativePath(repoRoot, file);
                    if (IsUnderRepositoryRoot(relative, FormationShowcaseRoot))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(file);
                    foreach (string token in forbiddenTokens)
                    {
                        if (ContainsIdentifierToken(text, token))
                        {
                            hits.Add($"{relative}: {token}");
                        }
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Non-Formation default/config assets must not author Formation runtime state or Formation-only orders:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        private static IEnumerable<string> FindFormationVocabularyHits(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            if (Path.GetFileName(file).Contains("Formation", StringComparison.Ordinal))
            {
                yield return relative + ": file name";
            }

            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Contains("Formation", StringComparison.Ordinal) ||
                    Regex.IsMatch(line, @"\bformation\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    yield return $"{relative}:{lineNumber}: {line.Trim()}";
                }
            }
        }

        private static bool ContainsIdentifierToken(string text, string token)
        {
            return Regex.IsMatch(
                text,
                $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> EnumerateRepositorySourceAndConfigFiles(string repoRoot)
        {
            foreach (string root in new[] { "src", "mods", "assets" })
            {
                foreach (string file in EnumerateFiles(Path.Combine(repoRoot, root), ".cs", ".json"))
                {
                    yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateFiles(string root, params string[] extensions)
        {
            Assert.That(Directory.Exists(root), Is.True, $"Missing repository directory: {root}");
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                ".tmp",
                "artifacts",
                "bin",
                "obj",
            };

            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => extensionSet.Contains(Path.GetExtension(file)))
                .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => excludedDirectories.Contains(segment)));
        }

        private static bool IsTestPath(string relativePath)
        {
            return IsUnderRepositoryRoot(relativePath, "src/Tests");
        }

        private static bool IsUnderRepositoryRoot(string relativePath, string root)
        {
            return relativePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   relativePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
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

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        }
    }
}
