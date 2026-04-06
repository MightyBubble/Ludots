using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using EntityCommandPanelMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillSandboxRuntime
    {
        private static readonly QueryDescription StressSelectableQuery = new QueryDescription().WithAll<Name, Team, MapEntity, AbilityStateBuffer>();
        private static readonly QueryDescription StressOrderBufferQuery = new QueryDescription().WithAll<Team, MapEntity, OrderBuffer>();
        private static readonly Vector4 SelectionPanelFill = new(0.05f, 0.08f, 0.11f, 0.88f);
        private static readonly Vector4 SelectionPanelBorder = new(0.41f, 0.74f, 0.89f, 0.95f);
        private static readonly Vector4 SelectionPanelTitle = new(0.94f, 0.83f, 0.47f, 1f);
        private static readonly Vector4 SelectionPanelText = new(0.90f, 0.94f, 0.98f, 1f);
        private static readonly Vector4 SelectionPanelHint = new(0.70f, 0.78f, 0.86f, 1f);

        private EntityCommandPanelHandle _focusPanelHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastPanelTarget = Entity.Null;
        private Entity _selectionIndicatorTarget = Entity.Null;
        private Entity _hoverIndicatorTarget = Entity.Null;
        private Entity _aimHoverIndicatorTarget = Entity.Null;
        private Entity _teamBViewer = Entity.Null;
        private Entity _debugViewer = Entity.Null;
        private string _lastMapId = string.Empty;
        private bool _scenarioTagsApplied;
        private bool _initialSelectionApplied;
        private readonly List<Entity> _teamAFormation = new();
        private readonly List<Entity> _teamBFormation = new();
        private readonly List<Entity> _teamBTargets = new();

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
            SyncSelectionViews(engine);
            ConsumeResetCameraRequest(engine);
            SyncCameraFollow(engine);
            SyncFocusPanel(engine);
            SyncHoverIndicator(engine);
            SyncAimHoverIndicator(engine);
        }

        private void EnsureScenarioState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.Equals(_lastMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                _lastMapId = mapId;
                _scenarioTagsApplied = false;
                _initialSelectionApplied = false;
            }

            EnsureControllableOwnership(engine);

            if (!_scenarioTagsApplied)
            {
                ApplyInitialTag(engine, ChampionSkillSandboxIds.EzrealCooldownName, ChampionSkillSandboxIds.EzrealBlockedTag);
                ApplyInitialTag(engine, ChampionSkillSandboxIds.GarenCourageName, ChampionSkillSandboxIds.GarenCourageTag);
                ApplyInitialTag(engine, ChampionSkillSandboxIds.JayceHammerName, ChampionSkillSandboxIds.JayceHammerTag);
                _scenarioTagsApplied = true;
            }

            if (!_initialSelectionApplied)
            {
                _initialSelectionApplied = SeedInitialSelection(engine);
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

        private void SyncSelectionViews(GameEngine engine)
        {
            SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
            Entity playerViewer = ResolveOrAssignLocalPlayer(engine, ResolveFirstControllableChampion(engine));
            if (selection == null || playerViewer == Entity.Null || !engine.World.IsAlive(playerViewer))
            {
                return;
            }

            if (!ChampionSkillSandboxIds.IsStressMap(engine.CurrentMapSession?.MapId.Value))
            {
                ApplySelectionViewChoice(engine, playerViewer, aiViewer: Entity.Null, debugViewer: Entity.Null);
                return;
            }

            _teamBViewer = EnsureViewerEntity(engine, _teamBViewer, "Stress Viewer Team B", playerId: 2);
            _debugViewer = EnsureViewerEntity(engine, _debugViewer, "Stress Viewer Debug", playerId: null);
            if (_teamBViewer == Entity.Null || _debugViewer == Entity.Null)
            {
                return;
            }

            CollectStressSelectionState(engine);
            BindStressSelectionViews(engine, selection, playerViewer, _teamBViewer, _debugViewer);
            ApplySelectionViewChoice(engine, playerViewer, _teamBViewer, _debugViewer);
            DrawStressSelectionOverlay(engine, selection);
        }

        private void CollectStressSelectionState(GameEngine engine)
        {
            _teamAFormation.Clear();
            _teamBFormation.Clear();
            _teamBTargets.Clear();

            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            engine.World.Query(in StressSelectableQuery, (Entity entity, ref Name _, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer _) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase))
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
                if (team.Id != 2 || !string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase))
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

        private void BindStressSelectionViews(
            GameEngine engine,
            SelectionRuntime selection,
            Entity playerViewer,
            Entity aiViewer,
            Entity debugViewer)
        {
            selection.ReplaceSelection(playerViewer, SelectionSetKeys.FormationPrimary, _teamAFormation.ToArray());
            selection.TryBindView(playerViewer, SelectionViewKeys.Formation, playerViewer, SelectionSetKeys.FormationPrimary);

            selection.ReplaceSelection(aiViewer, SelectionSetKeys.LivePrimary, _teamBTargets.ToArray());
            selection.TryBindView(aiViewer, SelectionViewKeys.Primary, aiViewer, SelectionSetKeys.LivePrimary);
            selection.ReplaceSelection(aiViewer, SelectionSetKeys.FormationPrimary, _teamBFormation.ToArray());
            selection.TryBindView(aiViewer, SelectionViewKeys.Formation, aiViewer, SelectionSetKeys.FormationPrimary);

            Entity commandSnapshot = ResolveLatestSelectionSnapshotContainer(engine);
            if (commandSnapshot != Entity.Null)
            {
                selection.TryBindView(debugViewer, SelectionViewKeys.CommandPreview, commandSnapshot);
            }
            else
            {
                selection.ReplaceSelection(debugViewer, SelectionSetKeys.CommandPreview, Array.Empty<Entity>());
                selection.TryBindView(debugViewer, SelectionViewKeys.CommandPreview, debugViewer, SelectionSetKeys.CommandPreview);
            }
        }

        private void ApplySelectionViewChoice(GameEngine engine, Entity playerViewer, Entity aiViewer, Entity debugViewer)
        {
            string choice = ResolveSelectionViewChoice(engine);
            switch (choice)
            {
                case ChampionSkillSandboxIds.PlayerFormationToolbarButtonId:
                    engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = playerViewer;
                    engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Formation;
                    break;

                case ChampionSkillSandboxIds.AiTargetToolbarButtonId:
                    if (aiViewer != Entity.Null)
                    {
                        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = aiViewer;
                        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
                    }
                    break;

                case ChampionSkillSandboxIds.AiFormationToolbarButtonId:
                    if (aiViewer != Entity.Null)
                    {
                        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = aiViewer;
                        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Formation;
                    }
                    break;

                case ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId:
                    if (debugViewer != Entity.Null)
                    {
                        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = debugViewer;
                        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.CommandPreview;
                    }
                    break;

                default:
                    engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = playerViewer;
                    engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
                    break;
            }
        }

        private void DrawStressSelectionOverlay(GameEngine engine, SelectionRuntime selection)
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

            if (!SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor current))
            {
                overlay.AddText(x + 16, y + 54, "No active selection view.", 15, SelectionPanelText, stableId: 42102, dirtySerial: 1);
                return;
            }

            string viewerLabel = ResolveEntityLabel(engine.World, current.Viewer) ?? $"Entity#{current.Viewer.Id}";
            string primaryLabel = ResolveEntityLabel(engine.World, current.Container.Primary) ?? "(none)";
            string members = BuildSelectionMemberPreview(engine.World, selection, current.Container.Container);

            overlay.AddText(x + 16, y + 54, $"View {ChampionSkillSandboxIds.ResolveSelectionViewLabel(ResolveSelectionViewChoice(engine))} | viewer={viewerLabel} | key={current.ViewKey}", 15, SelectionPanelText, stableId: 42103, dirtySerial: 1);
            overlay.AddText(x + 16, y + 78, $"Container {current.Container.AliasKey} | kind={current.Container.Kind} | rev={current.Container.Revision} | count={current.Container.MemberCount}", 14, SelectionPanelText, stableId: 42104, dirtySerial: 1);
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

        private static bool SeedInitialSelection(GameEngine engine)
        {
            SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
            Entity fallback = ResolveChampionEntity(engine, ChampionSkillSandboxIds.EzrealAlphaName);
            if (fallback == Entity.Null)
            {
                fallback = ResolveFirstControllableChampion(engine);
            }
            Entity owner = ResolveOrAssignLocalPlayer(engine, fallback);
            if (selection == null || owner == Entity.Null || !engine.World.IsAlive(owner))
            {
                return false;
            }

            if (selection.TryGetPrimary(owner, SelectionSetKeys.Ambient, out Entity selected) &&
                engine.World.IsAlive(selected))
            {
                selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.Ambient);
                engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
                engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
                return true;
            }

            if (fallback == Entity.Null)
            {
                return false;
            }

            Span<Entity> selectionBuffer = stackalloc Entity[1];
            selectionBuffer[0] = fallback;
            selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, selectionBuffer);
            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.Ambient);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            return true;
        }

        private static Entity ResolveOrAssignLocalPlayer(GameEngine engine, Entity fallback)
        {
            Entity local = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (engine.World.IsAlive(local))
            {
                return local;
            }

            Entity resolved = IsControllableChampion(engine, fallback)
                ? fallback
                : ResolveFirstControllableChampion(engine);
            if (resolved != Entity.Null)
            {
                engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = resolved;
            }

            return resolved;
        }

        private static void ApplyInitialTag(GameEngine engine, string entityName, string tagName)
        {
            Entity entity = ResolveChampionEntity(engine, entityName);
            if (entity == Entity.Null)
            {
                return;
            }

            if (!engine.World.Has<GameplayTagContainer>(entity))
            {
                engine.World.Add(entity, new GameplayTagContainer());
            }

            int tagId = TagRegistry.Register(tagName);
            ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
            if (!tags.HasTag(tagId))
            {
                tags.AddTag(tagId);
                engine.World.Set(entity, tags);
            }
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

            engine.GameSession.Camera.ActivateVirtualCamera(
                virtualCameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, definition.FollowTargetKind),
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
        }

        private static void SyncCameraFollow(GameEngine engine)
        {
            string followModeId = ResolveCameraFollowMode(engine);
            string activeCameraId = engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeCameraId))
            {
                return;
            }

            ICameraFollowTarget? followTarget = followModeId switch
            {
                var id when string.Equals(id, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.OrdinalIgnoreCase)
                    => CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, CameraFollowTargetKind.SelectedEntity),
                var id when string.Equals(id, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.OrdinalIgnoreCase)
                    => CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, CameraFollowTargetKind.SelectedGroup),
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
            SyncSelectionIndicator(engine, visible ? target : Entity.Null);
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
            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
            if (IsCommandPanelTarget(engine, selected))
            {
                return selected;
            }

            Entity local = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (IsControllableChampion(engine, local))
            {
                return local;
            }

            return Entity.Null;
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

        private static Entity ResolveLatestSelectionSnapshotContainer(GameEngine engine)
        {
            Entity bestContainer = Entity.Null;
            int bestOrderId = 0;

            if (engine.GetService(CoreServiceKeys.OrderQueue) is OrderQueue queue)
            {
                CollectLatestSelectionContainer(queue, ref bestOrderId, ref bestContainer);
            }

            engine.World.Query(in StressOrderBufferQuery, (Entity entity, ref Team team, ref MapEntity mapEntity, ref OrderBuffer orders) =>
            {
                ConsiderOrderSelection(orders.ActiveOrder.Order, orders.HasActive, ref bestOrderId, ref bestContainer);
                ConsiderOrderSelection(orders.PendingOrder.Order, orders.HasPending, ref bestOrderId, ref bestContainer);
                for (int i = 0; i < orders.QueuedCount; i++)
                {
                    ConsiderOrderSelection(orders.GetQueued(i).Order, include: true, ref bestOrderId, ref bestContainer);
                }
            });

            return bestContainer;
        }

        private static void CollectLatestSelectionContainer(OrderQueue queue, ref int bestOrderId, ref Entity bestContainer)
        {
            var liveContainers = new HashSet<Entity>();
            queue.CollectSelectionContainers(liveContainers);
            foreach (Entity container in liveContainers)
            {
                if (container != Entity.Null)
                {
                    bestContainer = container;
                }
            }
        }

        private static void ConsiderOrderSelection(in Order order, bool include, ref int bestOrderId, ref Entity bestContainer)
        {
            if (!include || !order.Args.Selection.HasContainer || order.Args.Selection.Container == Entity.Null)
            {
                return;
            }

            if (order.OrderId >= bestOrderId)
            {
                bestOrderId = order.OrderId;
                bestContainer = order.Args.Selection.Container;
            }
        }

        private static int CompareEntitiesByName(World world, Entity left, Entity right)
        {
            string leftName = ResolveEntityLabel(world, left) ?? string.Empty;
            string rightName = ResolveEntityLabel(world, right) ?? string.Empty;
            int byName = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.Id.CompareTo(right.Id);
        }

        private static string BuildSelectionMemberPreview(World world, SelectionRuntime selection, Entity container)
        {
            int count = selection.GetSelectionCount(container);
            if (count <= 0)
            {
                return "(empty)";
            }

            Entity[] members = new Entity[count];
            int written = selection.CopySelection(container, members);
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
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
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
            DestroySelectionIndicator(engine);
            DestroyHoverIndicator(engine);

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
            _selectionIndicatorTarget = Entity.Null;
            _hoverIndicatorTarget = Entity.Null;
            _aimHoverIndicatorTarget = Entity.Null;
            _scenarioTagsApplied = false;
            _initialSelectionApplied = false;
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
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.ResetCameraRequestKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.CameraFollowModeKey);
            engine.GlobalContext.Remove(ChampionSkillSandboxIds.SelectionViewChoiceKey);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewViewerEntity.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewKey.Name);
        }

        private void SyncSelectionIndicator(GameEngine engine, Entity target)
        {
            if (_selectionIndicatorTarget == target)
            {
                return;
            }

            DestroySelectionIndicator(engine);
            _selectionIndicatorTarget = target;
            if (target == Entity.Null)
            {
                return;
            }

            PresentationCommandBuffer? commands = engine.GetService(CoreServiceKeys.PresentationCommandBuffer);
            PerformerDefinitionRegistry? performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
            if (commands == null || performers == null)
            {
                return;
            }

            int definitionId = performers.GetId(ChampionSkillSandboxIds.SelectionIndicatorPerformerKey);
            if (definitionId <= 0)
            {
                throw new InvalidOperationException(
                    $"Performer '{ChampionSkillSandboxIds.SelectionIndicatorPerformerKey}' is required by ChampionSkillSandboxMod.");
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = definitionId,
                IdB = ChampionSkillSandboxIds.SelectionIndicatorScopeId,
                Source = target,
            });
        }

        private void DestroySelectionIndicator(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = ChampionSkillSandboxIds.SelectionIndicatorScopeId,
            });
        }

        private void SyncHoverIndicator(GameEngine engine)
        {
            SyncIndicator(
                engine,
                ResolveHoverIndicatorTarget(engine),
                ref _hoverIndicatorTarget,
                ChampionSkillSandboxIds.HoverIndicatorPerformerKey,
                ChampionSkillSandboxIds.HoverIndicatorScopeId);
        }

        private void SyncAimHoverIndicator(GameEngine engine)
        {
            SyncIndicator(
                engine,
                ResolveAimHoverIndicatorTarget(engine),
                ref _aimHoverIndicatorTarget,
                ChampionSkillSandboxIds.HoverIndicatorPerformerKey,
                ChampionSkillSandboxIds.AimHoverIndicatorScopeId);
        }

        private static Entity ResolveHoverIndicatorTarget(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var hoveredObj) ||
                hoveredObj is not Entity hovered ||
                hovered == Entity.Null ||
                !engine.World.IsAlive(hovered))
            {
                return Entity.Null;
            }

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
            if (selected == hovered)
            {
                return Entity.Null;
            }

            return hovered;
        }

        private static Entity ResolveAimHoverIndicatorTarget(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is not InputOrderMappingSystem mapping ||
                !mapping.IsAiming)
            {
                return Entity.Null;
            }

            Entity hovered = ResolveHoveredEntity(engine);
            if (hovered == Entity.Null)
            {
                return Entity.Null;
            }

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
            return selected == hovered ? Entity.Null : hovered;
        }

        private static Entity ResolveHoveredEntity(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var hoveredObj) ||
                hoveredObj is not Entity hovered ||
                hovered == Entity.Null ||
                !engine.World.IsAlive(hovered))
            {
                return Entity.Null;
            }

            return hovered;
        }

        private void DestroyHoverIndicator(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = ChampionSkillSandboxIds.HoverIndicatorScopeId,
            });
        }

        private void DestroyAimHoverIndicator(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = ChampionSkillSandboxIds.AimHoverIndicatorScopeId,
            });
        }

        private void SyncIndicator(
            GameEngine engine,
            Entity target,
            ref Entity currentTarget,
            string performerKey,
            int scopeId)
        {
            if (currentTarget == target)
            {
                return;
            }

            DestroyIndicator(engine, scopeId);
            currentTarget = target;
            if (target == Entity.Null)
            {
                return;
            }

            PresentationCommandBuffer? commands = engine.GetService(CoreServiceKeys.PresentationCommandBuffer);
            PerformerDefinitionRegistry? performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
            if (commands == null || performers == null)
            {
                return;
            }

            int definitionId = performers.GetId(performerKey);
            if (definitionId <= 0)
            {
                throw new InvalidOperationException(
                    $"Performer '{performerKey}' is required by ChampionSkillSandboxMod.");
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = definitionId,
                IdB = scopeId,
                Source = target,
            });
        }

        private static void DestroyIndicator(GameEngine engine, int scopeId)
        {
            if (engine.GetService(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = scopeId,
            });
        }
    }
}
