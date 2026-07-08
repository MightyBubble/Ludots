using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ludots.Core.EntityCollections;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// RFC-0065 (unified interaction/casting) §6.1 M9 statically assertable guardrails:
    /// association/collection infrastructure stays scenario-neutral, collections never migrate
    /// across control domains, the Interaction input layer carries no casting FSM, command intent
    /// slot routing stays semantic (DEC-14), and RelationshipRuntime remains the single edge
    /// mutation entry with a reverse index (no full-world scan fallback).
    /// </summary>
    [TestFixture]
    public sealed class Rfc0065InteractionCastingBoundaryContractTests
    {
        private static readonly Regex ForbiddenScenarioWord = new(
            @"\b(offline|mind_control|cinematic|hostile|enemy|garrison|attack)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Asset-layer variant: Core default config files additionally must not carry scenario
        // relationship/tag vocabulary such as alliance ("ally") or death state ("dead") — those
        // belong to mod fragments / test data (H-1/M-1/M-2 findings of the semantic-intrusion audit).
        private static readonly Regex ForbiddenAssetScenarioWord = new(
            @"\b(offline|mind_control|cinematic|hostile|enemy|garrison|attack|ally|dead)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void AssociationAndCollectionInfrastructure_ContainNoBusinessScenarioStringLiterals()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Relationships"),
                Path.Combine(repoRoot, "src", "Core", "EntityCollections")
            };

            var violations = new List<string>();
            int scannedFiles = 0;
            foreach (string root in roots)
            {
                Assert.That(Directory.Exists(root), Is.True, $"Missing RFC-0065 infrastructure directory {root}");
                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    scannedFiles++;
                    foreach ((int lineNumber, string literal) in ExtractStringLiterals(File.ReadAllText(file)))
                    {
                        Match match = ForbiddenScenarioWord.Match(literal);
                        if (match.Success)
                        {
                            violations.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: \"{match.Value}\" in literal \"{literal}\"");
                        }
                    }
                }
            }

            Assert.That(scannedFiles, Is.GreaterThan(0), "RFC-0065 scenario-literal contract scanned no source files.");
            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065 keeps Relationships/EntityCollections infrastructure scenario-neutral; " +
                "offline/mind_control/cinematic/hostile/enemy/garrison/attack semantics belong in config/mod layers:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void CoreDefaultConfigAssets_CarryNoScenarioVocabulary()
        {
            string repoRoot = FindRepoRoot();
            string configsRoot = Path.Combine(repoRoot, "assets", "Configs");
            string[] roots =
            {
                Path.Combine(configsRoot, "Relationships"),
                Path.Combine(configsRoot, "Input"),
                Path.Combine(configsRoot, "UI")
            };

            var violations = new List<string>();
            int scannedFiles = 0;
            foreach (string root in roots)
            {
                Assert.That(Directory.Exists(root), Is.True, $"Missing Core default config directory {root}");
                foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    scannedFiles++;
                    JsonNode? node = JsonNode.Parse(File.ReadAllText(file));
                    if (node is JsonObject rootObject &&
                        string.Equals(
                            ToRepoRelativePath(repoRoot, file),
                            "assets/Configs/Relationships/catalog.json",
                            StringComparison.Ordinal))
                    {
                        // Explicit exemption: the catalog stance section (Hostile/Friendly/Neutral)
                        // is TeamManager bridge-period reserved vocabulary (handoff §二.5) and is
                        // retired together with the bridge; everything else in the file is scanned.
                        rootObject.Remove("stance");
                    }

                    CollectJsonScenarioWordViolations(node, ToRepoRelativePath(repoRoot, file), "$", violations);
                }
            }

            Assert.That(scannedFiles, Is.GreaterThan(0), "RFC-0065 asset scenario-word contract scanned no config files.");
            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065 keeps Core default config assets scenario-neutral; " +
                "offline/mind_control/cinematic/hostile/enemy/garrison/attack/ally/dead vocabulary " +
                "belongs in mod fragments or test data:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void CollectionControlPlaneTypes_ExposeNoCrossDomainMigrationApi()
        {
            Type[] types =
            {
                typeof(EntityCollectionStore),
                typeof(DomainRoutedCollectionWriter),
                typeof(ControlPlaneView)
            };
            string[] forbiddenNameTokens = { "Migrate", "Move", "Transfer", "Handback" };

            var violations = new List<string>();
            foreach (Type type in types)
            {
                MethodInfo[] methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (MethodInfo method in methods)
                {
                    foreach (string token in EnumeratePascalCaseTokens(method.Name))
                    {
                        if (forbiddenNameTokens.Any(f => string.Equals(f, token, StringComparison.OrdinalIgnoreCase)))
                        {
                            violations.Add($"{type.Name}.{method.Name} (token '{token}')");
                        }
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065 DEC-4: collections never migrate/move/transfer/hand back across control domains; " +
                "each domain keeps its own rows and composite reads go through ControlPlaneView:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void CommandSourceAuthority_IsEntityCollectionBackedByDefault()
        {
            string repoRoot = FindRepoRoot();
            string entityCollectionTypesPath = Path.Combine(repoRoot, "src", "Core", "EntityCollections", "EntityCollectionTypes.cs");
            string gameEnginePath = Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs");
            string contextRuntimePath = Path.Combine(repoRoot, "src", "Core", "Input", "CommandSources", "EntityCollectionContextRuntime.cs");
            Assert.That(File.Exists(entityCollectionTypesPath), Is.True, $"Missing {entityCollectionTypesPath}");
            Assert.That(File.Exists(gameEnginePath), Is.True, $"Missing {gameEnginePath}");
            Assert.That(File.Exists(contextRuntimePath), Is.True, $"Missing {contextRuntimePath}");

            string entityCollectionTypes = File.ReadAllText(entityCollectionTypesPath);
            string gameEngine = File.ReadAllText(gameEnginePath);
            string contextRuntime = File.ReadAllText(contextRuntimePath);

            Assert.Multiple(() =>
            {
                Assert.That(EntityCollectionKeys.CommandSource, Is.EqualTo("collection.command.source"));
                Assert.That(entityCollectionTypes, Does.Contain("public const string CommandSource = \"collection.command.source\""),
                    "The command-source authority key must remain a first-class EntityCollectionKeys constant.");
                Assert.That(gameEngine, Does.Contain("registry.Register(EntityCollectionKeys.CommandSource)"),
                    "GameEngine must register the command-source collection key with EntityCollectionStore.");
                Assert.That(gameEngine, Does.Contain("InteractionContextFrameDescriptor.Create("),
                    "GameEngine must create a default interaction context frame for command routing.");
                Assert.That(gameEngine, Does.Contain("EntityCollectionKeys.CommandSource"),
                    "The default interaction context must use EntityCollectionKeys.CommandSource.");
                Assert.That(gameEngine, Does.Contain("EntityViewKeys.ControlPlaneCommand"),
                    "The default interaction context must bind command-source authority to the control-plane command view.");
                Assert.That(contextRuntime, Does.Contain("collections.KeyRegistry.Register(EntityCollectionKeys.CommandSource)"),
                    "EntityCollectionContextRuntime must fall back to collection.command.source when no explicit interaction frame is active.");
            });
        }

        [Test]
        public void RetiredFormalInputApis_DoNotAppearRepoWide()
        {
            string repoRoot = FindRepoRoot();
            string[] forbidden =
            {
                Join("Ludots.Core.Input.", "Selection"),
                Join("Selection", "Runtime"),
                Join("Selection", "Request"),
                Join("Selection", "Response"),
                Join("Selection", "SetKeys"),
                Join("Selection", "ViewKeys"),
                Join("Selection", "ContainerKind"),
                Join("Selection", "Eligibility"),
                Join("Order", "Selection", "Reference")
            };

            var violations = new List<string>();
            foreach (string rootName in new[] { "src", "mods" })
            {
                string root = Path.Combine(repoRoot, rootName);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (!IsScannedRepoFile(repoRoot, file))
                    {
                        continue;
                    }

                    AppendForbiddenSourceTokens(repoRoot, file, forbidden, violations);
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Formal input authority must be retired repo-wide; use EntityCollectionStore, " +
                "EntityCollectionKeys.CommandSource, and collection.command.source instead:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void GameJsonCommandSourceConfig_DoesNotUseRetiredTopLevelSelectionSection()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "assets"),
                Path.Combine(repoRoot, "mods")
            };

            var violations = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "game.json", SearchOption.AllDirectories))
                {
                    if (!IsScannedRepoFile(repoRoot, file))
                    {
                        continue;
                    }

                    JsonNode? node = JsonNode.Parse(File.ReadAllText(file));
                    if (node is JsonObject obj && obj.ContainsKey("selection"))
                    {
                        violations.Add($"{ToRepoRelativePath(repoRoot, file)}: top-level game.json selection section must be commandSource");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Move path preview, acquisition, and target-filter config belong under game.commandSource " +
                "so command-source authority is backed by EntityCollectionStore:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void InteractionInputLayer_ContainsNoCastingStateMachine()
        {
            string repoRoot = FindRepoRoot();
            string interactionDir = Path.Combine(repoRoot, "src", "Core", "Input", "Interaction");
            Assert.That(Directory.Exists(interactionDir), Is.True, $"Missing RFC-0065 interaction directory {interactionDir}");

            string[] forbidden =
            {
                "_isAiming",
                "\"states\"",
                "\"transitions\""
            };

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(interactionDir, "*.cs", SearchOption.AllDirectories))
            {
                AppendForbiddenSourceTokens(repoRoot, file, forbidden, violations);
            }

            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065 keeps the Interaction input layer FSM-free: no aiming state fields and no " +
                "states/transitions JSON schema; casting phases live in GAS, not in input code:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void CommandIntentSlotRouting_KeepsSemanticSelectorWhitelist()
        {
            string repoRoot = FindRepoRoot();
            string registryPath = Path.Combine(repoRoot, "src", "Core", "Input", "Interaction", "CommandIntentProfileRegistry.cs");
            string unitTestPath = Path.Combine(repoRoot, "src", "Tests", "GasTests", "CommandIntentProfileTests.cs");
            Assert.That(File.Exists(registryPath), Is.True, $"Missing {registryPath}");
            Assert.That(File.Exists(unitTestPath), Is.True, $"Missing {unitTestPath}");

            string registry = File.ReadAllText(registryPath);
            string unitTests = File.ReadAllText(unitTestPath);

            Assert.Multiple(() =>
            {
                // DEC-14 whitelist: the only slot selectors the registry compiles are semantic ones.
                Assert.That(registry, Does.Contain("\"byAbilityTag:\""),
                    "CommandIntentProfileRegistry must whitelist the byAbilityTag: semantic slot selector.");
                Assert.That(registry, Does.Contain("\"contextGroup:\""),
                    "CommandIntentProfileRegistry must whitelist the contextGroup: semantic slot selector.");
                Assert.That(registry, Does.Contain("is not a semantic selector"),
                    "CommandIntentProfileRegistry must fail fast on non-semantic slot selectors (bare slot indices).");
                Assert.That(registry, Does.Not.Contain("\"bySlotIndex"),
                    "CommandIntentProfileRegistry must not grow a bySlotIndex selector prefix (DEC-14 forbids bare slot indices).");

                // Behavior coverage lives in GasTests; keep the rejection unit test from silently disappearing.
                Assert.That(unitTests, Does.Contain("bySlotIndex:0"),
                    "GasTests CommandIntentProfileTests must keep the bySlotIndex:0 rejection case that exercises the DEC-14 throw.");
            });
        }

        [Test]
        public void CommandTargetPaths_RequireExplicitKnowledgeGate()
        {
            string repoRoot = FindRepoRoot();
            string localOrderSourcePath = Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "LocalOrderSourceHelper.cs");
            string commandIntentRegistryPath = Path.Combine(repoRoot, "src", "Core", "Input", "Interaction", "CommandIntentProfileRegistry.cs");
            string contextScoredResolverPath = Path.Combine(repoRoot, "src", "Core", "Input", "Orders", "ContextScoredOrderResolver.cs");

            string localOrderSource = File.ReadAllText(localOrderSourcePath);
            string commandIntentRegistry = File.ReadAllText(commandIntentRegistryPath);
            string contextScoredResolver = File.ReadAllText(contextScoredResolverPath);

            Assert.Multiple(() =>
            {
                Assert.That(localOrderSource, Does.Contain("KnowledgeCommandTargetGate"),
                    "CoreInputMod command target paths must use the explicit resolver-backed knowledge gate.");
                Assert.That(localOrderSource, Does.Not.Contain("CommandSourceEligibility.CanTargetCommand("),
                    "CoreInputMod hover/auto-target command paths must go through KnowledgeCommandTargetGate, not the raw command-source eligibility helper.");

                Assert.That(commandIntentRegistry, Does.Not.Contain("targetGate = null"),
                    "CommandIntentProfileRegistry must not make the target gate optional.");
                Assert.That(commandIntentRegistry, Does.Contain("throw new ArgumentNullException(nameof(targetGate))"),
                    "CommandIntentProfileRegistry must fail fast when the knowledge target gate is missing.");

                Assert.That(contextScoredResolver, Does.Not.Contain("candidateGate = null"),
                    "ContextScoredOrderResolver must not make spatial candidate gating optional.");
                Assert.That(contextScoredResolver, Does.Contain("throw new ArgumentNullException(nameof(candidateGate))"),
                    "ContextScoredOrderResolver must fail fast when the knowledge candidate gate is missing.");
            });
        }

        [Test]
        public void MassNavigationCore_ConsumesOrdersNotInputOrCommandSourceAuthority()
        {
            string repoRoot = FindRepoRoot();
            string massNavigationRoot = Path.Combine(repoRoot, "src", "Core", "MassNavigation");
            Assert.That(Directory.Exists(massNavigationRoot), Is.True, $"Missing {massNavigationRoot}");

            string[] forbidden =
            {
                "EntityCollectionStore",
                "EntityCollectionKeys.CommandSource",
                "\"collection.command.source\"",
                "EntityCollectionContextRuntime",
                "InteractionContextStack",
                "CommandSourceAcquisition",
                "InputOrderMappingSystem",
                "MassNavigationLocalCommandInputSystem"
            };

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(massNavigationRoot, "*.cs", SearchOption.AllDirectories))
            {
                AppendForbiddenSourceTokens(repoRoot, file, forbidden, violations);
            }

            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065: MassNavigation is an execution domain. It must ingest explicit OrderBuffer move orders " +
                "and must not resolve command actors from input, command-source collections, or interaction context APIs:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void InteractionShowcase_SeedsCommandSourceAuthorityDirectly()
        {
            string repoRoot = FindRepoRoot();
            string runtimePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "interaction",
                "InteractionShowcaseMod",
                "Runtime",
                "InteractionShowcaseRuntime.cs");
            Assert.That(File.Exists(runtimePath), Is.True, $"Missing {runtimePath}");

            string source = File.ReadAllText(runtimePath);
            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Contain("EntityCollectionStore"),
                    "The interaction showcase must publish through EntityCollectionStore.");
                Assert.That(source, Does.Contain("EntityCollectionKeys.CommandSource"),
                    "The interaction showcase must target collection.command.source explicitly.");
                Assert.That(source, Does.Contain("EntityCollectionRoleKind.CommandSource"),
                    "The interaction showcase descriptor must mark the collection as command-source authority.");
                Assert.That(source, Does.Contain("EntityCollectionSourceKind.Explicit"),
                    "The showcase command-source descriptor should remain an explicit host-seeded collection.");
                Assert.That(source, Does.Contain("collections.Replace(owner, in descriptor, actors, owner)"),
                    "The writer domain must be recorded as the local command-source owner.");
            });
        }

        [Test]
        public void RelationshipEdgeMutations_OnlyHappenInsideRelationshipRuntime()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "mods")
            };
            string[] explicitGenericMutations =
            {
                "AddRelationship<RelationshipEdgeSet>",
                "SetRelationship<RelationshipEdgeSet>",
                "RemoveRelationship<RelationshipEdgeSet>"
            };
            string[] inferredMutationCalls =
            {
                ".AddRelationship(",
                ".SetRelationship(",
                ".RemoveRelationship("
            };

            var violations = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = ToRepoRelativePath(repoRoot, file);
                    if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                        relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (relative.Equals("src/Core/Gameplay/Relationships/RelationshipRuntime.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string source = File.ReadAllText(file);
                    foreach (string token in explicitGenericMutations)
                    {
                        if (source.Contains(token, StringComparison.Ordinal))
                        {
                            violations.Add($"{relative}: {token}");
                        }
                    }

                    // Type inference evasion: touching RelationshipEdgeSet and calling the Arch edge
                    // mutation extensions in the same file is a bypass of RelationshipRuntime.
                    if (source.Contains("RelationshipEdgeSet", StringComparison.Ordinal))
                    {
                        foreach (string call in inferredMutationCalls)
                        {
                            if (source.Contains(call, StringComparison.Ordinal))
                            {
                                violations.Add($"{relative}: RelationshipEdgeSet + {call}");
                            }
                        }
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "RFC-0065: RelationshipRuntime is the single edge mutation entry (it owns the reverse index); " +
                "no other production code may add/set/remove RelationshipEdgeSet relationships directly:\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void RelationshipRuntimeCollectIncoming_UsesReverseIndexWithoutWorldScan()
        {
            string repoRoot = FindRepoRoot();
            string runtimePath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Relationships", "RelationshipRuntime.cs");
            Assert.That(File.Exists(runtimePath), Is.True, $"Missing {runtimePath}");

            string source = File.ReadAllText(runtimePath);
            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain("RelationshipQuery"),
                    "The removed full-world RelationshipQuery fallback must not return to RelationshipRuntime.");
                Assert.That(source, Does.Not.Contain("QueryDescription"),
                    "RelationshipRuntime must not define world queries; incoming edges come from the reverse index.");
                Assert.That(source, Does.Not.Contain("_world.Query"),
                    "RelationshipRuntime must not iterate the world to collect incoming edges.");
                Assert.That(source, Does.Contain("_reverseIndex.CopyIncoming"),
                    "CollectIncoming must read the reverse adjacency index.");
            });
        }

        /// <summary>
        /// Yields the contents of double-quoted string literals (normal, verbatim, and interpolated)
        /// with their starting line number, skipping // and /* */ comments and char literals so words
        /// in comments or identifiers never trip the scenario-literal guard.
        /// </summary>
        private static IEnumerable<(int LineNumber, string Literal)> ExtractStringLiterals(string source)
        {
            var results = new List<(int, string)>();
            var current = new StringBuilder();
            int line = 1;
            int literalStartLine = 0;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inNormalString = false;
            bool inVerbatimString = false;
            bool inCharLiteral = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (c == '\n')
                {
                    line++;
                    inLineComment = false;
                    if (inNormalString || inCharLiteral)
                    {
                        // Unterminated on this line (defensive); reset state.
                        inNormalString = false;
                        inCharLiteral = false;
                        current.Clear();
                    }

                    continue;
                }

                if (inLineComment)
                {
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inCharLiteral)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '\'')
                    {
                        inCharLiteral = false;
                    }

                    continue;
                }

                if (inNormalString)
                {
                    if (c == '\\')
                    {
                        current.Append(c);
                        if (next != '\0')
                        {
                            current.Append(next);
                            i++;
                        }
                    }
                    else if (c == '"')
                    {
                        inNormalString = false;
                        results.Add((literalStartLine, current.ToString()));
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (inVerbatimString)
                {
                    if (c == '"' && next == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inVerbatimString = false;
                        results.Add((literalStartLine, current.ToString()));
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '\'')
                {
                    inCharLiteral = true;
                    continue;
                }

                if (c == '"')
                {
                    // Consume optional @/$ prefixes already passed; detect verbatim by looking back.
                    bool verbatim = false;
                    for (int back = i - 1; back >= 0 && back >= i - 2; back--)
                    {
                        char p = source[back];
                        if (p == '@')
                        {
                            verbatim = true;
                        }
                        else if (p != '$')
                        {
                            break;
                        }
                    }

                    literalStartLine = line;
                    if (verbatim)
                    {
                        inVerbatimString = true;
                    }
                    else
                    {
                        inNormalString = true;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Recursively scans property names and string values of a parsed JSON document against
        /// <see cref="ForbiddenAssetScenarioWord"/>, recording violations with their JSON path.
        /// </summary>
        private static void CollectJsonScenarioWordViolations(
            JsonNode? node,
            string file,
            string jsonPath,
            List<string> violations)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (KeyValuePair<string, JsonNode?> pair in obj)
                    {
                        Match keyMatch = ForbiddenAssetScenarioWord.Match(pair.Key);
                        if (keyMatch.Success)
                        {
                            violations.Add($"{file}: {jsonPath}.{pair.Key}: \"{keyMatch.Value}\" in property name");
                        }

                        CollectJsonScenarioWordViolations(pair.Value, file, $"{jsonPath}.{pair.Key}", violations);
                    }

                    break;
                case JsonArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        CollectJsonScenarioWordViolations(array[i], file, $"{jsonPath}[{i}]", violations);
                    }

                    break;
                case JsonValue value when value.TryGetValue(out string? text):
                    Match match = ForbiddenAssetScenarioWord.Match(text);
                    if (match.Success)
                    {
                        violations.Add($"{file}: {jsonPath}: \"{match.Value}\" in value \"{text}\"");
                    }

                    break;
            }
        }

        private static IEnumerable<string> EnumeratePascalCaseTokens(string name)
        {
            int start = 0;
            for (int i = 1; i <= name.Length; i++)
            {
                if (i == name.Length || (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])))
                {
                    yield return name[start..i];
                    start = i;
                }
            }
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
                    if (line.Contains(forbidden[tokenIndex], StringComparison.Ordinal))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineIndex + 1}: {forbidden[tokenIndex]}: {line.Trim()}");
                        break;
                    }
                }
            }
        }

        private static string Join(params string[] parts)
        {
            return string.Concat(parts);
        }

        private static bool IsScannedRepoFile(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            return !relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                   !relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                   !relative.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) &&
                   !relative.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
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
