using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationOrderIngestionSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgentTag, MassNavigationAgentIndex, MassNavigationControllable, Team, OrderBuffer>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly int _idleScanIntervalFrames;
    private readonly int _orderTokenCapacity;
    private readonly int _bucketMemberCapacity;
    private readonly Dictionary<int, int> _bucketIndexByToken;
    private readonly List<OrderBucket> _buckets;
    private readonly HashSet<int> _activeTokens;
    private readonly List<int> _completedTokens;
    private int _usedBucketCount;
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame;

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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        ResolveMoveOrderType();

        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0 &&
            _simulation.CommandCountFrame <= 0 &&
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
                OrderBucket bucket = _buckets[bucketIndex];
                bucket.TeamId = teams[index].Id;
                bucket.Destination = moveArgs.DestinationCm;
                bucket.FormationMode = moveArgs.FormationMode;
                bucket.RotationRadians = moveArgs.RotationRadians;
                if (bucket.Members.Count >= _bucketMemberCapacity)
                {
                    throw new InvalidOperationException(
                        $"MassNavigation order ingestion token {token} required more than configured scenarioRuntime.runtimeCapacity.orderIngestionMemberCapacity {_bucketMemberCapacity} members.");
                }

                bucket.Members.Add(agentIndices[index].Value);
            }
        }

        for (int bucketIndex = 0; bucketIndex < _usedBucketCount; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            _simulation.NavGroupRuntime.UpsertOrderMoveCommand(
                _simulation.MassFlow,
                _simulation.AgentState,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination,
                bucket.FormationMode,
                bucket.RotationRadians);
        }

        OrderBufferSystem orderBufferSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("MassNavigationMod requires OrderBufferSystem to complete MassNavigation move orders.");

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
            _simulation.NavGroupRuntime.CompleteOrderGroup(_simulation.MassFlow, _completedTokens[i]);
        }

        _simulation.NavGroupRuntime.PruneInactiveOrderGroups(_simulation.MassFlow, _activeTokens);
        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = _simulation.FrameIndex;
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
            throw new InvalidOperationException($"MassNavigationMod requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        return _moveOrderTypeId;
    }

    private Entity ResolveControllableAgent(int agentIndex, int orderToken)
    {
        if (!_simulation.AgentState.TryGetControllableEntity(agentIndex, out Entity entity))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod order {orderToken} references controllable agent index {agentIndex}, but no controllable entity is bound at that MassNavigation agent index.");
        }

        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod order {orderToken} references controllable agent index {agentIndex}, but the bound entity is not alive.");
        }

        if (!_engine.World.Has<OrderBuffer>(entity))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod order {orderToken} references controllable agent index {agentIndex}, but the bound entity does not author OrderBuffer.");
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
        public MassNavigationFormationMode FormationMode { get; set; }
        public float RotationRadians { get; set; }
        public List<int> Members { get; }

        public void Reset()
        {
            Token = 0;
            TeamId = 0;
            Destination = default;
            FormationMode = MassNavigationFormationMode.None;
            RotationRadians = 0f;
            Members.Clear();
        }

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            FormationMode = MassNavigationFormationMode.None;
            RotationRadians = 0f;
            Members.Clear();
        }
    }
}


