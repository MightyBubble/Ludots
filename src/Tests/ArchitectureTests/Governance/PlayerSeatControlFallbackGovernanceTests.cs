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
    public sealed class PlayerSeatControlFallbackGovernanceTests
    {
        [Test]
        public void ProductionCode_HasNoHiddenDefaultLocalSeatOrPlayerFallbacks()
        {
            string repoRoot = FindRepoRoot();
            var rules = new[]
            {
                new ForbiddenPattern("EnsureDefaultSoleSeat", @"\bEnsureDefaultSoleSeat\b"),
                new ForbiddenPattern("default playerId 1 parameter", @"\bplayerId\s*=\s*1\b"),
                new ForbiddenPattern("default seat.0 parameter", @"\bseatId\s*=\s*""seat\.0"""),
                new ForbiddenPattern("MapLaunchContext.Create(int)", @"MapLaunchContext\s+Create\s*\(\s*int\s+playerId\b"),
            };
            var hits = new List<string>();

            foreach (string file in EnumerateProductionCodeFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                for (int i = 0; i < rules.Length; i++)
                {
                    Match match = Regex.Match(text, rules[i].Pattern, RegexOptions.CultureInvariant);
                    if (match.Success)
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}: {rules[i].Name}");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Player/seat/control entry points must not recreate hidden PlayerId 1 or seat.0 fallbacks:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void EntityCommandPanel_TargetResolution_DoesNotTreatSeatRegistryServiceAsLocalActorAlias()
        {
            string repoRoot = FindRepoRoot();
            string file = Path.Combine(repoRoot, "src", "Core", "Commands", "EntityCommandPanelCommands.cs");
            string text = File.ReadAllText(file);

            Assert.That(
                text,
                Does.Contain("\"solePossessedRep\""),
                "Command panels may target the explicit solePossessedRep context key.");
            Assert.That(
                text,
                Does.Not.Contain("string.Equals(contextKey, CoreServiceKeys.ClientLocalSeatRegistry.Name"),
                "ClientLocalSeatRegistry is a service name, not an entity target alias.");
        }

        [Test]
        public void ActiveAiAssets_DoNotSubmitOrdersAsPlayerZero()
        {
            string repoRoot = FindRepoRoot();
            var hits = new List<string>();
            var playerZero = new Regex(@"""PlayerId""\s*:\s*0\b", RegexOptions.CultureInvariant);

            foreach (string file in EnumerateActiveJsonFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                Match match = playerZero.Match(text);
                if (match.Success)
                {
                    hits.Add(ToRepoRelativePath(repoRoot, file));
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "PlayerId 0 is the no-player sentinel and must not author AI/order assets:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void ActiveConfigsAndReferenceDocs_DoNotUseRemovedStartupLocalPlayerFields()
        {
            string repoRoot = FindRepoRoot();
            var hits = new List<string>();
            var removedStartupField = new Regex(
                @"startup(Local|Selected)PlayerId",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            foreach (string file in EnumerateStartupFieldScanFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                Match match = removedStartupField.Match(text);
                if (match.Success)
                {
                    hits.Add($"{ToRepoRelativePath(repoRoot, file)}: {match.Value}");
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Active configs/reference docs must use startupLocalSeats, not removed startup player fields:\n" +
                string.Join("\n", hits));
        }

        private static IEnumerable<string> EnumerateProductionCodeFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "src", "Libraries", "Ludots.AgentBridge"),
                Path.Combine(repoRoot, "mods"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static IEnumerable<string> EnumerateActiveJsonFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "mods"),
                Path.Combine(repoRoot, "src", "Core"),
            };
            return EnumerateFiles(roots, "*.json");
        }

        private static IEnumerable<string> EnumerateStartupFieldScanFiles(string repoRoot)
        {
            foreach (string file in EnumerateActiveJsonFiles(repoRoot))
            {
                yield return file;
            }

            string referenceRoot = Path.Combine(repoRoot, "gitbook", "reference");
            if (!Directory.Exists(referenceRoot))
            {
                yield break;
            }

            foreach (string file in Directory.EnumerateFiles(referenceRoot, "*.md", SearchOption.AllDirectories)
                .Where(IsNotBuildArtifact))
            {
                yield return file;
            }
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
    }
}
