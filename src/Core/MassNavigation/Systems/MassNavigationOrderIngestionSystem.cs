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
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, OrderBuffer>()
        .WithNone<SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly int _idleScanIntervalFrames;
    private readonly int _orderTokenCapacity;
    private readonly int _bucketMemberCapacity;
    private readonly Dictionary<int, int> _bucketIndexByToken;
    private readonly List<OrderBucket> _buckets;
    private readonly HashSet<int> _activeTokens;
    private readonly List<int> _completedTokens;
    private readonly Entity[] _orderMemberEntities;
    private readonly byte[] _bucketCommandChanged;
    private MassNavigationRouteExecutionSink? _routeSink;
    private MassNavigationSimulationRuntime? _lastSimulation;
    private int _usedBucketCount;
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame;
    private uint _lastIncomingRevision;

    public MassNavigationOrderIngestionSystem(GameEngine engine, MassNavigationConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _idleScanIntervalFrames = config.Cadence.OrderIdleScanIntervalFrames;
        MassNavigationRuntimeCapacityConfig capacity = config.ScenarioRuntime.RuntimeCapacity;
        _orderTokenCapacity = capacity.OrderIngestionTokenCapacity;
        _bucketMemberCapacity = capacity.OrderIngestionMemberCapacity;
        _bucketIndexByToken = new Dictionary<int, int>(_orderTokenCapacity);
        _buckets = new List<OrderBucket>(_orderTokenCapacity);
        _activeTokens = new HashSet<int>(_orderTokenCapacity);
        _completedTokens = new List<int>(_orderTokenCapacity);
        _orderMemberEntities = new Entity[_bucketMemberCapacity];
        _bucketCommandChanged = new byte[_orderTokenCapacity];
        for (int i = 0; i < _orderTokenCapacity; i++)
        {
            _buckets.Add(new OrderBucket(_bucketMemberCapacity));
        }

        _lastIdleScanFrame = -_idleScanIntervalFrames;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (!ReferenceEquals(_lastSimulation, simulation))
        {
            _lastSimulation = simulation;
            _routeSink = null;
            _lastIncomingRevision = 0;
            _lastIdleScanFrame = -_idleScanIntervalFrames;
        }

        ResolveMoveOrderType();

        OrderBufferSystem orderBufferSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("MassNavigation runtime requires OrderBufferSystem to ingest MassNavigation move orders.");
        uint incomingRevision = orderBufferSystem.IncomingRevision;

        if (simulation.NavGroupRuntime.ActiveOrderGroupCount == 0 &&
            incomingRevision == _lastIncomingRevision &&
            simulation.FrameIndex - _lastIdleScanFrame < _idleScanIntervalFrames)
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
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
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
                OrderBucket bucket = _buckets[bucketIndex];
                bucket.AssignOrValidatePayload(
                    simulation.MassNavigationFlow.GetTeam(agentIndices[index].Value),
                    moveArgs.DestinationCm);
                if (bucket.Members.Count >= _bucketMemberCapacity)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation order ingestion token {token} required more than configured scenarioRuntime.runtimeCapacity.orderIngestionMemberCapacity {_bucketMemberCapacity} members.");
                }

                bucket.AddMember(agentIndices[index].Value);
            }
        }

        MassNavigationRouteExecutionSink? routeSink = ResolveRouteSink(simulation);
        PreflightOrderBuckets(simulation, routeSink);
        try
        {
            routeSink?.BeginSync();
            for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
            {
                OrderBucket bucket = _buckets[bucketIndex];
                if (bucket.Members.Count <= 0)
                {
                    continue;
                }

                simulation.NavGroupRuntime.UpsertOrderMoveCommand(
                    simulation.MassNavigationFlow,
                    simulation.AgentState,
                    bucket.Token,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                    bucket.TeamId,
                    bucket.Destination,
                    out bool commandChanged);

                if (commandChanged)
                {
                    int orderMemberCount = ResolveOrderMemberEntities(simulation, bucket);
                    simulation.FocusOrderTarget(
                        bucket.Destination,
                        _orderMemberEntities.AsSpan(0, orderMemberCount));
                    simulation.MarkCommandApply();
                }

                if (routeSink != null)
                {
                    ApplyRoutedAgentTargets(simulation, routeSink, bucket);
                }
            }

            for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
            {
                OrderBucket bucket = _buckets[bucketIndex];
                if (bucket.Members.Count <= 0 ||
                    !simulation.NavGroupRuntime.TryGetOrderGroup(bucket.Token, out bool arrived) ||
                    !arrived)
                {
                    continue;
                }

                _completedTokens.Add(bucket.Token);
                for (int i = 0; i < bucket.Members.Count; i++)
                {
                    int memberIndex = bucket.Members[i];
                    Entity member = ResolveControllableAgent(simulation, memberIndex, bucket.Token);
                    orderBufferSystem.NotifyOrderComplete(member);
                }
            }

            for (int i = 0; i < _completedTokens.Count; i++)
            {
                simulation.NavGroupRuntime.CompleteOrderGroup(simulation.MassNavigationFlow, _completedTokens[i]);
            }

            MassNavigationRouteExecutionSink? pruningRouteSink = routeSink ?? ResolveRouteSink(simulation);
            if (pruningRouteSink != null)
            {
                pruningRouteSink.EndSync();
                for (int i = 0; i < _completedTokens.Count; i++)
                {
                    pruningRouteSink.RemoveOrderToken(_completedTokens[i]);
                }
            }

            simulation.NavGroupRuntime.PruneInactiveOrderGroups(simulation.MassNavigationFlow, _activeTokens);
        }
        catch
        {
            routeSink?.CancelSync();
            throw;
        }

        _lastIncomingRevision = incomingRevision;
        if (simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = simulation.FrameIndex;
        }
    }

    private void PreflightOrderBuckets(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink? routeSink)
    {
        int newOrderGroupCount = 0;
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            _bucketCommandChanged[bucketIndex] = 0;
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            if (simulation.NavGroupRuntime.RequiresNewOrderGroup(bucket.Token))
            {
                newOrderGroupCount++;
            }

            bool commandChanged = simulation.NavGroupRuntime.PreflightOrderMoveCommand(
                simulation.MassNavigationFlow,
                simulation.AgentState,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination);
            _bucketCommandChanged[bucketIndex] = commandChanged ? (byte)1 : (byte)0;
        }

        simulation.NavGroupRuntime.EnsureCanAllocateNewOrderGroups(newOrderGroupCount);

        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0 || _bucketCommandChanged[bucketIndex] == 0)
            {
                continue;
            }

            int orderMemberCount = ResolveOrderMemberEntities(simulation, bucket);
            simulation.PreflightOrderTarget(
                bucket.Destination,
                _orderMemberEntities.AsSpan(0, orderMemberCount));
        }

        if (routeSink != null)
        {
            PreflightRoutedAgentTargets(simulation, routeSink);
        }
    }

    private int ResolveOrderMemberEntities(MassNavigationSimulationRuntime simulation, OrderBucket bucket)
    {
        int count = bucket.Members.Count;
        for (int i = 0; i < count; i++)
        {
            _orderMemberEntities[i] = ResolveControllableAgent(simulation, bucket.Members[i], bucket.Token);
        }

        return count;
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

    private MassNavigationRouteExecutionSink? ResolveRouteSink(MassNavigationSimulationRuntime simulation)
    {
        IPathService? pathService = _engine.GetService(CoreServiceKeys.PathService);
        PathStore? pathStore = _engine.GetService(CoreServiceKeys.PathStore);
        PathingConfig? pathingConfig = _engine.GetService(CoreServiceKeys.PathingConfig);
        if (pathService == null && pathStore == null && pathingConfig == null)
        {
            _routeSink = null;
            _engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
            return null;
        }

        if (pathService == null || pathStore == null || pathingConfig == null)
        {
            throw new InvalidOperationException(
                "MassNavigation route execution requires PathService, PathStore, and PathingConfig to be registered together.");
        }

        if (_routeSink != null && _routeSink.IsBoundTo(pathService, pathStore, pathingConfig))
        {
            return _routeSink;
        }

        MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
        _routeSink = new MassNavigationRouteExecutionSink(
            pathService,
            pathStore,
            pathingConfig,
            capacity.RouteStateCapacity,
            capacity.RouteWaypointCapacityPerAgent);
        _engine.SetService(MassNavigationKeys.RouteExecutionSink, _routeSink);
        return _routeSink;
    }

    private void PreflightRoutedAgentTargets(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink routeSink)
    {
        routeSink.BeginSync();
        try
        {
            for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
            {
                OrderBucket bucket = _buckets[bucketIndex];
                if (bucket.Members.Count <= 0)
                {
                    continue;
                }

                for (int i = 0; i < bucket.Members.Count; i++)
                {
                    int memberIndex = bucket.Members[i];
                    Entity member = ResolveControllableAgent(simulation, memberIndex, bucket.Token);
                    MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
                    MassNavigationRouteSinkResult result = routeSink.TrackRouteTarget(
                        simulation,
                        _engine.World,
                        member,
                        memberIndex,
                        bucket.Destination,
                        bucket.Token,
                        maxExpanded: capacity.RouteMaxExpandedPerRequest,
                        maxPoints: capacity.RouteWaypointCapacityPerAgent);
                    EnsureRouteTrackAccepted(result, bucket.Token, memberIndex);
                }
            }
        }
        catch
        {
            routeSink.CancelSync();
            throw;
        }

        routeSink.CancelSync();
    }

    private void ApplyRoutedAgentTargets(
        MassNavigationSimulationRuntime simulation,
        MassNavigationRouteExecutionSink routeSink,
        OrderBucket bucket)
    {
        for (int i = 0; i < bucket.Members.Count; i++)
        {
            int memberIndex = bucket.Members[i];
            Entity member = ResolveControllableAgent(simulation, memberIndex, bucket.Token);
            if (!simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(memberIndex, out float destinationX, out float destinationY))
            {
                throw new InvalidOperationException(
                    $"MassNavigation route execution could not resolve the order target for token {bucket.Token}, agent {memberIndex}.");
            }

            MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
            MassNavigationRouteSinkResult result = routeSink.TrackRouteTarget(
                simulation,
                _engine.World,
                member,
                memberIndex,
                new Vector2(destinationX, destinationY),
                bucket.Token,
                maxExpanded: capacity.RouteMaxExpandedPerRequest,
                maxPoints: capacity.RouteWaypointCapacityPerAgent);
            EnsureRouteTrackAccepted(result, bucket.Token, memberIndex);
        }
    }

    private static void EnsureRouteTrackAccepted(
        MassNavigationRouteSinkResult result,
        int orderToken,
        int memberIndex)
    {
        if (result.Status == MassNavigationRouteSinkStatus.NoConfiguredAgentType)
        {
            return;
        }

        if (!result.Tracked)
        {
            throw new InvalidOperationException(
                $"MassNavigation route execution failed for order {orderToken}, agent {memberIndex}: status={result.Status}, pathStatus={result.PathStatus}, domain={result.ResolvedDomain}, errorCode={result.ErrorCode}.");
        }
    }

    private Entity ResolveControllableAgent(MassNavigationSimulationRuntime simulation, int agentIndex, int orderToken)
    {
        if (!simulation.AgentState.TryGetControllableEntity(agentIndex, out Entity entity))
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
                $"MassNavigation order ingestion required more than configured scenarioRuntime.runtimeCapacity.orderIngestionTokenCapacity {_orderTokenCapacity} active order tokens.");
        }

        index = _usedBucketCount++;
        _bucketIndexByToken[token] = index;
        _buckets[index].Reset(token);

        return index;
    }

    private sealed class OrderBucket
    {
        public OrderBucket(int memberCapacity)
        {
            Members = new List<int>(memberCapacity);
        }

        public int Token { get; private set; }
        public int TeamId { get; set; }
        public Vector2 Destination { get; set; }
        public List<int> Members { get; }
        private bool HasPayload { get; set; }

        public void AssignOrValidatePayload(
            int teamId,
            Vector2 destination)
        {
            if (!HasPayload)
            {
                TeamId = teamId;
                Destination = destination;
                HasPayload = true;
                return;
            }

            if (TeamId != teamId ||
                Destination.X != destination.X ||
                Destination.Y != destination.Y)
            {
                throw new InvalidOperationException(
                    $"MassNavigation order ingestion token {Token} has conflicting move-order payloads. Shared order ids must carry one team and destination.");
            }
        }

        public void AddMember(int memberIndex)
        {
            Members.Add(memberIndex);
        }

        public void Reset()
        {
            Token = 0;
            TeamId = 0;
            Destination = default;
            HasPayload = false;
            Members.Clear();
        }

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            HasPayload = false;
            Members.Clear();
        }
    }
}
