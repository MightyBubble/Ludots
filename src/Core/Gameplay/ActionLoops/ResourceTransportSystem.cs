using Ludots.Core.Gameplay.GAS;
using Ludots.Platform.Abstractions;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.ActionLoops;

public sealed class ResourceTransportSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription TransportQuery = new QueryDescription()
        .WithAll<ResourceTransportProfile, ResourceTransportState, OrderBuffer, WorldPositionCm, PlayerOwner>();
    private static readonly QueryDescription SinkQuery = new QueryDescription()
        .WithAll<ResourceSinkProfile, WorldPositionCm, AttributeBuffer, PlayerOwner>();

    private readonly OrderQueue _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly IGameplayActionLoopGate _gate;
    private readonly TagOps _tagOps;

    public ResourceTransportSystem(
        World world,
        OrderQueue orders,
        OrderTypeRegistry orderTypes,
        IGameplayActionLoopGate gate,
        TagOps tagOps) : base(world)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
    }

    public override void Update(in float dt)
    {
        if (!_gate.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in TransportQuery))
        {
            ReadOnlySpan<ResourceTransportProfile> profiles = chunk.GetSpan<ResourceTransportProfile>();
            Span<ResourceTransportState> states = chunk.GetSpan<ResourceTransportState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity actor = Unsafe.Add(ref first, index);
                ref ResourceTransportState state = ref states[index];
                ref OrderBuffer buffer = ref buffers[index];
                ref readonly ResourceTransportProfile profile = ref profiles[index];

                if (buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == profile.GatherOrderTypeId)
                {
                    ValidateProfile(in profile);
                    Entity source = buffer.ActiveOrder.Order.Target;
                    if (!IsValidSource(source, profile.ResourceAttributeId))
                    {
                        OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                        state = default;
                        continue;
                    }

                    WorldCmInt2 sourcePosition = World.Get<WorldPositionCm>(source).ToWorldCmInt2();
                    state.TargetXCm = sourcePosition.X;
                    state.TargetYCm = sourcePosition.Y;
                    state.Phase = ResourceTransportPhase.TravellingToSource;
                    OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                    QueueMove(
                        actor,
                        owners[index].PlayerId,
                        sourcePosition.X,
                        sourcePosition.Y,
                        profile.MoveOrderTypeId,
                        ref state);
                    continue;
                }

                if (state.Phase == ResourceTransportPhase.Idle)
                {
                    continue;
                }

                ValidateProfile(in profile);
                if (buffer.HasActive)
                {
                    if (buffer.ActiveOrder.Order.OrderId == state.ExpectedMoveOrderId)
                    {
                        state.ExpectedMoveObserved = 1;
                        continue;
                    }

                    state = default;
                    continue;
                }

                if (state.ExpectedMoveOrderId > 0 && state.ExpectedMoveObserved == 0)
                {
                    continue;
                }

                if (state.Phase == ResourceTransportPhase.TravellingToSource)
                {
                    RequireArrival(
                        in positions[index],
                        state.TargetXCm,
                        state.TargetYCm,
                        profile.ArrivalRadiusCm,
                        "resource source");
                    state.ExpectedMoveOrderId = 0;
                    state.ExpectedMoveObserved = 0;
                    state.RemainingTicks = profile.LoadDurationTicks;
                    state.Phase = ResourceTransportPhase.Loading;
                    continue;
                }

                if (state.Phase == ResourceTransportPhase.Loading)
                {
                    state.RemainingTicks--;
                    if (state.RemainingTicks > 0)
                    {
                        continue;
                    }

                    ResolveUniqueSink(
                        owners[index].PlayerId,
                        profile.ResourceAttributeId,
                        out _,
                        out WorldCmInt2 sinkPosition,
                        out _);
                    state.Phase = ResourceTransportPhase.ReturningToSink;
                    QueueMove(
                        actor,
                        owners[index].PlayerId,
                        sinkPosition.X,
                        sinkPosition.Y,
                        profile.MoveOrderTypeId,
                        ref state);
                    continue;
                }

                if (state.Phase != ResourceTransportPhase.ReturningToSink)
                {
                    throw new InvalidOperationException($"Unsupported resource transport phase '{state.Phase}'.");
                }

                ResolveUniqueSink(
                    owners[index].PlayerId,
                    profile.ResourceAttributeId,
                    out Entity destination,
                    out WorldCmInt2 destinationPosition,
                    out AttributeBuffer destinationAttributes);
                RequireArrival(
                    in positions[index],
                    destinationPosition.X,
                    destinationPosition.Y,
                    profile.ArrivalRadiusCm,
                    "resource sink");
                AttributeMutationOps.SetCurrent(
                    World,
                    destination,
                    profile.ResourceAttributeId,
                    destinationAttributes.GetCurrent(profile.ResourceAttributeId) + profile.CargoAmount,
                    _tagOps);
                state = default;
            }
        }
    }

    private bool IsValidSource(Entity source, int resourceAttributeId)
    {
        return World.IsAlive(source) &&
            World.Has<ResourceSourceProfile>(source) &&
            World.Has<WorldPositionCm>(source) &&
            World.Get<ResourceSourceProfile>(source).ResourceAttributeId == resourceAttributeId;
    }

    private void ResolveUniqueSink(
        int playerId,
        int resourceAttributeId,
        out Entity sink,
        out WorldCmInt2 position,
        out AttributeBuffer attributes)
    {
        sink = Entity.Null;
        position = default;
        attributes = default;
        foreach (ref Chunk chunk in World.Query(in SinkQuery))
        {
            ReadOnlySpan<ResourceSinkProfile> profiles = chunk.GetSpan<ResourceSinkProfile>();
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ReadOnlySpan<AttributeBuffer> buffers = chunk.GetSpan<AttributeBuffer>();
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (owners[index].PlayerId != playerId ||
                    profiles[index].ResourceAttributeId != resourceAttributeId)
                {
                    continue;
                }

                if (sink != Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Resource transport resolved multiple sinks for player {playerId} and attribute {resourceAttributeId}.");
                }

                sink = Unsafe.Add(ref first, index);
                position = ResolveDockPosition(in positions[index], in profiles[index]);
                attributes = buffers[index];
            }
        }

        if (sink == Entity.Null)
        {
            throw new InvalidOperationException(
                $"Resource transport requires exactly one sink for player {playerId} and attribute {resourceAttributeId}.");
        }
    }

    private static WorldCmInt2 ResolveDockPosition(
        in WorldPositionCm sinkPosition,
        in ResourceSinkProfile profile)
    {
        WorldCmInt2 sink = sinkPosition.ToWorldCmInt2();
        try
        {
            return new WorldCmInt2(
                checked(sink.X + profile.DockOffsetXCm),
                checked(sink.Y + profile.DockOffsetYCm));
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "Resource sink dock offset resolves outside the supported world-centimeter range.",
                exception);
        }
    }

    private void QueueMove(
        Entity actor,
        int playerId,
        int x,
        int y,
        int moveOrderTypeId,
        ref ResourceTransportState state)
    {
        var move = new Order
        {
            OrderTypeId = moveOrderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Args = OrderArgs.CreateSingleWorldCm(new Vector3(x, 0f, y)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        if (!_orders.TryEnqueueAssigned(ref move))
        {
            throw new InvalidOperationException("OrderQueue is full while routing a resource transport move.");
        }

        state.ExpectedMoveOrderId = move.OrderId;
        state.ExpectedMoveObserved = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RequireArrival(
        in WorldPositionCm currentPosition,
        int destinationX,
        int destinationY,
        int arrivalRadiusCm,
        string destinationName)
    {
        WorldCmInt2 current = currentPosition.ToWorldCmInt2();
        long dx = current.X - (long)destinationX;
        long dy = current.Y - (long)destinationY;
        long radius = arrivalRadiusCm;
        if ((dx * dx) + (dy * dy) > radius * radius)
        {
            throw new InvalidOperationException(
                $"Resource transport move completed outside its arrival radius for {destinationName}.");
        }
    }

    private void ValidateProfile(in ResourceTransportProfile profile)
    {
        if (!_orderTypes.IsRegistered(profile.GatherOrderTypeId) ||
            !_orderTypes.IsRegistered(profile.MoveOrderTypeId))
        {
            throw new InvalidOperationException("Resource transport profile references an unregistered order type.");
        }
        if ((uint)profile.ResourceAttributeId >= AttributeRegistry.MaxAttributes ||
            !float.IsFinite(profile.CargoAmount) ||
            profile.CargoAmount <= 0f ||
            profile.LoadDurationTicks <= 0 ||
            profile.ArrivalRadiusCm <= 0)
        {
            throw new InvalidOperationException("Resource transport profile contains invalid authored values.");
        }
    }
}
