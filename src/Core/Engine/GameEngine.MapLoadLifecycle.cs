using System;
using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Scripting;

namespace Ludots.Core.Engine
{
    public partial class GameEngine
    {
        public MapSession CurrentMapSession { get; private set; }

        private readonly Dictionary<MapId, PendingMapLoadState> _pendingMapLoads = new();
        private readonly Dictionary<MapId, MapLoadStatus> _mapLoadStatuses = new();

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

        private void EnsureMapSessionInfrastructure()
        {
            if (MapSessions != null)
            {
                return;
            }

            MapSessions = new MapSessionManager();
            BoardIdRegistry = new BoardIdRegistry();
            SetService(CoreServiceKeys.MapSessions, MapSessions);
            SetService(CoreServiceKeys.BoardIdRegistry, BoardIdRegistry);
        }

        private void SetCurrentMapSession(MapSession session)
        {
            CurrentMapSession = session;
            if (session == null)
            {
                GlobalContext.Remove(CoreServiceKeys.MapSession.Name);
                GlobalContext.Remove(CoreServiceKeys.MapFeatureFlags.Name);
                GlobalContext.Remove(CoreServiceKeys.MapLoadStatus.Name);
                return;
            }

            SetService(CoreServiceKeys.MapSession, session);
            SetService(CoreServiceKeys.MapFeatureFlags, MapFeatureFlags.FromTags(session.MapConfig?.Tags));
            SetService(CoreServiceKeys.MapLoadStatus, GetMapLoadStatus(session.MapId));
        }

        private MapLoadStatus GetMapLoadStatus(MapId mapId)
        {
            return _mapLoadStatuses.TryGetValue(mapId, out MapLoadStatus status)
                ? status
                : MapLoadStatus.ImmediateSuccess;
        }

        private void SetMapLoadStatus(MapId mapId, MapLoadStatus status)
        {
            _mapLoadStatuses[mapId] = status;
            if (CurrentMapSession != null && CurrentMapSession.MapId == mapId)
            {
                SetService(CoreServiceKeys.MapLoadStatus, status);
            }
        }

        private ScriptContext CreateMapEventContext(MapSession session)
        {
            ScriptContext ctx = CreateContext();
            ctx.Set(CoreServiceKeys.MapId, session.MapId);
            ctx.Set(CoreServiceKeys.MapSession, session);
            ctx.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? new List<string>());
            ctx.Set(CoreServiceKeys.MapFeatureFlags, MapFeatureFlags.FromTags(session.MapConfig?.Tags));
            ctx.Set(CoreServiceKeys.MapLoadStatus, GetMapLoadStatus(session.MapId));
            return ctx;
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

        private void ProcessPendingMapLoads()
        {
            if (_pendingMapLoads.Count == 0)
            {
                return;
            }

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

        private void CompleteMapLoad(MapSession session, MapConfig mapConfig, MapLoadStatus loadStatus)
        {
            SetMapLoadStatus(session.MapId, loadStatus);
            SetCurrentMapSession(session);

            if (loadStatus.Succeeded)
            {
                SetMapEntitiesSuspended(session.MapId, false);
                ApplyDefaultCamera(mapConfig);
            }
            else
            {
                SetMapEntitiesSuspended(session.MapId, true);
                if (loadStatus.Failed)
                {
                    Diagnostics.Log.Warn(in LogChannels.Engine, $"Map '{session.MapId.Value}' completed with failure: {loadStatus.ErrorMessage}");
                }
            }

            ScriptContext finalCtx = CreateMapEventContext(session);
            Diagnostics.Log.Info(in LogChannels.Engine, $"Firing MapLoaded event for {session.MapId.Value}...");
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(session.MapId, GameEvents.MapLoaded, finalCtx));
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
    }
}
