using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    /// <summary>Resolves authored graph keys to Registry ids. Fail-closed.</summary>
    public static class GraphRegistryScriptResolver
    {
        public static int RequireId(string graphKey)
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
            => RequireProgram(registry, RequireId(graphKey));
    }
}
