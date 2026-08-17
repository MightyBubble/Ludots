using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod.Input;
using InteractionShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace InteractionShowcaseMod.Runtime
{
    internal sealed class InteractionShowcaseRuntime
    {
        private const int ShowcaseLocalPlayerId = 1;
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();

        private readonly InteractionShowcasePanelController _panelController;
        private bool _inputContextActive;
        private bool _showcaseHudSuppressed;
        private int _visibleUatFrame;
        private Entity[] _blinkActorsScratch = new Entity[8];
        private Entity[] _blinkSelectedScratch = new Entity[8];

        internal const string SavedCollectionKey = "showcase.interaction.command.saved";

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
            var input = context.Get(CoreServiceKeys.InputHandler);

            if (showcaseActive)
            {
                _visibleUatFrame = 0;
                ActivateInputContext(input);
                EnsureDefaultShowcaseMode(engine);
                SuppressNonEssentialHud(engine);
                RequireShowcaseSolePossessedRep(engine, activeMapId!);
                PublishShowcaseKnowledge(engine, activeMapId!);
                EnsureShowcaseCommandSourceView(engine);
                if (IsUiPanelSuppressed(engine))
                {
                    CloseEntityInfoPanels(context);
                }
                else
                {
                    EnsureEntityInfoPanels(context, engine);
                }

                RefreshPanel(engine);
            }
            else
            {
                CloseEntityInfoPanels(context);
                RestoreSuppressedHud(engine);
                ClearShowcaseModeIfOwned(engine);
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

            ClearShowcaseModeIfOwned(engine);
            CloseEntityInfoPanels(context);
            RestoreSuppressedHud(engine);
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

            ApplyVisibleUatTimelines(engine);

            if (IsUiPanelSuppressed(engine))
            {
                CloseEntityInfoPanels(engine);
                ClearPanelIfOwned(engine);
                return;
            }

            TrySeedHoverTargetFromLiveSelectionForVisibleUat(engine);

            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrRefresh(root, engine, activeMapId!);
        }

        private void ApplyVisibleUatTimelines(GameEngine engine)
        {
            bool schemeTimelineEnabled = IsEnabled(InteractionShowcaseIds.AutoSchemeTimelineEnvKey);
            bool blinkTimelineEnabled = IsEnabled(InteractionShowcaseIds.AutoBlinkTimelineEnvKey);
            if (!schemeTimelineEnabled && !blinkTimelineEnabled)
            {
                return;
            }

            _visibleUatFrame++;
            engine.GlobalContext[InteractionShowcaseIds.VisibleUatFrameKey] = _visibleUatFrame;

            if (blinkTimelineEnabled)
            {
                PublishBlinkDispatchEvidence(engine);
            }

            if (!schemeTimelineEnabled ||
                engine.GetService(CoreServiceKeys.ControlSchemeRuntime) is not ControlSchemeRuntime schemes)
            {
                return;
            }

            string targetScheme = _visibleUatFrame >= 90
                ? "scheme.wasd_move"
                : "scheme.default";
            if (schemes.SchemeIdRegistry.TryGetId(targetScheme, out int schemeId) &&
                schemes.ActiveSchemeId != schemeId)
            {
                schemes.TrySwitch(schemeId);
            }
        }

        private void PublishBlinkDispatchEvidence(GameEngine engine)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer))
            {
                throw new InvalidOperationException("Interaction blink evidence requires EntityCollectionStore and a live local player entity.");
            }

            int actorCount = CopyCommandSourceActors(collections, viewer);
            if (actorCount <= 0)
            {
                throw new InvalidOperationException("Interaction blink evidence requires a non-empty command-source collection.");
            }

            CastDispatchProfileRegistry dispatch = engine.GetService(CoreServiceKeys.CastDispatchProfileRegistry)
                ?? throw new InvalidOperationException("Interaction blink evidence requires CastDispatchProfileRegistry.");
            string profileId = ResolveBlinkDispatchProfileId(_visibleUatFrame);
            if (!dispatch.ProfileIdRegistry.TryGetId(profileId, out int profileRegistryId) ||
                !dispatch.IsInstalled(profileRegistryId))
            {
                throw new InvalidOperationException($"Interaction blink evidence requires installed dispatch profile '{profileId}'.");
            }

            EnsureScratchCapacity(ref _blinkSelectedScratch, actorCount);
            var context = new CastDispatchContext(
                engine.World,
                new Vector3(
                    InteractionShowcaseIds.BlinkEvidenceTargetWorldXCm,
                    0f,
                    InteractionShowcaseIds.BlinkEvidenceTargetWorldZCm),
                groupKey: 581_650L);
            int selectedCount = dispatch.SelectDispatchTargets(
                profileRegistryId,
                _blinkActorsScratch.AsSpan(0, actorCount),
                in context,
                _blinkSelectedScratch.AsSpan(0, actorCount),
                out CastDispatchRouting routing);

            var descriptor = EntityCollectionDescriptor.Create(
                InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey,
                EntityCollectionSourceKind.CollectionSnapshot,
                EntityCollectionRoleKind.Display,
                contextEntity: viewer,
                primaryEntity: selectedCount > 0 ? _blinkSelectedScratch[0] : Entity.Null,
                title: "Blink dispatch evidence",
                summary: BuildBlinkEvidenceSummary(profileId, selectedCount, actorCount, in routing));
            collections.Replace(
                viewer,
                in descriptor,
                _blinkSelectedScratch.AsSpan(0, selectedCount),
                viewer);
        }

        private int CopyCommandSourceActors(EntityCollectionStore collections, Entity viewer)
        {
            if (!collections.TryGetView(viewer, EntityCollectionKeys.CommandSource, out EntityCollectionView view))
            {
                return 0;
            }

            EnsureScratchCapacity(ref _blinkActorsScratch, view.Count);
            int copied = collections.CopyEntities(viewer, EntityCollectionKeys.CommandSource, _blinkActorsScratch.AsSpan(0, view.Count));
            if (copied != view.Count)
            {
                throw new InvalidOperationException(
                    $"Interaction blink evidence copied {copied} command-source row(s), expected {view.Count}.");
            }

            return copied;
        }

        private static void EnsureScratchCapacity(ref Entity[] scratch, int required)
        {
            if (scratch.Length >= required)
            {
                return;
            }

            int next = scratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref scratch, next);
        }

        private static string ResolveBlinkDispatchProfileId(int frame)
        {
            return frame < 90
                ? InteractionShowcaseIds.BlinkDispatchAllTogetherProfileId
                : frame < 180
                    ? InteractionShowcaseIds.BlinkDispatchOneByOneProfileId
                    : InteractionShowcaseIds.BlinkDispatchNearestTopNProfileId;
        }

        private static string BuildBlinkEvidenceSummary(
            string profileId,
            int selectedCount,
            int actorCount,
            in CastDispatchRouting routing)
        {
            string routingLabel = routing.Sequential ? "sequential" : "parallel";
            string orderLabel = routing.SharedOrderId ? "shared order" : "per-actor order";
            return $"{profileId}: {selectedCount}/{actorCount} hero(es), {routingLabel}, {orderLabel}.";
        }

        private static bool IsEnabled(string key)
        {
            return string.Equals(Environment.GetEnvironmentVariable(key), "1", StringComparison.Ordinal);
        }

        public bool SaveControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer))
            {
                return false;
            }

            Entity[] current = EntityCollectionContextRuntime.Snapshot(
                engine.GlobalContext,
                viewer,
                EntityCollectionKeys.CommandSource);
            if (current.Length <= 0)
            {
                return false;
            }

            PublishCollection(collections, viewer, ControlGroupCollectionKey(groupIndex), current, "Saved command group", $"{current.Length} hero(es)");
            PublishCollection(collections, viewer, SavedCollectionKey, current, "Saved command group", $"{current.Length} hero(es)");
            engine.GlobalContext[InteractionShowcaseIds.ActiveControlGroupKey] = groupIndex;
            return true;
        }

        public bool RecallControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) ||
                !collections.TryGetView(viewer, ControlGroupCollectionKey(groupIndex), out EntityCollectionView group) ||
                group.Count <= 0)
            {
                return false;
            }

            Entity[] members = new Entity[group.Count];
            int count = collections.CopyEntities(viewer, ControlGroupCollectionKey(groupIndex), members);
            if (count <= 0) return false;
            if (count != members.Length) Array.Resize(ref members, count);

            PublishShowcaseCommandSource(engine, viewer, members);
            PublishCollection(collections, viewer, SavedCollectionKey, members, "Saved command group", $"{members.Length} hero(es)");
            engine.GlobalContext[InteractionShowcaseIds.ActiveControlGroupKey] = groupIndex;
            return true;
        }

        public bool ShowLiveSelection(GameEngine engine)
        {
            engine.GlobalContext[InteractionShowcaseIds.ActiveControlGroupKey] = 0;
            return TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) &&
                   collections.TryGetView(viewer, EntityCollectionKeys.CommandSource, out EntityCollectionView view) &&
                   view.Count > 0;
        }

        public bool ShowFormationSelection(GameEngine engine)
        {
            return TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) &&
                   collections.TryGetView(viewer, SavedCollectionKey, out EntityCollectionView view) &&
                   view.Count > 0;
        }

        internal static string ControlGroupCollectionKey(int groupIndex) => $"showcase.interaction.command.group.{groupIndex}";

        private static bool TryResolveCollectionContext(GameEngine engine, out EntityCollectionStore collections, out Entity owner)
        {
            collections = default!;
            owner = Entity.Null;
            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore store)
            {
                return false;
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var localViewer) ||
                !engine.World.IsAlive(localViewer))
            {
                return false;
            }

            collections = store;
            owner = localViewer;
            return true;
        }

        private static void PublishCollection(
            EntityCollectionStore collections,
            Entity owner,
            string key,
            ReadOnlySpan<Entity> actors,
            string title,
            string summary)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                key,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: actors.Length > 0 ? actors[0] : Entity.Null,
                title: title,
                summary: summary);
            collections.Replace(owner, in descriptor, actors, owner);
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

            if (!TryResolveCommandSourceOwner(engine, out Arch.Core.Entity commandSourceOwner))
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
                    EntityInfoPanelTarget.EntityCollection(commandSourceOwner, EntityCollectionKeys.CommandSource),
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
            if (!TryResolveCommandSourceOwner(engine, out Arch.Core.Entity commandSourceOwner) ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections ||
                !EntityCollectionContextRuntime.TryGetPrimary(
                    engine.World,
                    collections,
                    commandSourceOwner,
                    EntityCollectionKeys.CommandSource,
                    out Arch.Core.Entity selected))
            {
                return null;
            }

            return EntityInfoPanelTarget.Fixed(selected);
        }

        private static bool TryResolveCommandSourceOwner(GameEngine engine, out Arch.Core.Entity owner)
        {
            owner = Arch.Core.Entity.Null;
            return ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out owner) &&
                   owner != Arch.Core.Entity.Null &&
                   engine.World.IsAlive(owner);
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

        private static void CloseEntityInfoPanels(GameEngine engine)
        {
            if (engine.GetService(EntityInfoPanelServiceKeys.Service) is not EntityInfoPanelService service ||
                engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseIfPresent(service, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
            CloseIfPresent(service, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
            CloseIfPresent(service, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
            CloseIfPresent(service, handles, InteractionShowcaseIds.SelectionViewUiHandleKey);
            CloseIfPresent(service, handles, InteractionShowcaseIds.ArcweaverOverlayHandleKey);
            CloseIfPresent(service, handles, InteractionShowcaseIds.VanguardOverlayHandleKey);
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

        private static void CloseIfPresent(EntityInfoPanelService service, EntityInfoPanelHandleStore handles, string handleKey)
        {
            if (handles.TryGet(handleKey, out EntityInfoPanelHandle handle))
            {
                service.Close(handle);
                handles.Remove(handleKey);
            }
        }

        private static bool IsUiPanelSuppressed(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InteractionShowcaseIds.SuppressUiPanelKey, out var suppressObj) &&
                   suppressObj is bool suppress &&
                   suppress;
        }

        private static void EnsureDefaultShowcaseMode(GameEngine engine)
        {
            ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId);
            if (!InteractionShowcaseIds.IsShowcaseMode(activeModeId))
            {
                ViewModeRuntime.TrySwitchTo(engine.GlobalContext, InteractionShowcaseIds.LolModeId);
            }
        }

        private static void ClearShowcaseModeIfOwned(GameEngine engine)
        {
            if (ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId) &&
                InteractionShowcaseIds.IsShowcaseMode(activeModeId))
            {
                ViewModeRuntime.TryClearActiveMode(engine.GlobalContext);
            }
        }

        private void SuppressNonEssentialHud(GameEngine engine)
        {
            if (_showcaseHudSuppressed)
            {
                return;
            }

            engine.GlobalContext[ViewModeSwitchSystem.ViewModeHudEnabledKey] = false;
            engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = false;
            _showcaseHudSuppressed = true;
        }

        private void RestoreSuppressedHud(GameEngine engine)
        {
            if (!_showcaseHudSuppressed)
            {
                return;
            }

            engine.GlobalContext[ViewModeSwitchSystem.ViewModeHudEnabledKey] = true;
            engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = true;
            _showcaseHudSuppressed = false;
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

        private static Entity RequireShowcaseSolePossessedRep(GameEngine engine, string activeMapId)
        {
            Entity possessed = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.IsAlive(possessed) ||
                !engine.World.TryGet(possessed, out PlayerOwner owner) ||
                !engine.World.TryGet(possessed, out MapEntity mapEntity) ||
                !IsShowcasePossessedPlayer(activeMapId, in owner, in mapEntity))
            {
                throw new InvalidOperationException(
                    "Interaction showcase requires sole ClientLocalSeat possession of map playerId 1 from launchContext.localSeats / startupLocalSeats.");
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

        private static bool IsShowcasePossessedPlayer(string activeMapId, in PlayerOwner owner, in MapEntity mapEntity)
        {
            return owner.PlayerId == ShowcaseLocalPlayerId &&
                   string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureShowcaseCommandSourceView(GameEngine engine)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer))
            {
                return;
            }

            Span<Entity> initialCommandActors = stackalloc Entity[3];
            int count = 0;
            AddInitialCommandActor(engine, InteractionShowcaseIds.ArcweaverName, initialCommandActors, ref count);
            AddInitialCommandActor(engine, InteractionShowcaseIds.VanguardName, initialCommandActors, ref count);
            AddInitialCommandActor(engine, InteractionShowcaseIds.CommanderName, initialCommandActors, ref count);
            if (count > 0)
            {
                if (!collections.TryGetView(viewer, EntityCollectionKeys.CommandSource, out EntityCollectionView commandView) ||
                    commandView.Count <= 0)
                {
                    PublishShowcaseCommandSource(engine, viewer, initialCommandActors[..count]);
                }

                if (!IsSpecializedInteractionShowcaseActive(engine))
                {
                    PublishShowcaseCommandSource(engine, viewer, initialCommandActors[..count]);
                    TrySeedHoverTargetForVisibleUat(engine, viewer, initialCommandActors[..count]);
                }
            }
        }

        private static bool IsSpecializedInteractionShowcaseActive(GameEngine engine)
        {
            return engine.GlobalContext.ContainsKey("SuperweaponContextShowcase.RuntimeState");
        }

        private static void PublishShowcaseCommandSource(GameEngine engine, Entity owner, ReadOnlySpan<Entity> actors)
        {
            if (actors.IsEmpty ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: actors[0],
                title: "Active hero command group",
                summary: "The showcase starts with these heroes ready for pointer commands.");
            collections.Replace(owner, in descriptor, actors, owner);
        }

        private static void TrySeedHoverTargetForVisibleUat(GameEngine engine, Entity owner, ReadOnlySpan<Entity> actors)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("LUDOTS_INTERACTION_SHOWCASE_SEED_HOVER_TARGET"), "1", StringComparison.Ordinal) ||
                actors.Length < 2 ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.HoveredEntity,
                EntityCollectionSourceKind.UiHover,
                EntityCollectionRoleKind.Display,
                contextEntity: owner,
                primaryEntity: actors[1],
                title: "Ground command hover check",
                summary: "A hovered hero is present so the screenshot proves the ground command still goes to the ground.");
            ReadOnlySpan<Entity> rows = stackalloc[] { actors[1] };
            collections.Replace(owner, in descriptor, rows, owner);
        }

        private static void TrySeedHoverTargetFromLiveSelectionForVisibleUat(GameEngine engine)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("LUDOTS_INTERACTION_SHOWCASE_SEED_HOVER_TARGET"), "1", StringComparison.Ordinal) ||
                !TryResolveCollectionContext(engine, out EntityCollectionStore commandCollections, out Entity viewer) ||
                !commandCollections.TryGet(viewer, EntityCollectionKeys.CommandSource, out EntityCollectionHandle commandHandle) ||
                !commandCollections.TryGetEntityAt(commandHandle, 1, out Entity hovered) ||
                hovered == Entity.Null ||
                !engine.World.IsAlive(hovered) ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.HoveredEntity,
                EntityCollectionSourceKind.UiHover,
                EntityCollectionRoleKind.Display,
                contextEntity: viewer,
                primaryEntity: hovered,
                title: "Ground command hover check",
                summary: "A hovered hero is present so the screenshot proves the ground command still goes to the ground.");
            ReadOnlySpan<Entity> rows = stackalloc[] { hovered };
            collections.Replace(viewer, in descriptor, rows, viewer);
        }

        private static void AddInitialCommandActor(GameEngine engine, string entityName, Span<Entity> destination, ref int count)
        {
            if ((uint)count >= (uint)destination.Length)
            {
                return;
            }

            Entity entity = ResolveNamedEntity(engine, entityName);
            if (entity != Entity.Null && engine.World.IsAlive(entity))
            {
                destination[count++] = entity;
            }
        }

        private static Entity ResolveNamedEntity(GameEngine engine, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }
    }
}
