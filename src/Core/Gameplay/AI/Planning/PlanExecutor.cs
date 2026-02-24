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
            OrderQueue queue)
        {
            var order = new Order
            {
                OrderTagId = spec.OrderTagId,
                PlayerId = spec.PlayerId,
                Actor = actor,
                Target = default,
                TargetContext = default,
                Args = default,
                SubmitStep = submitStep,
                SubmitMode = spec.SubmitMode
            };

            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly var b = ref bindings[i];
                switch (b.Op)
                {
                    case ActionBindingOp.IntToOrderI0:
                        if (ints.TryGet(b.SourceKey, out int i0)) order.Args.I0 = i0;
                        break;
                    case ActionBindingOp.IntToOrderI1:
                        if (ints.TryGet(b.SourceKey, out int i1)) order.Args.I1 = i1;
                        break;
                    case ActionBindingOp.IntToOrderI2:
                        if (ints.TryGet(b.SourceKey, out int i2)) order.Args.I2 = i2;
                        break;
                    case ActionBindingOp.IntToOrderI3:
                        if (ints.TryGet(b.SourceKey, out int i3)) order.Args.I3 = i3;
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

