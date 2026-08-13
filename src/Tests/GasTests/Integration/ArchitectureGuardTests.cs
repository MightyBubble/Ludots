using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance
{
    [Category("ci-gate")]
    [Category("arch-guard")]
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
        public void GasPresentationEvents_AreClearedOnlyByClearPresentationFlagsProjection()
        {
            var repoRoot = FindRepoRoot();
            string engineSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Core",
                "Engine",
                "GameEngine.cs"));
            string projectionSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Core",
                "Presentation",
                "Systems",
                "GameplayPresentationProjectionSystem.cs"));

            Assert.That(
                engineSource,
                Does.Not.Contain("_gasPresentationEvents?.Clear("),
                "The visual loop must not clear GAS events before a sliced simulation reaches ClearPresentationFlags.");
            Assert.That(
                projectionSource,
                Does.Contain("_gasEvents.Clear();"),
                "GameplayPresentationProjectionSystem owns GAS event cleanup after projection.");
            Assert.That(
                engineSource,
                Does.Contain("RegisterSystem(clearPresentationFlagsSystem, SystemGroup.ClearPresentationFlags);"),
                "Gameplay changed-bit cleanup must stay in the fixed ClearPresentationFlags phase after projection.");
            Assert.That(
                engineSource,
                Does.Not.Contain("RegisterPresentationSystem(clearPresentationFlagsSystem)"),
                "The visual loop must not clear gameplay changed-bit components before a sliced simulation reaches ClearPresentationFlags.");
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
        public void QuestPublicProtocol_MustNotLiveUnderNarrativeKeys()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "mods"),
                Path.Combine(repoRoot, "docs", "architecture"),
                Path.Combine(repoRoot, "gitbook", "architecture")
            };
            string[] forbidden =
            {
                "Narrative.Quest",
                "Narrative.Signal",
                "NarrativeEventKeys.Quest",
                "NarrativeEventKeys.Signal",
                "NarrativeServiceKeys.Quest",
                "NarrativeServiceKeys.Signal"
            };

            var hits = new List<string>();
            foreach (string dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(path =>
                        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
                {
                    AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Quest public protocol must use QuestEventKeys / QuestServiceKeys, not Narrative keys:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void CoreInput_ViewModeSwitchSystem_DoesNotRenderPersistentHud()
        {
            var repoRoot = FindRepoRoot();
            string file = Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "ViewModeSwitchSystem.cs");
            Assert.That(File.Exists(file), Is.True, $"Missing {ToRepoRelativePath(repoRoot, file)}");

            string[] forbidden =
            {
                "ScreenOverlayBuffer",
                "RenderModeHud",
                "ViewMode:"
            };

            var hits = new List<string>();
            AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "CoreInput view mode switching is gameplay input state. It must not render persistent debug HUD text into the top-left screen overlay:\n" +
                    string.Join("\n", hits));
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
                "CopyEntities(",
                "AttributeBuffer",
                ".HasAttribute("
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
        public void Issue610_WorldHudProjectionConsumesKnowledgeThroughSingleEntryPoint()
        {
            var repoRoot = FindRepoRoot();
            string hudPath = Path.Combine(
                repoRoot,
                "src",
                "Core",
                "Presentation",
                "Presenters",
                "WorldHudPresentBehavior.cs");
            string phaseResolverPath = Path.Combine(
                repoRoot,
                "src",
                "Core",
                "Presentation",
                "Presenters",
                "PresentPhaseResolver.cs");

            string hudSource = File.ReadAllText(hudPath);
            string phaseResolverSource = File.ReadAllText(phaseResolverPath);
            string normalizedPhaseResolver = Regex.Replace(phaseResolverSource, "\\s+", string.Empty);
            string[] forbiddenHudTokens =
            {
                "CoreServiceKeys.KnowledgeProjectionResolver",
                "KnowledgeProjectionResolver resolver",
                "KnowledgeProjectionResolver? resolver",
                "new KnowledgeProjectionResolver",
                ".TryResolve(",
                "KnowledgeRelationCollectionGrantStore",
                "KnowledgeRelationCollectionProjector",
                ".ProjectOutgoing(",
                ".CopyEntities(",
                "CopyEntities("
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    hudSource,
                    Does.Contain("KnowledgeProjectionConsumer.TryResolveForViewer"),
                    "World HUD should consume knowledge projection through the shared consumer, including relation-granted projection.");
                for (int i = 0; i < forbiddenHudTokens.Length; i++)
                {
                    Assert.That(
                        hudSource,
                        Does.Not.Contain(forbiddenHudTokens[i]),
                        $"World HUD should not bypass KnowledgeProjectionConsumer with {forbiddenHudTokens[i]}.");
                }

                Assert.That(
                    normalizedPhaseResolver,
                    Does.Contain("boolallowWorldHudProjection=shouldPresent&&input.HasAttributeProjection;"),
                    "World HUD projection allow must be gated by visibility and knowledge-authorized attributes only.");
            });
        }

        [Test]
        public void Epic322_GlobalHoveredEntityKeys_AreRemoved()
        {
            Assert.That(
                typeof(CoreServiceKeys).GetField("HoveredEntity"),
                Is.Null,
                "Epic #322 D6 requires hover state to live in EntityCollectionStore, not a flat CoreServiceKeys.HoveredEntity global.");
            Assert.That(
                typeof(ContextKeys).GetField("HoveredEntity"),
                Is.Null,
                "Epic #322 D6 requires hover state to live in EntityCollectionStore, not a flat ContextKeys.HoveredEntity global.");
        }

        [Test]
        public void Epic322_VectorAimPhase_IsRemoved()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core", "Input"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "docs", "architecture", "interaction")
            };

            var hits = new List<string>();
            for (int i = 0; i < directories.Length; i++)
            {
                string dir = directories[i];
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppendForbiddenSourceTokens(repoRoot, file, new[] { "VectorAimPhase" }, hits);
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 D7 removed the hardcoded VectorAimPhase name; aim state should expose targeting input slots instead:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_ModAbilityConfigs_DoNotDeclareAimVisualPresenters()
        {
            var repoRoot = FindRepoRoot();
            string modsDir = Path.Combine(repoRoot, "mods");
            Assert.That(Directory.Exists(modsDir), Is.True, $"Missing {modsDir}");

            string[] forbiddenKeys =
            {
                "indicator",
                "aimVisual",
                "areaPresenterId",
                "rangeCirclePresenterId",
                "previewPresenterId",
                "presenterId"
            };

            var hits = new List<string>();
            foreach (string file in Directory.EnumerateFiles(modsDir, "abilities.json", SearchOption.AllDirectories))
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(file));
                if (root == null)
                {
                    continue;
                }

                AppendForbiddenJsonKeys(repoRoot, file, root, "$", forbiddenKeys, hits);
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 requires ability configs to declare gameplay targeting only: targeting.castRangeCm + targeting.impactEffect. Aim visuals must be event-condition-action presenter rules:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_TargetSelector_DuplicateTargetTruth_IsRemoved()
        {
            var repoRoot = FindRepoRoot();
            string path = Path.Combine(repoRoot, "src", "Core", "Gameplay", "GAS", "Components", "TargetSelector.cs");

            Assert.That(
                File.Exists(path),
                Is.False,
                "Epic #322 ADR-1 keeps target resolution in the referenced impact effect; TargetSelector carried a parallel shape/range/radius/angle truth.");
        }

        [Test]
        public void Epic322_AbilityAimOverlayNaming_IsRemoved()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core", "Input"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "docs")
            };
            string[] forbidden =
            {
                "AbilityAimOverlayPresentationSystem",
                "AbilityAimOverlay"
            };

            var hits = new List<string>();
            for (int dirIndex = 0; dirIndex < directories.Length; dirIndex++)
            {
                string dir = directories[dirIndex];
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                "Epic #322 ability aim presentation is an event/collection projection consumed by presenter rules; overlay-named entry points must not return:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_ChampionSandboxPresentation_DoesNotBypassPresenterRules()
        {
            var repoRoot = FindRepoRoot();
            string sandboxDir = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "champion_skill_sandbox",
                "ChampionSkillSandboxMod");
            Assert.That(Directory.Exists(sandboxDir), Is.True, $"Missing {sandboxDir}");

            string[] forbidden =
            {
                "ChampionSkillSandboxVisualFeedback",
                "GasPresentationEventBuffer",
                "PresentationPrimitiveDrawBuffer",
                "PrimitiveDrawBuffer",
                "TransientMarkerBuffer",
                "WorldHudBatchBuffer"
            };

            var hits = FindForbiddenSourceTokens(repoRoot, new[] { sandboxDir }, forbidden);
            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 champion skill presentation must project events and let PresenterRuleSystem produce presenter commands. Sandbox code must not consume GAS events or write presentation buffers directly:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_CommandActorMovePathOverlayBridge_IsRemoved()
        {
            var repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core", "Input"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "docs", "architecture", "interaction")
            };
            string[] forbidden =
            {
                "SelectedMovePathOverlayBridge"
            };

            var hits = new List<string>();
            for (int dirIndex = 0; dirIndex < directories.Length; dirIndex++)
            {
                string dir = directories[dirIndex];
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 command actor move path presentation must publish MovePath events consumed by presenter rules; the old direct overlay bridge must not return:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_CommandActorMovePathPresentationSystem_PublishesEventsWithoutRenderBuffers()
        {
            var repoRoot = FindRepoRoot();
            string file = Path.Combine(
                repoRoot,
                "mods",
                "CoreInputMod",
                "Systems",
                "CommandActorMovePathPresentationSystem.cs");
            Assert.That(File.Exists(file), Is.True, $"Missing epic #322 command actor move path source {file}");

            string[] forbidden =
            {
                "GroundOverlayBuffer",
                "ScreenOverlayBuffer",
                "GroundOverlayItem",
                ".AddRect(",
                ".AddText(",
                ".TryAddLine(",
                ".TryAdd(new GroundOverlayItem"
            };

            var hits = new List<string>();
            AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 command actor move path presentation must publish MovePath events consumed by presenter rules; it must not read or write render buffers directly:\n" +
                    string.Join("\n", hits));
            }
        }

        [Test]
        public void Epic322_ShowcasePresentationSystems_PublishWorldFactsInsteadOfWritingRenderBuffers()
        {
            var repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "Systems", "RtsCommandSourceFeedbackPresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "entity_query_tactics", "EntityQueryTacticsShowcaseMod", "Systems", "EntityQueryTacticsPresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "relationship", "RelationshipShowcaseMod", "Systems", "RelationshipShowcasePresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "visual_terrain_editor", "VisualTerrainEditorMod", "Runtime", "VisualTerrainEditorRuntime.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "formation_capability", "FormationCapabilityShowcaseMod", "Runtime", "FormationCapabilityShowcaseObstacleOverlayPresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "formation_capability", "FormationCapabilityShowcaseMod", "Runtime", "FormationCapabilityShowcaseFormationOutlinePresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod", "Systems", "RoadSelectedRoutePresentationSystem.cs"),
                Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod", "Gameplay", "RoadRoutePreviewSplineBuilder.cs")
            };
            string[] forbidden =
            {
                "GroundOverlayBuffer",
                "RoadSplineBuffer",
                "WorldHudBatchBuffer",
                "new GroundOverlayItem",
                "new RoadSplineItem",
                "new WorldHudItem",
                ".TryAddLine(",
                ".TryAdd(new GroundOverlayItem",
                ".TryAdd(new WorldHudItem"
            };

            var hits = new List<string>();
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string file = files[fileIndex];
                Assert.That(File.Exists(file), Is.True, $"Missing epic #322 presentation source {file}");
                AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
            }

            if (hits.Count > 0)
            {
                Assert.Fail(
                    "Epic #322 showcase presentation systems must publish semantic world facts and let PresenterRuleSystem/presenter emit own render buffers:\n" +
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
                Path.Combine(repoRoot, "src", "Core", "Input", "CommandSources", "CommandSourceEligibility.cs"),
                Path.Combine(repoRoot, "src", "Core", "Input", "CommandSources", "CommandSourceAcquisitionSystem.cs"),
                Path.Combine(repoRoot, "src", "Core", "Input", "Interaction", "GasInputResponseSystem.cs"),
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
        public void Issue358_Physics2DHotPaths_AvoidRandomWorldAccessAndKeepThroughputGuarded()
        {
            var repoRoot = FindRepoRoot();
            string integration = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Ludots.Physics2D", "Systems", "IntegrationSystem2D.cs"));
            string impulses = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Ludots.Physics2D", "Systems", "ApplyImpulsesSystem2D.cs"));
            string adaptiveSpatial = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Ludots.Physics2D", "Systems", "AdaptiveSpatialSystem2D.cs"));
            string sleeping = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Ludots.Physics2D", "Systems", "SleepingSystem.cs"));

            Assert.Multiple(() =>
            {
                Assert.That(integration, Does.Not.Contain("World.TryGet"), "0Alloc tests are blind to TryGet throughput regressions; Integration must stay chunk/span based.");
                Assert.That(integration, Does.Not.Contain("World.Set("), "Integration hot path must mutate chunk spans instead of random World.Set writes.");
                Assert.That(integration, Does.Not.Contain("Nav" + "Desired" + "Velocity" + "2D"), "Navigation-to-Physics velocity handoff belongs in a gateable bridge, not the physics integrator.");
                Assert.That(adaptiveSpatial, Does.Not.Contain("World.Has<SleepingTag>"), "Broadphase should use BodySnapshot.IsSleeping instead of random World.Has calls.");
                Assert.That(impulses, Does.Not.Contain("World.Has<SleepingTag>"), "Impulse application should use CollisionPair.IsSleepingA/B snapshot bits.");
                Assert.That(sleeping, Does.Not.Contain("Dictionary<int, List<Entity>>"), "Sleeping island collection must not allocate one List per island.");
                Assert.That(sleeping, Does.Not.Contain("new List<Entity>"), "Sleeping island collection must reuse storage across frames.");
                Assert.That(sleeping, Does.Not.Contain("World.TryGet(pair.Entity"), "Wake collection should use collision-pair island snapshots instead of per-pair TryGet.");
                Assert.That(sleeping, Does.Not.Contain("World.Has<SleepingTag>(pair.Entity"), "Wake collection should use collision-pair sleeping snapshots instead of per-pair World.Has.");
            });
        }

        [Test]
        public void Issue361_Physics2DNavUatShowcases_AreFormalCapabilityStandardRoots()
        {
            var repoRoot = FindRepoRoot();
            string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
            string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
            string docs = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "capability-standard-showcases.md"));
            string gasTestsProject = File.ReadAllText(Path.Combine(repoRoot, "src", "Tests", "GasTests", "GasTests.csproj"));

            Issue361ShowcaseSpec[] specs =
            {
                new(
                    Issue: "#361",
                    Binding: "capability_standard_physics2d",
                    ModName: "CapabilityStandardPhysics2DMod",
                    ArtifactFolder: "capability-standard-physics2d",
                    AcceptanceTestName: "CapabilityStandardPhysics2DAcceptanceTests.cs",
                    PurePhysics: true),
                new(
                    Issue: "#361",
                    Binding: "capability_standard_physics2d_stress",
                    ModName: "CapabilityStandardPhysics2DStressMod",
                    ArtifactFolder: "capability-standard-physics2d-stress",
                    AcceptanceTestName: "CapabilityStandardPhysics2DStressShowcaseAcceptanceTests.cs",
                    PurePhysics: true),
                new(
                    Issue: "#361",
                    Binding: "capability_standard_physics2d_showcase",
                    ModName: "CapabilityStandardPhysics2DShowcaseMod",
                    ArtifactFolder: "capability-standard-physics2d-showcase",
                    AcceptanceTestName: "CapabilityStandardPhysics2DShowcaseAcceptanceTests.cs",
                    PurePhysics: true)
            };

            var missing = new List<string>();
            for (int i = 0; i < specs.Length; i++)
            {
                Issue361ShowcaseSpec spec = specs[i];
                string modDir = Path.Combine(repoRoot, "mods", "showcases", "capability_standard", spec.ModName);
                string acceptancePath = Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", spec.AcceptanceTestName);

                if (!Directory.Exists(modDir))
                {
                    missing.Add($"{spec.Issue}: missing mod directory {ToRepoRelativePath(repoRoot, modDir)}");
                    continue;
                }

                string gameJsonPath = Path.Combine(modDir, "assets", "game.json");
                string templatesPath = Path.Combine(modDir, "assets", "Entities", "templates.json");
                string configPath = Path.Combine(modDir, "assets", "Configs", "config_catalog.json");
                string projectFile = Path.Combine(modDir, $"{spec.ModName}.csproj");
                Assert.That(File.Exists(projectFile), Is.True, $"Missing {projectFile}");
                Assert.That(File.Exists(gameJsonPath), Is.True, $"Missing {gameJsonPath}");
                Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");
                Assert.That(File.Exists(templatesPath), Is.True, $"Missing {templatesPath}");
                Assert.That(File.Exists(acceptancePath), Is.True, $"Missing {acceptancePath}");

                string relativeModDir = ToRepoRelativePath(repoRoot, modDir);
                string acceptance = File.ReadAllText(acceptancePath);
                string gameJson = File.ReadAllText(gameJsonPath);
                string manifest = File.ReadAllText(Path.Combine(modDir, "mod.json"));
                string templates = File.ReadAllText(templatesPath);

                if (!launcherConfig.Contains($"\"name\": \"{spec.Binding}\"", StringComparison.Ordinal) ||
                    !launcherConfig.Contains(relativeModDir, StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: launcher.config.json missing binding {spec.Binding} -> {relativeModDir}");
                }

                if (!launcherPresets.Contains($"\"id\": \"{spec.Binding}_raylib\"", StringComparison.Ordinal) ||
                    !launcherPresets.Contains($"\"${spec.Binding}\"", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: launcher.presets.json missing raylib preset for {spec.Binding}");
                }

                if (!docs.Contains($"`{spec.Binding}`", StringComparison.Ordinal) ||
                    !docs.Contains(relativeModDir, StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: capability-standard-showcases.md missing {spec.Binding}");
                }

                if (!gasTestsProject.Contains($"{spec.ModName}.csproj", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: GasTests.csproj missing {spec.ModName}.csproj reference");
                }

                if (!acceptance.Contains("\"artifacts\"", StringComparison.Ordinal) ||
                    !acceptance.Contains("\"showcases\"", StringComparison.Ordinal) ||
                    !acceptance.Contains(spec.ArtifactFolder, StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: acceptance test missing artifacts/showcases/{spec.ArtifactFolder}");
                }

                if (spec.PurePhysics)
                {
                    Assert.That(gameJson, Does.Contain("\"physics2D\""));
                    Assert.That(gameJson, Does.Not.Contain("\"navigation" + "2D\""));
                    Assert.That(templates, Does.Not.Contain("Nav" + "Kinematics" + "2D"));
                    Assert.That(templates, Does.Not.Contain("Nav" + "Desired" + "Velocity" + "2D"));
                    Assert.That(templates, Does.Not.Contain("Nav" + "Obstacle" + "2D"));
                }

                if (gameJson.Contains("Camera.Profile.", StringComparison.Ordinal))
                {
                    Assert.That(manifest, Does.Contain("\"CameraProfilesMod\""),
                        $"{spec.Issue}: {spec.ModName} uses shared Camera.Profile.* IDs and must declare CameraProfilesMod so launcher presets are runnable.");
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(launcherConfig, Does.Not.Contain("Physics2DPlaygroundMod"));
                Assert.That(launcherPresets, Does.Not.Contain("Physics2DPlaygroundMod"));
                Assert.That(docs, Does.Contain("retires old `Physics2DPlaygroundMod` as formal entry"));
            });

            Assert.That(
                missing,
                Is.Empty,
                "Issue #361 requires every Physics2D/Nav UAT showcase to be a formal capability-standard root with launcher, docs, test, and artifact evidence:\n" +
                string.Join("\n", missing));
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
        public void Issue244_SingleScopeKeyContract()
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
        public void Issue251_ProgressionScopeMembers_ReuseSharedScopeResolverMembershipSources()
        {
            var repoRoot = FindRepoRoot();
            string evaluatorPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Progression", "ProgressionRequirementEvaluator.cs");
            string showcasePath = Path.Combine(repoRoot, "mods", "showcases", "team_research", "TeamResearchShowcaseMod", "mod.json");

            Assert.That(File.Exists(evaluatorPath), Is.True, $"Missing {evaluatorPath}");
            Assert.That(File.Exists(showcasePath), Is.True, $"Missing AAC-8 showcase mod {showcasePath}");

            string evaluator = File.ReadAllText(evaluatorPath);
            Assert.Multiple(() =>
            {
                Assert.That(evaluator, Does.Contain("_scopeResolver.ResolveMembers"));
                Assert.That(evaluator, Does.Not.Contain("InlineEntityQuery<"));
                Assert.That(evaluator, Does.Not.Contain("ScopeRefBuffer"));
                Assert.That(evaluator, Does.Not.Contain("CountScopeMembersJob"));
                Assert.That(evaluator, Does.Not.Contain("HashScopeMembersJob"));
            });
        }

        [Test]
        public void Issue248_OwnershipUsesOwnsRelationAndItemContainersDoNotCarryLogicalOwnerFields()
        {
            var repoRoot = FindRepoRoot();
            string containerPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Items", "ItemComponents.cs");
            string inventoryPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Items", "InventoryRuntimeService.cs");
            string ownershipPath = Path.Combine(repoRoot, "src", "Core", "Association", "OwnershipResolver.cs");
            string relationshipCatalogPath = Path.Combine(repoRoot, "assets", "Configs", "Relationships", "catalog.json");
            string showcasePath = Path.Combine(repoRoot, "mods", "showcases", "ownership_cascade", "OwnershipCascadeShowcaseMod", "mod.json");

            Assert.That(File.Exists(containerPath), Is.True, $"Missing {containerPath}");
            Assert.That(File.Exists(inventoryPath), Is.True, $"Missing {inventoryPath}");
            Assert.That(File.Exists(ownershipPath), Is.True, $"Missing {ownershipPath}");
            Assert.That(File.Exists(relationshipCatalogPath), Is.True, $"Missing {relationshipCatalogPath}");
            Assert.That(File.Exists(showcasePath), Is.True, $"Missing AAC-5 showcase mod {showcasePath}");

            string container = File.ReadAllText(containerPath);
            string inventory = File.ReadAllText(inventoryPath);
            string ownership = File.ReadAllText(ownershipPath);
            string catalog = File.ReadAllText(relationshipCatalogPath);

            Assert.Multiple(() =>
            {
                Assert.That(container, Does.Contain("public struct ItemContainerCm"));
                Assert.That(container, Does.Not.Contain("public Entity Owner"));
                Assert.That(container, Does.Not.Contain("OwnerKind"));
                Assert.That(inventory, Does.Contain("OwnershipResolver"));
                Assert.That(inventory, Does.Contain("_ownership.EnsureOwnership"));
                Assert.That(inventory, Does.Contain("_ownership.IsOwnedBy"));
                Assert.That(inventory, Does.Not.Contain("ItemContainerOwnerKind"));
                Assert.That(ownership, Does.Contain("RelationshipRuntime"));
                Assert.That(ownership, Does.Contain("CollectIncoming"));
                Assert.That(ownership, Does.Contain("CollectOutgoing"));
                Assert.That(catalog, Does.Contain("\"Owns\""));
            });
        }

        [Test]
        public void Issue249_ExchangeRelationshipGate_IsConfigDrivenAndValidatedBeforeReservations()
        {
            var repoRoot = FindRepoRoot();
            string modelPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeModel.cs");
            string runtimePath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeRuntime.cs");
            string loaderPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeConfigLoader.cs");
            string showcasePath = Path.Combine(repoRoot, "mods", "showcases", "diplomacy_trade_gate", "DiplomacyTradeGateShowcaseMod", "mod.json");

            Assert.That(File.Exists(modelPath), Is.True, $"Missing {modelPath}");
            Assert.That(File.Exists(runtimePath), Is.True, $"Missing {runtimePath}");
            Assert.That(File.Exists(loaderPath), Is.True, $"Missing {loaderPath}");
            Assert.That(File.Exists(showcasePath), Is.True, $"Missing AAC-6 showcase mod {showcasePath}");

            string model = File.ReadAllText(modelPath);
            string runtime = File.ReadAllText(runtimePath);
            string loader = File.ReadAllText(loaderPath);

            Assert.Multiple(() =>
            {
                Assert.That(model, Does.Contain("RelationshipDenied"));
                Assert.That(model, Does.Contain("ExchangeRelationshipRequirement"));
                Assert.That(model, Does.Contain("RelationshipRequirements"));
                Assert.That(loader, Does.Contain("relationshipRequirements"));
                Assert.That(loader, Does.Contain("ResolveRelationshipType"));
                Assert.That(loader, Does.Contain("ResolveRelationshipMetric"));
                Assert.That(loader, Does.Contain("ResolveRelationshipFlag"));
                Assert.That(runtime, Does.Contain("ValidateRelationships"));
                Assert.That(runtime.IndexOf("ValidateRelationships", StringComparison.Ordinal), Is.LessThan(runtime.IndexOf("_reservations.Add", StringComparison.Ordinal)));
                Assert.That(runtime, Does.Contain("ExchangeExecutionStatus.RelationshipDenied"));
            });
        }

        [Test]
        public void Issue250_ExchangeAttributeCostInput_UsesGasAttributesAndShowcaseMod()
        {
            var repoRoot = FindRepoRoot();
            string modelPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeModel.cs");
            string runtimePath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeRuntime.cs");
            string loaderPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeConfigLoader.cs");
            string showcasePath = Path.Combine(repoRoot, "mods", "showcases", "gold_market", "GoldMarketShowcaseMod", "mod.json");

            Assert.That(File.Exists(modelPath), Is.True, $"Missing {modelPath}");
            Assert.That(File.Exists(runtimePath), Is.True, $"Missing {runtimePath}");
            Assert.That(File.Exists(loaderPath), Is.True, $"Missing {loaderPath}");
            Assert.That(File.Exists(showcasePath), Is.True, $"Missing AAC-7 showcase mod {showcasePath}");

            string model = File.ReadAllText(modelPath);
            string runtime = File.ReadAllText(runtimePath);
            string loader = File.ReadAllText(loaderPath);

            Assert.Multiple(() =>
            {
                Assert.That(model, Does.Contain("AttributeCost"));
                Assert.That(model, Does.Contain("AttributeId"));
                Assert.That(runtime, Does.Contain("AttributeBuffer"));
                Assert.That(runtime, Does.Contain("_attributeCosts"));
                Assert.That(runtime, Does.Contain("AttributeCostRecord"));
                Assert.That(runtime, Does.Contain("attributes.SetCurrent"));
                Assert.That(loader, Does.Contain("AttributeRegistry.Register"));
                Assert.That(loader, Does.Contain("inputs[{index}].attribute"));
            });

            string[] forbidden =
            {
                "GoldMarket",
                "currency",
                "merchant",
                "vendor",
                "shop"
            };
            var hits = new List<string>();
            AppendForbiddenSourceTokens(repoRoot, modelPath, forbidden, hits);
            AppendForbiddenSourceTokens(repoRoot, runtimePath, forbidden, hits);
            Assert.That(
                hits,
                Is.Empty,
                "AAC-7 (#250) keeps Exchange Core semantic; market/currency terms belong in the showcase/config layer.");
        }

        [Test]
        public void Issue252_KnowledgeRelationCollectionGrants_AreFoldedIntoRelationshipCatalog()
        {
            var repoRoot = FindRepoRoot();
            string serviceKeysPath = Path.Combine(repoRoot, "src", "Core", "Scripting", "CoreServiceKeys.cs");
            string grantPath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeRelationCollectionGrants.cs");
            string catalogConfigPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Relationships", "Config", "RelationshipCatalogConfig.cs");
            string catalogRuntimePath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Relationships", "RelationshipCatalogRuntime.cs");
            string catalogLoaderPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Relationships", "Config", "RelationshipCatalogPipelineLoader.cs");
            string showcaseInstallerPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardParticipantViewsMod",
                "ParticipantViewKnowledgeShowcaseInstaller.cs");
            string showcaseMapPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardParticipantViewsMod",
                "assets",
                "Maps",
                "capability_standard_participant_views.json");
            string showcaseCatalogPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardParticipantViewsMod",
                "assets",
                "Relationships",
                "catalog.json");

            Assert.That(File.Exists(serviceKeysPath), Is.True, $"Missing {serviceKeysPath}");
            Assert.That(File.Exists(grantPath), Is.True, $"Missing {grantPath}");
            Assert.That(File.Exists(catalogConfigPath), Is.True, $"Missing {catalogConfigPath}");
            Assert.That(File.Exists(catalogRuntimePath), Is.True, $"Missing {catalogRuntimePath}");
            Assert.That(File.Exists(catalogLoaderPath), Is.True, $"Missing {catalogLoaderPath}");
            Assert.That(File.Exists(showcaseInstallerPath), Is.True, $"Missing {showcaseInstallerPath}");
            Assert.That(File.Exists(showcaseMapPath), Is.True, $"Missing {showcaseMapPath}");
            Assert.That(File.Exists(showcaseCatalogPath), Is.True, $"Missing {showcaseCatalogPath}");

            string serviceKeys = File.ReadAllText(serviceKeysPath);
            string grants = File.ReadAllText(grantPath);
            string catalogConfig = File.ReadAllText(catalogConfigPath);
            string catalogRuntime = File.ReadAllText(catalogRuntimePath);
            string catalogLoader = File.ReadAllText(catalogLoaderPath);
            string showcaseInstaller = File.ReadAllText(showcaseInstallerPath);
            string showcaseMap = File.ReadAllText(showcaseMapPath);
            string showcaseCatalog = File.ReadAllText(showcaseCatalogPath);

            Assert.Multiple(() =>
            {
                Assert.That(serviceKeys, Does.Not.Contain("KnowledgeRelationCollectionGrantStore"));
                Assert.That(grants, Does.Contain("public readonly struct KnowledgeRelationCollectionGrant"));
                Assert.That(grants, Does.Contain("RelationshipCatalogRuntime"));
                Assert.That(grants, Does.Not.Contain("public sealed class KnowledgeRelationCollectionGrantStore"));
                Assert.That(catalogConfig, Does.Contain("KnowledgeGrants"));
                Assert.That(catalogConfig, Does.Contain("RelationshipKnowledgeGrantConfig"));
                Assert.That(catalogRuntime, Does.Contain("CompileKnowledgeGrants"));
                Assert.That(catalogRuntime, Does.Contain("TryGetKnowledgeGrantAt"));
                Assert.That(catalogRuntime, Does.Contain("EntityCollectionStore"));
                Assert.That(catalogLoader, Does.Contain("KnowledgeGrants"));
                Assert.That(catalogLoader, Does.Contain("JsonStringEnumConverter"));
                Assert.That(showcaseInstaller, Does.Not.Contain("InstallGrants"));
                Assert.That(showcaseInstaller, Does.Not.Contain("KnowledgeGrantSpec"));
                Assert.That(showcaseMap, Does.Not.Contain("\"Grants\""));
                Assert.That(showcaseCatalog, Does.Contain("\"knowledgeGrants\""));
            });
        }

        [Test]
        public void Issue254_FeatureShowcaseCapabilityModsAndAcceptanceTests_ArePresent()
        {
            var repoRoot = FindRepoRoot();
            string gasTestsProject = Path.Combine(repoRoot, "src", "Tests", "GasTests", "GasTests.csproj");
            Assert.That(File.Exists(gasTestsProject), Is.True, $"Missing {gasTestsProject}");
            string gasTestsProjectText = File.ReadAllText(gasTestsProject);

            ShowcaseCapabilitySpec[] specs =
            {
                new(
                    Issue: "#245",
                    ModName: "AssociationStressShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "association_stress", "AssociationStressShowcaseMod"),
                    EntryFileName: "AssociationStressShowcaseModEntry.cs",
                    ProjectFileName: "AssociationStressShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "AssociationStressShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "association-stress-showcase"),
                new(
                    Issue: "#246",
                    ModName: "FogVisionDecayShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_vision_decay", "FogVisionDecayShowcaseMod"),
                    EntryFileName: "FogVisionDecayShowcaseModEntry.cs",
                    ProjectFileName: "FogVisionDecayShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "FogVisionDecayShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "fog-vision-decay-showcase"),
                new(
                    Issue: "#247",
                    ModName: "ScopeSwitchShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "scope_switch", "ScopeSwitchShowcaseMod"),
                    EntryFileName: "ScopeSwitchShowcaseModEntry.cs",
                    ProjectFileName: "ScopeSwitchShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "ScopeSwitchShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "scope-switch-showcase"),
                new(
                    Issue: "#248",
                    ModName: "OwnershipCascadeShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "ownership_cascade", "OwnershipCascadeShowcaseMod"),
                    EntryFileName: "OwnershipCascadeShowcaseModEntry.cs",
                    ProjectFileName: "OwnershipCascadeShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "OwnershipCascadeShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "ownership-cascade-showcase"),
                new(
                    Issue: "#249",
                    ModName: "DiplomacyTradeGateShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "diplomacy_trade_gate", "DiplomacyTradeGateShowcaseMod"),
                    EntryFileName: "DiplomacyTradeGateShowcaseModEntry.cs",
                    ProjectFileName: "DiplomacyTradeGateShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "DiplomacyTradeGateShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "diplomacy-trade-gate-showcase"),
                new(
                    Issue: "#250",
                    ModName: "GoldMarketShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "gold_market", "GoldMarketShowcaseMod"),
                    EntryFileName: "GoldMarketShowcaseModEntry.cs",
                    ProjectFileName: "GoldMarketShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "GoldMarketShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "gold-market-showcase"),
                new(
                    Issue: "#251",
                    ModName: "TeamResearchShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "team_research", "TeamResearchShowcaseMod"),
                    EntryFileName: "TeamResearchShowcaseModEntry.cs",
                    ProjectFileName: "TeamResearchShowcaseMod.csproj",
                    AcceptanceTestPath: Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "TeamResearchShowcaseAcceptanceTests.cs"),
                    ArtifactFolder: "team-research-showcase")
            };

            var missing = new List<string>();
            for (int i = 0; i < specs.Length; i++)
            {
                ShowcaseCapabilitySpec spec = specs[i];
                AssertShowcaseCapability(repoRoot, gasTestsProjectText, spec, missing);
            }

            Assert.That(
                missing,
                Is.Empty,
                "AAC-11 (#254) requires every player-visible Entity Association Core feature child issue to carry a real showcase mod and headless acceptance test:\n" +
                string.Join("\n", missing));
        }

        [Test]
        public void Issue254_AssociationCoreHotPaths_DoNotUseLinqOrIteratorBlocks()
        {
            var repoRoot = FindRepoRoot();
            string[] files =
            {
                Path.Combine(repoRoot, "src", "Core", "Association", "EntityKeyedSoaTable.cs"),
                Path.Combine(repoRoot, "src", "Core", "Association", "ScopeKey.cs"),
                Path.Combine(repoRoot, "src", "Core", "EntityCollections", "EntityCollectionStore.cs"),
                Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionStore.cs"),
                Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeRelationCollectionGrants.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeRuntime.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeScopedOperationStore.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "Progression", "ProgressionRequirementEvaluator.cs")
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
                Assert.That(File.Exists(file), Is.True, $"Missing AAC hot-path source file {file}");
                AppendForbiddenSourceTokens(repoRoot, file, forbidden, hits);
            }

            Assert.That(
                hits,
                Is.Empty,
                "AAC-11 (#254) keeps Entity Association Core hot paths allocation-conscious; use cached query descriptions, arrays/spans, and registries instead of LINQ/iterator/transient collection patterns:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void Issue254_Aac10IntegratedShowcase_ExistsAndReferencesFeatureShowcases()
        {
            var repoRoot = FindRepoRoot();
            string modDir = Path.Combine(repoRoot, "mods", "showcases", "fourx_association", "FourXAssociationShowcaseMod");
            string modJsonPath = Path.Combine(modDir, "mod.json");
            string entryPath = Path.Combine(modDir, "FourXAssociationShowcaseModEntry.cs");
            string projectPath = Path.Combine(modDir, "FourXAssociationShowcaseMod.csproj");
            string configPath = Path.Combine(modDir, "assets", "FourXAssociation", "fourx_association_config.json");
            string acceptancePath = Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "FourXAssociationShowcaseAcceptanceTests.cs");

            Assert.That(Directory.Exists(modDir), Is.True, $"Missing {modDir}");
            Assert.That(File.Exists(modJsonPath), Is.True, $"Missing {modJsonPath}");
            Assert.That(File.Exists(entryPath), Is.True, $"Missing {entryPath}");
            Assert.That(File.Exists(projectPath), Is.True, $"Missing {projectPath}");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");
            Assert.That(File.Exists(acceptancePath), Is.True, $"Missing {acceptancePath}");

            string modJson = File.ReadAllText(modJsonPath);
            string config = File.ReadAllText(configPath);
            string acceptance = File.ReadAllText(acceptancePath);

            string[] requiredShowcaseReferences =
            {
                "AssociationStressShowcaseMod",
                "FogVisionDecayShowcaseMod",
                "ScopeSwitchShowcaseMod",
                "OwnershipCascadeShowcaseMod",
                "DiplomacyTradeGateShowcaseMod",
                "GoldMarketShowcaseMod",
                "TeamResearchShowcaseMod"
            };

            Assert.Multiple(() =>
            {
                Assert.That(modJson, Does.Contain("FourXAssociationShowcaseMod"));
                Assert.That(acceptance, Does.Contain("fourx-association-showcase"));
                Assert.That(acceptance, Does.Contain("Path.Combine(repoRoot, \"artifacts\", \"acceptance\""));
                for (int i = 0; i < requiredShowcaseReferences.Length; i++)
                {
                    string reference = requiredShowcaseReferences[i];
                    Assert.That(modJson.Contains(reference, StringComparison.Ordinal) || config.Contains(reference, StringComparison.Ordinal), Is.True, $"Integrated 4X showcase must reference {reference}.");
                }
            });
        }

        [Test]
        public void Issue244_PendingCompositionContracts()
        {
            var repoRoot = FindRepoRoot();
            string commandSourceEligibilityPath = Path.Combine(repoRoot, "src", "Core", "Input", "CommandSources", "CommandSourceEligibility.cs");
            string resolverPath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionResolver.cs");
            string exchangeModelPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeModel.cs");
            string exchangeRuntimePath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Exchange", "ExchangeRuntime.cs");
            string progressionPath = Path.Combine(repoRoot, "src", "Core", "Gameplay", "Progression", "ProgressionRequirementEvaluator.cs");
            string scopePath = Path.Combine(repoRoot, "src", "Core", "Association", "ScopeKey.cs");
            string ownershipPath = Path.Combine(repoRoot, "src", "Core", "Association", "OwnershipResolver.cs");
            string gameEnginePath = Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs");

            string commandSourceEligibility = File.ReadAllText(commandSourceEligibilityPath);
            string resolver = File.ReadAllText(resolverPath);
            string exchangeModel = File.ReadAllText(exchangeModelPath);
            string exchangeRuntime = File.ReadAllText(exchangeRuntimePath);
            string progression = File.ReadAllText(progressionPath);
            string scope = File.ReadAllText(scopePath);
            string ownership = File.ReadAllText(ownershipPath);
            string gameEngine = File.ReadAllText(gameEnginePath);

            Assert.Multiple(() =>
            {
                Assert.That(commandSourceEligibility, Does.Contain("KnowledgeProjectionConsumer"));
                Assert.That(commandSourceEligibility, Does.Contain("CanInspectLive"));
                Assert.That(resolver, Does.Contain("ScopeKey"));
                Assert.That(exchangeRuntime, Does.Contain("RelationshipRuntime"));
                Assert.That(exchangeRuntime, Does.Contain("ValidateRelationships"));
                Assert.That(exchangeModel, Does.Contain("AttributeCost"));
                Assert.That(progression, Does.Contain("ScopeResolver"));
                Assert.That(progression, Does.Contain("ResolveMembers"));
                Assert.That(ownership, Does.Contain("RelationshipRuntime"));
                Assert.That(ownership, Does.Contain("CollectIncoming"));
                Assert.That(ownership, Does.Contain("CollectOutgoing"));
                Assert.That(gameEngine, Does.Contain("GetId(\"Owns\")"));
                Assert.That(scope, Does.Contain("RoleSlot"));
            });
        }

        [Test]
        public void Issue306_WarFogAdr_VisionDomainGuardrails()
        {
            var repoRoot = FindRepoRoot();
            string visionRoot = Path.Combine(repoRoot, "src", "Core", "Vision");
            string coreServiceKeysPath = Path.Combine(repoRoot, "src", "Core", "Scripting", "CoreServiceKeys.cs");
            string gameEnginePath = Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs");
            string knowledgeStorePath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionStore.cs");
            string knowledgeContractsPath = Path.Combine(repoRoot, "src", "Core", "Knowledge", "KnowledgeProjectionContracts.cs");

            Assert.That(Directory.Exists(visionRoot), Is.True, "FOG-2 starts the cell-keyed Vision domain root.");

            string[] visionFiles = Directory.GetFiles(visionRoot, "*.cs", SearchOption.AllDirectories);
            string visionSource = string.Join('\n', Array.ConvertAll(visionFiles, File.ReadAllText));
            string coreServiceKeys = File.ReadAllText(coreServiceKeysPath);
            string gameEngine = File.ReadAllText(gameEnginePath);
            string knowledgeStore = File.ReadAllText(knowledgeStorePath);
            string knowledgeContracts = File.ReadAllText(knowledgeContractsPath);

            Assert.Multiple(() =>
            {
                Assert.That(visionSource, Does.Contain("FogField"), "FOG-1 #306 section 1.1/1.3: fog stores cell-keyed visibility fields.");
                Assert.That(visionSource, Does.Contain("ScopeKey"), "FOG-1 #306 section 1.4: VisionScope is the existing ScopeKey contract.");
                Assert.That(visionSource, Does.Not.Contain("EntityKeyedSoaTable"), "FOG-1 #306 section 1.3: FogField must not use the entity-keyed AAC table.");
                Assert.That(visionSource, Does.Contain("CellVisibility"), "FOG-1 #306 section 1.4: layers carry Unseen/Explored/Visible/Denied states.");
                Assert.That(visionSource, Does.Contain("VerticalVisionRule"), "FOG-1 #306 section 1.5: vertical vision remains independent.");
                Assert.That(visionSource, Does.Contain("LineOfSightRule"), "FOG-1 #306 section 1.5: LoS remains an independent switch.");
                Assert.That(visionSource, Does.Contain("Concealment"), "FOG-1 #306 section 1.5: concealment remains an independent switch.");
                Assert.That(visionSource, Does.Contain("KnowledgeIdMask256"), "FOG-1 #306 section 1.6: projection uses finite Knowledge masks.");
                Assert.That(visionSource, Does.Contain("KnowledgeProjectionStore"), "FOG-1 #306 section 1.1: entity disclosure flows through Knowledge.");
                Assert.That(visionSource, Does.Contain("RelationshipRuntime"), "FOG-1 #306 section 1.7: sharing and cross-scope effects use relationship edges.");
                Assert.That(visionSource, Does.Not.Contain("PlayerId"), "FOG-1 #306 section 1.2/1.8: Core Vision has no participant business semantics.");
                Assert.That(visionSource, Does.Not.Contain("TeamId"), "FOG-1 #306 section 1.2/1.8: Core Vision has no team shortcuts.");
                Assert.That(knowledgeStore, Does.Contain("EntityKeyedSoaTable"), "Knowledge remains the entity-keyed projection SSOT.");
                Assert.That(knowledgeContracts, Does.Contain("KnowledgeIdMask256"));
                Assert.That(coreServiceKeys, Does.Contain("Vision"));
                Assert.That(gameEngine, Does.Contain("Vision"));
            });
        }

        [Test]
        public void Issue314_WarFogGuardrails_CellKeyedKnowledgeOnlyAndShowcasesExist()
        {
            var repoRoot = FindRepoRoot();
            string visionRoot = Path.Combine(repoRoot, "src", "Core", "Vision");
            string testsProjectPath = Path.Combine(repoRoot, "src", "Tests", "GasTests", "GasTests.csproj");
            string acceptancePath = Path.Combine(repoRoot, "src", "Tests", "GasTests", "Production", "FogOfWarShowcaseAcceptanceTests.cs");
            string gameEnginePath = Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs");
            string coreServiceKeysPath = Path.Combine(repoRoot, "src", "Core", "Scripting", "CoreServiceKeys.cs");
            string testsProject = File.ReadAllText(testsProjectPath);
            string gameEngine = File.ReadAllText(gameEnginePath);
            string coreServiceKeys = File.ReadAllText(coreServiceKeysPath);
            string[] visionFiles = Directory.GetFiles(visionRoot, "*.cs", SearchOption.AllDirectories);
            string[] forbiddenVisionTokens =
            {
                "EntityKeyedSoaTable",
                "PlayerId",
                "TeamId",
                "TeamManager",
                "ra2",
                "war3",
                "moba",
                "commando"
            };
            string[] forbiddenHotPathTokens =
            {
                "using System.Linq",
                ".Where(",
                ".Select(",
                ".ToArray(",
                ".ToList(",
                "yield return",
                "IEnumerator",
                "IEnumerable<",
                "new Dictionary<",
                "new List<"
            };

            var hits = new List<string>();
            for (int i = 0; i < visionFiles.Length; i++)
            {
                AppendForbiddenSourceTokens(repoRoot, visionFiles[i], forbiddenVisionTokens, hits);
                AppendForbiddenSourceTokens(repoRoot, visionFiles[i], forbiddenHotPathTokens, hits);
            }

            Assert.That(
                hits,
                Is.Empty,
                "FOG-9 #314 keeps Core Vision cell-keyed, topic-neutral, and allocation-conscious:\n" +
                string.Join("\n", hits));
            Assert.Multiple(() =>
            {
                Assert.That(coreServiceKeys, Does.Contain("VisionFogCellMap"), "FOG runtime must expose the shared elevation/occlusion/concealment source.");
                Assert.That(coreServiceKeys, Does.Contain("FogKnowledgeProjector"), "FOG runtime must expose the fog-to-Knowledge bridge.");
                Assert.That(gameEngine, Does.Contain("new FogCellMap"), "GameEngine must construct the runtime fog cell map.");
                Assert.That(gameEngine, Does.Contain("new FogKnowledgeProjector"), "GameEngine must register the fog-to-Knowledge bridge outside tests.");
                Assert.That(gameEngine, Does.Contain("elevation: visionFogCellMap"), "VisionResolver must receive elevation data in the real engine loop.");
                Assert.That(gameEngine, Does.Contain("occlusion: visionFogCellMap"), "VisionResolver must receive LoS occlusion data in the real engine loop.");
                Assert.That(gameEngine, Does.Contain("new VisionSystem"), "GameEngine must drive resolve/project from the simulation loop.");
                Assert.That(gameEngine, Does.Contain("SystemGroup.PostMovement"), "VisionSystem belongs after movement so it reads authoritative positions.");
            });

            ShowcaseCapabilitySpec[] specs =
            {
                new(
                    Issue: "#307",
                    ModName: "MultiLayerFogFieldShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "MultiLayerFogFieldShowcaseMod"),
                    EntryFileName: "MultiLayerFogFieldShowcaseModEntry.cs",
                    ProjectFileName: "MultiLayerFogFieldShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#308",
                    ModName: "VisionConeHighGroundShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "VisionConeHighGroundShowcaseMod"),
                    EntryFileName: "VisionConeHighGroundShowcaseModEntry.cs",
                    ProjectFileName: "VisionConeHighGroundShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#309",
                    ModName: "LineOfSightBrushShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "LineOfSightBrushShowcaseMod"),
                    EntryFileName: "LineOfSightBrushShowcaseModEntry.cs",
                    ProjectFileName: "LineOfSightBrushShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#310",
                    ModName: "ExploredMemoryShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "ExploredMemoryShowcaseMod"),
                    EntryFileName: "ExploredMemoryShowcaseModEntry.cs",
                    ProjectFileName: "ExploredMemoryShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#311",
                    ModName: "GapGeneratorShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "GapGeneratorShowcaseMod"),
                    EntryFileName: "GapGeneratorShowcaseModEntry.cs",
                    ProjectFileName: "GapGeneratorShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#312",
                    ModName: "StealthDetectionShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "StealthDetectionShowcaseMod"),
                    EntryFileName: "StealthDetectionShowcaseModEntry.cs",
                    ProjectFileName: "StealthDetectionShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#313",
                    ModName: "SharedVisionSnapshotShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "SharedVisionSnapshotShowcaseMod"),
                    EntryFileName: "SharedVisionSnapshotShowcaseModEntry.cs",
                    ProjectFileName: "SharedVisionSnapshotShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase"),
                new(
                    Issue: "#315",
                    ModName: "FogOfWarShowcaseMod",
                    ModDirectory: Path.Combine(repoRoot, "mods", "showcases", "fog_of_war", "FogOfWarShowcaseMod"),
                    EntryFileName: "FogOfWarShowcaseModEntry.cs",
                    ProjectFileName: "FogOfWarShowcaseMod.csproj",
                    AcceptanceTestPath: acceptancePath,
                    ArtifactFolder: "fog-of-war-showcase")
            };

            var missing = new List<string>();
            for (int i = 0; i < specs.Length; i++)
            {
                AssertShowcaseCapability(repoRoot, testsProject, specs[i], missing);
            }

            Assert.That(
                missing,
                Is.Empty,
                "FOG-9 #314 and FOG-10 #315 require formal War Fog showcase roots and headless acceptance evidence:\n" +
                string.Join("\n", missing));
        }

        private static void AssertShowcaseCapability(
            string repoRoot,
            string gasTestsProject,
            ShowcaseCapabilitySpec spec,
            List<string> missing)
        {
            string modJsonPath = Path.Combine(spec.ModDirectory, "mod.json");
            string entryPath = Path.Combine(spec.ModDirectory, spec.EntryFileName);
            string projectPath = Path.Combine(spec.ModDirectory, spec.ProjectFileName);
            if (!Directory.Exists(spec.ModDirectory))
            {
                missing.Add($"{spec.Issue}: missing mod directory {ToRepoRelativePath(repoRoot, spec.ModDirectory)}");
                return;
            }

            if (!File.Exists(modJsonPath))
            {
                missing.Add($"{spec.Issue}: missing mod.json {ToRepoRelativePath(repoRoot, modJsonPath)}");
            }
            else
            {
                string modJson = File.ReadAllText(modJsonPath);
                if (!modJson.Contains(spec.ModName, StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: mod.json does not declare {spec.ModName}");
                }
            }

            if (!File.Exists(entryPath))
            {
                missing.Add($"{spec.Issue}: missing mod entry {ToRepoRelativePath(repoRoot, entryPath)}");
            }
            else
            {
                string entry = File.ReadAllText(entryPath);
                if (!entry.Contains("context.OnEvent(GameEvents.GameStart", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: entry does not install runtime on GameStart");
                }

                if (!entry.Contains("context.OnEvent(GameEvents.MapLoaded", StringComparison.Ordinal) ||
                    !entry.Contains("context.OnEvent(GameEvents.MapResumed", StringComparison.Ordinal) ||
                    !entry.Contains("context.OnEvent(GameEvents.MapUnloaded", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: entry does not handle MapLoaded/MapResumed/MapUnloaded");
                }

                if (!entry.Contains("RegisterSystem(", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: entry does not register a simulation system");
                }

                if (!entry.Contains("RegisterPresentationSystem(", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: entry does not register a presentation system");
                }
            }

            if (!File.Exists(projectPath))
            {
                missing.Add($"{spec.Issue}: missing project file {ToRepoRelativePath(repoRoot, projectPath)}");
            }

            if (!File.Exists(spec.AcceptanceTestPath))
            {
                missing.Add($"{spec.Issue}: missing acceptance test {ToRepoRelativePath(repoRoot, spec.AcceptanceTestPath)}");
            }
            else
            {
                string acceptance = File.ReadAllText(spec.AcceptanceTestPath);
                if (!acceptance.Contains("[Test]", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: acceptance test has no NUnit [Test] method");
                }

                if (!acceptance.Contains("Path.Combine(repoRoot, \"artifacts\", \"acceptance\"", StringComparison.Ordinal) &&
                    !acceptance.Contains("Path.Combine(FindRepoRoot(), \"artifacts\", \"acceptance\"", StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: acceptance test does not write artifacts/acceptance evidence");
                }

                if (!acceptance.Contains(spec.ArtifactFolder, StringComparison.Ordinal))
                {
                    missing.Add($"{spec.Issue}: acceptance test does not name artifact folder {spec.ArtifactFolder}");
                }
            }

            string normalizedProjectFile = spec.ProjectFileName.Replace('\\', '/');
            if (!gasTestsProject.Contains(normalizedProjectFile, StringComparison.Ordinal))
            {
                missing.Add($"{spec.Issue}: GasTests.csproj does not reference {spec.ProjectFileName}");
            }
        }

        private readonly record struct ShowcaseCapabilitySpec(
            string Issue,
            string ModName,
            string ModDirectory,
            string EntryFileName,
            string ProjectFileName,
            string AcceptanceTestPath,
            string ArtifactFolder);

        private readonly record struct Issue361ShowcaseSpec(
            string Issue,
            string Binding,
            string ModName,
            string ArtifactFolder,
            string AcceptanceTestName,
            bool PurePhysics);

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

        private static void AppendForbiddenJsonKeys(
            string repoRoot,
            string file,
            JsonNode node,
            string jsonPath,
            IReadOnlyList<string> forbiddenKeys,
            List<string> hits)
        {
            if (node is JsonObject obj)
            {
                foreach ((string key, JsonNode? child) in obj)
                {
                    for (int i = 0; i < forbiddenKeys.Count; i++)
                    {
                        if (string.Equals(key, forbiddenKeys[i], StringComparison.Ordinal))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{jsonPath}.{key}");
                            break;
                        }
                    }

                    if (child != null)
                    {
                        AppendForbiddenJsonKeys(repoRoot, file, child, $"{jsonPath}.{key}", forbiddenKeys, hits);
                    }
                }

                return;
            }

            if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    JsonNode? child = arr[i];
                    if (child != null)
                    {
                        AppendForbiddenJsonKeys(repoRoot, file, child, $"{jsonPath}[{i}]", forbiddenKeys, hits);
                    }
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
