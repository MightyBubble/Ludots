using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public static class PlanExecutor
    {
        public static bool TrySubmitOrder(
            World world,
            in ActionOrderSpec spec,
            ReadOnlySpan<ActionBinding> bindings,
            Entity actor,
            ref BlackboardIntBuffer ints,
            ref BlackboardEntityBuffer entities,
            int submitStep,
            OrderQueue queue,
            OrderTypeRegistry orderTypeRegistry,
            out int submittedOrderId)
        {
            submittedOrderId = 0;
            if (orderTypeRegistry == null)
            {
                throw new ArgumentNullException(nameof(orderTypeRegistry));
            }

            if (spec.OrderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"AI plan attempted to submit invalid order type id {spec.OrderTypeId}.");
            }

            if (!orderTypeRegistry.IsRegistered(spec.OrderTypeId))
            {
                throw new InvalidOperationException(
                    $"AI plan attempted to submit unregistered order type id {spec.OrderTypeId}.");
            }

            int abilitySlotIndex = -1;
            Entity target = Entity.Null;
            Entity targetContext = Entity.Null;
            Vector3 moveDestination = default;
            bool hasMoveDestination = false;
            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly var b = ref bindings[i];
                switch (b.Op)
                {
                    case ActionBindingOp.IntToAbilitySlot:
                        if (ints.TryGet(b.SourceKey, out int abilitySlot)) abilitySlotIndex = abilitySlot;
                        break;
                    case ActionBindingOp.EntityToTarget:
                        if (entities.TryGet(b.SourceKey, out var t)) target = t;
                        break;
                    case ActionBindingOp.EntityToTargetContext:
                        if (entities.TryGet(b.SourceKey, out var tc)) targetContext = tc;
                        break;
                    case ActionBindingOp.EntityPositionToMoveDestination:
                        if (entities.TryGet(b.SourceKey, out var destinationEntity) &&
                            world.IsAlive(destinationEntity) &&
                            world.Has<WorldPositionCm>(destinationEntity))
                        {
                            var pos = world.Get<WorldPositionCm>(destinationEntity).Value.ToVector2();
                            moveDestination = new Vector3(pos.X, 0f, pos.Y);
                            hasMoveDestination = true;
                        }
                        break;
                }
            }

            Order order;
            switch (spec.PayloadKind)
            {
                case AiOrderPayloadKind.CastAbility:
                    if (abilitySlotIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"ORDER.BUILDER.ERR.CastAbilitySlotRequired: orderTypeId={spec.OrderTypeId}.");
                    }

                    order = OrderBuilder.CreateCastAbility(
                        spec.OrderTypeId,
                        spec.PlayerId,
                        actor,
                        target,
                        targetContext,
                        abilitySlotIndex,
                        spec.SubmitMode,
                        submitStep);
                    break;

                case AiOrderPayloadKind.TargetEntity:
                    order = OrderBuilder.CreateTargetEntity(
                        spec.OrderTypeId,
                        spec.PlayerId,
                        actor,
                        target,
                        spec.SubmitMode,
                        submitStep);
                    break;

                case AiOrderPayloadKind.MoveToWorldCm:
                    if (!hasMoveDestination)
                    {
                        throw new InvalidOperationException(
                            $"ORDER.BUILDER.ERR.MoveDestinationRequired: orderTypeId={spec.OrderTypeId}.");
                    }

                    order = OrderBuilder.CreateMoveToWorldCm(
                        spec.OrderTypeId,
                        spec.PlayerId,
                        actor,
                        moveDestination,
                        spec.SubmitMode,
                        submitStep);
                    break;

                case AiOrderPayloadKind.Stop:
                    order = OrderBuilder.CreateStop(
                        spec.OrderTypeId,
                        spec.PlayerId,
                        actor,
                        spec.SubmitMode,
                        submitStep);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"ORDER.BUILDER.ERR.UnsupportedAiOrderPayloadKind: kind={spec.PayloadKind}, orderTypeId={spec.OrderTypeId}.");
            }

            queue.EnsureOrderId(ref order);
            submittedOrderId = order.OrderId;
            orderTypeRegistry.TerminalResults.Retain(submittedOrderId);

            bool accepted = false;
            try
            {
                OrderSubmitResult result = queue.SubmitAssigned(ref order);
                accepted = OrderSubmitResultSemantics.IsAccepted(result);
                return accepted;
            }
            finally
            {
                if (!accepted)
                {
                    orderTypeRegistry.TerminalResults.Release(submittedOrderId);
                    submittedOrderId = 0;
                }
            }
        }
    }
}
