using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillSandboxRuntime
    {
        private const string HideRuntimeHudKey = "DiagnosticsOverlay.HideRuntimeHud";
        private static readonly QueryDescription StressSelectableQuery = new QueryDescription().WithAll<Name, Team, MapEntity, AbilityStateBuffer>();
        private static readonly QueryDescription StressOrderBufferQuery = new QueryDescription().WithAll<Team, MapEntity, OrderBuffer>();
        private static readonly Vector4 SelectionPanelFill = new(0.05f, 0.08f, 0.11f, 0.88f);
        private static readonly Vector4 SelectionPanelBorder = new(0.41f, 0.74f, 0.89f, 0.95f);
        private static readonly Vector4 SelectionPanelTitle = new(0.94f, 0.83f, 0.47f, 1f);
        private static readonly Vector4 SelectionPanelText = new(0.90f, 0.94f, 0.98f, 1f);
        private static readonly Vector4 SelectionPanelHint = new(0.70f, 0.78f, 0.86f, 1f);
        private static readonly Vector4 SandboxGuideFill = new(0.04f, 0.07f, 0.10f, 0.90f);
        private static readonly Vector4 SandboxGuideBorder = new(0.48f, 0.80f, 0.98f, 0.96f);
        private static readonly Vector4 SandboxGuideTitle = new(0.98f, 0.90f, 0.58f, 1f);
        private static readonly Vector4 SandboxGuideText = new(0.92f, 0.96f, 0.99f, 1f);
        private static readonly Vector4 SandboxGuideHint = new(0.74f, 0.84f, 0.92f, 1f);
        private static readonly Vector4 SandboxGuideAccent = new(0.60f, 0.90f, 0.54f, 1f);
        private static readonly Vector4 SandboxGuideWarn = new(1.00f, 0.72f, 0.52f, 1f);
        private static readonly Vector4 ShowcaseLaneFill = new(0.80f, 0.92f, 0.54f, 0.08f);
        private static readonly Vector4 ShowcaseLaneBorder = new(0.96f, 1.00f, 0.72f, 0.66f);
        private static readonly Vector4 StepInPreviewFill = new(0.36f, 0.90f, 1.00f, 0.12f);
        private static readonly Vector4 StepInPreviewBorder = new(0.62f, 0.96f, 1.00f, 0.92f);
        private static readonly Vector4 ChainPreviewFill = new(1.00f, 0.76f, 0.34f, 0.10f);
        private static readonly Vector4 ChainPreviewBorder = new(1.00f, 0.88f, 0.58f, 0.92f);
        private static readonly Vector4 SweepPreviewFill = new(0.60f, 0.94f, 0.52f, 0.12f);
        private static readonly Vector4 SweepPreviewBorder = new(0.76f, 0.98f, 0.70f, 0.92f);
        private static readonly Vector4 BreakerPreviewFill = new(1.00f, 0.44f, 0.46f, 0.12f);
        private static readonly Vector4 BreakerPreviewBorder = new(1.00f, 0.72f, 0.74f, 0.94f);

        private EntityCommandPanelHandle _focusPanelHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastPanelTarget = Entity.Null;
        private Entity _selectionIndicatorTarget = Entity.Null;
        private Entity _hoverIndicatorTarget = Entity.Null;
        private Entity _aimHoverIndicatorTarget = Entity.Null;
        private Entity _resolvedIndicatorTarget = Entity.Null;
        private Entity _teamBViewer = Entity.Null;
        private Entity _debugViewer = Entity.Null;
        private string _lastMapId = string.Empty;
        private string _lastCameraFollowMode = string.Empty;
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
            SyncDiagnosticsHudPreference(engine);
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
            SyncDiagnosticsHudPreference(engine);
            SyncSelectionViews(engine);
            ConsumeResetCameraRequest(engine);
            SyncCameraFollow(engine);
            SyncFocusPanel(engine);
            SyncHoverIndicator(engine);
            SyncAimHoverIndicator(engine);
            SyncResolvedContextIndicator(engine);
            DrawDuelistPreviewOverlays(engine);
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
                engine.GlobalContext[ChampionSkillSandboxIds.CameraFollowModeKey] = ChampionSkillSandboxIds.FollowSelectionToolbarButtonId;
            }

            if (!engine.GlobalContext.ContainsKey(ChampionSkillSandboxIds.SelectionViewChoiceKey))
            {
                engine.GlobalContext[ChampionSkillSandboxIds.SelectionViewChoiceKey] = ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId;
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
                DrawSandboxGuideOverlay(engine);
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
            Entity fallback = ResolveChampionEntity(engine, ChampionSkillSandboxIds.DuelistAlphaName);
            if (fallback == Entity.Null)
            {
                fallback = ResolveChampionEntity(engine, ChampionSkillSandboxIds.EzrealAlphaName);
            }
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
                ViewModeRuntime.TrySwitchTo(engine.GlobalContext, ChampionSkillSandboxIds.ActionModeId);
                activeModeId = ChampionSkillSandboxIds.ActionModeId;
            }

            if (engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is InputOrderMappingSystem mapping &&
                TryResolveInteractionMode(activeModeId, out InteractionModeType interactionMode) &&
                mapping.InteractionMode != interactionMode)
            {
                mapping.SetInteractionMode(interactionMode);
            }
        }

        private static bool TryResolveInteractionMode(string activeModeId, out InteractionModeType interactionMode)
        {
            if (string.Equals(activeModeId, ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase))
            {
                interactionMode = InteractionModeType.ContextScored;
                return true;
            }

            if (string.Equals(activeModeId, ChampionSkillSandboxIds.IndicatorModeId, StringComparison.OrdinalIgnoreCase))
            {
                interactionMode = InteractionModeType.SmartCastWithIndicator;
                return true;
            }

            if (string.Equals(activeModeId, ChampionSkillSandboxIds.PressReleaseModeId, StringComparison.OrdinalIgnoreCase))
            {
                interactionMode = InteractionModeType.PressReleaseAimCast;
                return true;
            }

            if (string.Equals(activeModeId, ChampionSkillSandboxIds.SmartCastModeId, StringComparison.OrdinalIgnoreCase))
            {
                interactionMode = InteractionModeType.SmartCast;
                return true;
            }

            interactionMode = default;
            return false;
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

        private void SyncCameraFollow(GameEngine engine)
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

            bool snap = !string.Equals(_lastCameraFollowMode, followModeId, StringComparison.OrdinalIgnoreCase);
            _lastCameraFollowMode = followModeId;
            engine.GameSession.Camera.SetFollowTarget(activeCameraId, followTarget, snapToFollowTargetWhenAvailable: snap);
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
                    Anchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 18f),
                    Size = new EntityCommandPanelSize(460f, 276f),
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

            service.SetVisible(_focusPanelHandle, visible);
            SyncSelectionIndicator(engine, visible ? target : Entity.Null);
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
            DestroyAimHoverIndicator(engine);
            DestroyResolvedContextIndicator(engine);

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
            _resolvedIndicatorTarget = Entity.Null;
            _scenarioTagsApplied = false;
            _initialSelectionApplied = false;
            _lastMapId = string.Empty;
            _lastCameraFollowMode = string.Empty;
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
            engine.GlobalContext.Remove(HideRuntimeHudKey);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewViewerEntity.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewKey.Name);
        }

        private static void SyncDiagnosticsHudPreference(GameEngine engine)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                engine.GlobalContext.Remove(HideRuntimeHudKey);
                return;
            }

            if (ChampionSkillSandboxIds.IsStressMap(mapId))
            {
                engine.GlobalContext.Remove(HideRuntimeHudKey);
                return;
            }

            if (ChampionSkillSandboxIds.IsSandboxMap(mapId))
            {
                engine.GlobalContext[HideRuntimeHudKey] = true;
            }
        }

        private void DrawSandboxGuideOverlay(GameEngine engine)
        {
            ScreenOverlayBuffer? overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            if (overlay == null)
            {
                return;
            }

            string selectedName = GetSelectedEntityName(engine);
            string hoveredName = GetHoveredEntityName(engine);
            string modeLabel = ResolveModeLabel(engine);

            overlay.AddRect(16, 18, 560, 156, SandboxGuideFill, SandboxGuideBorder, stableId: 43100, dirtySerial: 1);
            overlay.AddText(32, 42, "Melee Context Showcase", 22, SandboxGuideTitle, stableId: 43101, dirtySerial: 1);
            overlay.AddText(32, 68, $"Selected {selectedName} | Mode {modeLabel} | Hover {hoveredName}", 14, SandboxGuideText, stableId: 43102, dirtySerial: 1);

            if (!string.Equals(selectedName, ChampionSkillSandboxIds.DuelistAlphaName, StringComparison.OrdinalIgnoreCase))
            {
                overlay.AddText(32, 96, "1. Click Duelist Alpha. The green ground pips mark the melee lane and the D/E/F starter pack.", 14, SandboxGuideWarn, stableId: 43104, dirtySerial: 1);
                overlay.AddText(32, 122, "2. Hover a dummy and tap Space for auto melee. Q forces the manual three-hit chain. E clears the pack.", 14, SandboxGuideHint, stableId: 43105, dirtySerial: 1);
                return;
            }

            if (!string.Equals(GetActiveModeId(engine), ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase))
            {
                overlay.AddText(32, 96, "Press F5 to return to Auto mode. That's the one-button melee route for this sandbox.", 14, SandboxGuideWarn, stableId: 43107, dirtySerial: 1);
                overlay.AddText(32, 122, "Orange ring = your cursor target. White ring = the body Space will actually commit to.", 14, SandboxGuideHint, stableId: 43108, dirtySerial: 1);
                return;
            }

            BuildDuelistGuideLines(engine, out string headline, out string instruction, out string comboLine);
            overlay.AddText(32, 96, headline, 15, SandboxGuideAccent, stableId: 43110, dirtySerial: 1);
            overlay.AddText(32, 122, instruction, 14, SandboxGuideText, stableId: 43111, dirtySerial: 1);
            overlay.AddText(32, 148, comboLine, 14, SandboxGuideHint, stableId: 43112, dirtySerial: 1);
        }

        private void BuildDuelistGuideLines(GameEngine engine, out string headline, out string instruction, out string comboLine)
        {
            headline = "Space = auto melee. Hover the D/E/F pack and tap it.";
            instruction = "Orange ring = your cursor target. White ring = the body Space will really jump to.";
            comboLine = "Space reads the fight for you: far = Step In, close = chain, opened = breaker. Q still forces Q1 -> Q2 -> Q3.";

            int actionContextAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.ActionContext");
            Span<ContextScoredCandidateProbe> probes = stackalloc ContextScoredCandidateProbe[8];
            if (!ChampionSkillSandboxDuelistContextInspector.TryInspect(
                    engine,
                    actionContextAbilityId,
                    probes,
                    out Entity actor,
                    out Entity hovered,
                    out ContextGroupDefinition group,
                    out int probeCount,
                    out ContextScoredOrderResolution resolution))
            {
                return;
            }

            string targetName = ResolveEntityLabel(engine.World, resolution.Target) ?? "(none)";
            int resolvedAbilityId = probeCount > 0 ? probes[0].AbilityId : 0;
            string resolvedAbilityName = ResolveAbilityDisplayName(engine, resolvedAbilityId);
            string actorState = ResolveDuelistActorState(engine.World, actor);
            string targetState = ResolveDuelistTargetState(engine.World, resolution.Target != Entity.Null ? resolution.Target : hovered);
            string hoveredName = ResolveEntityLabel(engine.World, hovered) ?? "(none)";

            headline = $"{resolvedAbilityName} -> {targetName}";
            instruction = BuildDuelistGuideInstruction(hoveredName, targetName, resolvedAbilityId, actorState, targetState);
            comboLine = BuildDuelistComboPrompt(actorState, targetState, resolvedAbilityId);
        }

        private static string BuildProbeSummary(GameEngine engine, Span<ContextScoredCandidateProbe> probes, int probeCount)
        {
            if (probeCount <= 0)
            {
                return "none";
            }

            int count = Math.Min(3, probeCount);
            var labels = new string[count];
            for (int i = 0; i < count; i++)
            {
                ContextScoredCandidateProbe probe = probes[i];
                string abilityName = ResolveAbilityDisplayName(engine, probe.AbilityId);
                string targetName = ResolveEntityLabel(engine.World, probe.Target) ?? "(none)";
                labels[i] = $"{abilityName}->{targetName} ({probe.Score:0.#})";
            }

            return string.Join(" | ", labels);
        }

        private static string ReadEntityName(World world, Entity entity)
        {
            return ResolveEntityLabel(world, entity) ?? "(none)";
        }

        private static string BuildDuelistResolutionReason(int abilityId)
        {
            int stage1AbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage1");
            int stage2AbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage2");
            int breakerAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.OpeningBreaker");
            int stepInAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn");
            int crowdSweepAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep");

            if (abilityId == stepInAbilityId)
            {
                return "Target group is outside jab reach, so the lunge wins to close distance and keep the combo alive.";
            }

            if (abilityId == stage1AbilityId)
            {
                return "No combo window is active yet, so the opener wins once a single enemy is inside jab range.";
            }

            if (abilityId == stage2AbilityId)
            {
                return "Stage1 is primed on the Duelist, so the second jab outranks the opener on the engaged target.";
            }

            if (abilityId == breakerAbilityId)
            {
                return "The target is opened, so the heavy finisher outranks the lighter chain hits.";
            }

            if (abilityId == crowdSweepAbilityId)
            {
                return "No single-target finisher outranked the cone, so the crowd answer wins against the front arc.";
            }

            return "Context scoring found a legal strike, but the authored reason text for this ability is missing.";
        }

        private static string BuildDuelistGuideInstruction(
            string hoveredName,
            string targetName,
            int abilityId,
            string actorState,
            string targetState)
        {
            if (!string.Equals(hoveredName, targetName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(hoveredName, "(none)", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetName, "(none)", StringComparison.OrdinalIgnoreCase))
            {
                return $"Your cursor is on {hoveredName}, but Space will snap to {targetName}. That's the stronger commit right now.";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn"))
            {
                return "You are still outside jab range, so the auto route opens with Step In.";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep"))
            {
                return "Several bodies are stacked in front, so the pack read flips to Crowd Sweep.";
            }

            if (string.Equals(targetState, "opened", StringComparison.OrdinalIgnoreCase))
            {
                return "That target is opened up. Space will cash out with the heavy finisher now.";
            }

            if (string.Equals(actorState, "stage2 primed", StringComparison.OrdinalIgnoreCase))
            {
                return "The chain is already deep. One more good read turns into the finisher.";
            }

            if (string.Equals(actorState, "stage1 primed", StringComparison.OrdinalIgnoreCase))
            {
                return "The auto route is already mid-chain. Stay on target and it will keep pressing the combo.";
            }

            return "You are in the neutral beat. Hover a dummy and Space chooses the safest first melee hit for you.";
        }

        private static string BuildDuelistComboPrompt(
            string actorState,
            string targetState,
            int abilityId)
        {
            if (string.Equals(targetState, "opened", StringComparison.OrdinalIgnoreCase))
            {
                return "Two exits are live: Space auto-finishes with Opening Breaker, while Q keeps the manual chain route moving.";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep"))
            {
                return "Pack read: keep two or more dummies in front and Space or E should sweep them together.";
            }

            if (string.Equals(actorState, "stage2 primed", StringComparison.OrdinalIgnoreCase))
            {
                return "Manual route: Q ends the three-hit chain. Auto route: Space looks for the heavy breaker once the opening appears.";
            }

            if (string.Equals(actorState, "stage1 primed", StringComparison.OrdinalIgnoreCase))
            {
                return "Manual route: stay on the same dummy and Q goes to Chain Jab II. Auto route: Space keeps the combo rolling.";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn"))
            {
                return "You are still outside jab range, so Space opens with Step In before the chain starts.";
            }

            return "Space handles the first read for you: lunge in from range, then stay in and keep the pressure on one dummy.";
        }

        private static string ResolveDuelistBeatLabel(string actorState, string targetState, int abilityId)
        {
            if (string.Equals(targetState, "opened", StringComparison.OrdinalIgnoreCase) ||
                abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.OpeningBreaker"))
            {
                return "finisher";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep"))
            {
                return "crowd";
            }

            if (string.Equals(actorState, "stage2 primed", StringComparison.OrdinalIgnoreCase) ||
                abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage2"))
            {
                return "chain 2";
            }

            if (string.Equals(actorState, "stage1 primed", StringComparison.OrdinalIgnoreCase) ||
                abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage1"))
            {
                return "chain 1";
            }

            if (abilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn"))
            {
                return "gap close";
            }

            return "starter";
        }

        private static string ResolveDuelistActorState(World world, Entity actor)
        {
            if (actor == Entity.Null)
            {
                return "none";
            }

            int stage2TagId = TagRegistry.GetId("State.Champion.Duelist.Combo.Stage2");
            int stage1TagId = TagRegistry.GetId("State.Champion.Duelist.Combo.Stage1");
            if (HasTag(world, actor, stage2TagId))
            {
                return "stage2 primed";
            }

            if (HasTag(world, actor, stage1TagId))
            {
                return "stage1 primed";
            }

            return "neutral";
        }

        private static string ResolveDuelistTargetState(World world, Entity target)
        {
            if (target == Entity.Null)
            {
                return "none";
            }

            int openedTagId = TagRegistry.GetId("State.Champion.Duelist.Target.Opened");
            int primedTagId = TagRegistry.GetId("State.Champion.Duelist.Target.ComboPrimed");
            if (HasTag(world, target, openedTagId))
            {
                return "opened";
            }

            if (HasTag(world, target, primedTagId))
            {
                return "combo primed";
            }

            return "neutral";
        }

        private static bool HasTag(World world, Entity entity, int tagId)
        {
            return entity != Entity.Null &&
                   tagId > 0 &&
                   world.IsAlive(entity) &&
                   world.TryGet(entity, out GameplayTagContainer tags) &&
                   tags.HasTag(tagId);
        }

        private static string ResolveAbilityDisplayName(GameEngine engine, int abilityId)
        {
            if (abilityId <= 0 ||
                engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry) is not AbilityDefinitionRegistry abilities ||
                !abilities.TryGet(abilityId, out AbilityDefinition definition))
            {
                return "Unknown";
            }

            if (definition.HasPresentation && definition.Presentation != null)
            {
                return definition.Presentation.ResolveDisplayName($"Ability#{abilityId}");
            }

            return $"Ability#{abilityId}";
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected)
                ? ResolveEntityLabel(engine.World, selected) ?? "(none)"
                : "(none)";
        }

        private static string GetHoveredEntityName(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var hoveredObj) &&
                hoveredObj is Entity hovered &&
                hovered != Entity.Null)
            {
                return ResolveEntityLabel(engine.World, hovered) ?? "(none)";
            }

            return "(none)";
        }

        private static string ResolveModeLabel(GameEngine engine)
        {
            return GetActiveModeId(engine) switch
            {
                var id when string.Equals(id, ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase) => "Auto",
                var id when string.Equals(id, ChampionSkillSandboxIds.IndicatorModeId, StringComparison.OrdinalIgnoreCase) => "Preview",
                var id when string.Equals(id, ChampionSkillSandboxIds.PressReleaseModeId, StringComparison.OrdinalIgnoreCase) => "Confirm",
                _ => "Quick",
            };
        }

        private static string GetActiveModeId(GameEngine engine)
        {
            return ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId) &&
                   !string.IsNullOrWhiteSpace(activeModeId)
                ? activeModeId
                : ChampionSkillSandboxIds.ActionModeId;
        }

        private void DrawDuelistPreviewOverlays(GameEngine engine)
        {
            if (ChampionSkillSandboxIds.IsStressMap(engine.CurrentMapSession?.MapId.Value) ||
                engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer overlays)
            {
                return;
            }

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
            bool duelistSelected = string.Equals(
                ResolveEntityLabel(engine.World, selected),
                ChampionSkillSandboxIds.DuelistAlphaName,
                StringComparison.OrdinalIgnoreCase);
            bool actionMode = string.Equals(GetActiveModeId(engine), ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase);
            if (!duelistSelected || !actionMode)
            {
                DrawShowcaseLaneMarkers(engine.World, overlays);
                return;
            }

            int actionContextAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.ActionContext");
            Span<ContextScoredCandidateProbe> probes = stackalloc ContextScoredCandidateProbe[8];
            if (!ChampionSkillSandboxDuelistContextInspector.TryInspect(
                    engine,
                    actionContextAbilityId,
                    probes,
                    out Entity actor,
                    out Entity hovered,
                    out _,
                    out int probeCount,
                    out ContextScoredOrderResolution resolution) ||
                actor == Entity.Null ||
                probeCount <= 0 ||
                !TryGetGroundCenter(engine.World, actor, out Vector3 actorCenter))
            {
                return;
            }

            if (resolution.Target != Entity.Null)
            {
                AddGroundCircle(overlays, engine.World, resolution.Target, 0.72f, StepInPreviewFill, StepInPreviewBorder);
            }

            int resolvedAbilityId = probes[0].AbilityId;
            Vector3 previewTarget = actorCenter;
            if (resolution.Target != Entity.Null && TryGetGroundCenter(engine.World, resolution.Target, out Vector3 targetCenter))
            {
                previewTarget = targetCenter;
            }
            else if (hovered != Entity.Null && TryGetGroundCenter(engine.World, hovered, out Vector3 hoveredCenter))
            {
                previewTarget = hoveredCenter;
            }

            float rotation = MathF.Atan2(previewTarget.Z - actorCenter.Z, previewTarget.X - actorCenter.X);
            float distanceMeters = MathF.Max(
                0.1f,
                Vector2.Distance(
                    new Vector2(actorCenter.X, actorCenter.Z),
                    new Vector2(previewTarget.X, previewTarget.Z)));

            if (resolvedAbilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep"))
            {
                overlays.TryAdd(new GroundOverlayItem
                {
                    Shape = GroundOverlayShape.Cone,
                    Center = actorCenter,
                    Radius = 2.4f,
                    Angle = 1.08f,
                    Rotation = rotation,
                    FillColor = SweepPreviewFill,
                    BorderColor = SweepPreviewBorder,
                    BorderWidth = 0.03f
                });
                return;
            }

            Vector4 fillColor = ChainPreviewFill;
            Vector4 borderColor = ChainPreviewBorder;
            float widthMeters = 0.34f;
            if (resolvedAbilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn"))
            {
                fillColor = StepInPreviewFill;
                borderColor = StepInPreviewBorder;
                widthMeters = 0.42f;
            }
            else if (resolvedAbilityId == AbilityIdRegistry.GetId("Ability.Champion.Duelist.OpeningBreaker"))
            {
                fillColor = BreakerPreviewFill;
                borderColor = BreakerPreviewBorder;
                widthMeters = 0.40f;
            }

            overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = actorCenter,
                Length = distanceMeters,
                Width = widthMeters,
                Rotation = rotation,
                FillColor = fillColor,
                BorderColor = borderColor,
                BorderWidth = 0.03f
            });
        }

        private static void DrawShowcaseLaneMarkers(World world, GroundOverlayBuffer overlays)
        {
            AddGroundCircle(overlays, world, ChampionSkillSandboxIds.DuelistAlphaName, 0.78f, ShowcaseLaneFill, ShowcaseLaneBorder);
            AddGroundCircle(overlays, world, ChampionSkillSandboxIds.TargetDummyDName, 0.72f, ShowcaseLaneFill, ShowcaseLaneBorder);
            AddGroundCircle(overlays, world, ChampionSkillSandboxIds.TargetDummyEName, 0.72f, ShowcaseLaneFill, ShowcaseLaneBorder);
            AddGroundCircle(overlays, world, ChampionSkillSandboxIds.TargetDummyFName, 0.72f, ShowcaseLaneFill, ShowcaseLaneBorder);
        }

        private static void AddGroundCircle(
            GroundOverlayBuffer overlays,
            World world,
            string entityName,
            float radiusMeters,
            in Vector4 fillColor,
            in Vector4 borderColor)
        {
            Entity entity = FindEntityByName(world, entityName);
            if (entity != Entity.Null)
            {
                AddGroundCircle(overlays, world, entity, radiusMeters, fillColor, borderColor);
            }
        }

        private static void AddGroundCircle(
            GroundOverlayBuffer overlays,
            World world,
            Entity entity,
            float radiusMeters,
            in Vector4 fillColor,
            in Vector4 borderColor)
        {
            if (!TryGetGroundCenter(world, entity, out Vector3 center))
            {
                return;
            }

            overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = center,
                Radius = radiusMeters,
                FillColor = fillColor,
                BorderColor = borderColor,
                BorderWidth = 0.03f
            });
        }

        private static bool TryGetGroundCenter(World world, Entity entity, out Vector3 center)
        {
            center = default;
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.TryGet(entity, out WorldPositionCm positionCm))
            {
                return false;
            }

            Vector2 worldCm = positionCm.Value.ToVector2();
            center = new Vector3(WorldUnits.CmToM(worldCm.X), 0.08f, WorldUnits.CmToM(worldCm.Y));
            return true;
        }

        private static Entity FindEntityByName(World world, string expectedName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result != Entity.Null ||
                    !string.Equals(name.Value, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                result = entity;
            });
            return result;
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

        private void SyncResolvedContextIndicator(GameEngine engine)
        {
            SyncIndicator(
                engine,
                ResolveResolvedContextIndicatorTarget(engine),
                ref _resolvedIndicatorTarget,
                ChampionSkillSandboxIds.ResolvedIndicatorPerformerKey,
                ChampionSkillSandboxIds.ResolvedIndicatorScopeId);
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

            if (IsFriendlyHover(engine.World, selected, hovered))
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
            if (selected == hovered || IsFriendlyHover(engine.World, selected, hovered))
            {
                return Entity.Null;
            }

            return hovered;
        }

        private static Entity ResolveResolvedContextIndicatorTarget(GameEngine engine)
        {
            if (!string.Equals(GetActiveModeId(engine), ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase))
            {
                return Entity.Null;
            }

            int actionContextAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.ActionContext");
            if (actionContextAbilityId <= 0)
            {
                return Entity.Null;
            }

            Span<ContextScoredCandidateProbe> probes = stackalloc ContextScoredCandidateProbe[8];
            return ChampionSkillSandboxDuelistContextInspector.TryInspect(
                    engine,
                    actionContextAbilityId,
                    probes,
                    out _,
                    out _,
                    out _,
                    out _,
                    out ContextScoredOrderResolution resolution) &&
                   resolution.Target != Entity.Null &&
                   engine.World.IsAlive(resolution.Target)
                ? resolution.Target
                : Entity.Null;
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

        private static bool IsFriendlyHover(World world, Entity selected, Entity hovered)
        {
            return selected != Entity.Null &&
                   hovered != Entity.Null &&
                   world.IsAlive(selected) &&
                   world.IsAlive(hovered) &&
                   world.TryGet(selected, out Team selectedTeam) &&
                   world.TryGet(hovered, out Team hoveredTeam) &&
                   selectedTeam.Id == hoveredTeam.Id;
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

        private void DestroyResolvedContextIndicator(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = ChampionSkillSandboxIds.ResolvedIndicatorScopeId,
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
