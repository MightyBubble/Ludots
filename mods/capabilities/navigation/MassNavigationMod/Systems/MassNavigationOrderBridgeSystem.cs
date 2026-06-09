using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int ActiveScanIntervalFrames = 12;
    private const int ScanBudgetPerFrame = 1024;

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly Dictionary<int, int> _bucketIndexByToken = new();
    private readonly List<OrderBucket> _buckets = new();
    private readonly HashSet<int> _activeTokens = new();
    private readonly List<int> _completedTokens = new();
    private int _moveOrderTypeId;
    private int _lastIdleScanFrame = -IdleScanIntervalFrames;
    private int _lastActiveScanFrame = -ActiveScanIntervalFrames;
    private bool _scanActive;
    private int _scanCursor;

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

        long start = Stopwatch.GetTimestamp();
        ResolveMoveOrderType();

        bool hasNewCommand = _simulation.CommandCountFrame > 0;
        bool hasActiveOrderGroups = _simulation.NavGroupRuntime.ActiveOrderGroupCount > 0;
        if (!_scanActive && !ShouldStartScan(hasNewCommand, hasActiveOrderGroups))
        {
            return;
        }

        if (!_scanActive)
        {
            BeginScan();
        }

        bool scanCompleted = ContinueScan();
        if (!scanCompleted)
        {
            _simulation.ObserveCommandApply((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return;
        }

        SubmitBuckets();
        CompleteArrivedOrders();
        PruneInactiveOrders();

        if (_simulation.NavGroupRuntime.ActiveOrderGroupCount == 0)
        {
            _lastIdleScanFrame = _simulation.FrameIndex;
        }
        else
        {
            _lastActiveScanFrame = _simulation.FrameIndex;
        }

        _simulation.ObserveCommandApply((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }

    private bool ShouldStartScan(bool hasNewCommand, bool hasActiveOrderGroups)
    {
        if (!hasNewCommand &&
            !hasActiveOrderGroups &&
            _simulation.FrameIndex - _lastIdleScanFrame < IdleScanIntervalFrames)
        {
            return false;
        }

        if (!hasNewCommand &&
            hasActiveOrderGroups &&
            _simulation.FrameIndex - _lastActiveScanFrame < ActiveScanIntervalFrames)
        {
            return false;
        }

        return true;
    }

    private void BeginScan()
    {
        _bucketIndexByToken.Clear();
        _activeTokens.Clear();
        _completedTokens.Clear();
        for (int i = 0; i < _buckets.Count; i++)
        {
            _buckets[i].Members.Clear();
        }

        _scanCursor = 0;
        _scanActive = true;
    }

    private bool ContinueScan()
    {
        IReadOnlyList<Entity> agents = _simulation.AgentState.ControllableAgents;
        int count = agents.Count;
        int budget = Math.Max(1, ScanBudgetPerFrame);
        int processed = 0;
        while (_scanCursor < count && processed < budget)
        {
            Entity entity = agents[_scanCursor++];
            processed++;
            if (!_engine.World.IsAlive(entity) ||
                !_engine.World.Has<MassNavigationAgentIndex>(entity) ||
                !_engine.World.Has<Team>(entity) ||
                !_engine.World.Has<OrderBuffer>(entity))
            {
                continue;
            }

            ref MassNavigationAgentIndex agentIndex = ref _engine.World.Get<MassNavigationAgentIndex>(entity);
            ref Team team = ref _engine.World.Get<Team>(entity);
            ref OrderBuffer orders = ref _engine.World.Get<OrderBuffer>(entity);
            CollectActiveOrder(in agentIndex, in team, in orders);
        }

        if (_scanCursor < count)
        {
            return false;
        }

        _scanActive = false;
        _scanCursor = 0;
        return true;
    }

    private void CollectActiveOrder(
        in MassNavigationAgentIndex agentIndex,
        in Team team,
        in OrderBuffer orders)
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
        bucket.FormationMode = Enum.IsDefined(typeof(MassNavigationFormationMode), order.Args.I0)
            ? (MassNavigationFormationMode)order.Args.I0
            : MassNavigationFormationMode.None;
        bucket.RotationRadians = order.Args.F0;
        bucket.Members.Add(agentIndex.Value);
    }

    private void SubmitBuckets()
    {
        bool submittedAny = false;
        for (int bucketIndex = 0; bucketIndex < _buckets.Count; bucketIndex++)
        {
            OrderBucket bucket = _buckets[bucketIndex];
            if (bucket.Members.Count <= 0)
            {
                continue;
            }

            int assigned = _simulation.NavGroupRuntime.UpsertOrderMoveCommand(
                _simulation.MassFlow,
                bucket.Token,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members),
                bucket.TeamId,
                bucket.Destination,
                bucket.FormationMode,
                bucket.RotationRadians);
            if (assigned > 0)
            {
                _simulation.AcceptanceDiagnostics.RecordSubmittedOrder(
                    bucket.Token,
                    assigned,
                    bucket.Destination,
                    bucket.FormationMode,
                    _simulation.AcceptanceDiagnostics.ResolveDefaultStrategy());
                _simulation.AcceptanceDiagnostics.RecordTargetAllocation(
                    bucket.Members.Count,
                    assigned,
                    blockedSlotCount: Math.Max(0, bucket.Members.Count - assigned),
                    fallbackSlotCount: 0,
                    bucket.Destination,
                    bucket.FormationMode,
                    _simulation.MassFlow,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bucket.Members));
                submittedAny = true;
            }
        }

        if (submittedAny)
        {
            _simulation.MarkStructuralChange();
        }
    }

    private void CompleteArrivedOrders()
    {
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
                if ((uint)memberIndex >= (uint)_simulation.AgentState.ControllableCount)
                {
                    continue;
                }

                orderBufferSystem.NotifyOrderComplete(_simulation.AgentState.ControllableAgents[memberIndex]);
            }
        }

        for (int i = 0; i < _completedTokens.Count; i++)
        {
            _simulation.NavGroupRuntime.CompleteOrderGroup(_simulation.MassFlow, _completedTokens[i]);
        }
    }

    private void PruneInactiveOrders()
    {
        _simulation.NavGroupRuntime.PruneInactiveOrderGroups(_simulation.MassFlow, _activeTokens);
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
