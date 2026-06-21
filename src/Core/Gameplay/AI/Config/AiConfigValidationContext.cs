using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigValidationContext
    {
        public AiConfigValidationContext(
            OrderTypeRegistry orderTypes,
            AbilityDefinitionRegistry? abilities = null)
        {
            OrderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            Abilities = abilities;
        }

        public OrderTypeRegistry OrderTypes { get; }

        public AbilityDefinitionRegistry? Abilities { get; }
    }
}
