using System;
using System.Collections.Generic;
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
        private const float NameplateLiftPx = 52f;
        private const string ThemeAssetRoot = "NarrativeShowcaseMod:assets/PanelThemes";
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();

        private readonly IModContext _context;
        private readonly NarrativeShowcaseFrontendConfig _frontendConfig;
        private readonly List<string> _history = new();
        private bool _narrativeInputActive;
        private bool _interactionInputActive;
        private bool _taskHookInstalled;
        private readonly NarrativeShowcaseWorldEffects _worldEffects;
        private int _historySerial;

        internal NarrativeShowcaseRuntime(IModContext context)
        {
            _context = context;
            using var stream = context.GetResource($"{context.ModId}:assets/Frontend/narrative_frontend.json");
            _frontendConfig = NarrativeShowcaseFrontendConfig.Load(stream);
            _worldEffects = new NarrativeShowcaseWorldEffects(_frontendConfig, (eng, text) => AppendHistory(text));
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
                EnsurePlayerLocale(engine);
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

            AppendHistory(
                $"{ResolveSpeakerDisplay(engine, context.Get(DialogueServiceKeys.SpeakerId) ?? string.Empty)}{Tr(engine, "story.ui.punct.colon")}{context.Get(DialogueServiceKeys.BodyText) ?? string.Empty}");
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleDialogueChoiceCommittedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory($"{Tr(engine, _frontendConfig.Templates.DialogueChoiceCommittedPrefix)}{context.Get(DialogueServiceKeys.BodyText) ?? string.Empty}");

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

            AppendHistory($"{context.Get(SequencerServiceKeys.BodyText) ?? string.Empty}");
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
            AppendHistory(eventId);
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
            NarrativeDialogueChoicePanels.SyncVisibility(engine, dialogue);
            frontend.Publish(BuildPage(engine, dialogue, sequencer, tasks));
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

        internal float WardenInteractRangeCm => _frontendConfig.Interact.WardenRangeCm;

        internal float ShrineInteractRangeCm => _frontendConfig.Interact.ShrineRangeCm;

        internal bool IsNearNamed(GameEngine engine, string name, float rangeCm)
        {
            if (!TryFindEntityByName(engine.World, NarrativeShowcaseIds.PlayerName, out Entity player) ||
                !engine.World.TryGet(player, out WorldPositionCm playerPos) ||
                !TryFindEntityByName(engine.World, name, out Entity target) ||
                !engine.World.TryGet(target, out WorldPositionCm targetPos))
            {
                return false;
            }

            return IsNear(playerPos, targetPos, rangeCm);
        }

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
            AppendHistory(signalId);

            if (string.Equals(signalId, NarrativeShowcaseIds.SpawnBeastSignal, StringComparison.OrdinalIgnoreCase))
            {
                _worldEffects.SpawnBeast(engine);
            }
            else if (string.Equals(signalId, NarrativeShowcaseIds.RewardSignal, StringComparison.OrdinalIgnoreCase))
            {
                _worldEffects.ApplyReward(engine);
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
            bool standingPortrait = dialogueActive &&
                string.Equals(
                    dialogueView.PresentationProfile,
                    NarrativeShowcaseIds.PresentationStandingPortrait,
                    StringComparison.OrdinalIgnoreCase);
            bool showPanels = !standingPortrait || !hud.HidePanelsDuringStandingPortrait;

            var surfaces = new List<NarrativeFrontendSurfaceModel>(6);
            if ((!dialogueActive || hud.ShowPromptWithDialogue) &&
                (!sequenceActive || hud.ShowPromptWithSequence))
            {
                surfaces.Add(BuildPromptSurface(engine, dialogue, sequencer));
            }

            bool showObjective = showPanels
                && (!dialogueActive || hud.ShowObjectiveWithDialogue)
                && (!sequenceActive || hud.ShowObjectiveWithSequence);
            if (showObjective)
            {
                surfaces.Add(BuildObjectiveSurface(engine, tasks));
            }

            if (showPanels && hud.ShowHistoryAlways)
            {
                surfaces.Add(BuildHistorySurface(engine));
            }

            if (showPanels &&
                (hud.ShowVariablesAlways || (hud.ShowVariablesWhenNonZero && HasNonZeroStoryVariable(engine))))
            {
                surfaces.Add(BuildVariablesSurface(engine));
            }

            if (showPanels && _history.Count > 0)
            {
                surfaces.Add(BuildNotificationSurface(engine));
            }

            if (!hud.HideCastDuringStandingPortrait || !standingPortrait)
            {
                AddCastNameplates(engine, surfaces);
            }

            if (sequenceActive)
            {
                StoryPresentationProjector projector = RequireProjector(engine);
                StoryPresentationFrame frame = projector.ProjectSequence(sequence);
                AppendStoryFrame(engine, surfaces, frame);
            }

            if (dialogueActive)
            {
                StoryPresentationProjector projector = RequireProjector(engine);
                float? worldX = null;
                float? worldY = null;
                if (engine.GetService(CoreServiceKeys.StoryDefinitions) is StoryDefinitionRegistry story &&
                    story.TryGetProfile(dialogueView.PresentationProfile, out StoryPresentationProfileDefinition profile) &&
                    profile.Backend == StoryPresentationBackend.WorldProjected)
                {
                    if (!TryProjectSpeaker(engine, dialogue, dialogueView.SpeakerId, out float screenX, out float screenY))
                    {
                        throw new InvalidOperationException(
                            $"Presentation profile '{dialogueView.PresentationProfile}' requires IScreenProjector and a bound speaker entity with WorldPositionCm. Speaker '{dialogueView.SpeakerId}' could not be projected.");
                    }

                    worldX = screenX;
                    worldY = screenY;
                }

                StoryPresentationFrame frame = projector.ProjectDialogue(dialogueView, worldX, worldY);
                AppendStoryFrame(engine, surfaces, frame);
            }

            surfaces.RemoveAll(static surface => !surface.Visible);

            return new NarrativeFrontendPageState(
                _frontendConfig.OwnerId,
                BuildSignature(engine, dialogue, sequencer, tasks, surfaces),
                true,
                _frontendConfig.BackdropHex,
                surfaces);
        }

        private static StoryPresentationProjector RequireProjector(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.StoryPresentationProjector)
                ?? throw new InvalidOperationException(
                    "Narrative showcase requires StoryPresentationProjector engine service.");
        }

        private void AppendStoryFrame(
            GameEngine engine,
            List<NarrativeFrontendSurfaceModel> surfaces,
            StoryPresentationFrame frame)
        {
            PresentationDisplayResolver? display = engine.GetService(CoreServiceKeys.PresentationDisplayResolver);
            NarrativeFrontendPageState page = StoryPresentationFrontendAdapter.ToPage(
                _frontendConfig.OwnerId,
                frame,
                display,
                frameImageSrc: string.Empty);
            if (page.Surfaces == null)
            {
                return;
            }

            for (int i = 0; i < page.Surfaces.Count; i++)
            {
                surfaces.Add(ApplyFrontendChrome(engine, page.Surfaces[i]));
            }
        }

        /// <summary>
        /// Content comes from the Core string bag; layout / eyebrow / footer come from frontend config.
        /// World-projected bubbles keep projected TopLeft offsets and only take width/chrome from config.
        /// </summary>
        private NarrativeFrontendSurfaceModel ApplyFrontendChrome(
            GameEngine engine,
            NarrativeFrontendSurfaceModel surface)
        {
            // Geometry and colors are profile-owned (single writer, Core projector).
            // Chrome contributes skin text only: eyebrow / footer.
            NarrativeShowcaseSurfaceConfig? config = ResolveChromeConfig(surface.Kind);
            if (config == null)
            {
                return surface with
                {
                    FrameImageSrc = ResolveFrameImageSrc(engine, surface.Kind)
                };
            }

            string title = surface.Title;

            string footer;
            if (surface.Kind is NarrativeFrontendSurfaceKind.SubtitleBubble
                or NarrativeFrontendSurfaceKind.TransmissionOverlay)
            {
                footer = Tr(engine, _frontendConfig.Hints.SkipPrompt);
            }
            else if (surface.CountdownSeconds > 0f &&
                     !string.IsNullOrWhiteSpace(_frontendConfig.Hints.AutoAdvancePrompt))
            {
                footer = Tr(engine, _frontendConfig.Hints.AutoAdvancePrompt);
            }
            else if (!string.IsNullOrWhiteSpace(surface.Footer))
            {
                footer = surface.Footer;
            }
            else
            {
                footer = Tr(engine, config.Footer);
            }

            return surface with
            {
                Title = title,
                Subtitle = string.IsNullOrWhiteSpace(surface.Subtitle) ? Tr(engine, config.Eyebrow) : surface.Subtitle,
                Footer = footer,
                FrameImageSrc = ResolveFrameImageSrc(engine, surface.Kind)
            };
        }

        /// <summary>
        /// Ornate nine-slice frames belong on dialogue surfaces only.
        /// PromptRibbon and chrome panels already have theme.css skins; wrapping them
        /// in panel_frame stacks a second dialog-looking bar behind OverlayDialogue.
        /// </summary>
        private static string ResolveFrameImageSrc(
            GameEngine engine,
            NarrativeFrontendSurfaceKind kind)
        {
            return NarrativeFrontendThemeResolver.ResolveFrameImageSource(
                engine,
                ThemeAssetRoot,
                kind);
        }

        private NarrativeShowcaseSurfaceConfig? ResolveChromeConfig(NarrativeFrontendSurfaceKind kind)
        {
            return kind switch
            {
                NarrativeFrontendSurfaceKind.OverlayDialogue => _frontendConfig.OverlayDialogue,
                NarrativeFrontendSurfaceKind.DialogueBubble => _frontendConfig.DialogueBubble,
                NarrativeFrontendSurfaceKind.StandingPortrait => _frontendConfig.StandingPortrait,
                NarrativeFrontendSurfaceKind.SubtitleBubble => _frontendConfig.SubtitleBubble,
                NarrativeFrontendSurfaceKind.TransmissionOverlay => _frontendConfig.TransmissionOverlay,
                _ => null
            };
        }

        /// <summary>Frontend config text fields are TextToken ids — resolve through the catalog at use sites.</summary>
        private string Tr(GameEngine engine, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            return StoryTextResolution.FormatToken(
                engine.GetService(CoreServiceKeys.PresentationTextCatalog),
                engine.GetService(CoreServiceKeys.PresentationDisplayResolver),
                token);
        }

        private static string FirstNonEmpty(string primary, string fallback) =>
            string.IsNullOrWhiteSpace(primary) ? fallback ?? string.Empty : primary;

        private NarrativeFrontendSurfaceModel BuildPromptSurface(
            GameEngine engine,
            DialogueRuntime dialogue,
            SequencerRuntime sequencer)
        {
            string body = ResolvePromptBody(engine, dialogue, sequencer);
            string footer = sequencer.HasActiveSequence
                ? Tr(engine, _frontendConfig.Hints.SkipPrompt)
                : string.Empty;
            return CreateSurface(
                _frontendConfig.PromptRibbon,
                NarrativeFrontendSurfaceKind.PromptRibbon,
                Tr(engine, _frontendConfig.Hints.PromptTitle),
                body,
                footer);
        }

        private NarrativeFrontendSurfaceModel BuildObjectiveSurface(GameEngine engine, TaskRuntimeService tasks)
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
                    Tr(engine, _frontendConfig.ObjectiveTracker.Title),
                    BuildObjectiveSummary(engine, taskViews));
            }

            TaskObjectiveProgressView objective = ResolveCurrentObjective(activeTask.Value);
            string title = activeTask.Value.DisplayName;
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

        private NarrativeFrontendSurfaceModel BuildHistorySurface(GameEngine engine)
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
                Tr(engine, _frontendConfig.HistoryJournal.Title),
                string.Empty,
                Tr(engine, _frontendConfig.HistoryJournal.Footer),
                items);
        }

        private NarrativeFrontendSurfaceModel BuildVariablesSurface(GameEngine engine)
        {
            MapVariableStore? variables = engine.CurrentMapSession?.Variables;
            var items = new List<NarrativeFrontendSurfaceItem>(_frontendConfig.Variables.Length);
            for (int i = 0; i < _frontendConfig.Variables.Length; i++)
            {
                NarrativeShowcaseVariableConfig variable = _frontendConfig.Variables[i];
                string display = FormatVariable(engine, variables, variable.VariableId);
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: Tr(engine, variable.Label),
                    Value: display,
                    Caption: $"{Tr(engine, variable.Label)}{Tr(engine, "story.ui.punct.colon")}{display}",
                    AccentHex: variable.AccentHex,
                    Active: !string.IsNullOrWhiteSpace(display)));
            }

            return CreateSurface(
                _frontendConfig.VariablesPanel,
                NarrativeFrontendSurfaceKind.StatusPanel,
                Tr(engine, _frontendConfig.VariablesPanel.Title),
                string.Empty,
                Tr(engine, _frontendConfig.VariablesPanel.Footer),
                items);
        }

        private string ResolveSpeakerDisplay(GameEngine engine, string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId))
            {
                return string.Empty;
            }

            var story = engine.GetService(CoreServiceKeys.StoryDefinitions) as StoryDefinitionRegistry
                ?? throw new InvalidOperationException("Narrative showcase requires StoryDefinitionRegistry for speaker names.");
            return StoryTextResolution.ResolveSpeakerDisplayName(
                story,
                engine.GetService(CoreServiceKeys.PresentationTextCatalog),
                engine.GetService(CoreServiceKeys.PresentationDisplayResolver),
                speakerId);
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
            if (string.IsNullOrWhiteSpace(config.LayoutId))
            {
                throw new InvalidOperationException(
                    $"Narrative frontend surface '{kind}' requires layoutId.");
            }

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
                LayoutId: config.LayoutId,
                StyleClass: config.StyleClass);
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
            // 纯车道只认 bootstrap 开关；主题壳换皮仍走同一对话 showcase，框体/立绘随 panelTheme 变。
            return _frontendConfig.Bootstrap.PureStoryLane;
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

                AppendHistory($"{Tr(engine, _frontendConfig.Templates.TaskActivatedPrefix)}{objectiveText}");
            }
            else if (change.State == TaskInstanceState.Completed)
            {
                AppendHistory(Tr(engine, _frontendConfig.Templates.TaskCompleted));
            }

            RefreshPanel(engine);
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

            NarrativeDialogueChoicePanels.Hide(engine);
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

            if (TryFindEntityByName(engine.World, NarrativeShowcaseIds.SpawnedBeastEntityName, out Entity entity) && engine.World.TryGet(entity, out Name name))
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

        private void AppendHistory(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _historySerial++;
            _history.Add($"[{_historySerial:00}] {text}");
            if (_history.Count > _frontendConfig.Bootstrap.HistoryCapacity)
            {
                _history.RemoveAt(0);
            }
        }

        private string BuildSignature(
            GameEngine engine,
            DialogueRuntime dialogue,
            SequencerRuntime sequencer,
            TaskRuntimeService tasks,
            IReadOnlyList<NarrativeFrontendSurfaceModel> surfaces)
        {
            string dialogueSig = dialogue.TryGetActiveView(out DialogueView dialogueView)
                ? $"{dialogueView.DialogueId}|{dialogueView.NodeId}|{dialogueView.Choices.Count}|{dialogueView.Progress01:0.00}|{dialogueView.PresentationProfile}|{dialogueView.StandingImageId}|{dialogueView.PortraitImageId}"
                : string.Empty;
            string sequenceSig = sequencer.TryGetActiveView(out SequenceView sequence)
                ? $"{sequence.SequenceId}|{sequence.Time:0.00}|{sequence.ActiveSubtitles.Count}|{sequence.Paused}"
                : string.Empty;
            return string.Join("||",
                BuildTaskSummary(tasks),
                BuildVariableSummary(engine),
                dialogueSig,
                sequenceSig,
                BuildSurfaceLayoutSignature(surfaces),
                _historySerial,
                BeastSpawned(engine),
                BeastDefeated(engine),
                BuildCastSignature(engine));
        }

        private static string BuildSurfaceLayoutSignature(
            IReadOnlyList<NarrativeFrontendSurfaceModel> surfaces)
        {
            var parts = new string[surfaces.Count];
            for (int i = 0; i < surfaces.Count; i++)
            {
                NarrativeFrontendSurfaceModel surface = surfaces[i];
                parts[i] =
                    $"{surface.SurfaceId}:{surface.Anchor}:{surface.Width:0.###}:{surface.OffsetX:0.###}:{surface.OffsetY:0.###}:{surface.ZIndex}";
            }

            return string.Join(",", parts);
        }

        private string FormatVariable(GameEngine engine, MapVariableStore? variables, string variableId)
        {
            if (variables == null || !variables.Contains(variableId))
            {
                return string.Empty;
            }

            int value = variables.ReadInt(variableId);
            if (string.Equals(variableId, NarrativeShowcaseIds.EndingVariableId, StringComparison.OrdinalIgnoreCase))
            {
                return Tr(engine, _frontendConfig.ResolveEndingLabel(value));
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

        private string BuildObjectiveSummary(GameEngine engine, IReadOnlyList<TaskView> views)
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].State == TaskInstanceState.Active && views[i].Objectives.Count > 0)
                {
                    return views[i].Objectives[0].Title;
                }
            }

            return Tr(engine, "story.ui.objective.empty");
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

            float headOffsetYCm = NarrativeShowcaseCastDefaults.HeadOffsetYCm;
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

        private void EnsurePlayerLocale(GameEngine engine)
        {
            if (string.IsNullOrWhiteSpace(_frontendConfig.PlayerLocale))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection) is PresentationTextLocaleSelection locale
                && !string.Equals(locale.ActiveLocaleKey, _frontendConfig.PlayerLocale, StringComparison.OrdinalIgnoreCase))
            {
                locale.SetActiveLocale(_frontendConfig.PlayerLocale);
            }
        }

        private string ResolvePromptBody(GameEngine engine, DialogueRuntime dialogue, SequencerRuntime sequencer)
        {
            NarrativeShowcaseHintConfig hints = _frontendConfig.Hints;
            if (sequencer.HasActiveSequence)
            {
                if (sequencer.TryGetActiveView(out SequenceView sequence) &&
                    string.Equals(sequence.SequenceId, NarrativeShowcaseIds.TrialRevealSequenceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Tr(engine, FirstNonEmpty(hints.SkipPrompt, hints.IntroPrompt));
                }

                return Tr(engine, FirstNonEmpty(hints.IntroPrompt, hints.SkipPrompt));
            }

            if (dialogue.TryGetActiveView(out DialogueView activeDialogue))
            {
                return activeDialogue.Choices.Count > 0
                    ? FirstNonEmpty(hints.ChoicePrompt, hints.ContinuePrompt)
                    : FirstNonEmpty(hints.ContinuePrompt, hints.ChoicePrompt);
            }

            if (BeastSpawned(engine) && !BeastDefeated(engine))
            {
                return Tr(engine, hints.CombatPrompt);
            }

            bool nearWarden = IsNearNamed(engine, NarrativeShowcaseIds.ElderName, WardenInteractRangeCm);
            bool nearShrine = IsNearNamed(engine, NarrativeShowcaseIds.ShrineName, ShrineInteractRangeCm);

            if (BeastDefeated(engine))
            {
                return nearWarden
                    ? Tr(engine, FirstNonEmpty(hints.ReturnNearPrompt, hints.ReturnPrompt))
                    : Tr(engine, hints.ReturnPrompt);
            }

            if (engine.GetService(CoreServiceKeys.TaskRuntimeService) is TaskRuntimeService tasks)
            {
                if (tasks.TryGetState(NarrativeShowcaseIds.TrialTaskId, out TaskInstanceState trialState) &&
                    trialState == TaskInstanceState.Active)
                {
                    return nearShrine
                        ? Tr(engine, FirstNonEmpty(hints.ExploreShrineNearPrompt, hints.ExploreShrinePrompt))
                        : Tr(engine, FirstNonEmpty(hints.ExploreShrinePrompt, hints.ExplorePrompt));
                }

                if (tasks.TryGetState(NarrativeShowcaseIds.BriefingTaskId, out TaskInstanceState briefingState) &&
                    briefingState == TaskInstanceState.Active)
                {
                    return nearWarden
                        ? Tr(engine, FirstNonEmpty(hints.ExploreWardenNearPrompt, hints.ExploreWardenPrompt))
                        : Tr(engine, FirstNonEmpty(hints.ExploreWardenPrompt, hints.ExplorePrompt));
                }
            }

            return Tr(engine, FirstNonEmpty(hints.ExploreWardenPrompt, hints.ExplorePrompt));
        }

        private NarrativeFrontendSurfaceModel BuildNotificationSurface(GameEngine engine)
        {
            int take = Math.Min(2, _history.Count);
            var items = new List<NarrativeFrontendSurfaceItem>(take);
            for (int i = 0; i < take; i++)
            {
                string line = _history[_history.Count - 1 - i];
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: i == 0 ? Tr(engine, "story.ui.notification.now") : Tr(engine, "story.ui.notification.earlier"),
                    Value: line,
                    Active: i == 0));
            }

            return CreateSurface(
                _frontendConfig.NotificationStack,
                NarrativeFrontendSurfaceKind.NotificationStack,
                _frontendConfig.NotificationStack.Title,
                string.Empty,
                _frontendConfig.NotificationStack.Footer,
                items);
        }

        private void AddCastNameplates(GameEngine engine, List<NarrativeFrontendSurfaceModel> surfaces)
        {
            NarrativeShowcaseCastMemberConfig[] cast = _frontendConfig.Cast;
            for (int i = 0; i < cast.Length; i++)
            {
                NarrativeShowcaseCastMemberConfig member = cast[i];
                if (string.IsNullOrWhiteSpace(member.EntityName) ||
                    !TryProjectNamedEntity(engine, member.EntityName, member.HeadOffsetYCm, out float screenX, out float screenY))
                {
                    continue;
                }

                NarrativeShowcaseSurfaceConfig plate = _frontendConfig.Nameplate;
                surfaces.Add(new NarrativeFrontendSurfaceModel(
                    SurfaceId: $"{_frontendConfig.OwnerId}.nameplate.{member.EntityName}",
                    Kind: NarrativeFrontendSurfaceKind.WorldNameplate,
                    Anchor: NarrativeFrontendAnchor.TopLeft,
                    Title: Tr(engine, member.Title),
                    Subtitle: Tr(engine, member.Role),
                    Width: plate.Width,
                    OffsetX: screenX - UiMargin - (plate.Width * 0.5f),
                    OffsetY: screenY - UiMargin - NameplateLiftPx,
                    ZIndex: plate.ZIndex,
                    AccentHex: FirstNonEmpty(member.AccentHex, plate.AccentHex),
                    BackgroundHex: plate.BackgroundHex,
                    BorderHex: plate.BorderHex,
                    ForegroundHex: plate.ForegroundHex,
                    MutedHex: plate.MutedHex,
                    LayoutId: plate.LayoutId,
                    StyleClass: plate.StyleClass));
            }
        }

        private bool HasNonZeroStoryVariable(GameEngine engine)
        {
            MapVariableStore? variables = engine.CurrentMapSession?.Variables;
            if (variables == null)
            {
                return false;
            }

            for (int i = 0; i < _frontendConfig.Variables.Length; i++)
            {
                string variableId = _frontendConfig.Variables[i].VariableId;
                if (variables.Contains(variableId) && variables.ReadInt(variableId) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildCastSignature(GameEngine engine)
        {
            if (!TryFindEntityByName(engine.World, NarrativeShowcaseIds.PlayerName, out Entity player) ||
                !engine.World.TryGet(player, out WorldPositionCm playerPos))
            {
                return string.Empty;
            }

            Vector2 pos = playerPos.Value.ToVector2();
            return string.Join("|",
                $"{pos.X:0}",
                $"{pos.Y:0}",
                IsNearNamed(engine, NarrativeShowcaseIds.ElderName, WardenInteractRangeCm),
                IsNearNamed(engine, NarrativeShowcaseIds.ShrineName, ShrineInteractRangeCm));
        }

        private bool TryProjectNamedEntity(GameEngine engine, string name, float headOffsetYCm, out float screenX, out float screenY)
        {
            screenX = 0f;
            screenY = 0f;
            if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector ||
                !TryFindEntityByName(engine.World, name, out Entity entity) ||
                !engine.World.TryGet(entity, out WorldPositionCm worldPos))
            {
                return false;
            }

            Vector2 world = worldPos.Value.ToVector2();
            Vector2 screen;
            try
            {
                screen = projector.WorldToScreen(new Vector3(
                    world.X / 100f,
                    headOffsetYCm / 100f,
                    world.Y / 100f));
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            if (float.IsNaN(screen.X) || float.IsNaN(screen.Y))
            {
                return false;
            }

            screenX = screen.X;
            screenY = screen.Y;
            return true;
        }

        private static bool IsNear(WorldPositionCm a, WorldPositionCm b, float rangeCm)
        {
            Vector2 va = a.Value.ToVector2();
            Vector2 vb = b.Value.ToVector2();
            return Vector2.Distance(va, vb) <= rangeCm;
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
