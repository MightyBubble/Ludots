using System;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GasGraphOpRegistry
    {
        private static readonly Lazy<GraphOpRegistry> DefaultRegistry = new(CreateFrozenDefault);

        public static GraphOpRegistry Default => DefaultRegistry.Value;

        public static GraphOpRegistry CreateMutableDefault()
        {
            var registry = new GraphOpRegistry();
            RegisterBuiltins(registry);
            return registry;
        }

        public static void RegisterBuiltins(GraphOpRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                string? name = Enum.GetName(op);
                if (name == null)
                {
                    continue;
                }

                registry.Register(name, (ushort)op);
            }
        }

        private static GraphOpRegistry CreateFrozenDefault()
        {
            GraphOpRegistry registry = CreateMutableDefault();
            registry.Freeze();
            return registry;
        }
    }
}
