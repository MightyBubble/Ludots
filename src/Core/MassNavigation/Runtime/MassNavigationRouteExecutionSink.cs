using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.MassNavigation.Runtime;

public enum MassNavigationRouteSinkStatus : byte
{
    Applied = 0,
    NoConfiguredAgentType = 1,
    ServiceMissing = 2,
    SolveFailed = 3,
    CopyFailed = 4,
    EmptyPath = 5,
    AgentNotBound = 6,
    Tracked = 7,
}

public readonly struct MassNavigationRouteSinkResult
{
    public MassNavigationRouteSinkResult(
        MassNavigationRouteSinkStatus status,
        PathStatus pathStatus,
        PathDomain resolvedDomain,
        Vector2 waypointWorldCm,
        int waypointCount,
        int errorCode,
        int orderToken = 0,
        int agentIndex = -1)
    {
        Status = status;
        PathStatus = pathStatus;
        ResolvedDomain = resolvedDomain;
        WaypointWorldCm = waypointWorldCm;
        WaypointCount = waypointCount;
        ErrorCode = errorCode;
        OrderToken = orderToken;
        AgentIndex = agentIndex;
    }

    public MassNavigationRouteSinkStatus Status { get; }
    public PathStatus PathStatus { get; }
    public PathDomain ResolvedDomain { get; }
    public Vector2 WaypointWorldCm { get; }
    public int WaypointCount { get; }
    public int ErrorCode { get; }
    public int OrderToken { get; }
    public int AgentIndex { get; }
    public bool Applied => Status == MassNavigationRouteSinkStatus.Applied;
    public bool Tracked => Status == MassNavigationRouteSinkStatus.Tracked;
}

public sealed class MassNavigationRouteExecutionSink
{
    private const float WaypointAdvanceStopThresholdScale = 2f;
    private const float WaypointAdvanceBodyRadiusScale = 1.5f;

    private readonly IPathService _pathService;
    private readonly PathStore _pathStore;
    private readonly PathingConfig _pathingConfig;
    private readonly Dictionary<long, RouteState> _routesByKey;
    private readonly HashSet<long> _activeKeys;
    private readonly List<long> _keysToRemove;
    private readonly Stack<RouteState> _freeRoutes;
    private readonly int _routeStateCapacity;
    private readonly int _waypointCapacityPerAgent;
    private readonly RouteTrackPlan[] _syncPlans;
    private readonly RouteState[] _applyRoutes;
    private readonly Vector2[] _applyWaypoints;
    private readonly bool[] _applyResetRecovery;
    private readonly int[] _snapshotProfileIds;
    private readonly string?[] _snapshotAgentTypeIds;
    private readonly int[] _snapshotPointCounts;
    private readonly int[] _snapshotCurrentWaypointIndices;
    private readonly int[] _snapshotLastAppliedWaypointIndices;
    private readonly PathDomain[] _snapshotResolvedDomains;
    private readonly bool[] _snapshotRouteReady;
    private readonly bool[] _snapshotForceResetNextApply;
    private readonly int[] _snapshotPointXCm;
    private readonly int[] _snapshotPointYCm;
    private PathingAgentTypeConfig[] _agentTypesByProfileId = Array.Empty<PathingAgentTypeConfig>();
    private readonly int[] _xScratch;
    private readonly int[] _yScratch;
    private int _syncPlanCount;
    private bool _syncInProgress;

    public MassNavigationRouteExecutionSink(
        IPathService pathService,
        PathStore pathStore,
        PathingConfig pathingConfig,
        int routeStateCapacity = 1024,
        int waypointCapacityPerAgent = 64)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _pathingConfig = pathingConfig ?? throw new ArgumentNullException(nameof(pathingConfig));
        if (routeStateCapacity <= 0 || waypointCapacityPerAgent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeStateCapacity), "Route and waypoint capacities must be positive.");
        }

        _routeStateCapacity = routeStateCapacity;
        _waypointCapacityPerAgent = waypointCapacityPerAgent;
        _routesByKey = new Dictionary<long, RouteState>(routeStateCapacity);
        _activeKeys = new HashSet<long>(routeStateCapacity);
        _keysToRemove = new List<long>(routeStateCapacity);
        _freeRoutes = new Stack<RouteState>(routeStateCapacity);
        _syncPlans = new RouteTrackPlan[routeStateCapacity];
        _applyRoutes = new RouteState[routeStateCapacity];
        _applyWaypoints = new Vector2[routeStateCapacity];
        _applyResetRecovery = new bool[routeStateCapacity];
        _snapshotProfileIds = new int[routeStateCapacity];
        _snapshotAgentTypeIds = new string?[routeStateCapacity];
        _snapshotPointCounts = new int[routeStateCapacity];
        _snapshotCurrentWaypointIndices = new int[routeStateCapacity];
        _snapshotLastAppliedWaypointIndices = new int[routeStateCapacity];
        _snapshotResolvedDomains = new PathDomain[routeStateCapacity];
        _snapshotRouteReady = new bool[routeStateCapacity];
        _snapshotForceResetNextApply = new bool[routeStateCapacity];
        _snapshotPointXCm = new int[routeStateCapacity * waypointCapacityPerAgent];
        _snapshotPointYCm = new int[routeStateCapacity * waypointCapacityPerAgent];
        for (int i = 0; i < routeStateCapacity; i++)
        {
            _freeRoutes.Push(new RouteState(waypointCapacityPerAgent));
        }

        _xScratch = new int[waypointCapacityPerAgent];
        _yScratch = new int[waypointCapacityPerAgent];
        RebuildProfileIndex();
    }

    public int ActiveRouteCount => _routesByKey.Count;

    internal bool IsBoundTo(IPathService pathService, PathStore pathStore, PathingConfig pathingConfig)
    {
        return ReferenceEquals(_pathService, pathService) &&
            ReferenceEquals(_pathStore, pathStore) &&
            ReferenceEquals(_pathingConfig, pathingConfig);
    }

    public void BeginSync()
    {
        _activeKeys.Clear();
        _syncPlanCount = 0;
        _syncInProgress = true;
    }

    public void CancelSync()
    {
        if (!_syncInProgress)
        {
            return;
        }

        _syncPlanCount = 0;
        _syncInProgress = false;
        _activeKeys.Clear();
    }

    public MassNavigationRouteSinkResult TrackRouteTarget(
        MassNavigationSimulationRuntime simulation,
        World world,
        Entity agent,
        int agentIndex,
        Vector2 destinationWorldCm,
        int requestId,
        int maxExpanded,
        int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(world);

        if (!world.IsAlive(agent) || !world.TryGet(agent, out MassNavigationAgent authoredAgent))
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.AgentNotBound,
                PathStatus.InvalidRequest,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 1,
                orderToken: requestId,
                agentIndex: agentIndex);
        }

        if (!TryResolveAgentType(authoredAgent.ProfileId, out PathingAgentTypeConfig agentType))
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.NoConfiguredAgentType,
                PathStatus.InvalidRequest,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 2,
                orderToken: requestId,
                agentIndex: agentIndex);
        }

        if (!_syncInProgress)
        {
            throw new InvalidOperationException("MassNavigation route target tracking requires BeginSync before TrackRouteTarget.");
        }

        long key = PackKey(requestId, agentIndex);
        if (maxPoints <= 0 || maxPoints > _waypointCapacityPerAgent)
        {
            throw new InvalidOperationException(
                $"MassNavigation route request maxPoints {maxPoints} exceeds configured routeWaypointCapacityPerAgent {_waypointCapacityPerAgent}.");
        }

        bool knownActiveKey = _activeKeys.Contains(key);
        if (!knownActiveKey && _activeKeys.Count >= _routeStateCapacity)
        {
            throw new InvalidOperationException("MassNavigation route state capacity exceeded.");
        }

        var plan = new RouteTrackPlan(
            key,
            requestId,
            agentIndex,
            agent,
            authoredAgent.ProfileId,
            agentType.Id,
            destinationWorldCm,
            maxExpanded,
            maxPoints);
        if (knownActiveKey)
        {
            for (int i = 0; i < _syncPlanCount; i++)
            {
                if (_syncPlans[i].Key == key)
                {
                    _syncPlans[i] = plan;
                    return CreateTrackedResult(key, requestId, agentIndex);
                }
            }
        }

        _activeKeys.Add(key);
        _syncPlans[_syncPlanCount++] = plan;
        return CreateTrackedResult(key, requestId, agentIndex);
    }

    public void EndSync()
    {
        if (!_syncInProgress)
        {
            throw new InvalidOperationException("MassNavigation route sync must begin before EndSync.");
        }

        PreflightSyncRouteCapacity();
        ReleaseInactiveRoutes();
        for (int i = 0; i < _syncPlanCount; i++)
        {
            ApplyRouteTrackPlan(_syncPlans[i]);
        }

        _syncPlanCount = 0;
        _syncInProgress = false;
        _activeKeys.Clear();
    }

    private MassNavigationRouteSinkResult CreateTrackedResult(long key, int requestId, int agentIndex)
    {
        if (_routesByKey.TryGetValue(key, out RouteState? state) && state != null)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.Tracked,
                PathStatus.Found,
                state.ResolvedDomain,
                state.CurrentWaypointWorldCm,
                state.PointCount,
                errorCode: 0,
                orderToken: requestId,
                agentIndex: agentIndex);
        }

        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Tracked,
            PathStatus.Found,
            PathDomain.None,
            default,
            waypointCount: 0,
            errorCode: 0,
            orderToken: requestId,
            agentIndex: agentIndex);
    }

    private void PreflightSyncRouteCapacity()
    {
        int missingRouteCount = 0;
        foreach (long key in _activeKeys)
        {
            if (!_routesByKey.ContainsKey(key))
            {
                missingRouteCount++;
            }
        }

        int releasableRouteCount = 0;
        foreach (long key in _routesByKey.Keys)
        {
            if (!_activeKeys.Contains(key))
            {
                releasableRouteCount++;
            }
        }

        if (missingRouteCount > _freeRoutes.Count + releasableRouteCount)
        {
            throw new InvalidOperationException("MassNavigation route state capacity exceeded.");
        }
    }

    private void ReleaseInactiveRoutes()
    {
        _keysToRemove.Clear();
        foreach (long key in _routesByKey.Keys)
        {
            if (!_activeKeys.Contains(key))
            {
                _keysToRemove.Add(key);
            }
        }

        for (int i = 0; i < _keysToRemove.Count; i++)
        {
            ReleaseRoute(_keysToRemove[i]);
        }
    }

    private void ApplyRouteTrackPlan(RouteTrackPlan plan)
    {
        ref RouteState? state = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            _routesByKey,
            plan.Key,
            out bool exists);
        if (!exists || state == null)
        {
            state = _freeRoutes.Pop();
            state.Reset(plan.Key, plan.OrderToken, plan.AgentIndex);
        }

        bool destinationChanged =
            state.DestinationWorldCm.X != plan.DestinationWorldCm.X ||
            state.DestinationWorldCm.Y != plan.DestinationWorldCm.Y;
        bool profileChanged =
            state.ProfileId != plan.ProfileId ||
            !string.Equals(state.AgentTypeId, plan.AgentTypeId, StringComparison.Ordinal);
        bool entityChanged = state.Agent != plan.Agent;
        state.Agent = plan.Agent;
        state.ProfileId = plan.ProfileId;
        state.AgentTypeId = plan.AgentTypeId;
        state.DestinationWorldCm = plan.DestinationWorldCm;
        state.MaxExpanded = plan.MaxExpanded;
        state.MaxPoints = plan.MaxPoints;
        if (destinationChanged || profileChanged || entityChanged)
        {
            state.InvalidateRoute();
        }
    }

    public void RemoveOrderToken(int orderToken)
    {
        _keysToRemove.Clear();
        foreach (KeyValuePair<long, RouteState> route in _routesByKey)
        {
            if (route.Value.OrderToken == orderToken)
            {
                _keysToRemove.Add(route.Key);
            }
        }

        for (int i = 0; i < _keysToRemove.Count; i++)
        {
            ReleaseRoute(_keysToRemove[i]);
        }
    }

    public MassNavigationRouteSinkResult TryApplyTrackedRouteTargets(
        MassNavigationSimulationRuntime simulation,
        World world)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(world);

        int routeCount = CopyRoutesForApply();
        if (routeCount <= 0)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.Applied,
                PathStatus.Found,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 0);
        }

        MassNavigationRouteSinkResult lastPrepared = default;
        for (int i = 0; i < routeCount; i++)
        {
            RouteState state = _applyRoutes[i];
            CaptureApplySnapshot(i, state);
            MassNavigationRouteSinkResult result = TryPrepareRouteTarget(
                simulation,
                world,
                state,
                out Vector2 waypoint,
                out bool resetRecovery);
            if (!result.Applied)
            {
                RestoreApplySnapshots(i + 1);
                return result;
            }

            _applyWaypoints[i] = waypoint;
            _applyResetRecovery[i] = resetRecovery;
            lastPrepared = result;
        }

        for (int i = 0; i < routeCount; i++)
        {
            CommitPreparedRouteTarget(
                simulation,
                _applyRoutes[i],
                _applyWaypoints[i],
                _applyResetRecovery[i]);
        }

        return lastPrepared;
    }

    private void CaptureApplySnapshot(int slot, RouteState state)
    {
        _snapshotProfileIds[slot] = state.ProfileId;
        _snapshotAgentTypeIds[slot] = state.AgentTypeId;
        _snapshotPointCounts[slot] = state.PointCount;
        _snapshotCurrentWaypointIndices[slot] = state.CurrentWaypointIndex;
        _snapshotLastAppliedWaypointIndices[slot] = state.LastAppliedWaypointIndex;
        _snapshotResolvedDomains[slot] = state.ResolvedDomain;
        _snapshotRouteReady[slot] = state.RouteReady;
        _snapshotForceResetNextApply[slot] = state.ForceResetNextApply;

        int offset = slot * _waypointCapacityPerAgent;
        Array.Copy(state.PointXCm, 0, _snapshotPointXCm, offset, state.PointCount);
        Array.Copy(state.PointYCm, 0, _snapshotPointYCm, offset, state.PointCount);
    }

    private void RestoreApplySnapshots(int count)
    {
        for (int slot = 0; slot < count; slot++)
        {
            RouteState state = _applyRoutes[slot];
            state.ProfileId = _snapshotProfileIds[slot];
            state.AgentTypeId = _snapshotAgentTypeIds[slot];
            state.PointCount = _snapshotPointCounts[slot];
            state.CurrentWaypointIndex = _snapshotCurrentWaypointIndices[slot];
            state.LastAppliedWaypointIndex = _snapshotLastAppliedWaypointIndices[slot];
            state.ResolvedDomain = _snapshotResolvedDomains[slot];
            state.RouteReady = _snapshotRouteReady[slot];
            state.ForceResetNextApply = _snapshotForceResetNextApply[slot];

            int offset = slot * _waypointCapacityPerAgent;
            Array.Copy(_snapshotPointXCm, offset, state.PointXCm, 0, state.PointCount);
            Array.Copy(_snapshotPointYCm, offset, state.PointYCm, 0, state.PointCount);
        }
    }

    private int CopyRoutesForApply()
    {
        int count = 0;
        foreach (RouteState state in _routesByKey.Values)
        {
            _applyRoutes[count++] = state;
        }

        Array.Sort(_applyRoutes, 0, count, RouteStateApplyComparer.Instance);
        return count;
    }

    private MassNavigationRouteSinkResult TryPrepareRouteTarget(
        MassNavigationSimulationRuntime simulation,
        World world,
        RouteState state,
        out Vector2 waypoint,
        out bool resetRecovery)
    {
        waypoint = default;
        resetRecovery = false;
        if (!world.IsAlive(state.Agent) || !world.TryGet(state.Agent, out MassNavigationAgent authoredAgent))
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.AgentNotBound,
                PathStatus.InvalidRequest,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 1,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        bool hasAgentType = TryResolveAgentType(authoredAgent.ProfileId, out PathingAgentTypeConfig agentType);
        if (!hasAgentType)
        {
            state.InvalidateRoute();
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.NoConfiguredAgentType,
                PathStatus.InvalidRequest,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 2,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        if (authoredAgent.ProfileId != state.ProfileId ||
            !string.Equals(agentType.Id, state.AgentTypeId, StringComparison.Ordinal))
        {
            state.ProfileId = authoredAgent.ProfileId;
            state.AgentTypeId = agentType.Id;
            state.InvalidateRoute();
        }

        if (!state.RouteReady)
        {
            MassNavigationRouteSinkResult solve = TrySolveRoute(simulation, state);
            if (!solve.Applied)
            {
                return solve;
            }
        }

        if (state.PointCount <= 0)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.EmptyPath,
                PathStatus.Found,
                state.ResolvedDomain,
                default,
                waypointCount: 0,
                errorCode: 4,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        AdvanceWaypointCursor(simulation, state);
        waypoint = state.CurrentWaypointWorldCm;
        resetRecovery =
            state.ForceResetNextApply ||
            state.LastAppliedWaypointIndex != state.CurrentWaypointIndex;
        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Applied,
            PathStatus.Found,
            state.ResolvedDomain,
            waypoint,
            state.PointCount,
            errorCode: 0,
            orderToken: state.OrderToken,
            agentIndex: state.AgentIndex);
    }

    private static void CommitPreparedRouteTarget(
        MassNavigationSimulationRuntime simulation,
        RouteState state,
        Vector2 waypoint,
        bool resetRecovery)
    {
        simulation.SetAgentNavigationTargetWorldCm(
            state.AgentIndex,
            waypoint.X,
            waypoint.Y,
            resetRecovery);
        state.LastAppliedWaypointIndex = state.CurrentWaypointIndex;
        state.ForceResetNextApply = false;
    }

    private MassNavigationRouteSinkResult TrySolveRoute(
        MassNavigationSimulationRuntime simulation,
        RouteState state)
    {
        if (string.IsNullOrWhiteSpace(state.AgentTypeId))
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.NoConfiguredAgentType,
                PathStatus.InvalidRequest,
                PathDomain.None,
                default,
                waypointCount: 0,
                errorCode: 2,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        Vector2 startWorldCm = simulation.GetAgentWorldPositionCm(state.AgentIndex);
        var request = new PathRequest(
            state.OrderToken,
            state.Agent,
            PathDomain.Auto,
            state.AgentTypeId,
            PathEndpoint.FromWorldCm((int)MathF.Round(startWorldCm.X), (int)MathF.Round(startWorldCm.Y)),
            PathEndpoint.FromWorldCm((int)MathF.Round(state.DestinationWorldCm.X), (int)MathF.Round(state.DestinationWorldCm.Y)),
            new PathBudget(state.MaxExpanded, state.MaxPoints));

        if (!_pathService.TrySolve(in request, out PathResult path) ||
            path.Status != PathStatus.Found ||
            !path.Handle.IsValid)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.SolveFailed,
                path.Status,
                path.ResolvedDomain,
                default,
                waypointCount: 0,
                path.ErrorCode,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        EnsureScratch(state.MaxPoints > 0 ? state.MaxPoints : 1);
        bool copied = _pathService.TryCopyPath(in path.Handle, _xScratch, _yScratch, out int count);
        _pathStore.Release(in path.Handle);
        if (!copied)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.CopyFailed,
                path.Status,
                path.ResolvedDomain,
                default,
                waypointCount: 0,
                errorCode: 3,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        if (count <= 0)
        {
            return new MassNavigationRouteSinkResult(
                MassNavigationRouteSinkStatus.EmptyPath,
                path.Status,
                path.ResolvedDomain,
                default,
                waypointCount: 0,
                errorCode: 4,
                orderToken: state.OrderToken,
                agentIndex: state.AgentIndex);
        }

        state.EnsurePointCapacity(count);
        for (int i = 0; i < count; i++)
        {
            state.PointXCm[i] = _xScratch[i];
            state.PointYCm[i] = _yScratch[i];
        }

        state.PointCount = count;
        state.CurrentWaypointIndex = 0;
        state.LastAppliedWaypointIndex = -1;
        state.ResolvedDomain = path.ResolvedDomain;
        state.RouteReady = true;
        state.ForceResetNextApply = true;
        AdvanceWaypointCursor(simulation, state);
        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Applied,
            path.Status,
            path.ResolvedDomain,
            state.CurrentWaypointWorldCm,
            count,
            errorCode: 0,
            orderToken: state.OrderToken,
            agentIndex: state.AgentIndex);
    }

    private bool TryResolveAgentType(int profileId, out PathingAgentTypeConfig agentType)
    {
        if ((uint)profileId < (uint)_agentTypesByProfileId.Length)
        {
            agentType = _agentTypesByProfileId[profileId];
            return agentType != null;
        }

        agentType = null!;
        return false;
    }

    private void RebuildProfileIndex()
    {
        int maxProfileId = 0;
        for (int i = 0; i < _pathingConfig.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agentType = _pathingConfig.AgentTypes[i];
            if (agentType == null)
            {
                continue;
            }

            if (MassNavigationProfileRegistry.TryGetId(agentType.ProfileId, out int profileId) && profileId > maxProfileId)
            {
                maxProfileId = profileId;
            }
        }

        _agentTypesByProfileId = maxProfileId > 0
            ? new PathingAgentTypeConfig[maxProfileId + 1]
            : Array.Empty<PathingAgentTypeConfig>();

        for (int i = 0; i < _pathingConfig.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agentType = _pathingConfig.AgentTypes[i];
            if (agentType == null)
            {
                continue;
            }

            if (MassNavigationProfileRegistry.TryGetId(agentType.ProfileId, out int profileId))
            {
                _agentTypesByProfileId[profileId] = agentType;
            }
        }
    }

    private void EnsureScratch(int required)
    {
        if (_xScratch.Length >= required && _yScratch.Length >= required)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation route scratch requires {required} points, exceeding configured capacity {_xScratch.Length}.");
    }

    private void ReleaseRoute(long key)
    {
        if (_routesByKey.Remove(key, out RouteState? state) && state != null)
        {
            state.InvalidateRoute();
            _freeRoutes.Push(state);
        }
    }

    private static void AdvanceWaypointCursor(
        MassNavigationSimulationRuntime simulation,
        RouteState state)
    {
        if (state.PointCount <= 1)
        {
            return;
        }

        Vector2 position = simulation.GetAgentWorldPositionCm(state.AgentIndex);
        float threshold = ResolveWaypointAdvanceThresholdCm(simulation, state.AgentIndex);
        float thresholdSq = threshold * threshold;
        while (state.CurrentWaypointIndex < state.PointCount - 1)
        {
            Vector2 waypoint = state.CurrentWaypointWorldCm;
            float dx = waypoint.X - position.X;
            float dy = waypoint.Y - position.Y;
            if ((dx * dx) + (dy * dy) > thresholdSq)
            {
                return;
            }

            state.CurrentWaypointIndex++;
            state.ForceResetNextApply = true;
        }
    }

    private static float ResolveWaypointAdvanceThresholdCm(
        MassNavigationSimulationRuntime simulation,
        int agentIndex)
    {
        float stopThreshold = simulation.GetRuntimeGroupSemantics().UnitTargetStopThresholdCm * WaypointAdvanceStopThresholdScale;
        float bodyThreshold = simulation.GetAgentBodyRadiusCm(agentIndex) * WaypointAdvanceBodyRadiusScale;
        float threshold = MathF.Max(stopThreshold, bodyThreshold);
        return threshold > 0f ? threshold : 1f;
    }

    private static long PackKey(int orderToken, int agentIndex)
    {
        return ((long)orderToken << 32) ^ (uint)agentIndex;
    }

    private readonly struct RouteTrackPlan
    {
        public RouteTrackPlan(
            long key,
            int orderToken,
            int agentIndex,
            Entity agent,
            int profileId,
            string agentTypeId,
            Vector2 destinationWorldCm,
            int maxExpanded,
            int maxPoints)
        {
            Key = key;
            OrderToken = orderToken;
            AgentIndex = agentIndex;
            Agent = agent;
            ProfileId = profileId;
            AgentTypeId = agentTypeId;
            DestinationWorldCm = destinationWorldCm;
            MaxExpanded = maxExpanded;
            MaxPoints = maxPoints;
        }

        public long Key { get; }
        public int OrderToken { get; }
        public int AgentIndex { get; }
        public Entity Agent { get; }
        public int ProfileId { get; }
        public string AgentTypeId { get; }
        public Vector2 DestinationWorldCm { get; }
        public int MaxExpanded { get; }
        public int MaxPoints { get; }
    }

    private sealed class RouteState
    {
        public RouteState(int waypointCapacity)
        {
            PointXCm = new int[waypointCapacity];
            PointYCm = new int[waypointCapacity];
            LastAppliedWaypointIndex = -1;
        }

        public long Key { get; private set; }
        public int OrderToken { get; private set; }
        public int AgentIndex { get; private set; }
        public Entity Agent { get; set; }
        public int ProfileId { get; set; }
        public string? AgentTypeId { get; set; }
        public Vector2 DestinationWorldCm { get; set; }
        public int MaxExpanded { get; set; }
        public int MaxPoints { get; set; }
        public int[] PointXCm;
        public int[] PointYCm;
        public int PointCount { get; set; }
        public int CurrentWaypointIndex { get; set; }
        public int LastAppliedWaypointIndex { get; set; }
        public PathDomain ResolvedDomain { get; set; }
        public bool RouteReady { get; set; }
        public bool ForceResetNextApply { get; set; }

        public Vector2 CurrentWaypointWorldCm
        {
            get
            {
                if ((uint)CurrentWaypointIndex >= (uint)PointCount)
                {
                    return default;
                }

                return new Vector2(PointXCm[CurrentWaypointIndex], PointYCm[CurrentWaypointIndex]);
            }
        }

        public void InvalidateRoute()
        {
            RouteReady = false;
            PointCount = 0;
            CurrentWaypointIndex = 0;
            LastAppliedWaypointIndex = -1;
            ResolvedDomain = PathDomain.None;
            ForceResetNextApply = true;
        }

        public void Reset(long key, int orderToken, int agentIndex)
        {
            Key = key;
            OrderToken = orderToken;
            AgentIndex = agentIndex;
            Agent = Entity.Null;
            ProfileId = 0;
            AgentTypeId = null;
            DestinationWorldCm = default;
            MaxExpanded = 0;
            MaxPoints = 0;
            InvalidateRoute();
        }

        public void EnsurePointCapacity(int required)
        {
            if (PointXCm.Length >= required && PointYCm.Length >= required)
            {
                return;
            }

            throw new InvalidOperationException(
                $"MassNavigation route state requires {required} points, exceeding cold-phase capacity {PointXCm.Length}.");
        }
    }

    private sealed class RouteStateApplyComparer : IComparer<RouteState>
    {
        public static readonly RouteStateApplyComparer Instance = new();

        public int Compare(RouteState? x, RouteState? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int orderToken = x.OrderToken.CompareTo(y.OrderToken);
            return orderToken != 0
                ? orderToken
                : x.AgentIndex.CompareTo(y.AgentIndex);
        }
    }
}
