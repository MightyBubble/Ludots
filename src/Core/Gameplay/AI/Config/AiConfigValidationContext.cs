using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigValidationContext
    {
        public AiConfigValidationContext(
            OrderTypeRegistry orderTypes,
            IReadOnlyGraphScorer? graphScorer = null)
        {
            OrderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            GraphScorer = graphScorer;
        }

        public OrderTypeRegistry OrderTypes { get; }

        public IReadOnlyGraphScorer? GraphScorer { get; }
    }
}
