using System;
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
    private readonly int _routeCapacity;
    private readonly int _pointCapacity;
    private readonly RouteState[] _statesByAgent;
    private readonly int[] _activeAgentIndices;
    private readonly int[] _pointXCm;
    private readonly int[] _pointYCm;
    private readonly int[] _xScratch;
    private readonly int[] _yScratch;
    private PathingAgentTypeConfig[] _agentTypesByProfileId = Array.Empty<PathingAgentTypeConfig>();
    private int _activeRouteCount;
    private int _syncRevision;

    public MassNavigationRouteExecutionSink(
        IPathService pathService,
        PathStore pathStore,
        PathingConfig pathingConfig,
        int routeCapacity,
        int pointCapacity)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
        _pathingConfig = pathingConfig ?? throw new ArgumentNullException(nameof(pathingConfig));
        if (routeCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeCapacity));
        }

        if (pointCapacity <= 0 || pointCapacity > pathStore.MaxPointsPerPath)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCapacity));
        }

        _routeCapacity = routeCapacity;
        _pointCapacity = pointCapacity;
        _statesByAgent = new RouteState[routeCapacity];
        _activeAgentIndices = new int[routeCapacity];
        int totalPointCapacity = checked(routeCapacity * pointCapacity);
        _pointXCm = new int[totalPointCapacity];
        _pointYCm = new int[totalPointCapacity];
        _xScratch = new int[pointCapacity];
        _yScratch = new int[pointCapacity];
        StorageAllocationCount = 6;
        RebuildProfileIndex();
    }

    public int ActiveRouteCount => _activeRouteCount;
    public int PeakActiveRouteCount { get; private set; }
    public int PreparedAgentIndexCapacity => _routeCapacity;
    public int WaypointCapacityPerAgent => _pointCapacity;
    public int TotalWaypointSlots => _pointXCm.Length;
    public int StorageAllocationCount { get; private set; }

    public void BeginSync()
    {
        unchecked
        {
            _syncRevision++;
        }

        if (_syncRevision == 0)
        {
            _syncRevision = 1;
            for (int i = 0; i < _activeRouteCount; i++)
            {
                _statesByAgent[_activeAgentIndices[i]].LastSeenSyncRevision = 0;
            }
        }
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

        RequireRouteCapacity(agentIndex);
        RequirePointCapacity(maxPoints);
        ref RouteState state = ref _statesByAgent[agentIndex];
        if (!state.Active)
        {
            ActivateRouteSlot(ref state, agentIndex, requestId);
        }
        else if (state.OrderToken != requestId)
        {
            ResetRouteForOrder(ref state, requestId);
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

        state.LastSeenSyncRevision = _syncRevision;
        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Tracked,
            PathStatus.Found,
            state.ResolvedDomain,
            GetCurrentWaypoint(in state),
            state.PointCount,
            errorCode: 0,
            orderToken: requestId,
            agentIndex: agentIndex);
    }

    public void EndSync()
    {
        int activeIndex = 0;
        while (activeIndex < _activeRouteCount)
        {
            int agentIndex = _activeAgentIndices[activeIndex];
            if (_statesByAgent[agentIndex].LastSeenSyncRevision != _syncRevision)
            {
                RemoveActiveRouteAt(activeIndex);
                continue;
            }

            activeIndex++;
        }
    }

    public void RemoveOrderToken(int orderToken)
    {
        int activeIndex = 0;
        while (activeIndex < _activeRouteCount)
        {
            int agentIndex = _activeAgentIndices[activeIndex];
            if (_statesByAgent[agentIndex].OrderToken == orderToken)
            {
                RemoveActiveRouteAt(activeIndex);
                continue;
            }

            activeIndex++;
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
        for (int activeIndex = 0; activeIndex < _activeRouteCount; activeIndex++)
        {
            int agentIndex = _activeAgentIndices[activeIndex];
            MassNavigationRouteSinkResult result = TryApplyRouteTarget(
                simulation,
                world,
                ref _statesByAgent[agentIndex]);
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
        ref RouteState state)
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
            MassNavigationRouteSinkResult solve = TrySolveRoute(simulation, ref state);
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

        AdvanceWaypointCursor(simulation, ref state);
        Vector2 waypoint = GetCurrentWaypoint(in state);
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
        ref RouteState state)
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

        RequirePointCapacity(count);
        int pointOffset = state.PointOffset;
        for (int i = 0; i < count; i++)
        {
            _pointXCm[pointOffset + i] = _xScratch[i];
            _pointYCm[pointOffset + i] = _yScratch[i];
        }

        state.PointCount = count;
        state.CurrentWaypointIndex = 0;
        state.LastAppliedWaypointIndex = -1;
        state.ResolvedDomain = path.ResolvedDomain;
        state.RouteReady = true;
        state.ForceResetNextApply = true;
        AdvanceWaypointCursor(simulation, ref state);
        return new MassNavigationRouteSinkResult(
            MassNavigationRouteSinkStatus.Applied,
            path.Status,
            path.ResolvedDomain,
            GetCurrentWaypoint(in state),
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
        if (_agentTypesByProfileId.Length > 0)
        {
            StorageAllocationCount++;
        }

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

    private void RequireRouteCapacity(int agentIndex)
    {
        if ((uint)agentIndex < (uint)_routeCapacity)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation route tracking agent index {agentIndex} exceeds prepared route capacity {_routeCapacity} from runtime.capacity.groupMembershipAgentCapacity.");
    }

    private void RequirePointCapacity(int required)
    {
        if (required > 0 && required <= _pointCapacity)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation tracked route requires {required} points, exceeding prepared route point capacity {_pointCapacity}.");
    }

    private void AdvanceWaypointCursor(
        MassNavigationSimulationRuntime simulation,
        ref RouteState state)
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
            Vector2 waypoint = GetCurrentWaypoint(in state);
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

    private void ActivateRouteSlot(ref RouteState state, int agentIndex, int orderToken)
    {
        if (_activeRouteCount >= _routeCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation route tracking exceeds prepared route capacity {_routeCapacity}.");
        }

        int activeListIndex = _activeRouteCount++;
        _activeAgentIndices[activeListIndex] = agentIndex;
        state = new RouteState
        {
            Active = true,
            ActiveListIndex = activeListIndex,
            AgentIndex = agentIndex,
            PointOffset = checked(agentIndex * _pointCapacity),
            OrderToken = orderToken,
            LastAppliedWaypointIndex = -1,
            ForceResetNextApply = true,
        };
        PeakActiveRouteCount = Math.Max(PeakActiveRouteCount, _activeRouteCount);
    }

    private static void ResetRouteForOrder(ref RouteState state, int orderToken)
    {
        int activeListIndex = state.ActiveListIndex;
        int agentIndex = state.AgentIndex;
        int pointOffset = state.PointOffset;
        state = new RouteState
        {
            Active = true,
            ActiveListIndex = activeListIndex,
            AgentIndex = agentIndex,
            PointOffset = pointOffset,
            OrderToken = orderToken,
            LastAppliedWaypointIndex = -1,
            ForceResetNextApply = true,
        };
    }

    private void RemoveActiveRouteAt(int activeListIndex)
    {
        int removedAgentIndex = _activeAgentIndices[activeListIndex];
        int lastActiveIndex = --_activeRouteCount;
        if (activeListIndex != lastActiveIndex)
        {
            int movedAgentIndex = _activeAgentIndices[lastActiveIndex];
            _activeAgentIndices[activeListIndex] = movedAgentIndex;
            _statesByAgent[movedAgentIndex].ActiveListIndex = activeListIndex;
        }

        _statesByAgent[removedAgentIndex] = default;
    }

    private Vector2 GetCurrentWaypoint(in RouteState state)
    {
        if ((uint)state.CurrentWaypointIndex >= (uint)state.PointCount)
        {
            return default;
        }

        int pointIndex = state.PointOffset + state.CurrentWaypointIndex;
        return new Vector2(_pointXCm[pointIndex], _pointYCm[pointIndex]);
    }

    private struct RouteState
    {
        public bool Active;
        public int ActiveListIndex;
        public int LastSeenSyncRevision;
        public int OrderToken;
        public int AgentIndex;
        public int PointOffset;
        public Entity Agent;
        public int ProfileId;
        public string? AgentTypeId;
        public Vector2 DestinationWorldCm;
        public int MaxExpanded;
        public int MaxPoints;
        public int PointCount;
        public int CurrentWaypointIndex;
        public int LastAppliedWaypointIndex;
        public PathDomain ResolvedDomain;
        public bool RouteReady;
        public bool ForceResetNextApply;

        public void InvalidateRoute()
        {
            RouteReady = false;
            PointCount = 0;
            CurrentWaypointIndex = 0;
            LastAppliedWaypointIndex = -1;
            ResolvedDomain = PathDomain.None;
            ForceResetNextApply = true;
        }

    }
}
