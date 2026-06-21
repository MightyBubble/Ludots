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
                nameof(SystemGroup.RuntimeEntityBinding),
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
                Path.Combine(repoRoot, "mods"),
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

        [Test]
        public void Issue200_KnowledgeProjectionConsumers_DoNotTraverseRelationGrantedCollectionsOutsideCoreResolver()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core", "Input", "Selection"),
                Path.Combine(repoRoot, "src", "Core", "Presentation", "Minimap"),
                Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems"),
                Path.Combine(repoRoot, "mods", "capabilities", "participant_view", "ParticipantViewCapabilityMod", "Runtime"),
                Path.Combine(repoRoot, "mods", "capabilities", "participant_view", "ParticipantViewCapabilityMod", "UI")
            };
            string[] forbidden =
            {
                "KnowledgeRelationCollectionGrantStore",
                "KnowledgeRelationCollectionProjector",
                ".ProjectOutgoing(",
                ".CopyEntities(",
                "CopyEntities("
            };

            List<string> hits = FindForbiddenSourceTokens(repoRoot, directories, forbidden);

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Issue #200 expects presentation/input/participant-view consumers to use KnowledgeProjectionResolver instead of traversing relation-granted collections directly:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Issue200_ShowcaseKnowledgeInstaller_IsMetadataDrivenAndDoesNotUseTeamRelationshipShortcuts()
        {
            var repoRoot = FindRepoRoot();
            string installerPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardParticipantViewsMod",
                "ParticipantViewKnowledgeShowcaseInstaller.cs");
            Assert.That(File.Exists(installerPath), Is.True, $"Missing {installerPath}");

            string source = File.ReadAllText(installerPath);
            string[] forbidden =
            {
                "TeamManager.",
                "GetRelationship(",
                "SetRelationship(",
                "\"player:1\"",
                "\"team:1\"",
                "\"unit-"
            };

            var hits = new List<string>();
            for (int i = 0; i < forbidden.Length; i++)
            {
                if (source.Contains(forbidden[i], StringComparison.Ordinal))
                {
                    hits.Add(forbidden[i]);
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Issue #200 expects the participant visibility showcase installer to consume authored metadata and Core resolver services, not hardcoded participants or TeamManager visibility shortcuts.");
        }

        [Test]
        public void Issue200_CoreKnowledgeProjection_RemainsEntityCentricWithoutPlayerOrTeamVisibilityPaths()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core", "Knowledge")
            };
            string[] forbidden =
            {
                "PlayerId",
                "TeamId",
                "PlayerOwner",
                "TeamIdentity",
                "TeamManager",
                "RelationshipFilter"
            };

            List<string> hits = FindForbiddenSourceTokens(repoRoot, directories, forbidden);

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Issue #200 expects Core Knowledge Projection to land on entity projections instead of player/team visibility shortcuts:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Issue200_KnowledgeHotPaths_DoNotUseLinqOrIteratorBlocks()
        {
            var repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionResolver.cs"),
                Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionConsumer.cs"),
                Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeRelationCollectionGrants.cs"),
                Path.Combine(repoRoot, "src", "Core", "Input", "Selection", "SelectionEligibility.cs"),
                Path.Combine(repoRoot, "src", "Core", "Input", "Selection", "CurrentSelectionApplySystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "Input", "Selection", "GasSelectionResponseSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "Presentation", "Minimap", "MinimapRuntime.cs"),
                Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "TabTargetCycleSystem.cs"),
                Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "LocalOrderSourceHelper.cs")
            };
            string[] forbidden =
            {
                "using System.Linq",
                ".Where(",
                ".Select(",
                ".ToList(",
                "yield return",
                "IEnumerator",
                "IEnumerable<"
            };

            var hits = new List<string>();
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string file = files[fileIndex];
                Assert.That(File.Exists(file), Is.True, $"Missing hot-path source file {file}");
                AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Issue #200 expects warmed knowledge/input/minimap hot paths to stay SoA and avoid LINQ/iterator allocation patterns:\n" +
                    string.Join("\n", hits));
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

        private static List<string> FindForbiddenSourceTokens(
            string repoRoot,
            IReadOnlyList<string> directories,
            IReadOnlyList<string> forbidden)
        {
            var hits = new List<string>();
            for (int i = 0; i < directories.Count; i++)
            {
                string dir = directories[i];
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
                }
            }

            return hits;
        }

        private static void AppendForbiddenSourceTokens(
            string repoRoot,
            string file,
            IReadOnlyList<string> forbidden,
            List<string> hits)
        {
            string[] lines = File.ReadAllLines(file);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                for (int tokenIndex = 0; tokenIndex < forbidden.Count; tokenIndex++)
                {
                    string token = forbidden[tokenIndex];
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineIndex + 1}: {token}: {line.Trim()}");
                        break;
                    }
                }
            }
        }
    }
}
