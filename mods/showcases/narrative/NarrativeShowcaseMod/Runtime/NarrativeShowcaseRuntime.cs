using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Client;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;
using NarrativeShowcaseMod.Input;
using NarrativeShowcaseMod.Systems;

namespace NarrativeShowcaseMod.Runtime
{
    internal sealed class NarrativeShowcaseRuntime
    {
        private const int ShowcaseLocalPlayerId = 1;
        private const float UiMargin = 24f;
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();

        private readonly IModContext _context;
        private readonly NarrativeShowcaseFrontendConfig _frontendConfig;
        private readonly List<string> _history = new();
        private bool _narrativeInputActive;
        private bool _interactionInputActive;
        private bool _taskHookInstalled;
        private int _historySerial;
        private string _panelFrameSrc = string.Empty;
        private string _choiceFrameSrc = string.Empty;

        internal NarrativeShowcaseRuntime(IModContext context)
        {
            _context = context;
            using var stream = context.GetResource($"{context.ModId}:assets/Frontend/narrative_frontend.json");
            _frontendConfig = NarrativeShowcaseFrontendConfig.Load(stream);
            if (ReferenceEquals(_frontendConfig.DialogueBubble, _frontendConfig.OverlayDialogue) ||
                ReferenceEquals(_frontendConfig.DialogueBubble, _frontendConfig.StandingPortrait))
            {
                throw new InvalidOperationException("Narrative frontend surface configs must be distinct objects.");
            }

        }

        public Task HandleGameStartAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue("NarrativeShowcase.SystemsInstalled", out var installedObj) && installedObj is bool installed && installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["NarrativeShowcase.SystemsInstalled"] = true;
            engine.GlobalContext["NarrativeShowcase.Runtime"] = this;
            engine.RegisterSystem(new NarrativeShowcaseInteractionSystem(engine, this), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new NarrativeShowcasePanelPresentationSystem(engine, this));
            EnsureTaskHook(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            bool showcaseActive = string.Equals(activeMapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase);
            var input = context.Get(CoreServiceKeys.InputHandler);
            if (showcaseActive)
            {
                ActivateInputContexts(input);
                EnsureViewMode(engine);
                RequireShowcaseSolePossessedRep(engine, activeMapId);
                PublishShowcaseKnowledge(engine, activeMapId);
                EnsureBootstrapped(engine);
                RebindEntities(engine);
                RefreshPanel(engine);
                engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = true;
            }
            else
            {
                DeactivateInputContexts(input);
                ClearFrontend(engine);
                engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = false;
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value ?? string.Empty;
            if (!string.Equals(mapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            DeactivateInputContexts(context.Get(CoreServiceKeys.InputHandler));
            ClearFrontend(engine);
            ResetHistory();
            engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BootstrappedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = false;
            return Task.CompletedTask;
        }

        public Task HandleDialogueNodeEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.DialogueEntered, new Dictionary<string, string>
            {
                ["speaker"] = ResolveSpeakerDisplay(engine, context.Get(DialogueServiceKeys.SpeakerId) ?? string.Empty),
                ["bodyText"] = context.Get(DialogueServiceKeys.BodyText) ?? string.Empty,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleDialogueChoiceCommittedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.DialogueChoiceCommitted, new Dictionary<string, string>
            {
                ["bodyText"] = context.Get(DialogueServiceKeys.BodyText) ?? string.Empty,
            });

            string choiceId = context.Get(DialogueServiceKeys.DialogueChoiceId) ?? string.Empty;
            if (engine.GetService(CoreServiceKeys.TaskRuntimeService) is TaskRuntimeService tasks)
            {
                IReadOnlyList<string> signals = _frontendConfig.ResolveChoiceSignals(choiceId);
                for (int i = 0; i < signals.Count; i++)
                {
                    EmitShowcaseSignal(engine, tasks, signals[i]);
                }
            }

            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleSequencerSectionEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            string trackType = context.Get(SequencerServiceKeys.TrackType) ?? string.Empty;
            if (!string.Equals(trackType, SequenceTrackType.Subtitle.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.SequenceEntered, new Dictionary<string, string>
            {
                ["speaker"] = string.Empty,
                ["bodyText"] = context.Get(SequencerServiceKeys.BodyText) ?? string.Empty,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleSequencerSignalFiredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            string eventId = context.Get(SequencerServiceKeys.EventId) ?? string.Empty;
            AppendHistory(_frontendConfig.Templates.Signal, new Dictionary<string, string>
            {
                ["signalId"] = eventId,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleSequencerCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            string sequenceId = context.Get(SequencerServiceKeys.SequenceId) ?? string.Empty;
            if (string.Equals(sequenceId, NarrativeShowcaseIds.IntroSequenceId, StringComparison.OrdinalIgnoreCase))
            {
                if (engine.GetService(CoreServiceKeys.DialogueRuntime) is DialogueRuntime dialogue)
                {
                    dialogue.StartDialogue(NarrativeShowcaseIds.BriefingDialogueId);
                }
            }
            else if (string.Equals(sequenceId, NarrativeShowcaseIds.DemoOvertureSequenceId, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(sequenceId, _frontendConfig.Bootstrap.PureIntroSequenceId, StringComparison.OrdinalIgnoreCase))
            {
                if (engine.GetService(CoreServiceKeys.DialogueRuntime) is DialogueRuntime dialogue)
                {
                    string dialogueId = string.IsNullOrWhiteSpace(_frontendConfig.Bootstrap.PureBriefingDialogueId)
                        ? NarrativeShowcaseIds.DemoAudienceDialogueId
                        : _frontendConfig.Bootstrap.PureBriefingDialogueId;
                    dialogue.StartDialogue(dialogueId);
                }
            }
            else if (string.Equals(sequenceId, NarrativeShowcaseIds.TrialRevealSequenceId, StringComparison.OrdinalIgnoreCase))
            {
                if (engine.GetService(CoreServiceKeys.TaskRuntimeService) is TaskRuntimeService tasks)
                {
                    EmitShowcaseSignal(engine, tasks, NarrativeShowcaseIds.SpawnBeastSignal);
                }
            }

            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        internal void RefreshPanel(GameEngine engine)
        {
            if (!IsShowcaseActive(engine))
            {
                ClearFrontend(engine);
                return;
            }

            if (engine.GetService(NarrativeFrontendServiceKeys.Service) is not NarrativeFrontendService frontend ||
                engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue ||
                engine.GetService(CoreServiceKeys.SequencerRuntime) is not SequencerRuntime sequencer ||
                engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks)
            {
                return;
            }

            RebindEntities(engine);
            ResolveThemeFrames(engine);
            frontend.Publish(BuildPage(engine, dialogue, sequencer, tasks));
        }

        private void ResolveThemeFrames(GameEngine engine)
        {
            string themeId = engine.MergedConfig?.PanelTheme?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(themeId))
            {
                _panelFrameSrc = string.Empty;
                _choiceFrameSrc = string.Empty;
                return;
            }

            _panelFrameSrc = ResolveThemeImage(engine, themeId, "panel_frame.png");
            _choiceFrameSrc = ResolveThemeImage(engine, themeId, "choice_frame.png");
        }

        private static string ResolveThemeImage(GameEngine engine, string themeId, string fileName)
        {
            string vfsPath = $"NarrativeShowcaseMod:assets/PanelThemes/{themeId}/images/{fileName}";
            if (engine.VFS != null &&
                engine.VFS.TryResolveFullPath(vfsPath, out string resolved) &&
                File.Exists(resolved))
            {
                return resolved;
            }

            return string.Empty;
        }

        /// <summary>
        /// panelTheme 目录下可选覆盖说话人立绘/半身像（standing_*.png / portrait_*.png），证明换皮数据驱动。
        /// </summary>
        private static string? ResolveThemeSpeakerImage(GameEngine engine, string speakerId, bool standing)
        {
            string themeId = engine.MergedConfig?.PanelTheme?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(themeId) || string.IsNullOrWhiteSpace(speakerId))
            {
                return null;
            }

            string alias = speakerId;
            const string speakerPrefix = "speaker.";
            if (alias.StartsWith(speakerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                alias = alias.Substring(speakerPrefix.Length);
            }

            if (string.IsNullOrWhiteSpace(alias))
            {
                return null;
            }

            string fileName = standing ? $"standing_{alias}.png" : $"portrait_{alias}.png";
            string resolved = ResolveThemeImage(engine, themeId, fileName);
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }

        internal void RebindEntities(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            if (TryRenameSpawnedBeast(engine))
            {
                PublishShowcaseKnowledge(engine, NarrativeShowcaseIds.MapId);
            }

            BindByName(engine, dialogue, NarrativeShowcaseIds.PlayerAlias, NarrativeShowcaseIds.PlayerName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.ElderAlias, NarrativeShowcaseIds.ElderName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.ShrineAlias, NarrativeShowcaseIds.ShrineName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.BeastAlias, NarrativeShowcaseIds.BeastName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.PlayerSpeakerId, NarrativeShowcaseIds.PlayerName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.WardenSpeakerId, NarrativeShowcaseIds.ElderName);
            BindByName(engine, dialogue, NarrativeShowcaseIds.ShrineSpeakerId, NarrativeShowcaseIds.ShrineName);
        }

        internal bool IsShowcaseActive(GameEngine engine)
        {
            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            return string.Equals(activeMapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase);
        }

        internal bool BeastSpawned(GameEngine engine)
            => engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BeastSpawnedKey, out var value) && value is bool spawned && spawned;

        internal bool BeastDefeated(GameEngine engine)
            => engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BeastDefeatedKey, out var value) && value is bool defeated && defeated;

        internal void MarkBeastDefeated(GameEngine engine)
        {
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = true;
        }

        internal void EmitShowcaseSignal(GameEngine engine, TaskRuntimeService tasks, string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
            {
                return;
            }

            tasks.EmitSignal(signalId);
            AppendHistory(_frontendConfig.Templates.Signal, new Dictionary<string, string>
            {
                ["signalId"] = signalId,
            });

            if (string.Equals(signalId, NarrativeShowcaseIds.SpawnBeastSignal, StringComparison.OrdinalIgnoreCase))
            {
                SpawnBeast(engine);
            }
            else if (string.Equals(signalId, NarrativeShowcaseIds.RewardSignal, StringComparison.OrdinalIgnoreCase))
            {
                ApplyReward(engine);
            }
        }

        private NarrativeFrontendPageState BuildPage(
            GameEngine engine,
            DialogueRuntime dialogue,
            SequencerRuntime sequencer,
            TaskRuntimeService tasks)
        {
            bool dialogueActive = dialogue.TryGetActiveView(out DialogueView dialogueView);
            bool sequenceActive = sequencer.TryGetActiveView(out SequenceView sequence);
            NarrativeShowcaseStageHudConfig hud = _frontendConfig.StageHud ?? new NarrativeShowcaseStageHudConfig();

            var surfaces = new List<NarrativeFrontendSurfaceModel>(6)
            {
                BuildPromptSurface(engine, dialogue, sequencer),
            };

            bool showObjective = (!dialogueActive || hud.ShowObjectiveWithDialogue)
                && (!sequenceActive || hud.ShowObjectiveWithSequence);
            if (showObjective)
            {
                surfaces.Add(BuildObjectiveSurface(tasks));
            }

            if (hud.ShowHistoryAlways)
            {
                surfaces.Add(BuildHistorySurface());
            }

            if (hud.ShowVariablesAlways)
            {
                surfaces.Add(BuildVariablesSurface(engine));
            }

            if (sequenceActive)
            {
                surfaces.Add(BuildSequenceSurface(engine, sequence));
            }

            if (dialogueActive)
            {
                surfaces.Add(BuildDialogueSurface(engine, dialogue, dialogueView));
                if (dialogueView.Choices.Count > 0)
                {
                    surfaces.Add(BuildChoiceSurface(dialogueView));
                }
            }

            surfaces.RemoveAll(static surface => !surface.Visible);

            return new NarrativeFrontendPageState(
                _frontendConfig.OwnerId,
                BuildSignature(engine, dialogue, sequencer, tasks, surfaces.Count),
                true,
                _frontendConfig.BackdropHex,
                surfaces);
        }

        private NarrativeFrontendSurfaceModel BuildPromptSurface(
            GameEngine engine,
            DialogueRuntime dialogue,
            SequencerRuntime sequencer)
        {
            string body = BeastDefeated(engine)
                ? _frontendConfig.Hints.ReturnPrompt
                : BeastSpawned(engine)
                    ? _frontendConfig.Hints.CombatPrompt
                    : dialogue.TryGetActiveView(out DialogueView activeDialogue) && activeDialogue.Choices.Count > 0
                        ? _frontendConfig.Hints.ChoicePrompt
                        : _frontendConfig.Hints.ExplorePrompt;
            string footer = sequencer.HasActiveSequence
                ? _frontendConfig.Hints.SkipPrompt
                : string.Empty;
            return CreateSurface(
                _frontendConfig.PromptRibbon,
                NarrativeFrontendSurfaceKind.PromptRibbon,
                _frontendConfig.Hints.PromptTitle,
                body,
                footer);
        }

        private NarrativeFrontendSurfaceModel BuildObjectiveSurface(TaskRuntimeService tasks)
        {
            IReadOnlyList<TaskView> taskViews = tasks.CaptureViews();
            TaskView? activeTask = null;
            for (int i = 0; i < taskViews.Count; i++)
            {
                if (taskViews[i].State == TaskInstanceState.Active)
                {
                    activeTask = taskViews[i];
                    break;
                }
            }

            if (activeTask == null)
            {
                return CreateSurface(
                    _frontendConfig.ObjectiveTracker,
                    NarrativeFrontendSurfaceKind.ObjectiveTracker,
                    _frontendConfig.ObjectiveTracker.Title,
                    BuildObjectiveSummary(taskViews));
            }

            TaskObjectiveProgressView objective = ResolveCurrentObjective(activeTask.Value);
            string title = ReplaceTokens(_frontendConfig.Templates.ObjectiveTitleFormat, new Dictionary<string, string>
            {
                ["task"] = activeTask.Value.DisplayName,
            });
            return CreateSurface(
                _frontendConfig.ObjectiveTracker,
                NarrativeFrontendSurfaceKind.ObjectiveTracker,
                title,
                objective.Title,
                objective.Hint);
        }

        private static TaskObjectiveProgressView ResolveCurrentObjective(TaskView task)
        {
            for (int i = 0; i < task.Objectives.Count; i++)
            {
                if (!task.Objectives[i].Completed)
                {
                    return task.Objectives[i];
                }
            }

            return task.Objectives.Count > 0 ? task.Objectives[0] : default;
        }

        private NarrativeFrontendSurfaceModel BuildHistorySurface()
        {
            var items = new List<NarrativeFrontendSurfaceItem>(_history.Count);
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: $"#{_history.Count - i:00}",
                    Value: _history[i]));
            }

            return CreateSurface(
                _frontendConfig.HistoryJournal,
                NarrativeFrontendSurfaceKind.HistoryJournal,
                _frontendConfig.HistoryJournal.Title,
                string.Empty,
                _frontendConfig.HistoryJournal.Footer,
                items);
        }

        private NarrativeFrontendSurfaceModel BuildVariablesSurface(GameEngine engine)
        {
            MapVariableStore? variables = engine.CurrentMapSession?.Variables;
            var items = new List<NarrativeFrontendSurfaceItem>(_frontendConfig.Variables.Length);
            for (int i = 0; i < _frontendConfig.Variables.Length; i++)
            {
                NarrativeShowcaseVariableConfig variable = _frontendConfig.Variables[i];
                string display = FormatVariable(variables, variable.VariableId);
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: variable.Label,
                    Value: display,
                    Caption: ReplaceTokens(_frontendConfig.Templates.VariableCaptionFormat, new Dictionary<string, string>
                    {
                        ["label"] = variable.Label,
                        ["value"] = display,
                    }),
                    AccentHex: variable.AccentHex,
                    Active: !string.IsNullOrWhiteSpace(display)));
            }

            return CreateSurface(
                _frontendConfig.VariablesPanel,
                NarrativeFrontendSurfaceKind.StatusPanel,
                _frontendConfig.VariablesPanel.Title,
                string.Empty,
                _frontendConfig.VariablesPanel.Footer,
                items);
        }

        private NarrativeFrontendSurfaceModel BuildDialogueSurface(
            GameEngine engine,
            DialogueRuntime dialogue,
            DialogueView dialogueView)
        {
            string speaker = string.IsNullOrWhiteSpace(dialogueView.ResolvedSpeakerName)
                ? dialogueView.SpeakerId
                : dialogueView.ResolvedSpeakerName;
            string profile = dialogueView.PresentationProfile ?? string.Empty;
            bool standingPortrait = string.Equals(
                profile,
                NarrativeShowcaseIds.PresentationStandingPortrait,
                StringComparison.OrdinalIgnoreCase);
            bool worldBubble = string.Equals(profile, NarrativeShowcaseIds.PresentationWorldBubble, StringComparison.OrdinalIgnoreCase);
            bool overlay = string.Equals(profile, NarrativeShowcaseIds.PresentationDialogueOverlay, StringComparison.OrdinalIgnoreCase)
                || (!worldBubble && !standingPortrait && dialogueView.Choices.Count > 0);

            NarrativeShowcaseSurfaceConfig config = standingPortrait
                ? _frontendConfig.StandingPortrait
                : overlay
                    ? _frontendConfig.OverlayDialogue
                    : _frontendConfig.DialogueBubble;
            NarrativeFrontendSurfaceKind kind = standingPortrait
                ? NarrativeFrontendSurfaceKind.StandingPortrait
                : overlay
                    ? NarrativeFrontendSurfaceKind.OverlayDialogue
                    : NarrativeFrontendSurfaceKind.DialogueBubble;
            string footer = dialogueView.AutoAdvance
                ? _frontendConfig.Hints.AutoAdvancePrompt
                : config.Footer;

            float offsetX = config.OffsetX;
            float offsetY = config.OffsetY;
            NarrativeFrontendAnchor anchor = config.ResolveAnchor();
            if (worldBubble)
            {
                if (!TryProjectSpeaker(engine, dialogue, dialogueView.SpeakerId, out float screenX, out float screenY))
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{NarrativeShowcaseIds.PresentationWorldBubble}' requires IScreenProjector and a bound speaker entity with WorldPositionCm. Speaker '{dialogueView.SpeakerId}' could not be projected.");
                }


                anchor = NarrativeFrontendAnchor.TopLeft;
                offsetX = screenX - UiMargin;
                offsetY = screenY - UiMargin - 96f;
                engine.GlobalContext["NarrativeShowcase.LastWorldBubble"] =
                    $"{kind}|{config.Width}|{anchor}|{offsetX:0.###}|{offsetY:0.###}|{config.Eyebrow}";
            }

            string portraitSrc = ResolveThemeSpeakerImage(
                engine,
                dialogueView.SpeakerId,
                standingPortrait)
                ?? (standingPortrait ? dialogueView.StandingImageSrc : dialogueView.PortraitImageSrc);
            if (standingPortrait && string.IsNullOrWhiteSpace(portraitSrc))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{NarrativeShowcaseIds.PresentationStandingPortrait}' requires speaker '{dialogueView.SpeakerId}' to declare standingImageId with a resolvable image asset.");
            }


            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{_frontendConfig.OwnerId}.{kind}.{anchor}",
                Kind: kind,
                Anchor: anchor,
                Title: speaker,
                Subtitle: config.Eyebrow,
                Body: dialogueView.ResolvedText,
                Footer: string.IsNullOrWhiteSpace(footer) ? config.Footer : footer,
                Width: config.Width,
                OffsetX: offsetX,
                OffsetY: offsetY,
                ZIndex: config.ZIndex,
                WaitForInput: dialogueView.WaitForInput,
                Skippable: false,
                Progress01: dialogueView.Progress01,
                CountdownSeconds: dialogueView.AutoAdvanceSeconds > 0f
                    ? Math.Max(0f, dialogueView.AutoAdvanceSeconds - dialogueView.ElapsedSeconds)
                    : 0f,
                AccentHex: config.AccentHex,
                BackgroundHex: config.BackgroundHex,
                BorderHex: config.BorderHex,
                ForegroundHex: config.ForegroundHex,
                MutedHex: config.MutedHex,
                PortraitSrc: portraitSrc,
                PortraitSize: standingPortrait ? 980f : overlay ? 112f : 84f,
                FrameImageSrc: _panelFrameSrc);
        }

        private NarrativeFrontendSurfaceModel BuildChoiceSurface(DialogueView dialogue)
        {
            var items = new List<NarrativeFrontendSurfaceItem>(dialogue.Choices.Count);
            for (int i = 0; i < dialogue.Choices.Count; i++)
            {
                DialogueChoiceView choice = dialogue.Choices[i];
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: choice.ResolvedText,
                    Caption: choice.ChoiceId,
                    Active: i == 0,
                    Shortcut: (i + 1).ToString()));
            }

            return CreateSurface(
                _frontendConfig.ChoiceList,
                NarrativeFrontendSurfaceKind.ChoiceList,
                _frontendConfig.ChoiceList.Title,
                string.Empty,
                _frontendConfig.ChoiceList.Footer,
                items);
        }

        private NarrativeFrontendSurfaceModel BuildSequenceSurface(GameEngine engine, SequenceView sequence)
        {
            SequenceSubtitleView? subtitle = sequence.ActiveSubtitles.Count > 0
                ? sequence.ActiveSubtitles[0]
                : null;
            bool transmission = ContainsId(_frontendConfig.Routing.TransmissionSequenceIds, sequence.SequenceId);
            NarrativeShowcaseSurfaceConfig config = transmission
                ? _frontendConfig.TransmissionOverlay
                : _frontendConfig.SubtitleBubble;
            NarrativeFrontendSurfaceKind kind = transmission
                ? NarrativeFrontendSurfaceKind.TransmissionOverlay
                : NarrativeFrontendSurfaceKind.SubtitleBubble;

            string title = subtitle != null
                ? ResolveSpeakerDisplay(engine, subtitle.SpeakerId)
                : sequence.DisplayName;
            string body = subtitle?.ResolvedText ?? string.Empty;
            float progress01 = subtitle != null && subtitle.Duration > 0f
                ? Math.Clamp(subtitle.LocalElapsed / subtitle.Duration, 0f, 1f)
                : 0f;
            float countdown = subtitle != null
                ? Math.Max(0f, subtitle.Duration - subtitle.LocalElapsed)
                : 0f;

            return CreateSurface(
                config,
                kind,
                title,
                body,
                _frontendConfig.Hints.SkipPrompt,
                null,
                false,
                true,
                progress01,
                countdown);
        }

        private string ResolveSpeakerDisplay(GameEngine engine, string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId))
            {
                return string.Empty;
            }

            if (engine.GetService(CoreServiceKeys.PresentationDisplayResolver) is PresentationDisplayResolver display &&
                engine.GetService(CoreServiceKeys.StoryDefinitions) is StoryDefinitionRegistry story &&
                story.TryGetSpeaker(speakerId, out StorySpeakerDefinition speaker))
            {
                return display.FormatTokenOrThrow(speaker.DisplayNameToken);
            }

            return _frontendConfig.ResolveSpeakerLabel(speakerId);
        }

        private NarrativeFrontendSurfaceModel CreateSurface(
            NarrativeShowcaseSurfaceConfig config,
            NarrativeFrontendSurfaceKind kind,
            string title,
            string body,
            string footer = "",
            IReadOnlyList<NarrativeFrontendSurfaceItem>? items = null,
            bool waitForInput = false,
            bool skippable = false,
            float progress01 = -1f,
            float countdownSeconds = 0f)
        {
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{_frontendConfig.OwnerId}.{kind}.{config.ResolveAnchor()}",
                Kind: kind,
                Anchor: config.ResolveAnchor(),
                Title: title,
                Subtitle: config.Eyebrow,
                Body: body,
                Footer: string.IsNullOrWhiteSpace(footer) ? config.Footer : footer,
                Items: items,
                Width: config.Width,
                OffsetX: config.OffsetX,
                OffsetY: config.OffsetY,
                ZIndex: config.ZIndex,
                WaitForInput: waitForInput,
                Skippable: skippable,
                Progress01: progress01,
                CountdownSeconds: countdownSeconds,
                AccentHex: config.AccentHex,
                BackgroundHex: config.BackgroundHex,
                BorderHex: config.BorderHex,
                ForegroundHex: config.ForegroundHex,
                MutedHex: config.MutedHex,
                FrameImageSrc: kind == NarrativeFrontendSurfaceKind.ChoiceList
                    ? _choiceFrameSrc
                    : _panelFrameSrc);
        }

        private void EnsureBootstrapped(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BootstrappedKey, out var bootObj) && bootObj is bool booted && booted)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue ||
                engine.GetService(CoreServiceKeys.SequencerRuntime) is not SequencerRuntime sequencer ||
                engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks)
            {
                return;
            }

            EnsureTaskHook(engine);
            ResetHistory();
            dialogue.ResetState();
            sequencer.ResetState();
            tasks.ResetState();
            RebindEntities(engine);

            if (ShouldUsePureStoryLane(engine))
            {
                string sequenceId = string.IsNullOrWhiteSpace(_frontendConfig.Bootstrap.PureIntroSequenceId)
                    ? NarrativeShowcaseIds.DemoOvertureSequenceId
                    : _frontendConfig.Bootstrap.PureIntroSequenceId;
                sequencer.Start(sequenceId);
            }
            else
            {
                tasks.OfferOrStart(NarrativeShowcaseIds.BriefingTaskId);
                sequencer.Start(NarrativeShowcaseIds.IntroSequenceId);
            }

            engine.GlobalContext[NarrativeShowcaseIds.BootstrappedKey] = true;
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = false;
        }

        private bool ShouldUsePureStoryLane(GameEngine engine)
        {
            if (_frontendConfig.Bootstrap.PureStoryLane)
            {
                return true;
            }

            // 主题壳只改 panelTheme：非默认余烬皮时走纯过场+对话，用来证明数据驱动换皮。
            string themeId = engine.MergedConfig?.PanelTheme?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(themeId)
                && !string.Equals(themeId, "story-ember", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureTaskHook(GameEngine engine)
        {
            if (_taskHookInstalled ||
                engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks)
            {
                return;
            }

            tasks.TaskStateChanged += change => HandleTaskStateChanged(engine, tasks, change);
            _taskHookInstalled = true;
        }

        private void HandleTaskStateChanged(GameEngine engine, TaskRuntimeService tasks, TaskStateChangedInfo change)
        {
            if (!IsShowcaseActive(engine))
            {
                return;
            }

            if (change.State == TaskInstanceState.Active)
            {
                string objectiveText = string.Empty;
                if (tasks.TryGetDefinition(change.TaskId, out TaskDefinition definition) &&
                    definition.Objectives.Count > 0)
                {
                    objectiveText = definition.Objectives[0].Title;
                }

                AppendHistory(_frontendConfig.Templates.TaskActivated, new Dictionary<string, string>
                {
                    ["bodyText"] = objectiveText,
                });
            }
            else if (change.State == TaskInstanceState.Completed)
            {
                AppendHistory(_frontendConfig.Templates.TaskCompleted, new Dictionary<string, string>
                {
                    ["taskId"] = change.TaskId,
                });
            }

            RefreshPanel(engine);
        }

        private void SpawnBeast(GameEngine engine)
        {
            if (BeastSpawned(engine) || engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue queue)
            {
                return;
            }

            queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "interaction_enemy_bruiser",
                MapId = new Ludots.Core.Map.MapId(NarrativeShowcaseIds.MapId),
                HasWorldPosition = 1,
                WorldPositionCm = Fix64Vec2.FromInt(1960, 940),
                HasFacing = 1,
                FacingAngleRad = 3.14159f
            });
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = true;
            AppendHistory(_frontendConfig.Templates.BeastSpawned, null);
        }

        private void ApplyReward(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.RewardAppliedKey, out var rewardObj) && rewardObj is bool rewardApplied && rewardApplied)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue queue ||
                !TryFindEntityByName(engine.World, NarrativeShowcaseIds.PlayerName, out Entity player))
            {
                return;
            }

            int healEffectId = EffectTemplateIdRegistry.GetId("Effect.Narrative.BlessingHeal");
            int speedEffectId = EffectTemplateIdRegistry.GetId("Effect.Narrative.BlessingSpeed");
            if (healEffectId > 0)
            {
                queue.Publish(new EffectRequest { Source = player, Target = player, TemplateId = healEffectId });
            }

            if (speedEffectId > 0)
            {
                queue.Publish(new EffectRequest { Source = player, Target = player, TemplateId = speedEffectId });
            }

            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = true;
            AppendHistory(_frontendConfig.Templates.RewardApplied, null);
        }

        private void ActivateInputContexts(Ludots.Core.Input.Runtime.PlayerInputHandler input)
        {
            if (input == null)
            {
                return;
            }

            if (!_narrativeInputActive && input.HasContext(NarrativeShowcaseInputContexts.Showcase))
            {
                input.PushContext(NarrativeShowcaseInputContexts.Showcase);
                _narrativeInputActive = true;
            }

            if (!_interactionInputActive && input.HasContext(InteractionShowcaseIds.InputContextId))
            {
                input.PushContext(InteractionShowcaseIds.InputContextId);
                _interactionInputActive = true;
            }
        }

        private void DeactivateInputContexts(Ludots.Core.Input.Runtime.PlayerInputHandler input)
        {
            if (input == null)
            {
                return;
            }

            if (_interactionInputActive)
            {
                input.PopContext(InteractionShowcaseIds.InputContextId);
                _interactionInputActive = false;
            }

            if (_narrativeInputActive)
            {
                input.PopContext(NarrativeShowcaseInputContexts.Showcase);
                _narrativeInputActive = false;
            }
        }

        private void EnsureViewMode(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (!InteractionShowcaseIds.IsShowcaseMode(manager.ActiveMode?.Id))
            {
                manager.SwitchTo(InteractionShowcaseIds.LolModeId);
            }
        }

        private static Entity RequireShowcaseSolePossessedRep(GameEngine engine, string activeMapId)
        {
            Entity possessed = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.IsAlive(possessed) ||
                !engine.World.TryGet(possessed, out PlayerOwner owner) ||
                !engine.World.TryGet(possessed, out MapEntity mapEntity) ||
                owner.PlayerId != ShowcaseLocalPlayerId ||
                !string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Narrative showcase requires sole ClientLocalSeat possession of map playerId 1 from launchContext.localSeats / startupLocalSeats.");
            }

            return possessed;
        }

        private static void PublishShowcaseKnowledge(GameEngine engine, string activeMapId)
        {
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var viewer) ||
                !engine.World.IsAlive(viewer))
            {
                return;
            }

            KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("KnowledgeProjectionStore missing.");
            var empty = KnowledgeIdMask256.Empty;
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            engine.World.Query(in SelectableKnowledgeQuery, (Entity entity, ref CommandSourceSelectableTag _, ref MapEntity mapEntity) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                knowledge.Upsert(
                    viewer,
                    entity,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        empty,
                        empty,
                        empty,
                        viewer,
                        observedTick,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            });
        }

        private void ClearFrontend(GameEngine engine)
        {
            if (engine.GetService(NarrativeFrontendServiceKeys.Service) is NarrativeFrontendService frontend)
            {
                frontend.Clear(_frontendConfig.OwnerId);
            }
        }

        private void BindByName(GameEngine engine, DialogueRuntime dialogue, string alias, string name)
        {
            if (TryFindEntityByName(engine.World, name, out Entity entity))
            {
                dialogue.BindEntity(alias, entity);
            }
        }

        private bool TryRenameSpawnedBeast(GameEngine engine)
        {
            if (TryFindEntityByName(engine.World, NarrativeShowcaseIds.BeastName, out _))
            {
                return false;
            }

            if (TryFindEntityByName(engine.World, NarrativeShowcaseIds.SpawnedBeastTemplateName, out Entity entity) && engine.World.TryGet(entity, out Name name))
            {
                name.Value = NarrativeShowcaseIds.BeastName;
                engine.World.Set(entity, name);
                return true;
            }

            return false;
        }

        private void ResetHistory()
        {
            _history.Clear();
            _historySerial = 0;
        }

        private void AppendHistory(string template, IReadOnlyDictionary<string, string>? values)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return;
            }

            _historySerial++;
            _history.Add($"[{_historySerial:00}] {ReplaceTokens(template, values)}");
            if (_history.Count > 14)
            {
                _history.RemoveAt(0);
            }
        }

        private string BuildSignature(
            GameEngine engine,
            DialogueRuntime dialogue,
            SequencerRuntime sequencer,
            TaskRuntimeService tasks,
            int surfaceCount)
        {
            string dialogueSig = dialogue.TryGetActiveView(out DialogueView dialogueView)
                ? $"{dialogueView.DialogueId}|{dialogueView.NodeId}|{dialogueView.Choices.Count}|{dialogueView.Progress01:0.00}|{dialogueView.PresentationProfile}|{dialogueView.StandingImageSrc}|{dialogueView.PortraitImageSrc}"
                : string.Empty;
            string sequenceSig = sequencer.TryGetActiveView(out SequenceView sequence)
                ? $"{sequence.SequenceId}|{sequence.Time:0.00}|{sequence.ActiveSubtitles.Count}|{sequence.Paused}"
                : string.Empty;
            return string.Join("||",
                BuildTaskSummary(tasks),
                BuildVariableSummary(engine),
                dialogueSig,
                sequenceSig,
                surfaceCount,
                _historySerial,
                BeastSpawned(engine),
                BeastDefeated(engine));
        }

        private string FormatVariable(MapVariableStore? variables, string variableId)
        {
            if (variables == null || !variables.Contains(variableId))
            {
                return string.Empty;
            }

            int value = variables.ReadInt(variableId);
            if (string.Equals(variableId, NarrativeShowcaseIds.EndingVariableId, StringComparison.OrdinalIgnoreCase))
            {
                return _frontendConfig.ResolveEndingLabel(value);
            }

            return value.ToString();
        }

        private static string BuildTaskSummary(TaskRuntimeService tasks)
        {
            IReadOnlyList<TaskView> views = tasks.CaptureViews();
            var parts = new List<string>(views.Count);
            for (int i = 0; i < views.Count; i++)
            {
                parts.Add($"{views[i].TaskId}:{views[i].State}");
            }

            return string.Join(",", parts);
        }

        private static string BuildObjectiveSummary(IReadOnlyList<TaskView> views)
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].State == TaskInstanceState.Active && views[i].Objectives.Count > 0)
                {
                    return views[i].Objectives[0].Title;
                }
            }

            return "No active objective.";
        }

        private string BuildVariableSummary(GameEngine engine)
        {
            MapVariableStore? variables = engine.CurrentMapSession?.Variables;
            if (variables == null)
            {
                return string.Empty;
            }

            return string.Join(",",
                $"{NarrativeShowcaseIds.TrustVariableId}={SafeRead(variables, NarrativeShowcaseIds.TrustVariableId)}",
                $"{NarrativeShowcaseIds.LoreVariableId}={SafeRead(variables, NarrativeShowcaseIds.LoreVariableId)}",
                $"{NarrativeShowcaseIds.EndingVariableId}={SafeRead(variables, NarrativeShowcaseIds.EndingVariableId)}",
                $"{NarrativeShowcaseIds.TrialPhaseVariableId}={SafeRead(variables, NarrativeShowcaseIds.TrialPhaseVariableId)}");
        }

        private static int SafeRead(MapVariableStore variables, string name)
            => variables.Contains(name) ? variables.ReadInt(name) : 0;

        private bool TryProjectSpeaker(
            GameEngine engine,
            DialogueRuntime dialogue,
            string speakerId,
            out float screenX,
            out float screenY)
        {
            screenX = 0f;
            screenY = 0f;
            if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return false;
            }

            if (!dialogue.TryResolveEntity(speakerId, out Entity speaker) ||
                !engine.World.TryGet(speaker, out WorldPositionCm worldPos))
            {
                return false;
            }

            float headOffsetYCm = 140f;
            if (engine.GetService(CoreServiceKeys.StoryDefinitions) is StoryDefinitionRegistry story &&
                story.TryGetProfile(NarrativeShowcaseIds.PresentationWorldBubble, out StoryPresentationProfileDefinition profile))
            {
                headOffsetYCm = profile.WorldHeadOffsetYCm;
            }

            Vector2 world = worldPos.Value.ToVector2();
            Vector2 screen = projector.WorldToScreen(new Vector3(
                world.X / 100f,
                headOffsetYCm / 100f,
                world.Y / 100f));
            if (float.IsNaN(screen.X) || float.IsNaN(screen.Y))
            {
                return false;
            }

            screenX = screen.X;
            screenY = screen.Y;
            return true;
        }

        private static bool ContainsId(IEnumerable<string> ids, string value)
        {
            foreach (string id in ids)
            {
                if (string.Equals(id, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceTokens(string template, IReadOnlyDictionary<string, string>? values)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            string result = template;
            if (values != null)
            {
                foreach (var pair in values)
                {
                    result = result.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            }

            return result;
        }

        private static bool TryFindEntityByName(World world, string name, out Entity result)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found == Entity.Null && string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = entity;
                }
            });

            result = found;
            return found != Entity.Null;
        }
    }
}
