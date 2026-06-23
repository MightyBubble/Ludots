using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod.Input;
using InteractionShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace InteractionShowcaseMod.Runtime
{
    internal sealed class InteractionShowcaseRuntime
    {
        private const int ShowcaseLocalPlayerId = 1;
        private static readonly QueryDescription LocalPlayerCandidateQuery = new QueryDescription().WithAll<Name, PlayerOwner, MapEntity, AbilityStateBuffer>();
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<SelectionSelectableTag, MapEntity>();

        private readonly InteractionShowcasePanelController _panelController;
        private bool _inputContextActive;

        public InteractionShowcaseRuntime()
        {
            _panelController = new InteractionShowcasePanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool showcaseActive = InteractionShowcaseIds.IsShowcaseMap(activeMapId);
            var viewModeManager = ResolveViewModeManager(engine);
            var input = context.Get(CoreServiceKeys.InputHandler);

            if (showcaseActive)
            {
                ActivateInputContext(input);
                EnsureDefaultShowcaseMode(viewModeManager);
                EnsureShowcaseLocalPlayer(engine, activeMapId!);
                PublishShowcaseKnowledge(engine, activeMapId!);
                EnsureShowcaseSelectionView(engine);
                EnsureEntityInfoPanels(context, engine);
                RefreshPanel(engine);
            }
            else
            {
                CloseEntityInfoPanels(context);
                ClearShowcaseModeIfOwned(viewModeManager);
                DeactivateInputContext(input);
                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (string.IsNullOrWhiteSpace(mapId.Value) ||
                !InteractionShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                return Task.CompletedTask;
            }

            ClearShowcaseModeIfOwned(ResolveViewModeManager(engine));
            CloseEntityInfoPanels(context);
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!InteractionShowcaseIds.IsShowcaseMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GlobalContext.TryGetValue(InteractionShowcaseIds.SuppressUiPanelKey, out var suppressObj) &&
                suppressObj is bool suppress &&
                suppress)
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrRefresh(root, engine, activeMapId!, ResolveViewModeManager(engine));
        }

        public bool SaveControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Arch.Core.Entity viewer))
            {
                return false;
            }

            bool saved = SelectionControlGroupRuntime.TrySaveViewedSelectionToGroup(
                engine.World,
                engine.GlobalContext,
                selection,
                viewer,
                groupIndex,
                mirrorToFormation: true);
            if (saved)
            {
                engine.GlobalContext[InteractionShowcaseIds.ActiveControlGroupKey] = groupIndex;
            }

            return saved;
        }

        public bool RecallControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Arch.Core.Entity viewer))
            {
                return false;
            }

            bool recalled = SelectionControlGroupRuntime.TryRecallGroupToLive(
                engine.World,
                engine.GlobalContext,
                selection,
                viewer,
                groupIndex,
                mirrorToFormation: true);
            if (recalled)
            {
                engine.GlobalContext[InteractionShowcaseIds.ActiveControlGroupKey] = groupIndex;
            }

            return recalled;
        }

        public bool ShowLiveSelection(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Arch.Core.Entity viewer))
            {
                return false;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            return true;
        }

        public bool ShowFormationSelection(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Arch.Core.Entity viewer))
            {
                return false;
            }

            selection.TryGetOrCreateContainer(viewer, SelectionSetKeys.FormationPrimary, SelectionContainerKind.Formation, out _);
            selection.TryBindView(viewer, SelectionViewKeys.Formation, viewer, SelectionSetKeys.FormationPrimary);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Formation;
            return true;
        }

        private void ActivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || _inputContextActive)
            {
                return;
            }

            EnsureShowcaseInputSchema(input);
            input.PushContext(InteractionShowcaseInputContexts.Showcase);
            _inputContextActive = true;
        }

        private void DeactivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || !_inputContextActive)
            {
                return;
            }

            input.PopContext(InteractionShowcaseInputContexts.Showcase);
            _inputContextActive = false;
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private static void EnsureEntityInfoPanels(ScriptContext context, GameEngine engine)
        {
            if (engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectionViewUiHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.EntityCollectionInspector,
                    EntityInfoPanelSurface.Ui,
                    EntityInfoPanelTarget.CurrentSelectionView(),
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomLeft, 16f, 16f, 632f, 332f),
                    EntityInfoGasDetailFlags.None,
                    true));

            EntityInfoPanelTarget? selectedTarget = TryResolveSelectedTarget(engine);
            if (!selectedTarget.HasValue)
            {
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
                return;
            }

            bool createdComponentPanel = OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedComponentUiHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.ComponentInspector,
                    EntityInfoPanelSurface.Ui,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopRight, 16f, 16f, 408f, 320f),
                    EntityInfoGasDetailFlags.None,
                    true));

            if (createdComponentPanel &&
                handles.TryGet(InteractionShowcaseIds.SelectedComponentUiHandleKey, out EntityInfoPanelHandle componentHandle) &&
                engine.GetService(EntityInfoPanelServiceKeys.Service) is EntityInfoPanelService panelService)
            {
                panelService.SetAllComponentsEnabled(componentHandle, false);
            }
            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedGasUiHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.GasInspector,
                    EntityInfoPanelSurface.Ui,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomRight, 16f, 16f, 408f, 264f),
                    EntityInfoGasDetailFlags.ShowAttributeAggregateSources | EntityInfoGasDetailFlags.ShowModifierState,
                    true));

            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedGasOverlayHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.GasInspector,
                    EntityInfoPanelSurface.Overlay,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopCenter, 0f, 16f, 300f, 192f),
                    EntityInfoGasDetailFlags.ShowModifierState,
                    true));
        }

        private static EntityInfoPanelTarget? TryResolveSelectedTarget(GameEngine engine)
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Arch.Core.Entity selected) ||
                selected == Arch.Core.Entity.Null ||
                !engine.World.IsAlive(selected))
            {
                return null;
            }

            return EntityInfoPanelTarget.Fixed(selected);
        }

        private static bool OpenOrUpdate(
            ScriptContext context,
            EntityInfoPanelHandleStore handles,
            string handleKey,
            EntityInfoPanelRequest request)
        {
            if (handles.TryGet(handleKey, out _))
            {
                new UpdateEntityInfoPanelCommand
                {
                    HandleSlotKey = handleKey,
                    Visible = true,
                    Layout = request.Layout,
                    Target = request.Target,
                    GasDetailFlags = request.GasDetailFlags
                }.ExecuteAsync(context).GetAwaiter().GetResult();
                return false;
            }

            new OpenEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey,
                Request = request
            }.ExecuteAsync(context).GetAwaiter().GetResult();
            return true;
        }

        private static void CloseEntityInfoPanels(ScriptContext context)
        {
            if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectionViewUiHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.ArcweaverOverlayHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.VanguardOverlayHandleKey);
        }

        private static void CloseIfPresent(ScriptContext context, EntityInfoPanelHandleStore handles, string handleKey)
        {
            if (!handles.TryGet(handleKey, out _))
            {
                return;
            }

            new CloseEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey
            }.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
        {
            return CoreInputRuntimeServices.GetViewModeManager(engine);
        }

        private static void EnsureDefaultShowcaseMode(ViewModeManager? viewModeManager)
        {
            if (viewModeManager == null)
            {
                return;
            }

            string? activeModeId = viewModeManager.ActiveMode?.Id;
            if (!InteractionShowcaseIds.IsShowcaseMode(activeModeId))
            {
                viewModeManager.SwitchTo(InteractionShowcaseIds.LolModeId);
            }
        }

        private static void ClearShowcaseModeIfOwned(ViewModeManager? viewModeManager)
        {
            if (viewModeManager != null &&
                InteractionShowcaseIds.IsShowcaseMode(viewModeManager.ActiveMode?.Id))
            {
                viewModeManager.ClearActiveMode();
            }
        }

        private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(InteractionShowcaseInputContexts.Showcase))
            {
                throw new InvalidOperationException($"Missing input context: {InteractionShowcaseInputContexts.Showcase}");
            }

            string[] requiredActions =
            {
                "SkillQ",
                "SkillW",
                "SkillE",
                "SkillR",
                "SkillZ",
                "SkillF",
                "ActionAttack",
                "RuneBurst",
                InteractionShowcaseIds.WowModeActionId,
                InteractionShowcaseIds.LolModeActionId,
                InteractionShowcaseIds.Sc2ModeActionId,
                InteractionShowcaseIds.IndicatorModeActionId,
                InteractionShowcaseIds.ActionModeActionId,
                InteractionShowcaseIds.SelectionGroupRecall1ActionId,
                InteractionShowcaseIds.SelectionGroupRecall2ActionId,
                InteractionShowcaseIds.SelectionGroupRecall3ActionId,
                InteractionShowcaseIds.SelectionGroupRecall4ActionId,
                InteractionShowcaseIds.SelectionGroupSave1ActionId,
                InteractionShowcaseIds.SelectionGroupSave2ActionId,
                InteractionShowcaseIds.SelectionGroupSave3ActionId,
                InteractionShowcaseIds.SelectionGroupSave4ActionId
            };

            for (int i = 0; i < requiredActions.Length; i++)
            {
                if (!input.HasAction(requiredActions[i]))
                {
                    throw new InvalidOperationException($"Missing input action: {requiredActions[i]}");
                }
            }
        }

        private static bool TryResolveSelectionContext(GameEngine engine, out SelectionRuntime selection, out Arch.Core.Entity viewer)
        {
            selection = default!;
            viewer = Arch.Core.Entity.Null;
            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime runtime)
            {
                return false;
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
                viewerObj is not Arch.Core.Entity localViewer ||
                !engine.World.IsAlive(localViewer))
            {
                return false;
            }

            selection = runtime;
            viewer = localViewer;
            return true;
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

                if (string.Equals(name.Value, InteractionShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase))
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
            if (!engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity existing) ||
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

            engine.SetService(CoreServiceKeys.LocalPlayerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.LocalPlayerId, playerId);
        }

        private static void PublishShowcaseKnowledge(GameEngine engine, string activeMapId)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
                viewerObj is not Entity viewer ||
                !engine.World.IsAlive(viewer))
            {
                return;
            }

            KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("KnowledgeProjectionStore missing.");
            var empty = KnowledgeIdMask256.Empty;
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            engine.World.Query(in SelectableKnowledgeQuery, (Entity entity, ref SelectionSelectableTag _, ref MapEntity mapEntity) =>
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

        private static bool IsLocalPlayerCandidate(string activeMapId, in PlayerOwner owner, in MapEntity mapEntity)
        {
            return owner.PlayerId == ShowcaseLocalPlayerId &&
                   string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureShowcaseSelectionView(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Arch.Core.Entity viewer))
            {
                return;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary);
            selection.TryGetOrCreateContainer(viewer, SelectionSetKeys.FormationPrimary, SelectionContainerKind.Formation, out _);
            selection.TryBindView(viewer, SelectionViewKeys.Formation, viewer, SelectionSetKeys.FormationPrimary);
            if (!engine.GlobalContext.ContainsKey(CoreServiceKeys.SelectionViewViewerEntity.Name))
            {
                engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            }

            if (!engine.GlobalContext.ContainsKey(CoreServiceKeys.SelectionViewKey.Name))
            {
                engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            }
        }
    }
}
