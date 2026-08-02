using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigValidationContext
    {
        public AiConfigValidationContext(
            OrderTypeRegistry orderTypes,
            AbilityDefinitionRegistry? abilities = null,
            IReadOnlyGraphScorer? graphScorer = null)
        {
            OrderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            Abilities = abilities;
            GraphScorer = graphScorer;
        }

        public OrderTypeRegistry OrderTypes { get; }

        public AbilityDefinitionRegistry? Abilities { get; }

        public IReadOnlyGraphScorer? GraphScorer { get; }
    }
}
