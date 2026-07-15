using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Formation;

public sealed class FormationOrderSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FormationAnchorState, FormationCommandState, OrderBuffer, WorldPositionCm>()
        .WithNone<SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly IFormationRuntimeGate _runtimeGate;
    private readonly Entity[] _completedEntities;
    private readonly int[] _moveBatchOrderIds;
    private readonly int[] _moveBatchPlayerIds;
    private readonly int[] _moveBatchSubmitSteps;
    private readonly OrderSubmitMode[] _moveBatchSubmitModes;
    private readonly float[] _moveBatchTargetXCm;
    private readonly float[] _moveBatchTargetYCm;
    private readonly float[] _moveBatchPositionSumXCm;
    private readonly float[] _moveBatchPositionSumYCm;
    private readonly int[] _moveBatchActorCounts;
    private readonly int[] _moveBatchHashSlots;
    private readonly int _moveBatchHashMask;
    private int _moveBatchCount;
    private int _moveOrderTypeId;
    private int _rotateOrderTypeId;

    public FormationOrderSystem(
        GameEngine engine,
        IFormationRuntimeGate runtimeGate,
        int capacity)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtimeGate = runtimeGate ?? throw new ArgumentNullException(nameof(runtimeGate));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _completedEntities = new Entity[capacity];
        _moveBatchOrderIds = new int[capacity];
        _moveBatchPlayerIds = new int[capacity];
        _moveBatchSubmitSteps = new int[capacity];
        _moveBatchSubmitModes = new OrderSubmitMode[capacity];
        _moveBatchTargetXCm = new float[capacity];
        _moveBatchTargetYCm = new float[capacity];
        _moveBatchPositionSumXCm = new float[capacity];
        _moveBatchPositionSumYCm = new float[capacity];
        _moveBatchActorCounts = new int[capacity];
        int hashCapacity = NextPowerOfTwo(checked(capacity * 2));
        _moveBatchHashSlots = new int[hashCapacity];
        _moveBatchHashMask = hashCapacity - 1;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtimeGate.IsFormationRuntimeActive(_engine))
        {
            return;
        }

        ResolveOrderTypes();
        int pendingCount = CountPendingOrders();
        if (pendingCount <= 0)
        {
            return;
        }

        if (pendingCount > _completedEntities.Length)
        {
            throw new InvalidOperationException(
                $"Formation order processing requires {pendingCount} entries, exceeding configured capacity {_completedEntities.Length}.");
        }

        ValidatePendingOrders();
        BuildMoveBatches();
        int completedCount = 0;
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<FormationCommandState> commands = chunk.GetSpan<FormationCommandState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                ref OrderBuffer buffer = ref buffers[index];
                if (!buffer.HasActive)
                {
                    continue;
                }

                ref readonly Order order = ref buffer.ActiveOrder.Order;
                if (order.OrderTypeId == _moveOrderTypeId)
                {
                    int batchIndex = FindMoveBatch(in order);
                    int actorCount = _moveBatchActorCounts[batchIndex];
                    float targetXCm = order.Args.Spatial.WorldCm.X;
                    float targetYCm = order.Args.Spatial.WorldCm.Z;
                    if (actorCount > 1)
                    {
                        float centerXCm = _moveBatchPositionSumXCm[batchIndex] / actorCount;
                        float centerYCm = _moveBatchPositionSumYCm[batchIndex] / actorCount;
                        targetXCm += worldPositions[index].Value.X.ToFloat() - centerXCm;
                        targetYCm += worldPositions[index].Value.Y.ToFloat() - centerYCm;
                    }

                    commands[index].TargetCenterXCm = FormationNumericEncoding.RoundCm(targetXCm);
                    commands[index].TargetCenterYCm = FormationNumericEncoding.RoundCm(targetYCm);
                    commands[index].HasMoveTarget = 1;
                }
                else if (order.OrderTypeId == _rotateOrderTypeId)
                {
                    commands[index].TargetFacingMicroRad = FormationNumericEncoding.EncodeRadians(
                        FormationTargetPlanner.NormalizeFacingRadians(order.Args.F0));
                }
                else
                {
                    continue;
                }

                _completedEntities[completedCount++] = Unsafe.Add(ref entityFirst, index);
            }
        }

        OrderBufferSystem buffersSystem = _engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("Formation orders require OrderBufferSystem.");
        for (int i = 0; i < completedCount; i++)
        {
            buffersSystem.NotifyOrderComplete(_completedEntities[i]);
        }
    }

    private void BuildMoveBatches()
    {
        _moveBatchCount = 0;
        Array.Clear(_moveBatchHashSlots);
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            foreach (int index in chunk)
            {
                if (!buffers[index].HasActive)
                {
                    continue;
                }

                ref readonly Order order = ref buffers[index].ActiveOrder.Order;
                if (order.OrderTypeId != _moveOrderTypeId)
                {
                    continue;
                }

                int batchIndex = FindMoveBatch(in order, allowCreate: true);
                _moveBatchPositionSumXCm[batchIndex] += worldPositions[index].Value.X.ToFloat();
                _moveBatchPositionSumYCm[batchIndex] += worldPositions[index].Value.Y.ToFloat();
                _moveBatchActorCounts[batchIndex]++;
            }
        }
    }

    private int FindMoveBatch(in Order order, bool allowCreate = false)
    {
        float targetXCm = order.Args.Spatial.WorldCm.X;
        float targetYCm = order.Args.Spatial.WorldCm.Z;
        int hashSlot = ComputeMoveBatchHash(in order, targetXCm, targetYCm) & _moveBatchHashMask;
        while (true)
        {
            int batchIndexPlusOne = _moveBatchHashSlots[hashSlot];
            if (batchIndexPlusOne == 0)
            {
                if (!allowCreate)
                {
                    throw new InvalidOperationException(
                        $"Formation move order {order.OrderId} was not included in the validated move batch snapshot.");
                }

                if (_moveBatchCount >= _moveBatchActorCounts.Length)
                {
                    throw new InvalidOperationException(
                        $"Formation move batches exceed configured capacity {_moveBatchActorCounts.Length}.");
                }

                int createdBatchIndex = _moveBatchCount++;
                _moveBatchOrderIds[createdBatchIndex] = order.OrderId;
                _moveBatchPlayerIds[createdBatchIndex] = order.PlayerId;
                _moveBatchSubmitSteps[createdBatchIndex] = order.SubmitStep;
                _moveBatchSubmitModes[createdBatchIndex] = order.SubmitMode;
                _moveBatchTargetXCm[createdBatchIndex] = targetXCm;
                _moveBatchTargetYCm[createdBatchIndex] = targetYCm;
                _moveBatchPositionSumXCm[createdBatchIndex] = 0f;
                _moveBatchPositionSumYCm[createdBatchIndex] = 0f;
                _moveBatchActorCounts[createdBatchIndex] = 0;
                _moveBatchHashSlots[hashSlot] = createdBatchIndex + 1;
                return createdBatchIndex;
            }

            int batchIndex = batchIndexPlusOne - 1;
            if (_moveBatchOrderIds[batchIndex] == order.OrderId &&
                _moveBatchPlayerIds[batchIndex] == order.PlayerId &&
                _moveBatchSubmitSteps[batchIndex] == order.SubmitStep &&
                _moveBatchSubmitModes[batchIndex] == order.SubmitMode &&
                _moveBatchTargetXCm[batchIndex] == targetXCm &&
                _moveBatchTargetYCm[batchIndex] == targetYCm)
            {
                return batchIndex;
            }

            hashSlot = (hashSlot + 1) & _moveBatchHashMask;
        }
    }

    private static int ComputeMoveBatchHash(
        in Order order,
        float targetXCm,
        float targetYCm)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + order.OrderId;
            hash = (hash * 31) + order.PlayerId;
            hash = (hash * 31) + order.SubmitStep;
            hash = (hash * 31) + (int)order.SubmitMode;
            hash = (hash * 31) + targetXCm.GetHashCode();
            hash = (hash * 31) + targetYCm.GetHashCode();
            return hash;
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private void ValidatePendingOrders()
    {
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            foreach (int index in chunk)
            {
                if (!buffers[index].HasActive)
                {
                    continue;
                }

                ref readonly Order order = ref buffers[index].ActiveOrder.Order;
                if ((order.OrderTypeId == _moveOrderTypeId || order.OrderTypeId == _rotateOrderTypeId) &&
                    order.OrderId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Formation order requires a positive OrderId before batch classification; got {order.OrderId}.");
                }

                if (order.OrderTypeId == _moveOrderTypeId)
                {
                    if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                        order.Args.Spatial.Mode != OrderCollectionMode.Single ||
                        !float.IsFinite(order.Args.Spatial.WorldCm.X) ||
                        !float.IsFinite(order.Args.Spatial.WorldCm.Y) ||
                        !float.IsFinite(order.Args.Spatial.WorldCm.Z) ||
                        order.Args.I0 != 0 ||
                        order.Args.I1 != 0 ||
                        order.Args.I2 != 0 ||
                        order.Args.I3 != 0 ||
                        order.Args.F0 != 0f ||
                        order.Args.F1 != 0f ||
                        order.Args.F2 != 0f ||
                        order.Args.F3 != 0f ||
                        order.Args.Spatial.A0 != 0 ||
                        order.Args.Spatial.A1 != 0 ||
                        order.Args.Spatial.A2 != 0 ||
                        order.Args.Spatial.PointCount != 0)
                    {
                        throw new InvalidOperationException(
                            $"Formation move order {order.OrderId} requires one WorldCm target and no extra payload.");
                    }
                }
                else if (order.OrderTypeId == _rotateOrderTypeId &&
                         (!float.IsFinite(order.Args.F0) ||
                          order.Args.Spatial.Kind != OrderSpatialKind.None ||
                          order.Args.Spatial.Mode != OrderCollectionMode.None ||
                          order.Args.I0 != 0 ||
                          order.Args.I1 != 0 ||
                          order.Args.I2 != 0 ||
                          order.Args.I3 != 0 ||
                          order.Args.F1 != 0f ||
                          order.Args.F2 != 0f ||
                          order.Args.F3 != 0f ||
                          order.Args.Spatial.A0 != 0 ||
                          order.Args.Spatial.A1 != 0 ||
                          order.Args.Spatial.A2 != 0 ||
                          order.Args.Spatial.PointCount != 0))
                {
                    throw new InvalidOperationException(
                        $"Formation rotate order {order.OrderId} requires one finite target facing and no spatial or integer payload.");
                }
            }
        }
    }

    private int CountPendingOrders()
    {
        int count = 0;
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            foreach (int index in chunk)
            {
                if (buffers[index].HasActive &&
                    (buffers[index].ActiveOrder.Order.OrderTypeId == _moveOrderTypeId ||
                     buffers[index].ActiveOrder.Order.OrderTypeId == _rotateOrderTypeId))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void ResolveOrderTypes()
    {
        if (_moveOrderTypeId != 0 && _rotateOrderTypeId != 0)
        {
            return;
        }

        OrderTypeRegistry orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("Formation orders require OrderTypeRegistry.");
        if (!orderTypes.TryGetId(FormationOrderKeys.Move, out _moveOrderTypeId) ||
            !orderTypes.TryGetId(FormationOrderKeys.Rotate, out _rotateOrderTypeId))
        {
            throw new InvalidOperationException(
                $"Formation requires '{FormationOrderKeys.Move}' and '{FormationOrderKeys.Rotate}' order types.");
        }
    }
}
