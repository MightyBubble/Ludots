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

internal sealed class MassNavigationOrderBridgeSystem : ISystem<float>
{
    private const int IdleScanIntervalFrames = 6;

    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgentTag, MassNavigationAgentIndex, MassNavigationControllable, Team, OrderBuffer>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly Dictionary<int, int> _bucketIndexByToken = new();
    private readonly List<OrderBucket> _buckets = new();
    private readonly HashSet<int> _activeTokens = new();
    private readonly List<int> _completedTokens = new();
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame = -IdleScanIntervalFrames;

    public MassNavigationOrderBridgeSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
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
            _simulation.FrameIndex - _lastIdleScanFrame < IdleScanIntervalFrames)
        {
            return;
        }

        _bucketIndexByToken.Clear();
        _activeTokens.Clear();
        _completedTokens.Clear();
        for (int i = 0; i < _buckets.Count; i++)
        {
            _buckets[i].Members.Clear();
        }

        _engine.World.Query(in Query, (Entity entity, ref MassNavigationAgentIndex agentIndex, ref Team team, ref OrderBuffer orders) =>
        {
            if (!orders.HasActive || orders.ActiveOrder.Order.OrderTypeId != _moveOrderTypeId)
            {
                return;
            }

            ref readonly var order = ref orders.ActiveOrder.Order;
            int token = order.OrderId;
            if (token <= 0)
            {
                return;
            }

            _activeTokens.Add(token);
            int bucketIndex = GetOrCreateBucket(token);
            OrderBucket bucket = _buckets[bucketIndex];
            bucket.TeamId = team.Id;
            bucket.Destination = new Vector2(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z);
            bucket.FormationMode = ResolveFormationMode(order.Args.I0, token);
            bucket.RotationRadians = order.Args.F0;
            bucket.Members.Add(agentIndex.Value);
        });

        for (int bucketIndex = 0; bucketIndex < _buckets.Count; bucketIndex++)
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

        for (int bucketIndex = 0; bucketIndex < _buckets.Count; bucketIndex++)
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

    private static MassNavigationFormationMode ResolveFormationMode(int rawValue, int orderToken)
    {
        if (!System.Enum.IsDefined(typeof(MassNavigationFormationMode), rawValue))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod move order {orderToken} references unsupported formation mode value {rawValue}.");
        }

        return (MassNavigationFormationMode)rawValue;
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

        index = _bucketIndexByToken.Count;
        _bucketIndexByToken[token] = index;
        if (index == _buckets.Count)
        {
            _buckets.Add(new OrderBucket(token));
        }
        else
        {
            _buckets[index].Reset(token);
        }

        return index;
    }

    private sealed class OrderBucket
    {
        public OrderBucket(int token)
        {
            Token = token;
        }

        public int Token { get; private set; }
        public int TeamId { get; set; }
        public Vector2 Destination { get; set; }
        public MassNavigationFormationMode FormationMode { get; set; }
        public float RotationRadians { get; set; }
        public List<int> Members { get; } = new();

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


