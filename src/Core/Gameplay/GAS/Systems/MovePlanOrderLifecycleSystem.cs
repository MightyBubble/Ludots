using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.Gameplay.GAS.Systems;

/// <summary>Consumes typed MovePlanning results and owns OrderBuffer completion.</summary>
public sealed class MovePlanOrderLifecycleSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<OrderBuffer, MovePlanExecutionIntent, MovePlanExecutionResult>()
        .WithNone<SuspendedTag>();

    private readonly OrderTypeRegistry _orderTypeRegistry;
    private readonly int _moveOrderTypeId;

    public MovePlanOrderLifecycleSystem(World world, OrderTypeRegistry orderTypeRegistry, int moveOrderTypeId)
        : base(world)
    {
        _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
        if (moveOrderTypeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moveOrderTypeId));
        }

        _moveOrderTypeId = moveOrderTypeId;
    }

    public override void Update(in float dt)
    {
        foreach (ref var chunk in World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            Span<MovePlanExecutionIntent> intents = chunk.GetSpan<MovePlanExecutionIntent>();
            Span<MovePlanExecutionResult> results = chunk.GetSpan<MovePlanExecutionResult>();
            foreach (int index in chunk)
            {
                ref OrderBuffer buffer = ref buffers[index];
                ref MovePlanExecutionResult result = ref results[index];
                if (!buffer.HasActive ||
                    buffer.ActiveOrder.Order.OrderTypeId != _moveOrderTypeId ||
                    result.Kind == MovePlanExecutionResultKind.None ||
                    result.CommandGroupToken != buffer.ActiveOrder.Order.CommandCorrelationId)
                {
                    continue;
                }

                Entity entity = chunk.Entity(index);
                if (result.Kind == MovePlanExecutionResultKind.Arrived)
                {
                    OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                }
                else
                {
                    OrderSubmitter.CancelCurrent(World, entity, _orderTypeRegistry);
                }
                intents[index] = default;
                results[index] = default;
            }
        }
    }
}
