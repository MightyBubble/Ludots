using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.MovePlanning;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.StructureCollision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Engine
{
    public partial class GameEngine
    {
        private const string MassNavigationMovePlanOrderAdapterInstalledKey =
            "GameEngine.MassNavigationMovePlanOrderAdapterInstalled";

        public MapSession CurrentMapSession { get; private set; }

        // Board-scoped query facades (#1567 slice 2): one shared world partition, per-board
        // semantics. Entities stay world-indexed; a board scope swaps in that board's
        // converter/hex metrics/extent, so hex satellites answer with their own topology.
        private readonly Dictionary<string, Ludots.Core.Spatial.SpatialQueryService> _boardScopedQueries =
            new(System.StringComparer.OrdinalIgnoreCase);

        public bool TryGetBoardScopedSpatialQueries(string boardName, out Ludots.Core.Spatial.SpatialQueryService service)
        {
            service = null!;
            MapSession? session = CurrentMapSession;
            IBoard? board = session?.GetBoard(boardName);
            if (board == null || _spatialPartition == null)
            {
                return false;
            }

            if (_boardScopedQueries.TryGetValue(board.Name, out service!))
            {
                return true;
            }

            service = new Ludots.Core.Spatial.SpatialQueryService(
                new Ludots.Core.Spatial.ChunkedGridSpatialPartitionBackend(_spatialPartition, board.WorldSize));
            service.SetCoordinateConverter(board.CoordinateConverter);
            if (board is HexGridBoard hexBoard)
            {
                service.SetHexMetrics(hexBoard.HexMetrics);
            }
            _boardScopedQueries[board.Name] = service;
            return true;
        }

        private readonly Dictionary<MapId, PendingMapLoadState> _pendingMapLoads = new();
        private readonly Dictionary<MapId, PendingMapResumeState> _pendingMapResumes = new();
        private readonly Dictionary<MapId, MapLoadStatus> _mapLoadStatuses = new();
        private VertexMapContinuousHeightmap? _vertexMapContinuousHeightmap;

        private sealed class PendingMapLoadState
        {
            public PendingMapLoadState(MapSession session, MapConfig mapConfig, IPendingMapLoad pendingLoad)
            {
                Session = session;
                MapConfig = mapConfig;
                PendingLoad = pendingLoad;
            }

            public MapSession Session { get; }
            public MapConfig MapConfig { get; }
            public IPendingMapLoad PendingLoad { get; }
        }

        private sealed class PendingMapResumeState
        {
            public PendingMapResumeState(MapSession session, MapSession? closedSession, IPendingMapLoad pendingLoad)
            {
                Session = session;
                ClosedSession = closedSession;
                PendingLoad = pendingLoad;
            }

            public MapSession Session { get; }
            public MapSession? ClosedSession { get; }
            public IPendingMapLoad PendingLoad { get; }
        }

        private MapSessionManager EnsureMapSessionInfrastructure()
        {
            MapSessionManager? mapSessions = MapSessions;
            if (mapSessions != null)
            {
                return mapSessions;
            }

            mapSessions = new MapSessionManager();
            MapSessions = mapSessions;
            TriggerManager.MapSessions = mapSessions;
            BoardIdRegistry = new BoardIdRegistry();
            SetService(CoreServiceKeys.MapSessions, mapSessions);
            SetService(CoreServiceKeys.BoardIdRegistry, BoardIdRegistry);
            EnsureSaveParticipantRegistry();
            return mapSessions;
        }

        private void EnsureSaveParticipantRegistry()
        {
            if (GetService(CoreServiceKeys.SaveParticipants) != null)
            {
                return;
            }

            if (GameSession == null ||
                MapSessions == null ||
                GetService(CoreServiceKeys.TimeFlow) == null ||
                GetService(CoreServiceKeys.TaskRuntimeService) == null ||
                GetService(CoreServiceKeys.DialogueRuntime) == null)
            {
                return;
            }

            var registry = new SaveParticipantRegistry();
            CoreSaveParticipants.RegisterCore(this, registry);
            SetService(CoreServiceKeys.SaveParticipants, registry);
            SetService(CoreServiceKeys.CheckpointCoordinator, new CheckpointCoordinator());
        }

        private void SetCurrentMapSession(MapSession session)
        {
            _boardScopedQueries.Clear();
            if (CurrentMapSession != null && !ReferenceEquals(CurrentMapSession, session) &&
                GetService(CoreServiceKeys.MapLoadCompletionGate) is IMapLoadCompletionGateLifetime gateLifetime)
            {
                gateLifetime.Release(CurrentMapSession);
            }

            CurrentMapSession = session;
            if (session == null)
            {
                if (TryGetService(CoreServiceKeys.LogicViewRegistry, out Client.LogicViewRegistry? views) &&
                    views != null)
                {
                    views.ResetAllVirtualCameras();
                }

                RemoveService(CoreServiceKeys.MapId);
                RemoveService(CoreServiceKeys.MapSession);
                RemoveService(CoreServiceKeys.MapFeatureFlags);
                RemoveService(CoreServiceKeys.MapLoadStatus);
                RemoveService(CoreServiceKeys.MapLaunchContext);
                RemoveService(CoreServiceKeys.ContinuousHeightmap);
                RemoveService(CoreServiceKeys.StructureCollisionAsset);
                RemoveService(CoreServiceKeys.StructureCollisionRuntimeState);
                RemoveService(CoreServiceKeys.GroundSurfaceSampler);
                ParticipantBindingResolver.ClearFocused(GlobalContext);
                PublishFocusedMapLoadState();
                return;
            }

            SetService(CoreServiceKeys.MapId, session.MapId);
            SetService(CoreServiceKeys.MapSession, session);
            SetService(CoreServiceKeys.MapFeatureFlags, MapFeatureFlags.FromTags(session.MapConfig?.Tags));
            SetService(CoreServiceKeys.MapLoadStatus, GetMapLoadStatus(session.MapId));
            if (session.LaunchContext != null && !session.LaunchContext.IsEmpty)
            {
                SetService(CoreServiceKeys.MapLaunchContext, session.LaunchContext);
            }
            else
            {
                RemoveService(CoreServiceKeys.MapLaunchContext);
            }
            IContinuousHeightmap? continuousHeightmap = ResolveSessionContinuousHeightmap(session);
            if (continuousHeightmap != null)
            {
                SetService(CoreServiceKeys.ContinuousHeightmap, continuousHeightmap);
            }
            else
            {
                RemoveService(CoreServiceKeys.ContinuousHeightmap);
            }
            if (session.StructureCollisionAsset != null)
            {
                session.StructureCollisionRuntimeState ??= new StructureCollisionRuntimeState(session.StructureCollisionAsset);
                session.GroundSurfaceSampler ??= new GroundSurfaceSampler(
                    continuousHeightmap,
                    session.StructureCollisionAsset,
                    session.StructureCollisionRuntimeState);
                SetService(CoreServiceKeys.StructureCollisionAsset, session.StructureCollisionAsset);
                SetService(CoreServiceKeys.StructureCollisionRuntimeState, session.StructureCollisionRuntimeState);
                SetService(CoreServiceKeys.GroundSurfaceSampler, session.GroundSurfaceSampler);
            }
            else
            {
                RemoveService(CoreServiceKeys.StructureCollisionAsset);
                RemoveService(CoreServiceKeys.StructureCollisionRuntimeState);
                if (continuousHeightmap != null)
                {
                    session.GroundSurfaceSampler ??= new GroundSurfaceSampler(continuousHeightmap, null, null);
                    SetService(CoreServiceKeys.GroundSurfaceSampler, session.GroundSurfaceSampler);
                }
                else
                {
                    RemoveService(CoreServiceKeys.GroundSurfaceSampler);
                }
            }
            PublishSessionParticipants(session);
            PublishFocusedMapLoadState();
        }

        internal void SetCurrentMapSessionForTests(MapSession session)
        {
            SetCurrentMapSession(session);
        }

        private IContinuousHeightmap? ResolveSessionContinuousHeightmap(MapSession session)
        {
            if (session.ContinuousHeightmap != null)
            {
                return session.ContinuousHeightmap;
            }

            // .height 是唯一权威视觉高度源；仅当会话未声明 .height 且引擎已持有 VertexMap 时用逻辑格点补高度服务。
            // 适配器按需读取当前 VertexMap，地图热切换不会绑定到上一张图。
            if (VertexMap != null)
            {
                _vertexMapContinuousHeightmap ??= new VertexMapContinuousHeightmap(() => VertexMap);
                return _vertexMapContinuousHeightmap;
            }

            return null;
        }

        private MapLoadStatus GetMapLoadStatus(MapId mapId)
        {
            return _mapLoadStatuses.TryGetValue(mapId, out MapLoadStatus status)
                ? status
                : MapLoadStatus.ImmediateSuccess;
        }

        private MapLoadStatus GetInitialMapLoadStatus()
        {
            return GetService(CoreServiceKeys.MapLoadCompletionGate) != null
                ? MapLoadStatus.DeferredPending
                : MapLoadStatus.ImmediateSuccess;
        }

        private void SetMapLoadStatus(MapId mapId, MapLoadStatus status)
        {
            _mapLoadStatuses[mapId] = status;
            if (CurrentMapSession != null && CurrentMapSession.MapId == mapId)
            {
                SetService(CoreServiceKeys.MapLoadStatus, status);
                PublishFocusedMapLoadState();
            }
        }

        private void PublishFocusedMapLoadState()
        {
            IFocusedMapLoadStateSink sink = GetService(CoreServiceKeys.FocusedMapLoadStateSink);
            if (sink == null)
            {
                return;
            }

            sink.OnFocusedMapChanged(new FocusedMapLoadState(
                CurrentMapSession,
                CurrentMapSession != null ? GetMapLoadStatus(CurrentMapSession.MapId) : MapLoadStatus.ImmediateSuccess,
                MapSessions?.HasPendingReturn ?? false));
        }

        private ScriptContext CreateMapEventContext(MapSession session)
        {
            ScriptContext ctx = CreateContext();
            ctx.Set(CoreServiceKeys.MapId, session.MapId);
            ctx.Set(CoreServiceKeys.MapSession, session);
            ctx.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new List<string>());
            ctx.Set(CoreServiceKeys.MapFeatureFlags, MapFeatureFlags.FromTags(session.MapConfig?.Tags));
            ctx.Set(CoreServiceKeys.MapLoadStatus, GetMapLoadStatus(session.MapId));
            if (session.LaunchContext != null && !session.LaunchContext.IsEmpty)
            {
                ctx.Set(CoreServiceKeys.MapLaunchContext, session.LaunchContext);
            }

            return ctx;
        }

        private void WireMapVariableChangedDispatcher(MapSession session)
        {
            Gameplay.MapTriggers.MapVariableStore? variables = session?.Variables;
            if (variables == null)
            {
                return;
            }

            // The closure is bound once per map load; same-value writes never reach it
            // and the subscriber check below keeps unwatched hot-path writes at zero
            // cost — no event context is even built.
            variables.VariableChangedDispatcher = (mapId, varName, type, oldInt, newInt, oldFloat, newFloat) =>
            {
                if (!TriggerManager.HasMapEventSubscribers(mapId, GameEvents.MapVariableChanged))
                {
                    return;
                }

                ScriptContext ctx = CreateMapEventContext(session!);
                ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyVarName, varName);
                if (type == Gameplay.MapTriggers.MapVariableType.Int)
                {
                    ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyOldValueInt, oldInt);
                    ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyNewValueInt, newInt);
                }
                else
                {
                    ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyOldValueFloat, oldFloat);
                    ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyNewValueFloat, newFloat);
                }

                CompleteLifecycleEvent(
                    TriggerManager.FireMapEventAsync(mapId, GameEvents.MapVariableChanged, ctx));
            };
        }

        private void RestoreFocusedMapSession(MapSession session)
        {
            SetCurrentMapSession(session);

            IBoard primaryBoard = session.PrimaryBoard;
            if (primaryBoard != null)
            {
                ApplyBoardSpatialConfig(primaryBoard);
                LoadBoardTerrainData(session, session.MapConfig);
                LoadNavForMap(session.MapId.Value, session.MapConfig);
            }

            LoadPathingForSession(session);
            SetMapEntitiesSuspended(session.MapId, GetMapLoadStatus(session.MapId).Succeeded ? false : true);
            PublishSessionParticipants(session);
        }

        private bool TryStartPendingMapLoad(MapSession session, MapConfig mapConfig, bool isPush, out MapLoadStatus loadStatus)
        {
            loadStatus = MapLoadStatus.ImmediateSuccess;

            IMapLoadCompletionGate gate = GetService(CoreServiceKeys.MapLoadCompletionGate);
            if (gate == null)
            {
                return false;
            }

            try
            {
                MapPresentationAssetManifest presentationAssets = MapLoader.BuildPresentationAssetManifest(mapConfig);
                IPendingMapLoad pendingLoad = gate.BeginPendingLoad(new MapLoadCompletionRequest(this, session.MapId, mapConfig, session, isPush, presentationAssets));
                if (pendingLoad == null)
                {
                    return false;
                }

                MapLoadCompletionResult initialResult = pendingLoad.Poll();
                if (initialResult.State == MapLoadCompletionState.Pending)
                {
                    _pendingMapLoads[session.MapId] = new PendingMapLoadState(session, mapConfig, pendingLoad);
                    SetMapLoadStatus(session.MapId, MapLoadStatus.FromCompletion(initialResult, isDeferred: true));
                    return true;
                }

                loadStatus = MapLoadStatus.FromCompletion(initialResult, isDeferred: true);
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"Map load completion gate failed for '{session.MapId.Value}': {ex.Message}");
                loadStatus = MapLoadStatus.DeferredFailure(ex.Message);
                return false;
            }
        }

        private bool TryStartPendingMapResume(MapSession session, MapSession? closedSession, out MapLoadStatus loadStatus)
        {
            loadStatus = MapLoadStatus.ImmediateSuccess;

            IMapLoadCompletionGate gate = GetService(CoreServiceKeys.MapLoadCompletionGate);
            if (gate == null)
            {
                return false;
            }

            try
            {
                MapPresentationAssetManifest presentationAssets = MapLoader.BuildPresentationAssetManifest(session.MapConfig);
                IPendingMapLoad pendingLoad = gate.BeginPendingResume(new MapResumeCompletionRequest(this, session, closedSession, presentationAssets));
                if (pendingLoad == null)
                {
                    return false;
                }

                MapLoadCompletionResult initialResult = pendingLoad.Poll();
                if (initialResult.State == MapLoadCompletionState.Pending)
                {
                    _pendingMapResumes[session.MapId] = new PendingMapResumeState(session, closedSession, pendingLoad);
                    SetMapLoadStatus(session.MapId, MapLoadStatus.FromCompletion(initialResult, isDeferred: true));
                    return true;
                }

                loadStatus = MapLoadStatus.FromCompletion(initialResult, isDeferred: true);
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"Map resume completion gate failed for '{session.MapId.Value}': {ex.Message}");
                loadStatus = MapLoadStatus.DeferredFailure(ex.Message);
                return false;
            }
        }

        private void ProcessPendingMapLoads()
        {
            if (_pendingMapLoads.Count > 0)
            {
                var snapshot = new List<KeyValuePair<MapId, PendingMapLoadState>>(_pendingMapLoads);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    KeyValuePair<MapId, PendingMapLoadState> pair = snapshot[i];
                    if (!_pendingMapLoads.TryGetValue(pair.Key, out PendingMapLoadState pendingState) || !ReferenceEquals(pendingState, pair.Value))
                    {
                        continue;
                    }

                    if (CurrentMapSession == null || CurrentMapSession.MapId != pair.Key)
                    {
                        CancelPendingMapLoad(pair.Key, $"Map load canceled because '{pair.Key.Value}' lost focus before completion.", markFailed: true);
                        continue;
                    }

                    MapSession session = MapSessions?.GetSession(pair.Key);
                    if (session == null)
                    {
                        CancelPendingMapLoad(pair.Key, $"Map session '{pair.Key.Value}' disappeared before completion.", markFailed: false);
                        continue;
                    }

                    MapLoadCompletionResult result;
                    try
                    {
                        result = pendingState.PendingLoad.Poll();
                    }
                    catch (Exception ex)
                    {
                        result = MapLoadCompletionResult.Failed(ex.Message);
                    }

                    if (result.State == MapLoadCompletionState.Pending)
                    {
                        SetMapLoadStatus(pair.Key, MapLoadStatus.FromCompletion(result, isDeferred: true));
                        continue;
                    }

                    _pendingMapLoads.Remove(pair.Key);
                    try
                    {
                        CompleteMapLoad(session, pendingState.MapConfig, MapLoadStatus.FromCompletion(result, isDeferred: true));
                    }
                    catch (Exception ex)
                    {
                        RecordNetworkStartupFailure(session.MapId, ex);
                        throw;
                    }
                }
            }

            if (_pendingMapResumes.Count == 0)
            {
                return;
            }

            var resumeSnapshot = new List<KeyValuePair<MapId, PendingMapResumeState>>(_pendingMapResumes);
            for (int i = 0; i < resumeSnapshot.Count; i++)
            {
                KeyValuePair<MapId, PendingMapResumeState> pair = resumeSnapshot[i];
                if (!_pendingMapResumes.TryGetValue(pair.Key, out PendingMapResumeState pendingState) || !ReferenceEquals(pendingState, pair.Value))
                {
                    continue;
                }

                if (CurrentMapSession == null || CurrentMapSession.MapId != pair.Key)
                {
                    CancelPendingMapResume(pair.Key, $"Map resume canceled because '{pair.Key.Value}' lost focus before completion.", markFailed: true);
                    continue;
                }

                MapSession session = MapSessions?.GetSession(pair.Key);
                if (session == null)
                {
                    CancelPendingMapResume(pair.Key, $"Map session '{pair.Key.Value}' disappeared before resume completion.", markFailed: false);
                    continue;
                }

                MapLoadCompletionResult result;
                try
                {
                    result = pendingState.PendingLoad.Poll();
                }
                catch (Exception ex)
                {
                    result = MapLoadCompletionResult.Failed(ex.Message);
                }

                if (result.State == MapLoadCompletionState.Pending)
                {
                    SetMapLoadStatus(pair.Key, MapLoadStatus.FromCompletion(result, isDeferred: true));
                    continue;
                }

                _pendingMapResumes.Remove(pair.Key);
                CompleteMapResume(session, MapLoadStatus.FromCompletion(result, isDeferred: true));
            }
        }

        private void CompleteMapLoad(MapSession session, MapConfig mapConfig, MapLoadStatus loadStatus)
        {
            SetMapLoadStatus(session.MapId, loadStatus);
            SetCurrentMapSession(session);

            if (loadStatus.Succeeded)
            {
                SetMapEntitiesSuspended(session.MapId, false);
                ApplyDefaultCamera(mapConfig);
                if (_massNavigationRuntime.HandleMapFocused(this, session.MapId))
                {
                    InstallMassNavigationMovePlanOrderAdapter();
                }
            }
            else
            {
                SetMapEntitiesSuspended(session.MapId, true);
                if (loadStatus.Failed)
                {
                    Diagnostics.Log.Warn(in LogChannels.Engine, $"Map '{session.MapId.Value}' completed with failure: {loadStatus.ErrorMessage}");
                    ThrowIfNetworkStartupMapLoadFailed(session.MapId, loadStatus.ErrorMessage);
                }

                return;
            }

            ScriptContext finalCtx = CreateMapEventContext(session);
            Diagnostics.Log.Info(in LogChannels.Engine, $"Firing MapLoaded event for {session.MapId.Value}...");
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(session.MapId, GameEvents.MapLoaded, finalCtx));
            CaptureFocusedParticipantOverrides(session);
            session.TeamRelationships = TeamManager.CaptureSnapshot();
            TryActivateNetworkRuntime();
        }

        private void SetSessionParticipants(MapSession session, ParticipantBindingResult participants)
        {
            session.TeamEntityLookup = participants.Teams;
            session.PlayerEntityLookup = participants.Players;
            session.LocalSeats = participants.LocalSeats;
            session.TeamRelationships = participants.TeamRelationships;

            SeedPlayerInteractionPrefs(participants.Players);

            if (CurrentMapSession == session)
            {
                ParticipantBindingResolver.PublishFocused(GlobalContext, participants);
            }
        }

        /// <summary>
        /// Plant the game-instance InteractionPref seed on every bound player representative that
        /// carries no component yet (player data survives map switches and world saves; seeding
        /// never overwrites an existing preference). Readers of the component fail fast on a
        /// missing seed, so this is the only writer at map binding time.
        /// </summary>
        private void SeedPlayerInteractionPrefs(PlayerEntityLookup players)
        {
            foreach (KeyValuePair<int, Entity> entry in players.Entries)
            {
                Entity rep = entry.Value;
                if (rep == Entity.Null || !World.IsAlive(rep) || World.Has<Input.Interaction.InteractionPref>(rep))
                {
                    continue;
                }

                World.Add(rep, Input.Interaction.InteractionPref.FromSeed(_interactionPrefSeed));
            }
        }

        private void PublishSessionParticipants(MapSession session)
        {
            if (session == null)
            {
                ParticipantBindingResolver.ClearFocused(GlobalContext);
                return;
            }

            ParticipantBindingResolver.PublishFocused(
                GlobalContext,
                new ParticipantBindingResult(
                    session.TeamEntityLookup,
                    session.PlayerEntityLookup,
                    session.LocalSeats,
                    session.TeamRelationships));
        }

        private void CaptureFocusedParticipantOverrides(MapSession session)
        {
            if (session == null || CurrentMapSession != session)
            {
                return;
            }

            if (!GlobalContext.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not Client.ClientLocalSeatRegistry seats)
            {
                return;
            }

            var snapshot = new List<ResolvedLocalSeatPossession>(seats.Count);
            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                Client.ClientLocalSeat seat = seats.Require(ids[i]);
                if (!seat.HasPossession)
                {
                    continue;
                }

                snapshot.Add(new ResolvedLocalSeatPossession(
                    seat.SeatId,
                    seat.PossessedPlayerId,
                    seat.PossessedRep,
                    seat.ControlSchemeId));
            }

            session.LocalSeats = snapshot;
        }

        private void CompleteMapResume(MapSession session, MapLoadStatus loadStatus)
        {
            SetMapLoadStatus(session.MapId, loadStatus);
            RestoreFocusedMapSession(session);

            if (!loadStatus.Succeeded)
            {
                if (loadStatus.Failed)
                {
                    Diagnostics.Log.Warn(in LogChannels.Engine, $"Map '{session.MapId.Value}' resume completed with failure: {loadStatus.ErrorMessage}");
                }

                return;
            }

            ApplyDefaultCamera(session.MapConfig);
            if (_massNavigationRuntime.HandleMapFocused(this, session.MapId))
            {
                InstallMassNavigationMovePlanOrderAdapter();
            }
            ScriptContext resumeCtx = CreateMapEventContext(session);
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(session.MapId, GameEvents.MapResumed, resumeCtx));
            CaptureFocusedParticipantOverrides(session);
            session.TeamRelationships = TeamManager.CaptureSnapshot();
        }

        /// <summary>
        /// MovePlan order adapter (Projection + Lifecycle) installs when the mass-navigation
        /// runtime activates on its configured mapId — the activation contract — so downstream
        /// mods never wire the adapter themselves. Lives in the engine composition layer because
        /// RFC-0065 keeps the MassNavigation execution domain free of order-type knowledge; the
        /// projection system anchors directly before the MovePlan execution system the runtime
        /// installed during activation.
        /// </summary>
        internal void InstallMassNavigationMovePlanOrderAdapter()
        {
            if (GlobalContext.ContainsKey(MassNavigationMovePlanOrderAdapterInstalledKey))
            {
                return;
            }

            OrderTypeRegistry orderTypes = GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException(
                    "MassNavigation map focus requires OrderTypeRegistry before installing the MovePlan order adapter.");
            if (!orderTypes.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
            {
                throw new InvalidOperationException(
                    $"MassNavigation map focus requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}' " +
                    "before the MovePlan order adapter can install.");
            }

            // Composite engage plans (MoveThenCast) and legacy profiles submit plain
            // moveTo orders; on mass-navigation maps those must flow into the same
            // nav move-plan pipeline (formation slots + arrival completion), so the
            // adapter accepts both order ids.
            orderTypes.TryGetId("moveTo", out int legacyMoveOrderTypeId);
            InsertSystemBeforeRequired<IMovePlanCommandGroupExecutionSystem>(
                new MovePlanOrderProjectionSystem(World, moveOrderTypeId, legacyMoveOrderTypeId),
                SystemGroup.AbilityActivation);
            RegisterSystem(
                new MovePlanOrderLifecycleSystem(World, orderTypes, moveOrderTypeId, legacyMoveOrderTypeId),
                SystemGroup.AbilityActivation);
            GlobalContext[MassNavigationMovePlanOrderAdapterInstalledKey] = true;
        }

        private void CancelPendingMapLoad(MapId mapId, string reason, bool markFailed)
        {
            if (!_pendingMapLoads.TryGetValue(mapId, out PendingMapLoadState pendingState))
            {
                return;
            }

            _pendingMapLoads.Remove(mapId);

            try
            {
                pendingState.PendingLoad.Cancel();
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn(in LogChannels.Engine, $"CancelPendingMapLoad failed for '{mapId.Value}': {ex.Message}");
            }

            if (markFailed)
            {
                SetMapLoadStatus(mapId, MapLoadStatus.DeferredFailure(reason));
            }

            if (markFailed || _isRunning)
            {
                ThrowIfNetworkStartupMapLoadFailed(mapId, reason);
            }
        }

        private void CancelPendingMapResume(MapId mapId, string reason, bool markFailed)
        {
            if (!_pendingMapResumes.TryGetValue(mapId, out PendingMapResumeState pendingState))
            {
                return;
            }

            _pendingMapResumes.Remove(mapId);

            try
            {
                pendingState.PendingLoad.Cancel();
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn(in LogChannels.Engine, $"CancelPendingMapResume failed for '{mapId.Value}': {ex.Message}");
            }

            if (markFailed)
            {
                SetMapLoadStatus(mapId, MapLoadStatus.DeferredFailure(reason));
            }
        }
    }
}
