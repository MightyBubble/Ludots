using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using EntityCommandPanelMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillSandboxRuntime
    {
        private static readonly QueryDescription StressSelectableQuery = new QueryDescription().WithAll<Name, Team, MapEntity, AbilityStateBuffer>();
        private static readonly QueryDescription StressOrderBufferQuery = new QueryDescription().WithAll<Team, MapEntity, OrderBuffer>();
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();
        private static readonly Vector4 SelectionPanelFill = new(0.05f, 0.08f, 0.11f, 0.88f);
        private static readonly Vector4 SelectionPanelBorder = new(0.41f, 0.74f, 0.89f, 0.95f);
        private static readonly Vector4 SelectionPanelTitle = new(0.94f, 0.83f, 0.47f, 1f);
        private static readonly Vector4 SelectionPanelText = new(0.90f, 0.94f, 0.98f, 1f);
        private static readonly Vector4 SelectionPanelHint = new(0.70f, 0.78f, 0.86f, 1f);
        private const int ShowcaseLocalPlayerId = 1;
        private const string PlayerFormationCollectionKey = "collection.champion.player.formation";
        private const string AiTargetsCollectionKey = "collection.champion.ai.targets";
        private const string AiFormationCollectionKey = "collection.champion.ai.formation";
        private const string CommandPreviewCollectionKey = "collection.champion.command.preview";

        private EntityCommandPanelHandle _focusPanelHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastPanelTarget = Entity.Null;
        private Entity _teamBViewer = Entity.Null;
        private Entity _debugViewer = Entity.Null;
        private string _lastMapId = string.Empty;
        private bool _scenarioTagsApplied;
        private bool _initialCommandSourceApplied;
        private readonly List<Entity> _teamAFormation = new();
        private readonly List<Entity> _teamBFormation = new();
        private readonly List<Entity> _teamBTargets = new();
        private readonly List<Entity> _activeSelectionMirror = new();

        public void PrepareInput(GameEngine engine)
        {
            if (!ChampionSkillSandboxIds.IsSandboxMap(engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            EnsureMode(engine);
            EnsureScenarioState(engine);
            SyncSelectionViews(engine, drawOverlay: false);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (!ChampionSkillSandboxIds.IsSandboxMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return Task.CompletedTask;
            }

            EnsureMode(engine);
            EnsureScenarioState(engine);
            SyncFocusPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (ChampionSkillSandboxIds.IsSandboxMap(context.Get(CoreServiceKeys.MapId).Value))
            {
                Disable(engine);
            }

            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (!ChampionSkillSandboxIds.IsSandboxMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return;
            }

            EnsureMode(engine);
            EnsureScenarioState(engine);
            SyncSelectionViews(engine, drawOverlay: true);
            ConsumeResetCameraRequest(engine);
            SyncCameraFollow(engine);
            SyncFocusPanel(engine);
        }

        public void CaptureCommandSnapshot(GameEngine engine)
        {
            if (!ChampionSkillSandboxIds.IsStressMap(engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            EntityCollectionStore? collections = engine.GetService(CoreServiceKeys.EntityCollectionStore);
            if (collections == null)
            {
                return;
            }

            if (_debugViewer == Entity.Null || !engine.World.IsAlive(_debugViewer))
            {
                _debugViewer = EnsureViewerEntity(engine, _debugViewer, "Stress Viewer Debug", playerId: null);
            }

            if (_debugViewer == Entity.Null)
            {
                return;
            }

            Entity playerViewer = ResolveOrAssignLocalPlayer(engine, ResolveFirstControllableChampion(engine));
            Entity[] snapshot = SnapshotCollection(collections, playerViewer, EntityCollectionKeys.CommandSource);
            ReplaceCollection(collections, _debugViewer, CommandPreviewCollectionKey, EntityCollectionRoleKind.CommandPreview, snapshot, "Command preview");
        }

        private void EnsureScenarioState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.Equals(_lastMapId, mapId, StringComparison.Ordinal))
            {
                _lastMapId = mapId;
                _scenarioTagsApplied = false;
                _initialCommandSourceApplied = false;
            }

            EnsureControllableOwnership(engine);

            if (!_scenarioTagsApplied)
            {
                ApplyInitialTag(engine, ChampionSkillSandboxIds.EzrealCooldownName, ChampionSkillSandboxIds.EzrealBlockedTag);
                ApplyInitialTag(engine, ChampionSkillSandboxIds.GarenCourageName, ChampionSkillSandboxIds.GarenCourageTag);
                ApplyInitialTag(engine, ChampionSkillSandboxIds.JayceHammerName, ChampionSkillSandboxIds.JayceHammerTag);
                _scenarioTagsApplied = true;
            }

            if (!_initialCommandSourceApplied)
            {
                _initialCommandSourceApplied = SeedInitialCommandSource(engine);
            }

            if (!engine.GlobalContext.ContainsKey(ChampionSkillSandboxIds.CameraFollowModeKey))
            {
                engine.GlobalContext[ChampionSkillSandboxIds.CameraFollowModeKey] = ChampionSkillSandboxIds.FreeCameraToolbarButtonId;
            }

            if (!engine.GlobalContext.ContainsKey(ChampionSkillSandboxIds.SelectionViewChoiceKey))
            {
                engine.GlobalContext[ChampionSkillSandboxIds.SelectionViewChoiceKey] = ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId;
            }

            if (!engine.GlobalContext.ContainsKey(EntityCommandPanelShowcaseTheme.ContextKey))
            {
                engine.GlobalContext[EntityCommandPanelShowcaseTheme.ContextKey] = ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId();
            }
        }

        private void SyncSelectionViews(GameEngine engine, bool drawOverlay)
        {
            EntityCollectionStore? collections = engine.GetService(CoreServiceKeys.EntityCollectionStore);
            Entity playerViewer = ResolveOrAssignLocalPlayer(engine, ResolveFirstControllableChampion(engine));
            if (collections == null || playerViewer == Entity.Null || !engine.World.IsAlive(playerViewer))
            {
                return;
            }

            if (!ChampionSkillSandboxIds.IsStressMap(engine.CurrentMapSession?.MapId.Value))
            {
                PublishSelectableKnowledge(engine, playerViewer, aiViewer: Entity.Null, debugViewer: Entity.Null);
                ApplySelectionViewChoice(engine, collections, playerViewer, aiViewer: Entity.Null, debugViewer: Entity.Null);
                return;
            }

            _teamBViewer = EnsureViewerEntity(engine, _teamBViewer, "Stress Viewer Team B", playerId: 2);
            _debugViewer = EnsureViewerEntity(engine, _debugViewer, "Stress Viewer Debug", playerId: null);
            if (_teamBViewer == Entity.Null || _debugViewer == Entity.Null)
            {
                return;
            }

            CollectStressSelectionState(engine);
            PublishSelectableKnowledge(engine, playerViewer, _teamBViewer, _debugViewer);
            BindStressCollections(collections, playerViewer, _teamBViewer, _debugViewer);
            ApplySelectionViewChoice(engine, collections, playerViewer, _teamBViewer, _debugViewer);
            if (drawOverlay)
            {
                DrawStressSelectionOverlay(engine, collections);
            }
        }

        private static void PublishSelectableKnowledge(
            GameEngine engine,
            Entity playerViewer,
            Entity aiViewer,
            Entity debugViewer)
        {
            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeMapId))
            {
                return;
            }

            KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("KnowledgeProjectionStore missing.");
            var empty = KnowledgeIdMask256.Empty;
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            engine.World.Query(in SelectableKnowledgeQuery, (Entity target, ref CommandSourceSelectableTag _, ref MapEntity mapEntity) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, activeMapId, StringComparison.Ordinal))
                {
                    return;
                }

                UpsertLiveKnowledge(engine.World, knowledge, playerViewer, target, in empty, observedTick);
                UpsertLiveKnowledge(engine.World, knowledge, aiViewer, target, in empty, observedTick);
                UpsertLiveKnowledge(engine.World, knowledge, debugViewer, target, in empty, observedTick);
            });
        }

        private static void UpsertLiveKnowledge(
            World world,
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            Entity target,
            in KnowledgeIdMask256 empty,
            int observedTick)
        {
            if (viewer == Entity.Null || !world.IsAlive(viewer) || target == Entity.Null || !world.IsAlive(target))
            {
                return;
            }

            knowledge.Upsert(
                viewer,
                target,
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
        }

        private void CollectStressSelectionState(GameEngine engine)
        {
            _teamAFormation.Clear();
            _teamBFormation.Clear();
            _teamBTargets.Clear();

            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            engine.World.Query(in StressSelectableQuery, (Entity entity, ref Name _, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer _) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
                {
                    return;
                }

                if (team.Id == 1)
                {
                    _teamAFormation.Add(entity);
                }
                else if (team.Id == 2)
                {
                    _teamBFormation.Add(entity);
                }
            });

            _teamAFormation.Sort((left, right) => CompareEntitiesByName(engine.World, left, right));
            _teamBFormation.Sort((left, right) => CompareEntitiesByName(engine.World, left, right));

            var uniqueTargets = new HashSet<int>();
            engine.World.Query(in StressOrderBufferQuery, (Entity _, ref Team team, ref MapEntity mapEntity, ref OrderBuffer orders) =>
            {
                if (team.Id != 2 || !string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
                {
                    return;
                }

                AddOrderTarget(engine, orders.ActiveOrder.Order, orders.HasActive, uniqueTargets, _teamBTargets);
                AddOrderTarget(engine, orders.PendingOrder.Order, orders.HasPending, uniqueTargets, _teamBTargets);
                for (int i = 0; i < orders.QueuedCount; i++)
                {
                    AddOrderTarget(engine, orders.GetQueued(i).Order, include: true, uniqueTargets, _teamBTargets);
                }
            });
        }

        private void BindStressCollections(
            EntityCollectionStore collections,
            Entity playerViewer,
            Entity aiViewer,
            Entity debugViewer)
        {
            ReplaceCollection(collections, playerViewer, PlayerFormationCollectionKey, EntityCollectionRoleKind.Display, _teamAFormation.ToArray(), "Player formation");
            ReplaceCollection(collections, aiViewer, AiTargetsCollectionKey, EntityCollectionRoleKind.Display, _teamBTargets.ToArray(), "AI command targets");
            ReplaceCollection(collections, aiViewer, AiFormationCollectionKey, EntityCollectionRoleKind.Display, _teamBFormation.ToArray(), "AI formation");

            Entity[] commandSource = SnapshotCollection(collections, playerViewer, EntityCollectionKeys.CommandSource);
            ReplaceCollection(collections, debugViewer, CommandPreviewCollectionKey, EntityCollectionRoleKind.CommandPreview, commandSource, "Command preview");
        }

        private void ApplySelectionViewChoice(
            GameEngine engine,
            EntityCollectionStore collections,
            Entity playerViewer,
            Entity aiViewer,
            Entity debugViewer)
        {
            ResolveActiveCollectionChoice(engine, playerViewer, aiViewer, debugViewer, out Entity owner, out string key);
            engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionOwnerKey] = owner;
            engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionKey] = key;
            MirrorActiveSelectionViewToCommandSource(engine, collections, playerViewer, owner, key);
        }

        private void MirrorActiveSelectionViewToCommandSource(
            GameEngine engine,
            EntityCollectionStore collections,
            Entity playerViewer,
            Entity owner,
            string key)
        {
            if (playerViewer == Entity.Null ||
                owner == Entity.Null ||
                string.IsNullOrWhiteSpace(key) ||
                owner != playerViewer ||
                (owner == playerViewer && string.Equals(key, EntityCollectionKeys.CommandSource, StringComparison.Ordinal)))
            {
                return;
            }

            if (!collections.TryGet(owner, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view))
            {
                return;
            }

            _activeSelectionMirror.Clear();
            for (int i = 0; i < view.Count; i++)
            {
                if (collections.TryGetEntityAt(handle, i, out Entity entity) &&
                    entity != Entity.Null &&
                    engine.World.IsAlive(entity))
                {
                    _activeSelectionMirror.Add(entity);
                }
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                playerViewer,
                _activeSelectionMirror.Count > 0 ? _activeSelectionMirror[0] : Entity.Null,
                "Champion active selection",
                $"{_activeSelectionMirror.Count} entity(s) mirrored from {key}.");
            collections.Replace(playerViewer, descriptor, CollectionsMarshal.AsSpan(_activeSelectionMirror), playerViewer);
        }

        private void DrawStressSelectionOverlay(GameEngine engine, EntityCollectionStore collections)
        {
            ScreenOverlayBuffer? overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            if (overlay == null)
            {
                return;
            }

            int x = 20;
            int y = 120;
            overlay.AddRect(x, y, 520, 182, SelectionPanelFill, SelectionPanelBorder, stableId: 42100, dirtySerial: 1);
            overlay.AddText(x + 16, y + 26, "Selection SSOT", 20, SelectionPanelTitle, stableId: 42101, dirtySerial: 1);

            if (!TryResolveActiveCollection(engine, collections, out Entity owner, out EntityCollectionHandle handle, out EntityCollectionView current))
            {
                overlay.AddText(x + 16, y + 54, "No active selection view.", 15, SelectionPanelText, stableId: 42102, dirtySerial: 1);
                return;
            }

            string viewerLabel = ResolveEntityLabel(engine.World, owner) ?? $"Entity#{owner.Id}";
            string primaryLabel = ResolveEntityLabel(engine.World, current.PrimaryEntity) ?? "(none)";
            string members = BuildSelectionMemberPreview(engine.World, collections, handle, current.Count);

            overlay.AddText(x + 16, y + 54, $"View {ChampionSkillSandboxIds.ResolveSelectionViewLabel(ResolveSelectionViewChoice(engine))} | owner={viewerLabel} | key={current.Key}", 15, SelectionPanelText, stableId: 42103, dirtySerial: 1);
            overlay.AddText(x + 16, y + 78, $"Collection {current.Key} | role={current.Role} | rev={current.Revision} | count={current.Count}", 14, SelectionPanelText, stableId: 42104, dirtySerial: 1);
            overlay.AddText(x + 16, y + 100, $"Primary {primaryLabel}", 14, SelectionPanelText, stableId: 42105, dirtySerial: 1);
            overlay.AddText(x + 16, y + 122, $"Members {members}", 13, SelectionPanelHint, stableId: 42106, dirtySerial: 1);
            overlay.AddText(x + 16, y + 146, "Buttons: P1/P1F | AI/AIF | CMD", 13, SelectionPanelHint, stableId: 42107, dirtySerial: 1);
        }

        private static void EnsureControllableOwnership(GameEngine engine)
        {
            var query = new QueryDescription().WithAll<AbilityStateBuffer, Team>();
            engine.World.Query(in query, (Entity entity, ref AbilityStateBuffer _, ref Team team) =>
            {
                if (team.Id != 1 || engine.World.Has<PlayerOwner>(entity))
                {
                    return;
                }

                engine.World.Add(entity, new PlayerOwner { PlayerId = 1 });
            });
        }

        private static bool SeedInitialCommandSource(GameEngine engine)
        {
            EntityCollectionStore? collections = engine.GetService(CoreServiceKeys.EntityCollectionStore);
            Entity initialCommandActor = ResolveChampionEntity(engine, ChampionSkillSandboxIds.EzrealAlphaName);
            Entity owner = ResolveOrAssignLocalPlayer(engine, initialCommandActor);
            if (collections == null || owner == Entity.Null || !engine.World.IsAlive(owner))
            {
                return false;
            }

            if (collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView existing) &&
                existing.PrimaryEntity != Entity.Null &&
                engine.World.IsAlive(existing.PrimaryEntity))
            {
                engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionOwnerKey] = owner;
                engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionKey] = EntityCollectionKeys.CommandSource;
                return true;
            }

            if (initialCommandActor == Entity.Null)
            {
                return false;
            }

            Span<Entity> commandSourceBuffer = stackalloc Entity[1];
            commandSourceBuffer[0] = initialCommandActor;
            ReplaceCollection(
                collections,
                owner,
                EntityCollectionKeys.CommandSource,
                EntityCollectionRoleKind.CommandSource,
                commandSourceBuffer,
                "Initial command source");
            engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionOwnerKey] = owner;
            engine.GlobalContext[ChampionSkillSandboxIds.ActiveCollectionKey] = EntityCollectionKeys.CommandSource;
            return true;
        }

        private static Entity ResolveOrAssignLocalPlayer(GameEngine engine, Entity preferredLocalPlayer)
        {
            int playerId = ResolveLocalPlayerId(engine, preferredLocalPlayer);
            if (playerId <= 0)
            {
                return Entity.Null;
            }

            return PublishLocalPlayerBinding(engine, playerId);
        }

        private static int ResolveLocalPlayerId(GameEngine engine, Entity preferredLocalPlayer)
        {
            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine.GlobalContext);
            if (seats.TryGetSoleSeat(out ClientLocalSeat seat) && seat.HasPossession)
            {
                return seat.PossessedPlayerId;
            }

            if (preferredLocalPlayer != Entity.Null &&
                engine.World.IsAlive(preferredLocalPlayer) &&
                engine.World.TryGet(preferredLocalPlayer, out PlayerOwner preferredOwner) &&
                preferredOwner.PlayerId > 0)
            {
                return preferredOwner.PlayerId;
            }

            Entity firstControllable = ResolveFirstControllableChampion(engine);
            if (firstControllable != Entity.Null &&
                engine.World.TryGet(firstControllable, out PlayerOwner firstOwner) &&
                firstOwner.PlayerId > 0)
            {
                return firstOwner.PlayerId;
            }

            return ShowcaseLocalPlayerId;
        }

        private static Entity PublishLocalPlayerBinding(GameEngine engine, int playerId)
        {
            if (playerId <= 0)
            {
                return Entity.Null;
            }

            if (!engine.TryGetService(CoreServiceKeys.PlayerEntityLookup, out PlayerEntityLookup lookup) ||
                lookup == null)
            {
                throw new InvalidOperationException("ChampionSkillSandbox requires PlayerEntityLookup from map participant bindings.");
            }

            if (!lookup.TryGet(playerId, out Entity localPlayer) ||
                localPlayer == Entity.Null ||
                !engine.World.IsAlive(localPlayer))
            {
                throw new InvalidOperationException(
                    $"ChampionSkillSandbox requires a live player representative for PlayerId {playerId}. " +
                    "Declare the player in the map Players binding instead of using a controllable champion as the representative.");
            }

            ClientLocalSeatBindings.BindSoleSeat(engine, localPlayer, playerId);

            return localPlayer;
        }

        private static void ApplyInitialTag(GameEngine engine, string entityName, string tagName)
        {
            Entity entity = ResolveChampionEntity(engine, entityName);
            if (entity == Entity.Null)
            {
                return;
            }

            int tagId = TagRegistry.Register(tagName);
            TagStateInstaller.EnsureInstalled(engine.World, entity);
            TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("ChampionSkillSandbox requires engine TagOps.");
            tagOps.AddTag(engine.World, entity, tagId);
        }

        private void EnsureMode(GameEngine engine)
        {
            if (!ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId) ||
                !ChampionSkillSandboxIds.IsSandboxMode(activeModeId))
            {
                ViewModeRuntime.TrySwitchTo(engine.GlobalContext, ChampionSkillSandboxIds.SmartCastModeId);
            }
        }

        private static void ConsumeResetCameraRequest(GameEngine engine)
        {
            bool requested = false;
            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input &&
                input.PressedThisFrame(ChampionSkillSandboxIds.ResetCameraActionId))
            {
                requested = true;
            }

            if (engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.ResetCameraRequestKey, out var resetObj) &&
                resetObj is bool resetRequested &&
                resetRequested)
            {
                requested = true;
            }

            engine.GlobalContext.Remove(ChampionSkillSandboxIds.ResetCameraRequestKey);

            if (requested)
            {
                ResetCamera(engine);
            }
        }

        private static void ResetCamera(GameEngine engine)
        {
            var session = engine.CurrentMapSession;
            var cameraConfig = session?.MapConfig?.DefaultCamera;
            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            if (session == null || registry == null)
            {
                return;
            }

            string virtualCameraId = string.IsNullOrWhiteSpace(cameraConfig?.VirtualCameraId)
                ? ChampionSkillSandboxIds.TacticalCameraId
                : cameraConfig.VirtualCameraId;

            if (!registry.TryGet(virtualCameraId, out var definition) || definition == null)
            {
                return;
            }

            engine.GlobalContext[ChampionSkillSandboxIds.CameraFollowModeKey] = ChampionSkillSandboxIds.FreeCameraToolbarButtonId;
            engine.GameSession.Camera.ActivateVirtualCamera(
                virtualCameraId,
                blendDurationSeconds: 0f,
                followTarget: null,
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable,
                resetRuntimeState: true);

            if (cameraConfig == null)
            {
                return;
            }

            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = virtualCameraId,
                TargetCm = (cameraConfig.TargetXCm.HasValue || cameraConfig.TargetYCm.HasValue)
                    ? new Vector2(cameraConfig.TargetXCm ?? 0f, cameraConfig.TargetYCm ?? 0f)
                    : null,
                Yaw = cameraConfig.Yaw,
                Pitch = cameraConfig.Pitch,
                DistanceCm = cameraConfig.DistanceCm,
                FovYDeg = cameraConfig.FovYDeg,
            });
            engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
        }

        private static void SyncCameraFollow(GameEngine engine)
        {
            string followModeId = ResolveCameraFollowMode(engine);
            string activeCameraId = engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeCameraId))
            {
                return;
            }

            Entity commandSourceOwner = Entity.Null;
            if (string.Equals(followModeId, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.Ordinal) ||
                string.Equals(followModeId, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.Ordinal))
            {
                commandSourceOwner = ResolveOrAssignLocalPlayer(engine, ResolveFirstControllableChampion(engine));
                if (commandSourceOwner == Entity.Null || !engine.World.IsAlive(commandSourceOwner))
                {
                    engine.GameSession.Camera.SetFollowTarget(activeCameraId, null, snapToFollowTargetWhenAvailable: false);
                    return;
                }
            }

            ICameraFollowTarget? followTarget = followModeId switch
            {
                var id when string.Equals(id, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.Ordinal)
                    => CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, CameraFollowTargetKind.EntityCollectionPrimary, commandSourceOwner, EntityCollectionKeys.CommandSource),
                var id when string.Equals(id, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.Ordinal)
                    => CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, CameraFollowTargetKind.EntityCollectionGroup, commandSourceOwner, EntityCollectionKeys.CommandSource),
                _ => null
            };

            engine.GameSession.Camera.SetFollowTarget(activeCameraId, followTarget, snapToFollowTargetWhenAvailable: false);
        }

        private static string ResolveCameraFollowMode(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.CameraFollowModeKey, out var modeObj) &&
                modeObj is string modeId &&
                ChampionSkillSandboxIds.IsCameraFollowMode(modeId))
            {
                return modeId;
            }

            return ChampionSkillSandboxIds.FreeCameraToolbarButtonId;
        }

        private void SyncFocusPanel(GameEngine engine)
        {
            IEntityCommandPanelService? service = engine.GetService(CoreServiceKeys.EntityCommandPanelService);
            if (service == null)
            {
                return;
            }

            Entity target = ResolvePanelTarget(engine);
            bool visible = target != Entity.Null;

            if (!_focusPanelHandle.IsValid)
            {
                Entity initialTarget = visible ? target : ResolveFirstControllableChampion(engine);
                _focusPanelHandle = service.Open(new EntityCommandPanelOpenRequest
                {
                    TargetEntity = initialTarget,
                    SourceId = "gas.ability-slots",
                    InstanceKey = "champion-skill-sandbox.focus",
                    Anchor = ResolvePanelAnchor(engine),
                    Size = ResolvePanelSize(engine),
                    InitialGroupIndex = 0,
                    StartVisible = visible
                });
                _lastPanelTarget = initialTarget;
            }

            if (!_focusPanelHandle.IsValid)
            {
                return;
            }

            if (visible && _lastPanelTarget != target)
            {
                service.RebindTarget(_focusPanelHandle, target);
                _lastPanelTarget = target;
            }

            service.SetAnchor(_focusPanelHandle, ResolvePanelAnchor(engine));
            service.SetSize(_focusPanelHandle, ResolvePanelSize(engine));
            service.SetVisible(_focusPanelHandle, visible);
        }

        private static EntityCommandPanelAnchor ResolvePanelAnchor(GameEngine engine)
        {
            return EntityCommandPanelShowcaseTheme.ResolveSandboxAnchor(ResolveShowcaseTheme(engine));
        }

        private static EntityCommandPanelSize ResolvePanelSize(GameEngine engine)
        {
            return EntityCommandPanelShowcaseTheme.ResolveSandboxSize(ResolveShowcaseTheme(engine));
        }

        private static string ResolveShowcaseTheme(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(EntityCommandPanelShowcaseTheme.ContextKey, out object? themeObj) &&
                themeObj is string themeId)
            {
                return EntityCommandPanelShowcaseTheme.Normalize(themeId, ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId());
            }

            return ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId();
        }

        private static Entity ResolvePanelTarget(GameEngine engine)
        {
            Entity commandSourcePrimary = TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                EntityCollectionContextRuntime.TryGetPrimary(
                    engine.World,
                    engine.GlobalContext,
                    owner,
                    EntityCollectionKeys.CommandSource,
                    out Entity primary)
                ? primary
                : Entity.Null;
            if (IsCommandPanelTarget(engine, commandSourcePrimary))
            {
                return commandSourcePrimary;
            }

            Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (IsControllableChampion(engine, local))
            {
                return local;
            }

            return Entity.Null;
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }

        private static bool IsCommandPanelTarget(GameEngine engine, Entity entity)
        {
            return entity != Entity.Null &&
                   engine.World.IsAlive(entity) &&
                   engine.World.Has<AbilityStateBuffer>(entity);
        }

        private static bool IsControllableChampion(GameEngine engine, Entity entity)
        {
            return entity != Entity.Null &&
                   engine.World.IsAlive(entity) &&
                   engine.World.Has<AbilityStateBuffer>(entity) &&
                   engine.World.TryGet(entity, out PlayerOwner owner) &&
                   owner.PlayerId == 1;
        }

        private static Entity EnsureViewerEntity(GameEngine engine, Entity current, string name, int? playerId)
        {
            if (engine.World.IsAlive(current))
            {
                return current;
            }

            Entity viewer = engine.World.Create(new Name { Value = name });
            if (playerId.HasValue)
            {
                engine.World.Add(viewer, new PlayerOwner { PlayerId = playerId.Value });
            }

            return viewer;
        }

        private static string ResolveSelectionViewChoice(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.SelectionViewChoiceKey, out var choiceObj) &&
                choiceObj is string choice &&
                ChampionSkillSandboxIds.IsSelectionViewButton(choice))
            {
                return choice;
            }

            return ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId;
        }

        private static void AddOrderTarget(
            GameEngine engine,
            in Order order,
            bool include,
            HashSet<int> seen,
            List<Entity> destination)
        {
            if (!include || order.Target == Entity.Null || !engine.World.IsAlive(order.Target) || !seen.Add(order.Target.Id))
            {
                return;
            }

            destination.Add(order.Target);
        }

        private static int CompareEntitiesByName(World world, Entity left, Entity right)
        {
            string leftName = ResolveEntityLabel(world, left) ?? string.Empty;
            string rightName = ResolveEntityLabel(world, right) ?? string.Empty;
            int byName = string.Compare(leftName, rightName, StringComparison.Ordinal);
            return byName != 0 ? byName : left.Id.CompareTo(right.Id);
        }

        private static void ResolveActiveCollectionChoice(
            GameEngine engine,
            Entity playerViewer,
            Entity aiViewer,
            Entity debugViewer,
            out Entity owner,
            out string key)
        {
            string choice = ResolveSelectionViewChoice(engine);
            owner = playerViewer;
            key = EntityCollectionKeys.CommandSource;

            if (string.Equals(choice, ChampionSkillSandboxIds.PlayerFormationToolbarButtonId, StringComparison.Ordinal))
            {
                key = PlayerFormationCollectionKey;
                return;
            }

            if (string.Equals(choice, ChampionSkillSandboxIds.AiTargetToolbarButtonId, StringComparison.Ordinal) &&
                aiViewer != Entity.Null)
            {
                owner = aiViewer;
                key = AiTargetsCollectionKey;
                return;
            }

            if (string.Equals(choice, ChampionSkillSandboxIds.AiFormationToolbarButtonId, StringComparison.Ordinal) &&
                aiViewer != Entity.Null)
            {
                owner = aiViewer;
                key = AiFormationCollectionKey;
                return;
            }

            if (string.Equals(choice, ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId, StringComparison.Ordinal) &&
                debugViewer != Entity.Null)
            {
                owner = debugViewer;
                key = CommandPreviewCollectionKey;
            }
        }

        private static bool TryResolveActiveCollection(
            GameEngine engine,
            EntityCollectionStore collections,
            out Entity owner,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            owner = Entity.Null;
            handle = EntityCollectionHandle.Invalid;
            view = default;

            if (engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.ActiveCollectionOwnerKey, out object? ownerObj) &&
                ownerObj is Entity storedOwner &&
                engine.World.IsAlive(storedOwner))
            {
                owner = storedOwner;
            }
            else
            {
                owner = ResolveOrAssignLocalPlayer(engine, ResolveFirstControllableChampion(engine));
            }

            string key = engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.ActiveCollectionKey, out object? keyObj) &&
                         keyObj is string storedKey &&
                         !string.IsNullOrWhiteSpace(storedKey)
                ? storedKey
                : EntityCollectionKeys.CommandSource;

            return owner != Entity.Null &&
                   collections.TryGet(owner, key, out handle) &&
                   collections.TryGetView(handle, out view);
        }

        private static Entity[] SnapshotCollection(EntityCollectionStore collections, Entity owner, string key)
        {
            if (owner == Entity.Null ||
                !collections.TryGet(owner, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return Array.Empty<Entity>();
            }

            var members = new Entity[view.Count];
            int written = collections.CopyEntities(handle, 0, members);
            if (written != members.Length)
            {
                Array.Resize(ref members, written);
            }

            return members;
        }

        private static void ReplaceCollection(
            EntityCollectionStore collections,
            Entity owner,
            string key,
            EntityCollectionRoleKind role,
            ReadOnlySpan<Entity> members,
            string title)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                key,
                EntityCollectionSourceKind.Explicit,
                role,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                title,
                $"{members.Length} entity(s)");
            collections.Replace(owner, descriptor, members, owner);
        }

        private static string BuildSelectionMemberPreview(
            World world,
            EntityCollectionStore collections,
            EntityCollectionHandle handle,
            int count)
        {
            if (count <= 0)
            {
                return "(empty)";
            }

            Entity[] members = new Entity[count];
            int written = collections.CopyEntities(handle, 0, members);
            int previewCount = Math.Min(5, written);
            var labels = new List<string>(previewCount + 1);
            for (int i = 0; i < previewCount; i++)
            {
                labels.Add(ResolveEntityLabel(world, members[i]) ?? $"Entity#{members[i].Id}");
            }

            if (written > previewCount)
            {
                labels.Add($"+{written - previewCount} more");
            }

            return string.Join(", ", labels);
        }

        private static string? ResolveEntityLabel(World world, Entity entity)
        {
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                return null;
            }

            return world.TryGet(entity, out Name name) ? name.Value : null;
        }

        private static Entity ResolveChampionEntity(GameEngine engine, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static Entity ResolveFirstControllableChampion(GameEngine engine)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<AbilityStateBuffer, PlayerOwner>();
            engine.World.Query(in query, (Entity entity, ref AbilityStateBuffer _, ref PlayerOwner owner) =>
            {
                if (result != Entity.Null || owner.PlayerId != 1)
                {
                    return;
                }

                result = entity;
            });
            return result;
        }

        private void Disable(GameEngine engine)
        {
            if (_focusPanelHandle.IsValid &&
                engine.GetService(CoreServiceKeys.EntityCommandPanelService) is IEntityCommandPanelService service)
            {
                service.Close(_focusPanelHandle);
            }

            if (ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId) &&
                ChampionSkillSandboxIds.IsSandboxMode(activeModeId))
            {
                ViewModeRuntime.TryClearActiveMode(engine.GlobalContext);
            }

            _focusPanelHandle = EntityCommandPanelHandle.Invalid;
            _lastPanelTarget = Entity.Null;
            _scenarioTagsApplied = false;
            _initialCommandSourceApplied = false;
            _lastMapId = string.Empty;
            if (engine.World.IsAlive(_teamBViewer))
            {
                engine.World.Destroy(_teamBViewer);
            }

            if (engine.World.IsAlive(_debugViewer))
            {
                engine.World.Destroy(_debugViewer);
            }

            _teamBViewer = Entity.Null;
            _debugViewer = Entity.Null;
            _teamAFormation.Clear();
            _teamBFormation.Clear();
            _teamBTargets.Clear();
            _activeSelectionMirror.Clear();
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.ResetCameraRequestKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.CameraFollowModeKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.SelectionViewChoiceKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.ActiveCollectionOwnerKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.ActiveCollectionKey);
        }
    }
}
