using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    /// <summary>Resolves authored graph keys to Registry ids. Fail-closed.</summary>
    public static class GraphRegistryScriptResolver
    {
        [Obsolete("String graph keys are not a behavior entrypoint; use ActionLib names or registered graph ids.")]
        public static int RequireId(string graphKey)
            => ResolveId(graphKey);

        private static int ResolveId(string graphKey)
        {
            if (string.IsNullOrWhiteSpace(graphKey))
            {
                throw new ArgumentException("Graph key is required.", nameof(graphKey));
            }

            int id = GraphIdRegistry.GetId(graphKey);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph '{graphKey}' is not registered in GraphIdRegistry. Load GAS/graphs.json first.");
            }

            return id;
        }

        public static ReadOnlySpan<GraphInstruction> RequireProgram(GraphProgramRegistry registry, int graphId)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (!registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            return program;
        }

        public static ReadOnlySpan<GraphInstruction> RequireProgram(GraphProgramRegistry registry, string graphKey)
            => RequireProgram(registry, ResolveId(graphKey));

        public static int RequireActionId(GraphActionCatalog catalog, string actionName)
            => RequireActionId(catalog, actionName, GraphActionHost.Script);

        public static int RequireActionId(GraphActionCatalog catalog, string actionName, GraphActionHost expectedHost)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new ArgumentException("Action name is required.", nameof(actionName));
            }

            return catalog.Require(actionName, expectedHost);
        }
    }
}
