using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigValidationContext
    {
        public AiConfigValidationContext(
            OrderTypeRegistry orderTypes,
            GraphProgramRegistry? graphs = null)
        {
            OrderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            Graphs = graphs;
        }

        public OrderTypeRegistry OrderTypes { get; }

        public GraphProgramRegistry? Graphs { get; }
    }
}
