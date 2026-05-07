using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavOrderBridgeSystem : ISystem<float>
{
    private const int IdleScanIntervalFrames = 6;

    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavAgentTag, MassNavAgentIndex, Team, OrderBuffer>();

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private readonly Dictionary<int, int> _bucketIndexByToken = new();
    private readonly List<OrderBucket> _buckets = new();
    private readonly HashSet<int> _activeTokens = new();
    private readonly List<int> _completedTokens = new();
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame = -IdleScanIntervalFrames;

    public MassNavOrderBridgeSystem(GameEngine engine, MassNavSimulationRuntime simulation)
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (!TryResolveMoveOrderType())
        {
            return;
        }

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

        _engine.World.Query(in Query, (Entity entity, ref MassNavAgentIndex agentIndex, ref Team team, ref OrderBuffer orders) =>
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
            bucket.FormationMode = System.Enum.IsDefined(typeof(MassNavFormationMode), order.Args.I0)
                ? (MassNavFormationMode)order.Args.I0
                : MassNavFormationMode.None;
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
                _simulation.WebParity,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination,
                bucket.FormationMode,
                bucket.RotationRadians);
        }

        if (_engine.GetService(CoreServiceKeys.OrderBufferSystem) is not OrderBufferSystem orderBufferSystem)
        {
            return;
        }

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
                if ((uint)memberIndex >= (uint)_simulation.AgentState.ControllableCount)
                {
                    continue;
                }

                orderBufferSystem.NotifyOrderComplete(_simulation.AgentState.ControllableAgents[memberIndex]);
            }
        }

        for (int i = 0; i < _completedTokens.Count; i++)
        {
            _simulation.NavGroupRuntime.CompleteOrderGroup(_simulation.WebParity, _completedTokens[i]);
        }

        _simulation.NavGroupRuntime.PruneInactiveOrderGroups(_simulation.WebParity, _activeTokens);
        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = _simulation.FrameIndex;
        }
    }

    private bool TryResolveMoveOrderType()
    {
        if (_moveOrderTypeId > 0)
        {
            return true;
        }

        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry)
        {
            return false;
        }

        return registry.TryGetId(MassNavOrderKeys.Move, out _moveOrderTypeId);
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
        public MassNavFormationMode FormationMode { get; set; }
        public float RotationRadians { get; set; }
        public List<int> Members { get; } = new();

        public void Reset(int token)
        {
            Token = token;
            TeamId = 0;
            Destination = default;
            FormationMode = MassNavFormationMode.None;
            RotationRadians = 0f;
            Members.Clear();
        }
    }
}
