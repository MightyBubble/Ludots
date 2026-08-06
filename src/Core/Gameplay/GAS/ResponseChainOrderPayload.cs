using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Typed payload helpers for response-chain activate/pass/negate orders.
    /// Runtime still stores the activate-effect template id in OrderArgs.I0.
    /// </summary>
    public static class ResponseChainOrderPayload
    {
        public static void ConfigureActivateEffect(ref Order order, int effectTemplateId)
        {
            if (effectTemplateId <= 0)
            {
                throw new InvalidOperationException(
                    "Response chain activate-effect payload requires a positive effect template id.");
            }

            OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, effectTemplateId);
        }

        public static int RequireActivateEffectTemplateId(in Order order)
        {
            int effectTemplateId = order.Args.I0;
            if (effectTemplateId <= 0)
            {
                throw new InvalidOperationException(
                    "Response chain activate-effect payload requires a positive effect template id.");
            }

            return effectTemplateId;
        }

        public static bool TryGetActivateEffectTemplateId(in Order order, out int effectTemplateId)
        {
            effectTemplateId = order.Args.I0;
            return effectTemplateId > 0;
        }
    }
}
