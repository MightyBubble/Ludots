using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Story
{
    /// <summary>
    /// Cucumber-style Story Runtime modernization coverage for #1083 fail-closed migration paths
    /// and focused Sequencer section/signal behavior.
    /// </summary>
    [TestFixture]
    public sealed class StoryRuntimeModernizationTests
    {
        [Test]
        public void LegacyNarrativeVariablesCatalogPath_FailsClosedWithMigrationMessage()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Narrative/variables.json", ConfigMergePolicy.DeepObject));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LegacyNarrativeConfigGuard.RejectIfPresent(catalog))!;

            Assert.That(ex.Message, Does.Contain("Narrative/variables.json"));
            Assert.That(ex.Message, Does.Contain(LegacyNarrativeConfigGuard.MigrationMessage));
        }

        [Test]
        public void LegacyNarrativeDialoguesAndCinematicsCatalogPaths_FailClosed()
        {
            foreach (string path in new[] { "Narrative/dialogues.json", "Narrative/cinematics.json" })
            {
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry(path, ConfigMergePolicy.ArrayById, idField: "id"));

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => LegacyNarrativeConfigGuard.RejectIfPresent(catalog))!;

                Assert.That(ex.Message, Does.Contain(path));
                Assert.That(ex.Message, Does.Contain("Migrate content to Dialogue/dialogues.json"));
            }
        }

        [Test]
        public void DialogueConfigLoader_RejectsLegacyInlineConditionsActionsAndText()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                WriteCatalog(root, """
                [
                  { "Path": "Dialogue/dialogues.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
                WriteAsset(root, "Dialogue/dialogues.json", """
                [
                  {
                    "id": "Dialogue.Legacy.Bad",
                    "displayName": "Legacy",
                    "entryNode": "n1",
                    "nodes": [
                      {
                        "id": "n1",
                        "lineId": "line.unused",
                        "presentationProfile": "story.dialogue_overlay",
                        "choices": [
                          {
                            "id": "c1",
                            "text": "legacy inline choice text",
                            "conditions": [ { "kind": "VariableIntEquals", "variableId": "lore", "intValue": 1 } ],
                            "actions": [ { "kind": "SetVariableInt", "variableId": "trust", "intValue": 1 } ],
                            "nextNodeId": "n2"
                          }
                        ]
                      }
                    ]
                  }
                ]
                """);

                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var registry = new DialogueDefinitionRegistry();
                var loader = new DialogueConfigLoader(pipeline, registry);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
                Assert.That(ex.Message, Does.Contain("legacy"));
                Assert.That(ex.Message, Does.Contain("conditionGraphId"));
                Assert.That(ex.Message, Does.Contain(LegacyNarrativeConfigGuard.MigrationMessage));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void DialogueConfigLoader_RejectsLegacyStartNodeIdAndInlineSpeakerText()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                WriteCatalog(root, """
                [
                  { "Path": "Dialogue/dialogues.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
                WriteAsset(root, "Dialogue/dialogues.json", """
                [
                  {
                    "id": "Dialogue.Legacy.Start",
                    "displayName": "Legacy Start",
                    "startNodeId": "old_entry",
                    "entryNode": "old_entry",
                    "nodes": [
                      {
                        "id": "old_entry",
                        "text": "inline body",
                        "speakerName": "Warden",
                        "presentationProfile": "story.dialogue_overlay"
                      }
                    ]
                  }
                ]
                """);

                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var registry = new DialogueDefinitionRegistry();
                var loader = new DialogueConfigLoader(pipeline, registry);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
                Assert.That(ex.Message, Does.Contain("startNodeId"));
                Assert.That(ex.Message, Does.Contain(LegacyNarrativeConfigGuard.MigrationMessage));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void DialogueConfigLoader_AcceptsModernConditionGraphIdAndActionGraphIdShape()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                WriteCatalog(root, """
                [
                  { "Path": "Dialogue/dialogues.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
                WriteAsset(root, "Dialogue/dialogues.json", """
                [
                  {
                    "id": "Dialogue.Modern.ChoiceGraphs",
                    "displayName": "Modern",
                    "entryNode": "entry",
                    "nodes": [
                      {
                        "id": "entry",
                        "lineId": "line.modern.entry",
                        "presentationProfile": "story.dialogue_overlay",
                        "choices": [
                          {
                            "id": "gated",
                            "lineId": "line.modern.choice",
                            "conditionGraphId": "Graph.Story.Condition.AlwaysTrue",
                            "actionGraphId": "Graph.Story.Action.NoOp",
                            "nextNode": "exit"
                          }
                        ]
                      },
                      {
                        "id": "exit",
                        "lineId": "line.modern.exit",
                        "presentationProfile": "story.dialogue_overlay"
                      }
                    ]
                  }
                ]
                """);

                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var registry = new DialogueDefinitionRegistry();
                var loader = new DialogueConfigLoader(pipeline, registry);

                Assert.DoesNotThrow(() => loader.Load(catalog));
                Assert.That(registry.TryGet("Dialogue.Modern.ChoiceGraphs", out DialogueDefinition definition), Is.True);
                Assert.That(definition.Nodes[0].Choices[0].ConditionGraphId, Is.EqualTo("Graph.Story.Condition.AlwaysTrue"));
                Assert.That(definition.Nodes[0].Choices[0].ActionGraphId, Is.EqualTo("Graph.Story.Action.NoOp"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void DialogueRuntime_ChoiceWithoutConditionGraph_IsAvailableAndAdvanceClearsSession()
        {
            using GameEngine engine = CreateCoreEngine();
            var dialogues = new DialogueDefinitionRegistry();
            var story = new StoryDefinitionRegistry();
            RegisterUnitSpeakers(story);
            DialogueRuntime dialogue = CreateIsolatedDialogueRuntime(engine, dialogues, story);

            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.hello",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.hello"
            });
            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.choice",
                SpeakerId = "speaker.player",
                TextToken = "story.unit.choice"
            });
            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.exit",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.exit"
            });
            story.Register(new StoryPresentationProfileDefinition
            {
                Id = "story.dialogue_overlay",
                Backend = StoryPresentationBackend.ScreenOverlay,
                SurfaceKind = "OverlayDialogue",
                Anchor = "BottomCenter",
                Width = 760f,
                ImageSize = 112f,
                ZIndex = 60,
                ChoiceAnchor = "BottomRight",
                ChoiceWidth = 440f,
                ChoiceOffsetY = 12f,
                ChoiceZIndex = 61
            });
            dialogues.Register(new DialogueDefinition
            {
                Id = "dialogue.unit.choice",
                EntryNode = "hello",
                Nodes =
                {
                    new DialogueNodeDefinition
                    {
                        Id = "hello",
                        LineId = "line.unit.hello",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition
                            {
                                Id = "go",
                                LineId = "line.unit.choice",
                                NextNode = "exit"
                            }
                        }
                    },
                    new DialogueNodeDefinition
                    {
                        Id = "exit",
                        LineId = "line.unit.exit",
                        PresentationProfile = "story.dialogue_overlay"
                    }
                }
            });

            dialogue.StartDialogue("dialogue.unit.choice");
            Assert.That(dialogue.TryGetActiveView(out DialogueView open), Is.True);
            Assert.That(open.Choices.Count, Is.EqualTo(1));
            Assert.That(open.Choices[0].ChoiceId, Is.EqualTo("go"));
            Assert.That(open.Choices[0].LineId, Is.EqualTo("line.unit.choice"));
            Assert.That(open.PortraitImageId, Is.Not.Null);
            Assert.That(open.StandingImageId, Is.Not.Null);

            var projector = new StoryPresentationProjector(story);
            StoryPresentationFrame frame = projector.ProjectDialogue(open);
            Assert.That(frame.Handle.IsValid, Is.True);
            Assert.That(frame.Handle.StreamId, Is.EqualTo("dialogue.unit.choice"));
            Assert.That(frame.Surfaces.Count, Is.EqualTo(2));
            Assert.That(frame.Surfaces[0].Body, Is.EqualTo("story.unit.hello").Or.Not.Empty);
            Assert.That(frame.Surfaces[1].SurfaceKind, Is.EqualTo("ChoiceList"));
            Assert.That(frame.Surfaces[1].Choices![0].ChoiceId, Is.EqualTo("go"));
            Assert.That(frame.Surfaces[1].Choices![0].Text, Is.Not.Null);

            dialogue.ChooseOption(0);
            Assert.That(dialogue.TryGetActiveView(out DialogueView afterChoice), Is.True);
            Assert.That(afterChoice.NodeId, Is.EqualTo("exit"));
            Assert.That(afterChoice.Choices.Count, Is.EqualTo(0));

            dialogue.AdvanceDialogue();
            Assert.That(dialogue.HasActiveDialogue, Is.False);
        }

        [Test]
        public void DialogueRuntime_ChoiceCommit_CompletesOldestDialogConfirmWaiter()
        {
            using GameEngine engine = CreateCoreEngine();
            GraphCallbackService callbacks = engine.GetService(CoreServiceKeys.GraphCallbackService)
                ?? throw new InvalidOperationException("GraphCallbackService missing.");
            var dialogues = new DialogueDefinitionRegistry();
            var story = new StoryDefinitionRegistry();
            RegisterUnitSpeakers(story);
            DialogueRuntime dialogue = CreateIsolatedDialogueRuntime(engine, dialogues, story);

            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.hello",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.hello"
            });
            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.choice",
                SpeakerId = "speaker.player",
                TextToken = "story.unit.choice"
            });
            story.Register(new StoryPresentationProfileDefinition
            {
                Id = "story.dialogue_overlay",
                Backend = StoryPresentationBackend.ScreenOverlay,
                SurfaceKind = "OverlayDialogue",
                Anchor = "BottomCenter",
                Width = 760f,
                ImageSize = 112f,
                ZIndex = 60,
                ChoiceAnchor = "BottomRight",
                ChoiceWidth = 440f,
                ChoiceOffsetY = 12f,
                ChoiceZIndex = 61
            });
            dialogues.Register(new DialogueDefinition
            {
                Id = "dialogue.unit.confirm",
                EntryNode = "hello",
                Nodes =
                {
                    new DialogueNodeDefinition
                    {
                        Id = "hello",
                        LineId = "line.unit.hello",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition
                            {
                                Id = "go",
                                LineId = "line.unit.choice",
                                NextNode = string.Empty
                            }
                        }
                    }
                }
            });

            var mount = new RecordingCallbackTarget();
            callbacks.PushResumeTarget(mount);
            callbacks.BeginAwait(GraphCallbackTypes.DialogConfirm, default, Arch.Core.Entity.Null, resultBoolRegister: 2);
            callbacks.PopResumeTarget(mount);

            dialogue.StartDialogue("dialogue.unit.confirm");
            dialogue.ChooseOption(0);

            Assert.That(
                callbacks.TryGetOldestLiveHandleByCallbackType(GraphCallbackTypes.DialogConfirm, out _),
                Is.False,
                "Completed DialogConfirm waiter must not stay selectable as live.");

            callbacks.Drain();
            Assert.That(mount.ResumeCount, Is.EqualTo(1));
            Assert.That(mount.LastConfirmed, Is.True);
            Assert.That(mount.LastResultBoolRegister, Is.EqualTo(2));
            Assert.That(dialogue.HasActiveDialogue, Is.False);
        }

        private sealed class RecordingCallbackTarget : Ludots.Core.GraphRuntime.IGraphCallbackResumeTarget
        {
            public int ResumeCount { get; private set; }
            public bool LastConfirmed { get; private set; }
            public int LastResultBoolRegister { get; private set; } = -1;
            public bool IsCallbackResumeAlive => true;

            public void ResumeAfterGraphCallback(int handleId, bool confirmed, int resultBoolRegister)
            {
                ResumeCount++;
                LastConfirmed = confirmed;
                LastResultBoolRegister = resultBoolRegister;
            }
        }

        [Test]
        public void SequencerRuntime_ActivatesCameraAndSubtitleSections_AndFiresSignalOnce()
        {
            using GameEngine engine = CreateCoreEngine();
            const string signalActionGraphId = "Graph.Story.Unit.SignalNoOp";
            RegisterHaltTriggerGraph(engine, signalActionGraphId);

            var sequences = new SequenceDefinitionRegistry();
            var story = new StoryDefinitionRegistry();
            RegisterUnitSpeakers(story);
            SequencerRuntime sequencer = CreateIsolatedSequencerRuntime(engine, sequences, story);

            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.subtitle",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.subtitle"
            });

            sequences.Register(new SequenceDefinition
            {
                Id = "Sequence.Unit.CameraSubtitleSignal",
                DisplayNameToken = "story.unit.sequence.main",
                ClearCameraOnComplete = false,
                Clock = new SequenceClockDefinition { Rate = 1f },
                Tracks =
                {
                    new SequenceTrackDefinition
                    {
                        Type = SequenceTrackType.Camera,
                        Profile = "Camera.Unit.Close",
                        Start = 0f,
                        Duration = 1f
                    },
                    new SequenceTrackDefinition
                    {
                        Type = SequenceTrackType.Subtitle,
                        LineId = "line.unit.subtitle",
                        PresentationProfile = "story.immersive_subtitle",
                        Start = 0f,
                        Duration = 1f
                    },
                    new SequenceTrackDefinition
                    {
                        Type = SequenceTrackType.Signal,
                        EventId = "unit.signal.once",
                        ActionGraphId = signalActionGraphId,
                        Start = 0.4f
                    }
                }
            });

            int sectionEntered = 0;
            int signalFired = 0;
            string? lastCameraProfile = null;
            string? lastSignalEventId = null;
            engine.TriggerManager.RegisterEventHandler(SequencerEventKeys.SectionEntered, context =>
            {
                sectionEntered++;
                string trackType = context.Get(SequencerServiceKeys.TrackType) ?? string.Empty;
                if (string.Equals(trackType, SequenceTrackType.Camera.ToString(), StringComparison.Ordinal))
                {
                    lastCameraProfile = engine.GetService(CoreServiceKeys.VirtualCameraRequest)?.Id;
                }

                return Task.CompletedTask;
            });
            engine.TriggerManager.RegisterEventHandler(SequencerEventKeys.SignalFired, context =>
            {
                signalFired++;
                lastSignalEventId = context.Get(SequencerServiceKeys.EventId);
                return Task.CompletedTask;
            });

            sequencer.Start("Sequence.Unit.CameraSubtitleSignal");
            Assert.That(sequencer.HasActiveSequence, Is.True);
            Assert.That(sequencer.TryGetActiveView(out SequenceView atStart), Is.True);
            Assert.That(atStart.ActiveCameraProfile, Is.EqualTo("Camera.Unit.Close"));
            Assert.That(atStart.ActiveSubtitles.Count, Is.EqualTo(1));
            Assert.That(atStart.ActiveSubtitles[0].ResolvedText, Is.EqualTo("字幕轨"));
            Assert.That(sectionEntered, Is.EqualTo(2));
            Assert.That(engine.GetService(CoreServiceKeys.VirtualCameraRequest)?.Id, Is.EqualTo("Camera.Unit.Close"));
            Assert.That(lastCameraProfile, Is.EqualTo("Camera.Unit.Close"));
            Assert.That(signalFired, Is.EqualTo(0));

            sequencer.Update(0.45f);
            Assert.That(signalFired, Is.EqualTo(1));
            Assert.That(lastSignalEventId, Is.EqualTo("unit.signal.once"));

            sequencer.Update(0.1f);
            Assert.That(signalFired, Is.EqualTo(1), "Signal tracks must fire once per playthrough.");
        }

        [Test]
        public void SequencerRuntime_PauseBlocksClock_ResumeAndSetRateAdvance_SeekRefiresSignals()
        {
            using GameEngine engine = CreateCoreEngine();
            const string signalActionGraphId = "Graph.Story.Unit.ClockSignalNoOp";
            RegisterHaltTriggerGraph(engine, signalActionGraphId);

            var sequences = new SequenceDefinitionRegistry();
            var story = new StoryDefinitionRegistry();
            RegisterUnitSpeakers(story);
            SequencerRuntime sequencer = CreateIsolatedSequencerRuntime(engine, sequences, story);

            sequences.Register(new SequenceDefinition
            {
                Id = "Sequence.Unit.ClockControls",
                DisplayNameToken = "story.unit.sequence.clock",
                ClearCameraOnComplete = false,
                Clock = new SequenceClockDefinition { Rate = 1f },
                Tracks =
                {
                    new SequenceTrackDefinition
                    {
                        Type = SequenceTrackType.Camera,
                        Profile = "Camera.Unit.Clock",
                        Start = 0f,
                        Duration = 2f
                    },
                    new SequenceTrackDefinition
                    {
                        Type = SequenceTrackType.Signal,
                        EventId = "unit.clock.signal",
                        ActionGraphId = signalActionGraphId,
                        Start = 0.5f
                    }
                }
            });

            int signalFired = 0;
            engine.TriggerManager.RegisterEventHandler(SequencerEventKeys.SignalFired, _ =>
            {
                signalFired++;
                return Task.CompletedTask;
            });

            sequencer.Start("Sequence.Unit.ClockControls");
            Assert.That(sequencer.TryGetActiveView(out SequenceView started), Is.True);
            Assert.That(started.Time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(started.Rate, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(started.Paused, Is.False);

            sequencer.Pause();
            Assert.That(sequencer.TryGetActiveView(out SequenceView paused), Is.True);
            Assert.That(paused.Paused, Is.True);
            sequencer.Update(1f);
            Assert.That(sequencer.TryGetActiveView(out SequenceView stillPaused), Is.True);
            Assert.That(stillPaused.Time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(signalFired, Is.EqualTo(0));

            sequencer.Resume();
            sequencer.SetRate(2f);
            Assert.That(sequencer.TryGetActiveView(out SequenceView resumed), Is.True);
            Assert.That(resumed.Paused, Is.False);
            Assert.That(resumed.Rate, Is.EqualTo(2f).Within(0.0001f));

            sequencer.Update(0.2f);
            Assert.That(sequencer.TryGetActiveView(out SequenceView mid), Is.True);
            Assert.That(mid.Time, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(signalFired, Is.EqualTo(0));

            sequencer.Update(0.1f);
            Assert.That(signalFired, Is.EqualTo(1));

            sequencer.Seek(0f);
            Assert.That(sequencer.HasActiveSequence, Is.True);
            Assert.That(sequencer.TryGetActiveView(out SequenceView seeked), Is.True);
            Assert.That(seeked.Time, Is.EqualTo(0f).Within(0.0001f));

            sequencer.Seek(0.5f);
            Assert.That(signalFired, Is.EqualTo(2), "Seek must clear fired signals and allow SignalTrack to fire again.");

            sequencer.Skip();
            Assert.That(sequencer.HasActiveSequence, Is.False);
        }

        [Test]
        public void DialogueRuntime_HidesChoiceWhenConditionGraphReturnsZero()
        {
            using GameEngine engine = CreateCoreEngine();
            const string conditionGraphId = "Graph.Story.Unit.ConditionFalse";
            RegisterHaltQueryGraphReturning(engine, conditionGraphId, returnValue: 0);

            var dialogues = new DialogueDefinitionRegistry();
            var story = new StoryDefinitionRegistry();
            RegisterUnitSpeakers(story);
            DialogueRuntime dialogue = CreateIsolatedDialogueRuntime(engine, dialogues, story);

            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.gate",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.gate"
            });
            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.locked",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.locked"
            });
            story.Register(new StoryLineDefinition
            {
                Id = "line.unit.open",
                SpeakerId = "speaker.guide",
                TextToken = "story.unit.open"
            });

            dialogues.Register(new DialogueDefinition
            {
                Id = "dialogue.unit.gated",
                DisplayName = "Gated",
                EntryNode = "root",
                Nodes =
                {
                    new DialogueNodeDefinition
                    {
                        Id = "root",
                        LineId = "line.unit.gate",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition
                            {
                                Id = "locked",
                                LineId = "line.unit.locked",
                                ConditionGraphId = conditionGraphId,
                                NextNode = "root"
                            },
                            new DialogueChoiceDefinition
                            {
                                Id = "always",
                                LineId = "line.unit.open",
                                NextNode = "root"
                            }
                        }
                    }
                }
            });

            dialogue.StartDialogue("dialogue.unit.gated");
            Assert.That(dialogue.TryGetActiveView(out DialogueView view), Is.True);
            Assert.That(view.Choices.Count, Is.EqualTo(1));
            Assert.That(view.Choices[0].ChoiceId, Is.EqualTo("always"));
        }

        private static GameEngine CreateCoreEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            return engine;
        }


        private static void RegisterUnitSpeakers(StoryDefinitionRegistry story)
        {
            story.Register(new StorySpeakerDefinition
            {
                Id = "speaker.guide",
                DisplayNameToken = "story.unit.speaker.guide",
                PortraitImageId = "image.unit.guide.portrait",
                StandingImageId = "image.unit.guide.standing"
            });
            story.Register(new StorySpeakerDefinition
            {
                Id = "speaker.player",
                DisplayNameToken = "story.unit.speaker.player",
                PortraitImageId = "image.unit.player.portrait",
                StandingImageId = "image.unit.player.standing"
            });
        }

        private static PresentationTextCatalog CreateUnitTextCatalog()
        {
            var templates = new (string Token, string Text)[]
            {
                ("story.unit.speaker.guide", "单元向导"),
                ("story.unit.speaker.player", "单元玩家"),
                ("story.unit.hello", "向导打招呼"),
                ("story.unit.choice", "选择离开"),
                ("story.unit.exit", "道别"),
                ("story.unit.subtitle", "字幕轨"),
                ("story.unit.gate", "闸门开放"),
                ("story.unit.locked", "闸门锁定"),
                ("story.unit.open", "闸门开启"),
                ("story.unit.sequence.main", "单元演出"),
                ("story.unit.sequence.clock", "时钟控制"),
                ("story.unit.sequence.gated", "闸控"),
            };

            var tokenIds = new Ludots.Core.Registry.StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var tokens = new PresentationTextTokenDefinition[64];
            foreach (var (token, _) in templates)
            {
                int tokenId = tokenIds.Register(token);
                tokens[tokenId] = new PresentationTextTokenDefinition { TokenId = tokenId, Key = token, ArgCount = 0 };
            }
            tokenIds.Freeze();

            var localeIds = new Ludots.Core.Registry.StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int localeId = localeIds.Register("zh-CN");
            localeIds.Freeze();
            var localeTemplates = new PresentationTextTemplate[64];
            foreach (var (token, text) in templates)
            {
                int tokenId = tokenIds.GetId(token);
                localeTemplates[tokenId] = new PresentationTextTemplate(
                    text,
                    new[] { new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, text, 0) });
            }
            var locales = new PresentationTextLocaleTable[2];
            locales[localeId] = new PresentationTextLocaleTable(localeId, "zh-CN", localeTemplates);

            return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: localeId);
        }

        private static DialogueRuntime CreateIsolatedDialogueRuntime(
            GameEngine engine,
            DialogueDefinitionRegistry dialogues,
            StoryDefinitionRegistry story)
        {
            TaskRuntimeService tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService)
                ?? throw new InvalidOperationException("TaskRuntimeService missing.");
            StoryGraphInvoker graphs = engine.GetService(CoreServiceKeys.StoryGraphInvoker)
                ?? new StoryGraphInvoker(engine);
            return new DialogueRuntime(engine, dialogues, story, graphs, tasks, CreateUnitTextCatalog());
        }

        private static SequencerRuntime CreateIsolatedSequencerRuntime(
            GameEngine engine,
            SequenceDefinitionRegistry sequences,
            StoryDefinitionRegistry story)
        {
            TaskRuntimeService tasks = engine.GetService(CoreServiceKeys.TaskRuntimeService)
                ?? throw new InvalidOperationException("TaskRuntimeService missing.");
            StoryGraphInvoker graphs = engine.GetService(CoreServiceKeys.StoryGraphInvoker)
                ?? new StoryGraphInvoker(engine);
            return new SequencerRuntime(engine, sequences, story, graphs, tasks, CreateUnitTextCatalog());
        }

        private static void RegisterHaltTriggerGraph(GameEngine engine, string graphName)
            => RegisterHaltGraphReturning(engine, graphName, GraphKind.TriggerGraph, returnValue: 1);

        private static void RegisterHaltQueryGraphReturning(GameEngine engine, string graphName, int returnValue)
            => RegisterHaltGraphReturning(engine, graphName, GraphKind.Query, returnValue);

        private static void RegisterHaltTriggerGraphReturning(GameEngine engine, string graphName, int returnValue)
            => RegisterHaltGraphReturning(engine, graphName, GraphKind.TriggerGraph, returnValue);

        private static void RegisterHaltGraphReturning(
            GameEngine engine,
            string graphName,
            GraphKind kind,
            int returnValue)
        {
            RegistryMapping[] mappings = GraphIdRegistry.SnapshotMappings();
            GraphIdRegistry.Clear();
            Array.Sort(mappings, (a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < mappings.Length; i++)
            {
                GraphIdRegistry.Register(mappings[i].Name);
            }

            int graphId = GraphIdRegistry.Register(graphName);
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = returnValue },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            TriggerGraphEntry[]? entries = kind == GraphKind.TriggerGraph
                ? new[] { new TriggerGraphEntry("story_invoke", "Story.ManualInvoke", startPc: 0, once: true) }
                : null;
            engine.GetService(CoreServiceKeys.GraphProgramRegistry)!
                .Register(graphId, program, kind, GraphInstructionSourceMap.Empty, null, entries);
        }

        private static string CreateTempRoot(out ConfigPipeline pipeline)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_StoryRuntimeModernizationTests",
                Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            pipeline = new ConfigPipeline(vfs, modLoader);
            return root;
        }

        private static void WriteCatalog(string root, string contents)
        {
            File.WriteAllText(Path.Combine(root, "Core", "config_catalog.json"), contents);
        }

        private static void WriteAsset(string root, string relativePath, string contents)
        {
            string path = Path.Combine(root, "Core", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        private static void DeleteTempRoot(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }
    }
}
