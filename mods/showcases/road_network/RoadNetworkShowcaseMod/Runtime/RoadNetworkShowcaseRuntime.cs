using Arch.Core;
using Ludots.Core.Components;
using System.Threading.Tasks;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Scripting;
using CoreInputMod.ViewMode;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.UI;
using Ludots.UI;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Config;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal sealed class RoadNetworkShowcaseRuntime
    {
        private string? _activeMapId;
        private readonly RoadNetworkShowcasePanelController _panelController;
        private readonly RoadNetworkShowcaseDebugLogWriter _debugLogWriter;

        public enum ShowcaseCommandPreset : byte
        {
            None = 0,
            LongHaulToRedCapital = 1,
            NorthFlankToNorthWatch = 2,
            SouthGuardToSouthWatch = 3,
        }

        public RoadNetworkShowcaseRuntime()
        {
            _panelController = new RoadNetworkShowcasePanelController(this);
            _debugLogWriter = new RoadNetworkShowcaseDebugLogWriter();
        }

        public NodeGraphBoard? ActiveBoard { get; private set; }
        public RoadNetworkScenarioDefinition? Scenario { get; private set; }
        public bool IsActive => ActiveBoard != null && Scenario != null;
        public string LastSubmitStatus { get; private set; } = "Road command ready. Right-click near a road or fort.";
        public string? LatestDebugSnapshotPath => _debugLogWriter.SnapshotPath;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!RoadNetworkShowcaseIds.IsShowcaseMap(mapId) ||
                engine.CurrentMapSession?.PrimaryBoard is not NodeGraphBoard board)
            {
                Unbind(engine);
                return Task.CompletedTask;
            }

            if (ReferenceEquals(board, ActiveBoard) && string.Equals(mapId, _activeMapId))
            {
                return Task.CompletedTask;
            }

            Unbind(engine);
            _activeMapId = mapId;
            ActiveBoard = board;
            Scenario = RoadNetworkScenarioDefinition.Create(board.LoadedChunksSource.ChunkSizeCm);
            engine.GlobalContext[RoadNetworkShowcaseIds.ScenarioServiceKey] = Scenario;
            engine.GlobalContext[RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey] = board.LoadedChunksSource;
            board.LoadedChunksSource.ChunkLoaded += HandleChunkLoaded;

            ApplyInitialPlayableCamera(engine);
            PrimeInitialChunkWindow(engine);
            EnsurePrimaryPlayerControl(engine);
            PublishLocalRoadColumnKnowledge(engine);
            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (RoadNetworkShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                Unbind(engine);
            }

            return Task.CompletedTask;
        }

        public void UpdateLoadedChunks(GameEngine engine)
        {
            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            EnsurePrimaryPlayerControl(engine);
            PublishLocalRoadColumnKnowledge(engine);

            if (engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out var statusObj) &&
                statusObj is string status &&
                !string.IsNullOrWhiteSpace(status))
            {
                LastSubmitStatus = status;
            }

            var target = engine.GameSession.Camera.State.TargetCm;
            ActiveBoard.LoadedChunksSource.Update(
                (int)target.X,
                (int)target.Y,
                Scenario.StreamingRadiusCm);
            RefreshPanel(engine);
        }

        public int LoadedChunkCount => ActiveBoard?.LoadedChunksSource.ActiveChunkKeys.Count ?? 0;
        public int LoadedNodeCount => ActiveBoard?.GraphRuntime.CurrentGraph.NodeCount ?? 0;

        public bool TryResetCamera(GameEngine engine)
        {
            if (engine == null || !IsActive)
            {
                return false;
            }

            ApplyInitialPlayableCamera(engine);
            LastSubmitStatus = "Camera reset to local staging view.";
            return true;
        }

        public bool TryFocusLandmark(GameEngine engine, RoadNetworkScenarioDefinition.RoadLandmarkId landmarkId, string status)
        {
            if (!IsActive || Scenario == null || !Scenario.TryGetLandmarkWorldCm(landmarkId, out Vector3 landmarkWorldCm))
            {
                return false;
            }

            ActivateTacticalCamera(engine);
            string cameraId = ResolveConfiguredVirtualCameraId(engine);
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = cameraId,
                TargetCm = new Vector2(landmarkWorldCm.X, landmarkWorldCm.Z)
            });
            engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
            LastSubmitStatus = status;
            RefreshPanel(engine);
            return true;
        }

        public bool TryReloadScenario(GameEngine engine)
        {
            if (engine == null || string.IsNullOrWhiteSpace(_activeMapId))
            {
                return false;
            }

            string mapId = _activeMapId;
            MapLaunchContext? launchContext = engine.CurrentMapSession?.LaunchContext
                ?? MapLaunchContext.Create(engine.MergedConfig.StartupSelectedPlayerId);
            engine.LoadMap(MapLoadRequest.FromMapId(mapId, launchContext));
            LastSubmitStatus = "Scenario reloaded.";
            RefreshPanel(engine);
            return true;
        }

        public bool TryRunPreset(GameEngine engine, ShowcaseCommandPreset preset)
        {
            if (engine == null ||
                !TryResolvePreset(engine, preset, out Entity actor, out Vector3 targetWorldCm, out string status))
            {
                return false;
            }

            if (engine.GetService(CoreServiceKeys.OrderQueue) is not OrderQueue orders ||
                !engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config ||
                !config.Constants.OrderTypeIds.TryGetValue("moveTo", out int moveToOrderTypeId) ||
                moveToOrderTypeId <= 0)
            {
                return false;
            }

            if (!TryResolveLocalPlayerId(engine, out int playerId))
            {
                return false;
            }

            var expander = new RoadMoveOrderExpander(engine.World, engine.GlobalContext, orders, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = new Order
            {
                OrderTypeId = moveToOrderTypeId,
                Actor = actor,
                PlayerId = playerId,
                SubmitMode = OrderSubmitMode.Immediate
            };
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = targetWorldCm;
            bool submitted = expander.TrySubmit(in order);
            if (submitted)
            {
                LastSubmitStatus = status;
            }

            RefreshPanel(engine);
            return submitted;
        }

        public RoadNetworkShowcasePanelState BuildPanelState(GameEngine engine)
        {
            return new RoadNetworkShowcasePanelStateBuilder(engine, this).Build();
        }

        private static void PublishLocalRoadColumnKnowledge(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
                viewerObj is not Entity viewer ||
                !engine.World.IsAlive(viewer) ||
                !engine.World.TryGet(viewer, out Team viewerTeam))
            {
                return;
            }

            KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new System.InvalidOperationException("Road Network showcase requires KnowledgeProjectionStore before publishing local road column knowledge.");
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            var empty = KnowledgeIdMask256.Empty;
            var query = new QueryDescription().WithAll<RoadColumnTag, Team, SelectionSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref RoadColumnTag roadColumn, ref Team team, ref SelectionSelectableTag selectable) =>
            {
                if (team.Id != viewerTeam.Id)
                {
                    return;
                }

                store.Upsert(
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

        private void ActivateTacticalCamera(GameEngine engine)
        {
            string cameraId = ResolveConfiguredVirtualCameraId(engine);
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out object? managerObj) &&
                managerObj is ViewModeManager viewModeManager &&
                TryResolveViewModeId(viewModeManager, cameraId, out string modeId))
            {
                viewModeManager.SwitchTo(modeId, applyCamera: false);
            }

            ClearPendingCameraRequests(engine);

            if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(cameraId, out VirtualCameraDefinition? definition) ||
                definition == null)
            {
                return;
            }

            engine.GameSession.Camera.ResetVirtualCameras();
            engine.GameSession.Camera.ActivateVirtualCamera(
                cameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(engine.World, engine.GlobalContext, definition.FollowTargetKind),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);
        }

        private static void ClearPendingCameraRequests(GameEngine engine)
        {
            engine.GlobalContext.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.CameraPoseRequest.Name);
        }

        private void ApplyInitialPlayableCamera(GameEngine engine)
        {
            ActivateTacticalCamera(engine);
            Vector2 target = ResolveInitialCameraTarget(engine);
            string cameraId = ResolveConfiguredVirtualCameraId(engine);
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = cameraId,
                TargetCm = target
            });
            engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
        }

        private void EnsurePrimaryPlayerControl(GameEngine engine)
        {
            Entity owner = ResolveLocalPlayerEntity(engine);
            if (owner == Entity.Null)
            {
                return;
            }

            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            if (engine.World.TryGet(owner, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
            {
                engine.GlobalContext[CoreServiceKeys.LocalPlayerId.Name] = playerOwner.PlayerId;
            }

            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is SelectionRuntime selection)
            {
                EnsureSelectionComponents(engine.World, owner, selection, engine.GlobalContext);
                if (ShouldSeedLivePrimarySelection(engine.World, selection, owner))
                {
                    Span<Entity> initialSelection = stackalloc Entity[1];
                    initialSelection[0] = owner;
                    selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, initialSelection);
                }
            }
        }

        private Vector2 ResolveInitialCameraTarget(GameEngine engine)
        {
            CameraConfig? configuredCamera = engine.CurrentMapSession?.MapConfig.DefaultCamera;
            if (configuredCamera?.TargetXCm.HasValue == true &&
                configuredCamera.TargetYCm.HasValue)
            {
                return new Vector2(configuredCamera.TargetXCm.Value, configuredCamera.TargetYCm.Value);
            }

            Entity owner = ResolveLocalPlayerEntity(engine);
            if (owner != Entity.Null &&
                engine.World.IsAlive(owner) &&
                engine.World.Has<WorldPositionCm>(owner))
            {
                return engine.World.Get<WorldPositionCm>(owner).Value.ToVector2();
            }

            if (Scenario != null &&
                Scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.WestGate, out Vector3 westGateWorldCm))
            {
                return new Vector2(westGateWorldCm.X, westGateWorldCm.Z);
            }

            return Vector2.Zero;
        }

        private static string ResolveConfiguredVirtualCameraId(GameEngine engine)
        {
            string? cameraId = engine.CurrentMapSession?.MapConfig.DefaultCamera?.VirtualCameraId;
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new InvalidOperationException("Road Network showcase requires map DefaultCamera.VirtualCameraId.");
            }

            return cameraId;
        }

        private static bool TryResolveViewModeId(ViewModeManager viewModeManager, string virtualCameraId, out string modeId)
        {
            for (int i = 0; i < viewModeManager.Modes.Count; i++)
            {
                ViewModeConfig mode = viewModeManager.Modes[i];
                if (string.Equals(mode.VirtualCameraId, virtualCameraId, StringComparison.Ordinal))
                {
                    modeId = mode.Id;
                    return true;
                }
            }

            modeId = string.Empty;
            return false;
        }

        private void PrimeInitialChunkWindow(GameEngine engine)
        {
            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            var target = engine.GameSession.Camera.State.TargetCm;
            ActiveBoard.LoadedChunksSource.Update(
                (int)target.X,
                (int)target.Y,
                Scenario.StreamingRadiusCm);

            foreach (long chunkKey in ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                HandleChunkLoaded(chunkKey);
            }
        }

        private static void EnsureSelectionComponents(World world, Entity owner, SelectionRuntime selection, System.Collections.Generic.Dictionary<string, object> globals)
        {
            if (!world.Has<SelectionDragState>(owner))
            {
                world.Add(owner, default(SelectionDragState));
            }

            selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
            globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        }

        private static bool ShouldSeedLivePrimarySelection(World world, SelectionRuntime selection, Entity owner)
        {
            if (!selection.TryGetSelectionEntity(owner, SelectionSetKeys.LivePrimary, out Entity container))
            {
                return true;
            }

            int count = selection.GetSelectionCount(container);
            if (count <= 0)
            {
                return true;
            }

            var selected = new Entity[count];
            int written = selection.CopySelection(container, selected);
            for (int i = 0; i < written; i++)
            {
                if (world.IsAlive(selected[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static Entity ResolveLocalPlayerEntity(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                localObj is Entity local &&
                engine.World.IsAlive(local))
            {
                return local;
            }

            Entity sessionLocal = engine.CurrentMapSession?.LocalPlayerEntity ?? Entity.Null;
            return sessionLocal != Entity.Null && engine.World.IsAlive(sessionLocal)
                ? sessionLocal
                : Entity.Null;
        }

        private static bool TryResolveLocalPlayerId(GameEngine engine, out int playerId)
        {
            playerId = 0;
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? localIdObj) &&
                localIdObj is int localId &&
                localId > 0)
            {
                playerId = localId;
                return true;
            }

            playerId = engine.CurrentMapSession?.LocalPlayerId ?? 0;
            return playerId > 0;
        }

        private void HandleChunkLoaded(long chunkKey)
        {
            if (ActiveBoard == null || Scenario == null)
            {
                return;
            }

            if (Scenario.TryGetGraphChunk(chunkKey, out GraphChunkData chunk))
            {
                ActiveBoard.GraphStore.AddOrReplace(chunkKey, chunk);
            }
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            RoadNetworkShowcasePanelState state = BuildPanelState(engine);
            _panelController.MountOrSync(root, engine, state);
            _debugLogWriter.WriteLatest(state);
        }

        private void Unbind(GameEngine? engine = null)
        {
            if (engine?.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            if (ActiveBoard != null)
            {
                ActiveBoard.LoadedChunksSource.ChunkLoaded -= HandleChunkLoaded;
                ActiveBoard.LoadedChunksSource.Reset();
            }

            if (engine != null)
            {
                engine.GlobalContext.Remove(RoadNetworkShowcaseIds.ScenarioServiceKey);
                engine.GlobalContext.Remove(RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey);
            }

            _activeMapId = null;
            ActiveBoard = null;
            Scenario = null;
            LastSubmitStatus = "Road command ready. Right-click near a road or fort.";
        }

        private bool TryResolvePreset(GameEngine engine, ShowcaseCommandPreset preset, out Entity actor, out Vector3 targetWorldCm, out string status)
        {
            actor = ResolveCurrentActor(engine);
            targetWorldCm = default;
            status = string.Empty;
            if (actor == Entity.Null || !engine.World.IsAlive(actor) || Scenario == null)
            {
                return false;
            }

            return preset switch
            {
                ShowcaseCommandPreset.LongHaulToRedCapital => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.RedCapital,
                    "Preset launched: Long Haul to Red Capital.",
                    out targetWorldCm,
                    out status),
                ShowcaseCommandPreset.NorthFlankToNorthWatch => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.NorthWatch,
                    "Preset launched: North Flank to North Watch.",
                    out targetWorldCm,
                    out status),
                ShowcaseCommandPreset.SouthGuardToSouthWatch => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.SouthWatch,
                    "Preset launched: South Guard to South Watch.",
                    out targetWorldCm,
                    out status),
                _ => false,
            };
        }

        private bool ResolvePresetTarget(
            RoadNetworkScenarioDefinition.RoadLandmarkId landmarkId,
            string status,
            out Vector3 targetWorldCm,
            out string resolvedStatus)
        {
            targetWorldCm = default;
            resolvedStatus = status;
            return Scenario != null && Scenario.TryGetLandmarkWorldCm(landmarkId, out targetWorldCm);
        }

        private Entity ResolveCurrentActor(GameEngine engine)
        {
            if (SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                engine.World.IsAlive(selected))
            {
                return selected;
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? actorObj) &&
                actorObj is Entity actor &&
                engine.World.IsAlive(actor))
            {
                return actor;
            }

            return ResolveLocalPlayerEntity(engine);
        }

        private static string DescribeActor(GameEngine engine, Entity actor)
        {
            string name = engine.World.Has<Name>(actor) ? engine.World.Get<Name>(actor).Value : $"#{actor.Id}";
            if (!engine.World.Has<WorldPositionCm>(actor))
            {
                return name;
            }

            var worldCm = engine.World.Get<WorldPositionCm>(actor).Value;
            return $"{name} ({worldCm.X.ToFloat():0},{worldCm.Y.ToFloat():0})";
        }
    }
}
