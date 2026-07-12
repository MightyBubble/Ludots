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
    private readonly int _waypointCapacityPerAgent;
    private PathingAgentTypeConfig[] _agentTypesByProfileId = Array.Empty<PathingAgentTypeConfig>();
    private readonly int[] _xScratch;
    private readonly int[] _yScratch;

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

        _waypointCapacityPerAgent = waypointCapacityPerAgent;
        _routesByKey = new Dictionary<long, RouteState>(routeStateCapacity);
        _activeKeys = new HashSet<long>(routeStateCapacity);
        _keysToRemove = new List<long>(routeStateCapacity);
        _freeRoutes = new Stack<RouteState>(routeStateCapacity);
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

        long key = PackKey(requestId, agentIndex);
        if (maxPoints <= 0 || maxPoints > _waypointCapacityPerAgent)
        {
            throw new InvalidOperationException(
                $"MassNavigation route request maxPoints {maxPoints} exceeds configured routeWaypointCapacityPerAgent {_waypointCapacityPerAgent}.");
        }

        ref RouteState? state = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            _routesByKey,
            key,
            out bool exists);
        if (!exists || state == null)
        {
            if (_freeRoutes.Count <= 0)
            {
                _routesByKey.Remove(key);
                throw new InvalidOperationException("MassNavigation route state capacity exceeded.");
            }

            state = _freeRoutes.Pop();
            state.Reset(key, requestId, agentIndex);
        }

        bool destinationChanged =
            state.DestinationWorldCm.X != destinationWorldCm.X ||
            state.DestinationWorldCm.Y != destinationWorldCm.Y;
        bool profileChanged =
            state.ProfileId != authoredAgent.ProfileId ||
            !string.Equals(state.AgentTypeId, agentType.Id, StringComparison.Ordinal);
        bool entityChanged = state.Agent != agent;
        state.Agent = agent;
        state.ProfileId = authoredAgent.ProfileId;
        state.AgentTypeId = agentType.Id;
        state.DestinationWorldCm = destinationWorldCm;
        state.MaxExpanded = maxExpanded;
        state.MaxPoints = maxPoints;
        if (destinationChanged || profileChanged || entityChanged)
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
            ReleaseRoute(_keysToRemove[i]);
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
}
