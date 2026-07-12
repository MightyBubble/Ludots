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
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly int _idleScanIntervalFrames;
    private readonly int _orderTokenCapacity;
    private readonly int _bucketMemberCapacity;
    private readonly Dictionary<int, int> _bucketIndexByToken;
    private readonly List<OrderBucket> _buckets;
    private readonly HashSet<int> _activeTokens;
    private readonly List<int> _completedTokens;
    private readonly List<int> _staleSignatureTokens;
    private readonly Dictionary<int, OrderApplicationSignature> _appliedOrderSignatures;
    private readonly Entity[] _orderMemberEntities;
    private MassNavigationRouteExecutionSink? _routeSink;
    private int _usedBucketCount;
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame;
    private uint _lastIncomingRevision;

    public MassNavigationOrderIngestionSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
        _idleScanIntervalFrames = simulation.Cadence.OrderIdleScanIntervalFrames;
        MassNavigationRuntimeCapacityConfig capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity;
        _orderTokenCapacity = capacity.OrderIngestionTokenCapacity;
        _bucketMemberCapacity = capacity.OrderIngestionMemberCapacity;
        _bucketIndexByToken = new Dictionary<int, int>(_orderTokenCapacity);
        _buckets = new List<OrderBucket>(_orderTokenCapacity);
        _activeTokens = new HashSet<int>(_orderTokenCapacity);
        _completedTokens = new List<int>(_orderTokenCapacity);
        _staleSignatureTokens = new List<int>(_orderTokenCapacity);
        _appliedOrderSignatures = new Dictionary<int, OrderApplicationSignature>(_orderTokenCapacity);
        _orderMemberEntities = new Entity[_bucketMemberCapacity];
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
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        ResolveMoveOrderType();

        OrderBufferSystem orderBufferSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("MassNavigation runtime requires OrderBufferSystem to ingest MassNavigation move orders.");
        uint incomingRevision = orderBufferSystem.IncomingRevision;

        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0 &&
            incomingRevision == _lastIncomingRevision &&
            _simulation.FrameIndex - _lastIdleScanFrame < _idleScanIntervalFrames)
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
                    _simulation.MassNavigationFlow.GetTeam(agentIndices[index].Value),
                    moveArgs.DestinationCm);
                if (bucket.Members.Count >= _bucketMemberCapacity)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation order ingestion token {token} required more than configured scenarioRuntime.runtimeCapacity.orderIngestionMemberCapacity {_bucketMemberCapacity} members.");
                }

                bucket.AddMember(agentIndices[index].Value);
            }
        }

        MassNavigationRouteExecutionSink? routeSink = ResolveRouteSink();
        routeSink?.BeginSync();
        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            OrderApplicationSignature signature = bucket.CreateSignature();
            bool commandChanged = !_appliedOrderSignatures.TryGetValue(bucket.Token, out OrderApplicationSignature previous) ||
                previous != signature;
            _appliedOrderSignatures[bucket.Token] = signature;

            _simulation.NavGroupRuntime.UpsertOrderMoveCommand(
                _simulation.MassNavigationFlow,
                _simulation.AgentState,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination);

            if (commandChanged)
            {
                int orderMemberCount = ResolveOrderMemberEntities(bucket);
                _simulation.FocusOrderTarget(
                    bucket.Destination,
                    _orderMemberEntities.AsSpan(0, orderMemberCount));
                _simulation.MarkCommandApply();
            }

            if (routeSink != null)
            {
                ApplyRoutedAgentTargets(routeSink, bucket);
            }
        }

        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0 ||
                !_simulation.NavGroupRuntime.TryGetOrderGroup(bucket.Token, out bool arrived) ||
                !arrived)
            {
                continue;
            }

            _completedTokens.Add(bucket.Token);
            for (int i = 0; i < bucket.Members.Count; i++)
            {
                int memberIndex = bucket.Members[i];
                Entity member = ResolveControllableAgent(memberIndex, bucket.Token);
                orderBufferSystem.NotifyOrderComplete(member);
            }
        }

        for (int i = 0; i < _completedTokens.Count; i++)
        {
            _simulation.NavGroupRuntime.CompleteOrderGroup(_simulation.MassNavigationFlow, _completedTokens[i]);
        }

        MassNavigationRouteExecutionSink? pruningRouteSink = routeSink ?? ResolveRouteSink();
        if (pruningRouteSink != null)
        {
            pruningRouteSink.EndSync();
            for (int i = 0; i < _completedTokens.Count; i++)
            {
                pruningRouteSink.RemoveOrderToken(_completedTokens[i]);
            }
        }

        _simulation.NavGroupRuntime.PruneInactiveOrderGroups(_simulation.MassNavigationFlow, _activeTokens);
        PruneInactiveOrderSignatures();
        _lastIncomingRevision = incomingRevision;
        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = _simulation.FrameIndex;
        }
    }

    private int ResolveOrderMemberEntities(OrderBucket bucket)
    {
        int count = bucket.Members.Count;
        for (int i = 0; i < count; i++)
        {
            _orderMemberEntities[i] = ResolveControllableAgent(bucket.Members[i], bucket.Token);
        }

        return count;
    }

    private void PruneInactiveOrderSignatures()
    {
        _staleSignatureTokens.Clear();
        foreach (int token in _appliedOrderSignatures.Keys)
        {
            if (!_activeTokens.Contains(token))
            {
                _staleSignatureTokens.Add(token);
            }
        }

        for (int i = 0; i < _staleSignatureTokens.Count; i++)
        {
            _appliedOrderSignatures.Remove(_staleSignatureTokens[i]);
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

    private MassNavigationRouteExecutionSink? ResolveRouteSink()
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

        MassNavigationRuntimeCapacityConfig capacity = _simulation.Config.ScenarioRuntime.RuntimeCapacity;
        _routeSink = new MassNavigationRouteExecutionSink(
            pathService,
            pathStore,
            pathingConfig,
            capacity.RouteStateCapacity,
            capacity.RouteWaypointCapacityPerAgent);
        _engine.SetService(MassNavigationKeys.RouteExecutionSink, _routeSink);
        return _routeSink;
    }

    private void ApplyRoutedAgentTargets(MassNavigationRouteExecutionSink routeSink, OrderBucket bucket)
    {
        for (int i = 0; i < bucket.Members.Count; i++)
        {
            int memberIndex = bucket.Members[i];
            Entity member = ResolveControllableAgent(memberIndex, bucket.Token);
            if (!_simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(memberIndex, out float destinationX, out float destinationY))
            {
                throw new InvalidOperationException(
                    $"MassNavigation route execution could not resolve the order target for token {bucket.Token}, agent {memberIndex}.");
            }

            MassNavigationRouteSinkResult result = routeSink.TrackRouteTarget(
                _simulation,
                _engine.World,
                member,
                memberIndex,
                new Vector2(destinationX, destinationY),
                bucket.Token,
                maxExpanded: 2048,
                maxPoints: 64);
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
        if (!_simulation.AgentState.TryGetControllableEntity(agentIndex, out Entity entity))
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
        private int MemberHash { get; set; }

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
            MemberHash = unchecked((MemberHash * 397) ^ memberIndex);
        }

        public OrderApplicationSignature CreateSignature()
        {
            return new OrderApplicationSignature(
                TeamId,
                Destination,
                Members.Count,
                MemberHash);
        }

        public void Reset()
        {
            Token = 0;
            TeamId = 0;
            Destination = default;
            HasPayload = false;
            MemberHash = 0;
            Members.Clear();
        }

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            HasPayload = false;
            MemberHash = 0;
            Members.Clear();
        }
    }

    private readonly record struct OrderApplicationSignature(
        int TeamId,
        Vector2 Destination,
        int MemberCount,
        int MemberHash);
}
