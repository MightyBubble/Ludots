using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.Gameplay.GAS.Systems;

/// <summary>
/// Projects an activated spatial move order into the neutral MovePlanning contract.
/// Order interpretation stays in the GAS phase; execution consumers never inspect OrderBuffer.
/// </summary>
public sealed class MovePlanOrderProjectionSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<OrderBuffer, MovePlanExecutionIntent, MovePlanExecutionResult>()
        .WithNone<SuspendedTag>();

    private readonly int _moveOrderTypeId;
    public MovePlanOrderProjectionSystem(World world, int moveOrderTypeId)
        : base(world)
    {
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
                ref MovePlanExecutionIntent intent = ref intents[index];
                ref MovePlanExecutionResult result = ref results[index];
                if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _moveOrderTypeId)
                {
                    if (intent.Mode == MovePlanExecutionMode.CommandGroup)
                    {
                        int projectedToken = intent.CommandGroupToken;
                        intent = default;
                        if (result.CommandGroupToken == projectedToken)
                        {
                            result = default;
                        }
                    }
                    continue;
                }

                ref readonly Order order = ref buffer.ActiveOrder.Order;
                if (order.OrderId <= 0 ||
                    order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                    order.Args.Spatial.Mode != OrderCollectionMode.Single ||
                    !float.IsFinite(order.Args.Spatial.WorldCm.X) ||
                    !float.IsFinite(order.Args.Spatial.WorldCm.Z))
                {
                    intent = default;
                    result = new MovePlanExecutionResult
                    {
                        CommandGroupToken = order.OrderId,
                        Kind = MovePlanExecutionResultKind.Failed,
                        FailureReason = MovePlanFailureReason.ExecutionUnavailable,
                    };
                    continue;
                }

                if (intent.Mode != MovePlanExecutionMode.CommandGroup ||
                    intent.CommandGroupToken != order.OrderId)
                {
                    intent = default;
                    intent.CommandGroupToken = order.OrderId;
                }

                intent.TargetWorldCm = new System.Numerics.Vector2(
                    order.Args.Spatial.WorldCm.X,
                    order.Args.Spatial.WorldCm.Z);
                intent.Mode = MovePlanExecutionMode.CommandGroup;
                intent.HasTarget = 1;
                result = default;
            }
        }
    }
}
