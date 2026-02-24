using System;
using System.Collections.Generic;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphValidator
    {
        public static List<GraphDiagnostic> Validate(GraphConfig cfg)
        {
            var diagnostics = new List<GraphDiagnostic>();

            if (cfg == null)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingGraphId, "Graph config is null.", string.Empty));
                return diagnostics;
            }

            string graphId = cfg.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(graphId))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingGraphId, "Graph id is missing.", string.Empty));
                return diagnostics;
            }

            if (string.IsNullOrWhiteSpace(cfg.Entry))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingEntry, "Graph entry node id is missing.", graphId));
            }

            var nodesById = new Dictionary<string, GraphNodeConfig>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cfg.Nodes.Count; i++)
            {
                var node = cfg.Nodes[i];
                if (node == null) continue;

                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Node at index {i} is missing id.", graphId));
                    continue;
                }

                if (!nodesById.TryAdd(node.Id, node))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.DuplicateNodeId, $"Duplicate node id '{node.Id}'.", graphId, node.Id));
                }

                if (!GraphNodeOpParser.TryParse(node.Op, out _))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.UnknownNodeOp, $"Unknown node op '{node.Op}'.", graphId, node.Id));
                }
            }

            if (!string.IsNullOrWhiteSpace(cfg.Entry) && !nodesById.ContainsKey(cfg.Entry))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Entry node '{cfg.Entry}' not found.", graphId, cfg.Entry));
            }

            foreach (var kvp in nodesById)
            {
                var node = kvp.Value;

                if (!string.IsNullOrWhiteSpace(node.Next) && !nodesById.ContainsKey(node.Next))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Node '{node.Id}' references missing Next '{node.Next}'.", graphId, node.Id));
                }

                if (node.Inputs != null)
                {
                    for (int i = 0; i < node.Inputs.Count; i++)
                    {
                        var inputId = node.Inputs[i];
                        if (string.IsNullOrWhiteSpace(inputId)) continue;
                        if (!nodesById.ContainsKey(inputId))
                        {
                            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Node '{node.Id}' input[{i}] references missing node '{inputId}'.", graphId, node.Id));
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(cfg.Entry) && nodesById.ContainsKey(cfg.Entry))
            {
                DetectNextCycle(cfg.Entry, nodesById, graphId, diagnostics, out var reachable);

                foreach (var kvp in nodesById)
                {
                    if (!reachable.Contains(kvp.Key))
                    {
                        diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Warning, GraphDiagnosticCodes.UnreachableNode, $"Node '{kvp.Key}' is unreachable from entry.", graphId, kvp.Key));
                    }
                }

                DetectDataDependencyCycle(nodesById, graphId, diagnostics);
            }

            return diagnostics;
        }

        private static void DetectNextCycle(
            string entry,
            Dictionary<string, GraphNodeConfig> nodesById,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            out HashSet<string> reachable)
        {
            reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string current = entry;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (!nodesById.TryGetValue(current, out var node)) break;

                if (!visited.Add(current))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.NextCycle, $"Cycle detected in Next chain at node '{current}'.", graphId, current));
                    break;
                }

                reachable.Add(current);
                current = node.Next ?? string.Empty;
            }
        }

        private static void DetectDataDependencyCycle(
            Dictionary<string, GraphNodeConfig> nodesById,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            var state = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in nodesById)
            {
                state[kvp.Key] = 0;
            }

            foreach (var kvp in nodesById)
            {
                if (state[kvp.Key] != 0) continue;
                if (Visit(kvp.Key, nodesById, state))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.DataDependencyCycle, "Cycle detected in data dependency graph.", graphId));
                    break;
                }
            }
        }

        private static bool Visit(string nodeId, Dictionary<string, GraphNodeConfig> nodesById, Dictionary<string, byte> state)
        {
            state[nodeId] = 1;
            var node = nodesById[nodeId];
            if (node.Inputs != null)
            {
                for (int i = 0; i < node.Inputs.Count; i++)
                {
                    var depId = node.Inputs[i];
                    if (string.IsNullOrWhiteSpace(depId)) continue;
                    if (!nodesById.ContainsKey(depId)) continue;

                    byte depState = state[depId];
                    if (depState == 1) return true;
                    if (depState == 0 && Visit(depId, nodesById, state)) return true;
                }
            }
            state[nodeId] = 2;
            return false;
        }
    }
}

