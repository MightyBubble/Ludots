using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationOrderIngestionSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, OrderBuffer>()
        .WithNone<SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly MassNavigationRuntimeBinding _binding;
    private MassNavigationSimulationRuntime Simulation => _binding.RequireCurrent();
    private int _idleScanIntervalFrames;
    private int _orderTokenCapacity;
    private int _bucketMemberCapacity;
    private readonly Dictionary<int, int> _bucketIndexByToken = new();
    private OrderBucket[] _buckets = Array.Empty<OrderBucket>();
    private int[] _scanAgentIndices = Array.Empty<int>();
    private int[] _scanBucketIndices = Array.Empty<int>();
    private int[] _groupedAgentIndices = Array.Empty<int>();
    private int[] _bucketWriteOffsets = Array.Empty<int>();
    private readonly HashSet<int> _activeTokens = new();
    private readonly List<int> _completedTokens = new();
    private MassNavigationRouteExecutionSink? _routeSink;
    private int _usedBucketCount;
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame;
    private int _observedRuntimeBindingRevision = -1;
    private int _storageAllocationCount;

    public MassNavigationOrderIngestionSystem(GameEngine engine, MassNavigationRuntimeBinding binding)
    {
        _engine = engine;
        _binding = binding;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        RequirePreparedRuntime();

        ResolveMoveOrderType();

        if (Simulation.NavGroupRuntime.ActiveOrderGroupCount == 0 &&
            Simulation.CommandCountFrame <= 0 &&
            Simulation.FrameIndex - _lastIdleScanFrame < _idleScanIntervalFrames)
        {
            return;
        }

        _bucketIndexByToken.Clear();
        _activeTokens.Clear();
        _completedTokens.Clear();
        for (int i = 0; i < _usedBucketCount; i++)
        {
            _buckets[i].Reset();
        }

        _usedBucketCount = 0;
        int usedMemberCount = 0;
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            Span<Team> teams = chunk.GetSpan<Team>();
            Span<OrderBuffer> orderBuffers = chunk.GetSpan<OrderBuffer>();

            foreach (int index in chunk)
            {
                ref OrderBuffer orders = ref orderBuffers[index];
                if (!orders.HasActive || orders.ActiveOrder.Order.OrderTypeId != _moveOrderTypeId)
                {
                    continue;
                }

                ref readonly var order = ref orders.ActiveOrder.Order;
                int token = order.OrderId;
                if (token <= 0)
                {
                    continue;
                }

                int bucketIndex = GetOrCreateBucket(token);
                _activeTokens.Add(token);
                MassNavigationMoveOrderArgs moveArgs = MassNavigationMoveOrderArgs.Decode(in order);
                ref OrderBucket bucket = ref _buckets[bucketIndex];
                bucket.AssignOrValidatePayload(
                    teams[index].Id,
                    moveArgs.DestinationCm,
                    moveArgs.FormationMode,
                    moveArgs.RotationRadians);
                if (usedMemberCount >= _bucketMemberCapacity)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation order ingestion requires more than runtime.capacity.orderIngestionMemberCapacity {_bucketMemberCapacity} total active members.");
                }

                _scanAgentIndices[usedMemberCount] = agentIndices[index].Value;
                _scanBucketIndices[usedMemberCount] = bucketIndex;
                bucket.MemberCount++;
                usedMemberCount++;
            }
        }

        int groupedOffset = 0;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            ref OrderBucket bucket = ref _buckets[bucketIndex];
            bucket.MemberOffset = groupedOffset;
            _bucketWriteOffsets[bucketIndex] = groupedOffset;
            groupedOffset += bucket.MemberCount;
        }

        for (int memberIndex = 0; memberIndex < usedMemberCount; memberIndex++)
        {
            int bucketIndex = _scanBucketIndices[memberIndex];
            _groupedAgentIndices[_bucketWriteOffsets[bucketIndex]++] = _scanAgentIndices[memberIndex];
        }

        Simulation.ObserveOrderIngestionOccupancy(_usedBucketCount, usedMemberCount);

        MassNavigationRouteExecutionSink? routeSink = _routeSink;
        routeSink?.BeginSync();
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            ref OrderBucket bucket = ref _buckets[bucketIndex];
            if (bucket.MemberCount <= 0)
            {
                continue;
            }

            ReadOnlySpan<int> members = GetBucketMembers(in bucket);

            Simulation.NavGroupRuntime.UpsertOrderMoveCommand(
                Simulation.MassNavigationFlow,
                Simulation.AgentState,
                bucket.Token,
                members,
                bucket.TeamId,
                bucket.Destination,
                bucket.FormationMode,
                bucket.RotationRadians);

            if (routeSink != null)
            {
                ApplyRoutedAgentTargets(routeSink, in bucket, members);
            }
        }

        OrderBufferSystem orderBufferSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("MassNavigation runtime requires OrderBufferSystem to complete MassNavigation move orders.");

        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            ref OrderBucket bucket = ref _buckets[bucketIndex];
            if (bucket.MemberCount <= 0 ||
                !Simulation.NavGroupRuntime.TryGetOrderGroup(bucket.Token, out bool arrived) ||
                !arrived)
            {
                continue;
            }

            _completedTokens.Add(bucket.Token);
            ReadOnlySpan<int> members = GetBucketMembers(in bucket);
            for (int i = 0; i < members.Length; i++)
            {
                int memberIndex = members[i];
                Entity member = ResolveControllableAgent(memberIndex, bucket.Token);
                orderBufferSystem.NotifyOrderComplete(member);
            }
        }

        for (int i = 0; i < _completedTokens.Count; i++)
        {
            Simulation.NavGroupRuntime.CompleteOrderGroup(Simulation.MassNavigationFlow, _completedTokens[i]);
        }

        MassNavigationRouteExecutionSink? pruningRouteSink = routeSink;
        if (pruningRouteSink != null)
        {
            pruningRouteSink.EndSync();
            for (int i = 0; i < _completedTokens.Count; i++)
            {
                pruningRouteSink.RemoveOrderToken(_completedTokens[i]);
            }
        }

        Simulation.NavGroupRuntime.PruneInactiveOrderGroups(Simulation.MassNavigationFlow, _activeTokens);
        if (Simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = Simulation.FrameIndex;
        }
    }

    private int ResolveMoveOrderType()
    {
        if (_moveOrderTypeId > 0)
        {
            return _moveOrderTypeId;
        }

        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavigationOrderKeys.Move, out _moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassNavigation runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        return _moveOrderTypeId;
    }

    private MassNavigationRouteExecutionSink? PrepareRouteSink(MassNavigationSimulationRuntime simulation)
    {
        if (_routeSink != null)
        {
            return _routeSink;
        }

        IPathService? pathService = _engine.GetService(CoreServiceKeys.PathService);
        PathStore? pathStore = _engine.GetService(CoreServiceKeys.PathStore);
        PathingConfig? pathingConfig = _engine.GetService(CoreServiceKeys.PathingConfig);
        if (pathService == null && pathStore == null && pathingConfig == null)
        {
            return null;
        }

        if (pathService == null || pathStore == null || pathingConfig == null)
        {
            throw new InvalidOperationException(
                "MassNavigation route execution requires PathService, PathStore, and PathingConfig to be registered together.");
        }

        _routeSink = new MassNavigationRouteExecutionSink(
            pathService,
            pathStore,
            pathingConfig,
            simulation.Plan.Capacity.GroupMembershipAgentCapacity,
            simulation.Plan.Capacity.RouteWaypointCapacityPerAgent);
        _engine.SetService(MassNavigationKeys.RouteExecutionSink, _routeSink);
        return _routeSink;
    }

    private void ApplyRoutedAgentTargets(
        MassNavigationRouteExecutionSink routeSink,
        in OrderBucket bucket,
        ReadOnlySpan<int> members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            int memberIndex = members[i];
            Entity member = ResolveControllableAgent(memberIndex, bucket.Token);
            if (!Simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(memberIndex, out float destinationX, out float destinationY))
            {
                throw new InvalidOperationException(
                    $"MassNavigation route execution could not resolve the order target for token {bucket.Token}, agent {memberIndex}.");
            }

            MassNavigationRouteSinkResult result = routeSink.TrackRouteTarget(
                Simulation,
                _engine.World,
                member,
                memberIndex,
                new Vector2(destinationX, destinationY),
                bucket.Token,
                maxExpanded: 2048,
                maxPoints: Simulation.Plan.Capacity.RouteWaypointCapacityPerAgent);
            if (result.Status == MassNavigationRouteSinkStatus.NoConfiguredAgentType)
            {
                continue;
            }

            if (!result.Tracked)
            {
                throw new InvalidOperationException(
                    $"MassNavigation route execution failed for order {bucket.Token}, agent {memberIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
            }
        }
    }

    private Entity ResolveControllableAgent(int agentIndex, int orderToken)
    {
        if (!Simulation.AgentState.TryGetControllableEntity(agentIndex, out Entity entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime order {orderToken} references controllable agent index {agentIndex}, but no controllable entity is bound at that MassNavigation agent index.");
        }

        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime order {orderToken} references controllable agent index {agentIndex}, but the bound entity is not alive.");
        }

        if (!_engine.World.Has<OrderBuffer>(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime order {orderToken} references controllable agent index {agentIndex}, but the bound entity does not author OrderBuffer.");
        }

        return entity;
    }

    private int GetOrCreateBucket(int token)
    {
        if (_bucketIndexByToken.TryGetValue(token, out int index))
        {
            return index;
        }

        if (_usedBucketCount >= _orderTokenCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation order ingestion required more than runtime.capacity.orderIngestionTokenCapacity {_orderTokenCapacity} active order tokens.");
        }

        index = _usedBucketCount++;
        _bucketIndexByToken[token] = index;
        _buckets[index].Reset(token);

        return index;
    }

    internal void PrepareForRuntime(MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!ReferenceEquals(_binding.Current, simulation))
        {
            throw new InvalidOperationException(
                "MassNavigation order ingestion can only prepare the simulation currently owned by MassNavigationRuntimeBinding.");
        }

        if (_observedRuntimeBindingRevision == _binding.Revision)
        {
            return;
        }

        _observedRuntimeBindingRevision = _binding.Revision;
        _idleScanIntervalFrames = simulation.Cadence.OrderIdleScanIntervalFrames;
        MassNavigationRuntimeCapacityPlan capacity = simulation.Plan.Capacity;
        _orderTokenCapacity = capacity.OrderIngestionTokenCapacity;
        _bucketMemberCapacity = capacity.OrderIngestionMemberCapacity;
        _bucketIndexByToken.Clear();
        _bucketIndexByToken.EnsureCapacity(_orderTokenCapacity);
        _activeTokens.Clear();
        _activeTokens.EnsureCapacity(_orderTokenCapacity);
        _completedTokens.Clear();
        _completedTokens.EnsureCapacity(_orderTokenCapacity);
        _buckets = new OrderBucket[_orderTokenCapacity];
        _bucketWriteOffsets = new int[_orderTokenCapacity];
        _scanAgentIndices = new int[_bucketMemberCapacity];
        _scanBucketIndices = new int[_bucketMemberCapacity];
        _groupedAgentIndices = new int[_bucketMemberCapacity];
        _storageAllocationCount += 5;

        _usedBucketCount = 0;
        _lastIdleScanFrame = -_idleScanIntervalFrames;
        _routeSink = null;
        _engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
        PrepareRouteSink(simulation);
        simulation.ObserveOrderIngestionPreparation(
            _orderTokenCapacity,
            _bucketMemberCapacity,
            _storageAllocationCount);
    }

    private void RequirePreparedRuntime()
    {
        if (_observedRuntimeBindingRevision != _binding.Revision)
        {
            throw new InvalidOperationException(
                "MassNavigation order ingestion was not prepared during the active map-focus transaction.");
        }
    }

    private ReadOnlySpan<int> GetBucketMembers(in OrderBucket bucket)
    {
        return _groupedAgentIndices.AsSpan(bucket.MemberOffset, bucket.MemberCount);
    }

    private struct OrderBucket
    {
        public int Token { get; private set; }
        public int TeamId { get; set; }
        public Vector2 Destination { get; set; }
        public MassNavigationFormationMode FormationMode { get; set; }
        public float RotationRadians { get; set; }
        public int MemberOffset { get; set; }
        public int MemberCount { get; set; }
        private bool HasPayload { get; set; }

        public void AssignOrValidatePayload(
            int teamId,
            Vector2 destination,
            MassNavigationFormationMode formationMode,
            float rotationRadians)
        {
            if (!HasPayload)
            {
                TeamId = teamId;
                Destination = destination;
                FormationMode = formationMode;
                RotationRadians = rotationRadians;
                HasPayload = true;
                return;
            }

            if (TeamId != teamId ||
                Destination.X != destination.X ||
                Destination.Y != destination.Y ||
                FormationMode != formationMode ||
                RotationRadians != rotationRadians)
            {
                throw new InvalidOperationException(
                    $"MassNavigation order ingestion token {Token} has conflicting move-order payloads. Shared order ids must carry one team, destination, formation, and rotation.");
            }
        }

        public void Reset()
        {
            Token = 0;
            TeamId = 0;
            Destination = default;
            FormationMode = MassNavigationFormationMode.None;
            RotationRadians = 0f;
            HasPayload = false;
            MemberOffset = 0;
            MemberCount = 0;
        }

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            FormationMode = MassNavigationFormationMode.None;
            RotationRadians = 0f;
            HasPayload = false;
            MemberOffset = 0;
            MemberCount = 0;
        }
    }
}
