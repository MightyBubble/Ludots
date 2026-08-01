using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public static class PlanExecutor
    {
        public static bool TrySubmitOrder(
            in ActionOrderSpec spec,
            ReadOnlySpan<ActionBinding> bindings,
            Entity actor,
            ref BlackboardIntBuffer ints,
            ref BlackboardEntityBuffer entities,
            int submitStep,
            OrderQueue queue,
            OrderTypeRegistry? orderTypeRegistry = null)
        {
            if (spec.OrderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"AI plan attempted to submit invalid order type id {spec.OrderTypeId}.");
            }

            if (orderTypeRegistry != null && !orderTypeRegistry.IsRegistered(spec.OrderTypeId))
            {
                throw new InvalidOperationException(
                    $"AI plan attempted to submit unregistered order type id {spec.OrderTypeId}.");
            }

            var order = OrderBuilder.Create(
                spec.OrderTypeId,
                spec.PlayerId,
                actor,
                Entity.Null,
                Entity.Null,
                spec.SubmitMode,
                submitStep);

            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly var b = ref bindings[i];
                switch (b.Op)
                {
                    case ActionBindingOp.IntToOrderArg0:
                        if (ints.TryGet(b.SourceKey, out int i0)) OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, i0);
                        break;
                    case ActionBindingOp.IntToOrderArg1:
                        if (ints.TryGet(b.SourceKey, out int i1)) OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I1, i1);
                        break;
                    case ActionBindingOp.IntToOrderArg2:
                        if (ints.TryGet(b.SourceKey, out int i2)) OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I2, i2);
                        break;
                    case ActionBindingOp.IntToOrderArg3:
                        if (ints.TryGet(b.SourceKey, out int i3)) OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I3, i3);
                        break;
                    case ActionBindingOp.EntityToTarget:
                        if (entities.TryGet(b.SourceKey, out var t)) order.Target = t;
                        break;
                    case ActionBindingOp.EntityToTargetContext:
                        if (entities.TryGet(b.SourceKey, out var tc)) order.TargetContext = tc;
                        break;
                }
            }

            return queue.TryEnqueue(in order);
        }
    }
}
