using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;
using NarrativeShowcaseMod.Input;
using NarrativeShowcaseMod.Systems;
using Ludots.Tests.TestCommon;

namespace NarrativeShowcaseMod.Runtime
{
    internal sealed class NarrativeShowcaseRuntime
    {
        private const int ShowcaseLocalPlayerId = 1;
        private static readonly QueryDescription LocalPlayerCandidateQuery = new QueryDescription().WithAll<Name, PlayerOwner, MapEntity, AbilityStateBuffer>();
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();

        private readonly IModContext _context;
        private readonly NarrativeShowcaseFrontendConfig _frontendConfig;
        private readonly List<string> _history = new();
        private bool _narrativeInputActive;
        private bool _interactionInputActive;
        private int _historySerial;

        internal NarrativeShowcaseRuntime(IModContext context)
        {
            _context = context;
            using var stream = context.GetResource($"{context.ModId}:assets/Frontend/narrative_frontend.json");
            _frontendConfig = NarrativeShowcaseFrontendConfig.Load(stream);
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
            engine.RegisterSystem(new NarrativeShowcaseInteractionSystem(engine, this), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new NarrativeShowcasePanelPresentationSystem(engine, this));
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
                EnsureShowcaseLocalPlayer(engine, activeMapId);
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

        public Task HandleQuestStageChangedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.QuestStageChanged, new Dictionary<string, string>
            {
                ["bodyText"] = context.Get(QuestServiceKeys.ObjectiveText) ?? string.Empty,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleQuestCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.QuestCompleted, new Dictionary<string, string>
            {
                ["questId"] = context.Get(QuestServiceKeys.QuestId) ?? string.Empty,
            });
            RefreshPanel(engine);
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
                ["speaker"] = context.Get(NarrativeServiceKeys.SpeakerName) ?? string.Empty,
                ["bodyText"] = context.Get(NarrativeServiceKeys.BodyText) ?? string.Empty,
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
                ["bodyText"] = context.Get(NarrativeServiceKeys.BodyText) ?? string.Empty,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleCinematicStepEnteredAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            AppendHistory(_frontendConfig.Templates.CinematicEntered, new Dictionary<string, string>
            {
                ["speaker"] = context.Get(NarrativeServiceKeys.SpeakerName) ?? string.Empty,
                ["bodyText"] = context.Get(NarrativeServiceKeys.BodyText) ?? string.Empty,
            });
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleQuestSignalAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            string signalId = context.Get(QuestServiceKeys.SignalId) ?? string.Empty;
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

            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleCinematicCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return Task.CompletedTask;
            }

            string cinematicId = context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty;
            if (string.Equals(cinematicId, NarrativeShowcaseIds.IntroCinematicId, StringComparison.OrdinalIgnoreCase))
            {
                director.StartDialogue(NarrativeShowcaseIds.BriefingDialogueId);
            }
            else if (string.Equals(cinematicId, NarrativeShowcaseIds.TrialRevealCinematicId, StringComparison.OrdinalIgnoreCase))
            {
                director.EmitSignal(NarrativeShowcaseIds.SpawnBeastSignal);
            }

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
                engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            RebindEntities(engine);
            frontend.Publish(BuildPage(engine, director));
        }

        internal void RebindEntities(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            if (TryRenameSpawnedBeast(engine))
            {
                PublishShowcaseKnowledge(engine, NarrativeShowcaseIds.MapId);
            }

            BindByName(engine, director, NarrativeShowcaseIds.PlayerAlias, NarrativeShowcaseIds.PlayerName);
            BindByName(engine, director, NarrativeShowcaseIds.ElderAlias, NarrativeShowcaseIds.ElderName);
            BindByName(engine, director, NarrativeShowcaseIds.ShrineAlias, NarrativeShowcaseIds.ShrineName);
            BindByName(engine, director, NarrativeShowcaseIds.BeastAlias, NarrativeShowcaseIds.BeastName);
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

        private NarrativeFrontendPageState BuildPage(GameEngine engine, NarrativeDirector director)
        {
            var surfaces = new List<NarrativeFrontendSurfaceModel>(8)
            {
                BuildPromptSurface(engine, director),
                BuildObjectiveSurface(director),
                BuildHistorySurface(),
                BuildVariablesSurface(director),
            };

            if (director.TryGetActiveCinematicView(out NarrativeCinematicView cinematic))
            {
                surfaces.Add(BuildCinematicSurface(cinematic));
            }

            if (director.TryGetActiveDialogueView(out NarrativeDialogueView dialogue))
            {
                surfaces.Add(BuildDialogueSurface(dialogue));
                if (dialogue.Choices.Count > 0)
                {
                    surfaces.Add(BuildChoiceSurface(dialogue));
                }
            }

            surfaces.RemoveAll(static surface => !surface.Visible);

            return new NarrativeFrontendPageState(
                _frontendConfig.OwnerId,
                BuildSignature(engine, director, surfaces.Count),
                true,
                _frontendConfig.BackdropHex,
                surfaces);
        }

        private NarrativeFrontendSurfaceModel BuildPromptSurface(GameEngine engine, NarrativeDirector director)
        {
            string body = BeastDefeated(engine)
                ? _frontendConfig.Hints.ReturnPrompt
                : BeastSpawned(engine)
                    ? _frontendConfig.Hints.CombatPrompt
                    : director.TryGetActiveDialogueView(out NarrativeDialogueView dialogue) && dialogue.Choices.Count > 0
                        ? _frontendConfig.Hints.ChoicePrompt
                        : _frontendConfig.Hints.ExplorePrompt;
            string footer = director.HasActiveCinematic
                ? _frontendConfig.Hints.SkipPrompt
                : string.Empty;
            return CreateSurface(
                _frontendConfig.PromptRibbon,
                NarrativeFrontendSurfaceKind.PromptRibbon,
                _frontendConfig.Hints.PromptTitle,
                body,
                footer);
        }

        private NarrativeFrontendSurfaceModel BuildObjectiveSurface(NarrativeDirector director)
        {
            IReadOnlyList<QuestView> quests = director.GetQuestViews();
            QuestView? activeQuest = null;
            for (int i = 0; i < quests.Count; i++)
            {
                if (quests[i].State == QuestState.Active)
                {
                    activeQuest = quests[i];
                    break;
                }
            }

            if (activeQuest == null)
            {
                return CreateSurface(_frontendConfig.ObjectiveTracker, NarrativeFrontendSurfaceKind.ObjectiveTracker, _frontendConfig.ObjectiveTracker.Title, director.BuildObjectiveSummary());
            }

            string title = ReplaceTokens(_frontendConfig.Templates.ObjectiveTitleFormat, new Dictionary<string, string>
            {
                ["quest"] = activeQuest.DisplayName,
                ["stage"] = activeQuest.StageTitle,
            });
            return CreateSurface(
                _frontendConfig.ObjectiveTracker,
                NarrativeFrontendSurfaceKind.ObjectiveTracker,
                title,
                activeQuest.ObjectiveText,
                activeQuest.ObjectiveHint);
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

        private NarrativeFrontendSurfaceModel BuildVariablesSurface(NarrativeDirector director)
        {
            var items = new List<NarrativeFrontendSurfaceItem>(_frontendConfig.Variables.Length);
            for (int i = 0; i < _frontendConfig.Variables.Length; i++)
            {
                NarrativeShowcaseVariableConfig variable = _frontendConfig.Variables[i];
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: variable.Label,
                    Value: director.GetVariableText(variable.VariableId),
                    Caption: ReplaceTokens(_frontendConfig.Templates.VariableCaptionFormat, new Dictionary<string, string>
                    {
                        ["label"] = variable.Label,
                        ["value"] = director.GetVariableText(variable.VariableId),
                    }),
                    AccentHex: variable.AccentHex,
                    Active: !string.IsNullOrWhiteSpace(director.GetVariableText(variable.VariableId))));
            }

            return CreateSurface(
                _frontendConfig.VariablesPanel,
                NarrativeFrontendSurfaceKind.StatusPanel,
                _frontendConfig.VariablesPanel.Title,
                string.Empty,
                _frontendConfig.VariablesPanel.Footer,
                items);
        }

        private NarrativeFrontendSurfaceModel BuildDialogueSurface(NarrativeDialogueView dialogue)
        {
            NarrativeShowcaseSurfaceConfig config = dialogue.Choices.Count > 0
                ? _frontendConfig.OverlayDialogue
                : _frontendConfig.DialogueBubble;
            NarrativeFrontendSurfaceKind kind = dialogue.Choices.Count > 0
                ? NarrativeFrontendSurfaceKind.OverlayDialogue
                : NarrativeFrontendSurfaceKind.DialogueBubble;
            string footer = dialogue.AutoAdvance
                ? _frontendConfig.Hints.AutoAdvancePrompt
                : config.Footer;
            return CreateSurface(
                config,
                kind,
                dialogue.SpeakerName,
                dialogue.BodyText,
                footer,
                null,
                dialogue.WaitForInput,
                false,
                dialogue.Progress01,
                dialogue.AutoAdvanceSeconds > 0f ? Math.Max(0f, dialogue.AutoAdvanceSeconds - dialogue.ElapsedSeconds) : 0f);
        }

        private NarrativeFrontendSurfaceModel BuildChoiceSurface(NarrativeDialogueView dialogue)
        {
            var items = new List<NarrativeFrontendSurfaceItem>(dialogue.Choices.Count);
            for (int i = 0; i < dialogue.Choices.Count; i++)
            {
                NarrativeDialogueChoiceView choice = dialogue.Choices[i];
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: choice.Text,
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

        private NarrativeFrontendSurfaceModel BuildCinematicSurface(NarrativeCinematicView cinematic)
        {
            bool transmission = ContainsId(_frontendConfig.Routing.TransmissionCinematicIds, cinematic.CinematicId);
            NarrativeShowcaseSurfaceConfig config = transmission
                ? _frontendConfig.TransmissionOverlay
                : _frontendConfig.SubtitleBubble;
            NarrativeFrontendSurfaceKind kind = transmission
                ? NarrativeFrontendSurfaceKind.TransmissionOverlay
                : NarrativeFrontendSurfaceKind.SubtitleBubble;
            string footer = cinematic.RequiresAdvance
                ? _frontendConfig.Hints.SkipPrompt
                : _frontendConfig.Hints.AutoAdvancePrompt;
            return CreateSurface(
                config,
                kind,
                cinematic.SpeakerName,
                cinematic.BodyText,
                footer,
                null,
                cinematic.RequiresAdvance,
                true,
                cinematic.Progress01,
                cinematic.RequiresAdvance ? 0f : Math.Max(0f, cinematic.DurationSeconds - cinematic.ElapsedSeconds));
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
                MutedHex: config.MutedHex);
        }

        private void EnsureBootstrapped(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BootstrappedKey, out var bootObj) && bootObj is bool booted && booted)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            ResetHistory();
            director.ResetState();
            RebindEntities(engine);
            director.StartQuest(NarrativeShowcaseIds.QuestId);
            director.StartCinematic(NarrativeShowcaseIds.IntroCinematicId);
            engine.GlobalContext[NarrativeShowcaseIds.BootstrappedKey] = true;
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = false;
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

        private static void EnsureShowcaseLocalPlayer(GameEngine engine, string activeMapId)
        {
            if (TryResolveExistingLocalPlayer(engine, activeMapId, out _))
            {
                return;
            }

            Entity firstCandidate = Entity.Null;
            Entity preferredCandidate = Entity.Null;
            int firstPlayerId = 0;
            int preferredPlayerId = 0;

            engine.World.Query(in LocalPlayerCandidateQuery, (Entity entity, ref Name name, ref PlayerOwner owner, ref MapEntity mapEntity, ref AbilityStateBuffer _) =>
            {
                if (!IsLocalPlayerCandidate(activeMapId, in owner, in mapEntity))
                {
                    return;
                }

                if (firstCandidate == Entity.Null)
                {
                    firstCandidate = entity;
                    firstPlayerId = owner.PlayerId;
                }

                if (string.Equals(name.Value, NarrativeShowcaseIds.PlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    preferredCandidate = entity;
                    preferredPlayerId = owner.PlayerId;
                }
            });

            Entity resolved = preferredCandidate != Entity.Null ? preferredCandidate : firstCandidate;
            int resolvedPlayerId = preferredCandidate != Entity.Null ? preferredPlayerId : firstPlayerId;
            if (resolved == Entity.Null)
            {
                return;
            }

            PublishShowcaseLocalPlayer(engine, resolved, resolvedPlayerId);
        }

        private static bool TryResolveExistingLocalPlayer(GameEngine engine, string activeMapId, out Entity localPlayer)
        {
            localPlayer = Entity.Null;
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity existing) ||
                !engine.World.IsAlive(existing) ||
                !engine.World.TryGet(existing, out PlayerOwner owner) ||
                !engine.World.TryGet(existing, out MapEntity mapEntity) ||
                !IsLocalPlayerCandidate(activeMapId, in owner, in mapEntity))
            {
                return false;
            }

            localPlayer = existing;
            PublishShowcaseLocalPlayer(engine, existing, owner.PlayerId);
            return true;
        }

        private static void PublishShowcaseLocalPlayer(GameEngine engine, Entity localPlayer, int playerId)
        {
            if (playerId <= 0)
            {
                return;
            }

            if (!engine.TryGetService(CoreServiceKeys.PlayerEntityLookup, out PlayerEntityLookup lookup) ||
                lookup == null ||
                (lookup.TryGet(playerId, out Entity existing) && existing != localPlayer))
            {
                lookup = new PlayerEntityLookup();
                engine.SetService(CoreServiceKeys.PlayerEntityLookup, lookup);
            }

            if (!lookup.TryGet(playerId, out _))
            {
                lookup.Register(playerId, localPlayer);
            }

            ClientLocalSeatTestBindings.BindSoleSeat(engine, localPlayer, playerId);
        }

        private static bool IsLocalPlayerCandidate(string activeMapId, in PlayerOwner owner, in MapEntity mapEntity)
        {
            return owner.PlayerId == ShowcaseLocalPlayerId &&
                   string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.OrdinalIgnoreCase);
        }

        private static void PublishShowcaseKnowledge(GameEngine engine, string activeMapId)
        {
            if (!engine.ClientLocalSeatAccess.TryGetSolePossessedRep(GlobalContext, out Entity viewerObj) ||
                viewerObj is not Entity viewer ||
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

        private void BindByName(GameEngine engine, NarrativeDirector director, string alias, string name)
        {
            if (TryFindEntityByName(engine.World, name, out Entity entity))
            {
                director.BindEntity(alias, entity);
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

        private string BuildSignature(GameEngine engine, NarrativeDirector director, int surfaceCount)
        {
            string dialogueSig = director.TryGetActiveDialogueView(out NarrativeDialogueView dialogue)
                ? $"{dialogue.DialogueId}|{dialogue.NodeId}|{dialogue.Choices.Count}|{dialogue.Progress01:0.00}"
                : string.Empty;
            string cinematicSig = director.TryGetActiveCinematicView(out NarrativeCinematicView cinematic)
                ? $"{cinematic.CinematicId}|{cinematic.StepId}|{cinematic.Progress01:0.00}|{cinematic.RequiresAdvance}"
                : string.Empty;
            return string.Join("||",
                director.BuildQuestSummary(),
                director.BuildObjectiveSummary(),
                director.BuildVariableSummary(NarrativeShowcaseIds.TrustVariableId, NarrativeShowcaseIds.LoreVariableId, NarrativeShowcaseIds.EndingVariableId),
                dialogueSig,
                cinematicSig,
                surfaceCount,
                _historySerial,
                BeastSpawned(engine),
                BeastDefeated(engine));
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
