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

        [Test]
        public void ExchangeCore_RemainsSemanticAndDoesNotAdoptScenarioNames()
        {
            var repoRoot = FindRepoRoot();
            string exchangeDir = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange");
            Assert.That(Directory.Exists(exchangeDir), Is.True, $"Missing {exchangeDir}");

            string[] forbidden =
            {
                "merchant",
                "vendor",
                "forge",
                "recipe",
                "trade"
            };

            var hits = new List<string>();
            foreach (var file in Directory.EnumerateFiles(exchangeDir, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    for (int tokenIndex = 0; tokenIndex < forbidden.Length; tokenIndex++)
                    {
                        if (line.Contains(forbidden[tokenIndex], StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineIndex + 1}: {forbidden[tokenIndex]}: {line.Trim()}");
                            break;
                        }
                    }
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Core Exchange must stay scenario-agnostic; commerce, crafting, and diplomacy names belong in config/mod layers:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void ExchangeHotPath_DoesNotUseLinqOrTransientCollections()
        {
            var repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeRuntime.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeScopedOperationStore.cs")
            };
            string[] forbidden =
            {
                "using System.Linq",
                ".Where(",
                ".Select(",
                ".ToArray(",
                ".ToList(",
                "yield return",
                "IEnumerator",
                "IEnumerable<",
                "new List<",
                "new Dictionary<"
            };

            var hits = new List<string>();
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string file = files[fileIndex];
                Assert.That(File.Exists(file), Is.True, $"Missing Exchange hot-path source file {file}");
                string[] lines = File.ReadAllLines(file);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (line.Contains("private readonly", StringComparison.Ordinal) &&
                        (line.Contains("new List<", StringComparison.Ordinal) ||
                         line.Contains("new Dictionary<", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    for (int tokenIndex = 0; tokenIndex < forbidden.Length; tokenIndex++)
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

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Exchange runtime hot path should stay allocation-conscious and avoid LINQ/iterator patterns:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Issue244_EntityAssociationAdr_UsesIssueSsotAndDoesNotCreateRepositoryAdr()
        {
            var repoRoot = FindRepoRoot();

            string gitbookArchitectureIndex = Path.Combine(repoRoot, "gitbook", "architecture", "README.md");
            string agents = Path.Combine(repoRoot, "AGENTS.md");
            string claude = Path.Combine(repoRoot, "CLAUDE.md");

            Assert.That(File.Exists(gitbookArchitectureIndex), Is.True, $"Missing {gitbookArchitectureIndex}");
            Assert.That(File.Exists(agents), Is.True, $"Missing {agents}");
            Assert.That(File.Exists(claude), Is.True, $"Missing {claude}");

            string gitbookIndex = File.ReadAllText(gitbookArchitectureIndex);
            string agentsText = File.ReadAllText(agents);
            string claudeText = File.ReadAllText(claude);

            Assert.Multiple(() =>
            {
                Assert.That(gitbookIndex, Does.Contain("Entity Association Core"));
                Assert.That(gitbookIndex, Does.Contain("#239"));
                Assert.That(gitbookIndex, Does.Contain("#244"));
                Assert.That(gitbookIndex, Does.Contain("ADR SSOT"));
                Assert.That(agentsText, Does.Contain("#239"));
                Assert.That(agentsText, Does.Contain("#244"));
                Assert.That(claudeText, Does.Contain("#239"));
                Assert.That(claudeText, Does.Contain("#244"));
            });

            string adrDir = Path.Combine(repoRoot, "docs", "adr");
            Assert.That(Directory.Exists(adrDir), Is.True, $"Missing {adrDir}");

            var forbiddenAdrFiles = Directory
                .EnumerateFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    string lowerName = name.ToLowerInvariant();
                    if (lowerName.Contains("entity-association", StringComparison.Ordinal) ||
                        lowerName.Contains("aac", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    string text = File.ReadAllText(path);
                    return text.Contains("Entity Association Core", StringComparison.Ordinal) ||
                           text.Contains("AAC-", StringComparison.Ordinal);
                })
                .Select(path => ToRepoRelativePath(repoRoot, path))
                .ToArray();

            Assert.That(
                forbiddenAdrFiles,
                Is.Empty,
                "Issue #244 is the Entity Association Core ADR SSOT; do not create repository ADR files for AAC.");
        }

        [Test]
        public void Issue244_EntityAssociationAdr_DefinesUatShowcaseStandardAndExceptions()
        {
            var repoRoot = FindRepoRoot();
            string gitbookArchitectureIndex = Path.Combine(repoRoot, "gitbook", "architecture", "README.md");
            string text = File.ReadAllText(gitbookArchitectureIndex);

            string[] required =
            {
                "UAT showcase capability mod",
                "2.5",
                "#245",
                "#246",
                "#247",
                "#248",
                "#249",
                "#250",
                "#251",
                "#253",
                "#244",
                "#252",
                "#254",
                "#255"
            };

            var missing = required
                .Where(token => !text.Contains(token, StringComparison.Ordinal))
                .ToArray();

            Assert.That(
                missing,
                Is.Empty,
                "Issue #244 ADR clause 2.5 requires the architecture entry to point to the showcase-producing child issues and meta exceptions.");
        }

        [Test]
        public void Issue245_KnowledgeAndEntityCollection_UseSingleEntityKeyedSoaTable()
        {
            var repoRoot = FindRepoRoot();
            string tablePath = Path.Combine(repoRoot, "src", "Core", "Association", "EntityKeyedSoaTable.cs");
            string knowledgePath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionStore.cs");
            string collectionPath = Path.Combine(repoRoot, "src", "Core", "EntityCollections", "EntityCollectionStore.cs");

            Assert.That(File.Exists(tablePath), Is.True, $"Missing shared association base {tablePath}");
            Assert.That(File.Exists(knowledgePath), Is.True, $"Missing {knowledgePath}");
            Assert.That(File.Exists(collectionPath), Is.True, $"Missing {collectionPath}");

            string table = File.ReadAllText(tablePath);
            string knowledge = File.ReadAllText(knowledgePath);
            string collection = File.ReadAllText(collectionPath);

            Assert.Multiple(() =>
            {
                Assert.That(table, Does.Contain("public sealed class EntityKeyedSoaTable"));
                Assert.That(table, Does.Contain("EntityKeyedSoaKey"));
                Assert.That(table, Does.Contain("CopyByPrimary"));
                Assert.That(table, Does.Contain("Compact()"));
                Assert.That(knowledge, Does.Contain("EntityKeyedSoaTable<"));
                Assert.That(collection, Does.Contain("EntityKeyedSoaTable<"));
            });

            string[] duplicatedSparseTableTokens =
            {
                "_bucketHeads",
                "_entryNext",
                "_entrySlots",
                "BucketIndex(",
                "Rehash(",
                "LoadFactor"
            };

            var hits = new List<string>();
            AppendForbiddenSourceTokens(repoRoot, knowledgePath, duplicatedSparseTableTokens, hits);
            AppendForbiddenSourceTokens(repoRoot, collectionPath, duplicatedSparseTableTokens, hits);

            Assert.That(
                hits,
                Is.Empty,
                "AAC-2 (#245) requires KnowledgeProjectionStore and EntityCollectionStore to reuse EntityKeyedSoaTable instead of each hand-writing sparse hash/entry tables.");
        }

        [Test]
        public void Issue244_PendingAac4_SingleScopeKeyContract()
        {
            var repoRoot = FindRepoRoot();
            string scopePath = Path.Combine(repoRoot, "src", "Core", "Association", "ScopeKey.cs");
            string exchangeModelPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeModel.cs");
            string progressionDomainPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Progression", "ProgressionDomain.cs");
            string knowledgeResolverPath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionResolver.cs");

            Assert.That(File.Exists(scopePath), Is.True, $"Missing shared scope contract {scopePath}");
            Assert.That(File.Exists(exchangeModelPath), Is.True, $"Missing {exchangeModelPath}");
            Assert.That(File.Exists(progressionDomainPath), Is.True, $"Missing {progressionDomainPath}");
            Assert.That(File.Exists(knowledgeResolverPath), Is.True, $"Missing {knowledgeResolverPath}");

            string scope = File.ReadAllText(scopePath);
            string exchange = File.ReadAllText(exchangeModelPath);
            string progression = File.ReadAllText(progressionDomainPath);
            string knowledge = File.ReadAllText(knowledgeResolverPath);

            Assert.Multiple(() =>
            {
                Assert.That(scope, Does.Contain("public readonly struct ScopeKey"));
                Assert.That(scope, Does.Contain("public enum RoleSlot"));
                Assert.That(scope, Does.Contain("public sealed class ScopeResolver"));
                Assert.That(exchange, Does.Contain("RoleSlot"));
                Assert.That(exchange, Does.Contain("ScopeKey"));
                Assert.That(progression, Does.Contain("ScopeKey"));
                Assert.That(progression, Does.Contain("RoleSlot"));
                Assert.That(knowledge, Does.Contain("ScopeKey"));
                Assert.That(knowledge, Does.Contain("RoleResolverContext"));
            });

            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "mods", "capabilities", "participant_view"),
                Path.Combine(repoRoot, "mods", "showcases", "item_system")
            };
            string[] forbidden =
            {
                "ExchangeActorSlot",
                "ProgressionScopeSpec",
                "ProgressionScopeKind",
                "ProgressionRequirementEntitySource",
                "viewerScopes"
            };

            List<string> hits = FindForbiddenSourceTokens(repoRoot, directories, forbidden);
            Assert.That(
                hits,
                Is.Empty,
                "AAC-4 (#247) requires Exchange, Progression, Knowledge, and participant-view consumers to use the shared ScopeKey/RoleSlot contract:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void Issue244_PendingCompositionContracts()
        {
            Assert.Ignore("AAC-5..AAC-8/#248..#251 will make Ownership relations, Relationship-gated Exchange, Collection+Relationship-fed Progression membership, and Inventory settlement executable contracts.");
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
