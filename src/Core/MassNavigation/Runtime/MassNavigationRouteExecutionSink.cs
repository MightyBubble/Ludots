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
    private readonly Dictionary<long, RouteState> _routesByKey = new();
    private readonly HashSet<long> _activeKeys = new();
    private readonly List<long> _keysToRemove = new();
    private PathingAgentTypeConfig[] _agentTypesByProfileId = Array.Empty<PathingAgentTypeConfig>();
    private int[] _xScratch = Array.Empty<int>();
    private int[] _yScratch = Array.Empty<int>();

    public MassNavigationRouteExecutionSink(IPathService pathService, PathStore pathStore, PathingConfig pathingConfig)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _pathingConfig = pathingConfig ?? throw new ArgumentNullException(nameof(pathingConfig));
        RebuildProfileIndex();
    }

    public int ActiveRouteCount => _routesByKey.Count;
    public int StorageAllocationCount { get; private set; }

    public void BeginSync()
    {
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

        int pointCapacity = Math.Max(1, maxPoints);
        PrepareScratch(pointCapacity);
        long key = PackKey(requestId, agentIndex);
        ref RouteState? state = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            _routesByKey,
            key,
            out bool exists);
        if (!exists || state == null)
        {
            state = new RouteState(key, requestId, agentIndex, pointCapacity);
            StorageAllocationCount++;
        }
        else if (state.PreparePointCapacity(pointCapacity))
        {
            StorageAllocationCount++;
        }

        bool destinationChanged =
            state.DestinationWorldCm.X != destinationWorldCm.X ||
            state.DestinationWorldCm.Y != destinationWorldCm.Y;
        bool profileChanged =
            state.ProfileId != authoredAgent.ProfileId ||
            !string.Equals(state.AgentTypeId, agentType.Id, StringComparison.Ordinal);
        bool entityChanged = state.Agent != agent;
        bool budgetChanged = state.MaxExpanded != maxExpanded || state.MaxPoints != maxPoints;
        state.Agent = agent;
        state.ProfileId = authoredAgent.ProfileId;
        state.AgentTypeId = agentType.Id;
        state.DestinationWorldCm = destinationWorldCm;
        state.MaxExpanded = maxExpanded;
        state.MaxPoints = maxPoints;
        if (destinationChanged || profileChanged || entityChanged || budgetChanged)
        {
            state.InvalidateRoute();
        }

        _activeKeys.Add(key);
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

    public void EndSync()
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
            _routesByKey.Remove(_keysToRemove[i]);
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
            _routesByKey.Remove(_keysToRemove[i]);
        }
    }

    public MassNavigationRouteSinkResult TryApplyTrackedRouteTargets(
        MassNavigationSimulationRuntime simulation,
        World world)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(world);

        bool appliedAny = false;
        MassNavigationRouteSinkResult lastApplied = default;
        foreach (RouteState state in _routesByKey.Values)
        {
            MassNavigationRouteSinkResult result = TryApplyRouteTarget(simulation, world, state);
            if (!result.Applied)
            {
                return result;
            }

            appliedAny = true;
            lastApplied = result;
        }

        if (appliedAny)
        {
            return lastApplied;
        }

        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Applied,
            PathStatus.Found,
            PathDomain.None,
            default,
            waypointCount: 0,
            errorCode: 0);
    }

    private MassNavigationRouteSinkResult TryApplyRouteTarget(
        MassNavigationSimulationRuntime simulation,
        World world,
        RouteState state)
    {
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
        Vector2 waypoint = state.CurrentWaypointWorldCm;
        bool resetRecovery =
            state.ForceResetNextApply ||
            state.LastAppliedWaypointIndex != state.CurrentWaypointIndex;
        simulation.SetAgentNavigationTargetWorldCm(
            state.AgentIndex,
            waypoint.X,
            waypoint.Y,
            resetRecovery);
        state.LastAppliedWaypointIndex = state.CurrentWaypointIndex;
        state.ForceResetNextApply = false;
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

        RequireScratch(state.MaxPoints > 0 ? state.MaxPoints : 1);
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

        state.RequirePointCapacity(count);
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

    private void PrepareScratch(int required)
    {
        if (_xScratch.Length >= required && _yScratch.Length >= required)
        {
            return;
        }

        _xScratch = new int[required];
        _yScratch = new int[required];
        StorageAllocationCount++;
    }

    private void RequireScratch(int required)
    {
        if (_xScratch.Length < required || _yScratch.Length < required)
        {
            throw new InvalidOperationException(
                $"MassNavigation route solve required {required} scratch points, exceeding prepared route scratch capacity {Math.Min(_xScratch.Length, _yScratch.Length)}.");
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

    private sealed class RouteState
    {
        public RouteState(long key, int orderToken, int agentIndex, int pointCapacity)
        {
            Key = key;
            OrderToken = orderToken;
            AgentIndex = agentIndex;
            LastAppliedWaypointIndex = -1;
            PointXCm = new int[pointCapacity];
            PointYCm = new int[pointCapacity];
        }

        public long Key { get; }
        public int OrderToken { get; }
        public int AgentIndex { get; }
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

        public bool PreparePointCapacity(int required)
        {
            if (PointXCm.Length >= required && PointYCm.Length >= required)
            {
                return false;
            }

            PointXCm = new int[required];
            PointYCm = new int[required];
            return true;
        }

        public void RequirePointCapacity(int required)
        {
            if (PointXCm.Length < required || PointYCm.Length < required)
            {
                throw new InvalidOperationException(
                    $"MassNavigation tracked route required {required} points, exceeding prepared point capacity {Math.Min(PointXCm.Length, PointYCm.Length)}.");
            }
        }
    }
}
