using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.StructureCollision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Engine
{
    public partial class GameEngine
    {
        public MapSession CurrentMapSession { get; private set; }

        private readonly Dictionary<MapId, PendingMapLoadState> _pendingMapLoads = new();
        private readonly Dictionary<MapId, PendingMapResumeState> _pendingMapResumes = new();
        private readonly Dictionary<MapId, MapLoadStatus> _mapLoadStatuses = new();
        private VertexMapVisualHeightmap? _vertexMapVisualHeightmap;

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
                GetService(CoreServiceKeys.NarrativeDirector) == null)
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
                RemoveService(CoreServiceKeys.VisualHeightmap);
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
            IVisualHeightmap? visualHeightmap = ResolveSessionVisualHeightmap(session);
            if (visualHeightmap != null)
            {
                SetService(CoreServiceKeys.VisualHeightmap, visualHeightmap);
            }
            else
            {
                RemoveService(CoreServiceKeys.VisualHeightmap);
            }
            if (session.StructureCollisionAsset != null)
            {
                session.StructureCollisionRuntimeState ??= new StructureCollisionRuntimeState(session.StructureCollisionAsset);
                session.GroundSurfaceSampler ??= new GroundSurfaceSampler(
                    visualHeightmap,
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
                if (visualHeightmap != null)
                {
                    session.GroundSurfaceSampler ??= new GroundSurfaceSampler(visualHeightmap, null, null);
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

        private IVisualHeightmap? ResolveSessionVisualHeightmap(MapSession session)
        {
            if (session.VisualHeightmap != null)
            {
                return session.VisualHeightmap;
            }

            // .vhtm 是唯一权威视觉高度源；仅当会话未声明 vhtm 且引擎已持有 VertexMap 时用逻辑格点补高度服务。
            // 适配器按需读取当前 VertexMap，地图热切换不会绑定到上一张图。
            if (VertexMap != null)
            {
                _vertexMapVisualHeightmap ??= new VertexMapVisualHeightmap(() => VertexMap);
                return _vertexMapVisualHeightmap;
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

        private void WireMapVariablePhaseDispatcher(MapSession session)
        {
            Gameplay.MapTriggers.MapVariableStore? variables = session?.Variables;
            if (variables == null)
            {
                return;
            }

            variables.PhaseChangedDispatcher = (mapId, varName, newValue) =>
            {
                ScriptContext ctx = CreateMapEventContext(session!);
                ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyVarName, varName);
                ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyPhase, newValue);
                ctx.Set(Gameplay.MapTriggers.MapVariableStore.PayloadKeyVarValueInt, newValue);
                CompleteLifecycleEvent(
                    TriggerManager.FireMapEventAsync(mapId, new EventKey(Gameplay.MapTriggers.MapVariableStore.PhaseChangedEventName), ctx));
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
                IPendingMapLoad pendingLoad = gate.BeginPendingLoad(new MapLoadCompletionRequest(this, session.MapId, mapConfig, session, isPush));
                if (pendingLoad == null)
                {
                    return false;
                }

                MapLoadCompletionResult initialResult = pendingLoad.Poll();
                if (initialResult.State == MapLoadCompletionState.Pending)
                {
                    _pendingMapLoads[session.MapId] = new PendingMapLoadState(session, mapConfig, pendingLoad);
                    SetMapLoadStatus(session.MapId, MapLoadStatus.DeferredPending);
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
                IPendingMapLoad pendingLoad = gate.BeginPendingResume(new MapResumeCompletionRequest(this, session, closedSession));
                if (pendingLoad == null)
                {
                    return false;
                }

                MapLoadCompletionResult initialResult = pendingLoad.Poll();
                if (initialResult.State == MapLoadCompletionState.Pending)
                {
                    _pendingMapResumes[session.MapId] = new PendingMapResumeState(session, closedSession, pendingLoad);
                    SetMapLoadStatus(session.MapId, MapLoadStatus.DeferredPending);
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
                        continue;
                    }

                    _pendingMapLoads.Remove(pair.Key);
                    CompleteMapLoad(session, pendingState.MapConfig, MapLoadStatus.FromCompletion(result, isDeferred: true));
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
                _massNavigationRuntime.HandleMapFocused(this, session.MapId);
            }
            else
            {
                SetMapEntitiesSuspended(session.MapId, true);
                if (loadStatus.Failed)
                {
                    Diagnostics.Log.Warn(in LogChannels.Engine, $"Map '{session.MapId.Value}' completed with failure: {loadStatus.ErrorMessage}");
                }

                return;
            }

            ScriptContext finalCtx = CreateMapEventContext(session);
            Diagnostics.Log.Info(in LogChannels.Engine, $"Firing MapLoaded event for {session.MapId.Value}...");
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(session.MapId, GameEvents.MapLoaded, finalCtx));
            CaptureFocusedParticipantOverrides(session);
            session.TeamRelationships = TeamManager.CaptureSnapshot();
        }

        private void SetSessionParticipants(MapSession session, ParticipantBindingResult participants)
        {
            session.TeamEntityLookup = participants.Teams;
            session.PlayerEntityLookup = participants.Players;
            session.LocalSeats = participants.LocalSeats;
            session.TeamRelationships = participants.TeamRelationships;

            if (CurrentMapSession == session)
            {
                ParticipantBindingResolver.PublishFocused(GlobalContext, participants);
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
            _massNavigationRuntime.HandleMapFocused(this, session.MapId);
            ScriptContext resumeCtx = CreateMapEventContext(session);
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(session.MapId, GameEvents.MapResumed, resumeCtx));
            CaptureFocusedParticipantOverrides(session);
            session.TeamRelationships = TeamManager.CaptureSnapshot();
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
