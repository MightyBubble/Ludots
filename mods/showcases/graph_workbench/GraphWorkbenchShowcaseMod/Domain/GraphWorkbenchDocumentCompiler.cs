using Ludots.Core.NodeLibraries.GASGraph;

namespace GraphWorkbenchShowcaseMod.Domain;

public static class GraphWorkbenchDocumentCompiler
{
    public static GraphWorkbenchCompileResult Compile(GraphWorkbenchDocument document, int appliedRevision)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<GraphWorkbenchDiagnostic>();
        ValidateDocumentShape(document, diagnostics);
        if (HasErrors(diagnostics))
        {
            return CreateResult(false, document.Revision, appliedRevision, diagnostics);
        }

        for (int i = 0; i < document.Graphs.Count; i++)
        {
            GraphWorkbenchGraphDocument graph = document.Graphs[i];
            GraphConfig config = ToGraphConfig(graph);
            var (_, graphDiagnostics) = GraphCompiler.Compile(config);
            for (int d = 0; d < graphDiagnostics.Count; d++)
            {
                GraphDiagnostic diagnostic = graphDiagnostics[d];
                diagnostics.Add(new GraphWorkbenchDiagnostic(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.GraphId,
                    diagnostic.NodeId ?? string.Empty,
                    diagnostic.Message));
            }
        }

        bool success = !HasErrors(diagnostics);
        return CreateResult(success, document.Revision, success ? document.Revision : appliedRevision, diagnostics);
    }

    private static GraphWorkbenchCompileResult CreateResult(
        bool success,
        int draftRevision,
        int appliedRevision,
        List<GraphWorkbenchDiagnostic> diagnostics)
    {
        string summary = success
            ? $"Compiled revision {draftRevision}."
            : $"Compile failed for revision {draftRevision}.";
        return new GraphWorkbenchCompileResult(
            success,
            draftRevision,
            appliedRevision,
            summary,
            diagnostics.ToArray());
    }

    private static void ValidateDocumentShape(GraphWorkbenchDocument document, List<GraphWorkbenchDiagnostic> diagnostics)
    {
        if (document.SchemaVersion != 1)
        {
            diagnostics.Add(Error("document", string.Empty, "GW0001", "Graph workbench document requires schemaVersion 1."));
        }

        if (document.Graphs.Count == 0)
        {
            diagnostics.Add(Error("document", string.Empty, "GW0002", "At least one Graph document is required."));
        }

        var graphIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < document.Graphs.Count; i++)
        {
            GraphWorkbenchGraphDocument graph = document.Graphs[i];
            if (!RequireId(graph.Id, "graph", i, diagnostics) || !graphIds.Add(graph.Id))
            {
                diagnostics.Add(Error(graph.Id, string.Empty, "GW0003", $"Duplicate or missing graph id '{graph.Id}'."));
                continue;
            }

            ValidateGraph(graph, diagnostics);
        }

        var fsmIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < document.StateMachines.Count; i++)
        {
            GraphWorkbenchStateMachineDocument fsm = document.StateMachines[i];
            if (!RequireId(fsm.Id, "stateMachine", i, diagnostics) || !fsmIds.Add(fsm.Id))
            {
                diagnostics.Add(Error(fsm.Id, string.Empty, "GW0010", $"Duplicate or missing FSM id '{fsm.Id}'."));
                continue;
            }

            ValidateNodeGraphRefs(fsm.Id, fsm.Nodes, graphIds, diagnostics);
            ValidateEdges(fsm.Id, fsm.Nodes, fsm.Edges, diagnostics);
        }

        var btIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < document.BehaviorTrees.Count; i++)
        {
            GraphWorkbenchBehaviorTreeDocument bt = document.BehaviorTrees[i];
            if (!RequireId(bt.Id, "behaviorTree", i, diagnostics) || !btIds.Add(bt.Id))
            {
                diagnostics.Add(Error(bt.Id, string.Empty, "GW0020", $"Duplicate or missing BT id '{bt.Id}'."));
                continue;
            }

            ValidateNodeGraphRefs(bt.Id, bt.Nodes, graphIds, diagnostics);
            ValidateEdges(bt.Id, bt.Nodes, bt.Edges, diagnostics);
        }
    }

    private static void ValidateGraph(GraphWorkbenchGraphDocument graph, List<GraphWorkbenchDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(graph.EntryNodeId))
        {
            diagnostics.Add(Error(graph.Id, string.Empty, "GW0100", "Graph entry node is required."));
        }

        ValidateEdges(graph.Id, graph.Nodes, graph.Edges, diagnostics);
        var nodeIds = new HashSet<string>(graph.Nodes.Select(static node => node.Id), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(graph.EntryNodeId) && !nodeIds.Contains(graph.EntryNodeId))
        {
            diagnostics.Add(Error(graph.Id, graph.EntryNodeId, "GW0101", $"Graph entry node '{graph.EntryNodeId}' is missing."));
        }
    }

    private static void ValidateNodeGraphRefs(
        string documentId,
        List<GraphWorkbenchNodeDocument> nodes,
        HashSet<string> graphIds,
        List<GraphWorkbenchDiagnostic> diagnostics)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            GraphWorkbenchNodeDocument node = nodes[i];
            if (string.IsNullOrWhiteSpace(node.ImplementationGraphId))
            {
                continue;
            }

            if (!graphIds.Contains(node.ImplementationGraphId))
            {
                diagnostics.Add(Error(
                    documentId,
                    node.Id,
                    "GW0200",
                    $"Node '{node.Id}' references missing implementation graph '{node.ImplementationGraphId}'."));
            }
        }
    }

    private static void ValidateEdges(
        string documentId,
        List<GraphWorkbenchNodeDocument> nodes,
        List<GraphWorkbenchEdgeDocument> edges,
        List<GraphWorkbenchDiagnostic> diagnostics)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            GraphWorkbenchNodeDocument node = nodes[i];
            if (!RequireId(node.Id, "node", i, diagnostics) || !nodeIds.Add(node.Id))
            {
                diagnostics.Add(Error(documentId, node.Id, "GW0300", $"Duplicate or missing node id '{node.Id}'."));
            }
        }

        for (int i = 0; i < edges.Count; i++)
        {
            GraphWorkbenchEdgeDocument edge = edges[i];
            if (string.IsNullOrWhiteSpace(edge.Source) || !nodeIds.Contains(edge.Source))
            {
                diagnostics.Add(Error(documentId, edge.Source, "GW0301", $"Edge '{edge.Id}' references missing source '{edge.Source}'."));
            }

            if (string.IsNullOrWhiteSpace(edge.Target) || !nodeIds.Contains(edge.Target))
            {
                diagnostics.Add(Error(documentId, edge.Target, "GW0302", $"Edge '{edge.Id}' references missing target '{edge.Target}'."));
            }
        }
    }

    private static GraphConfig ToGraphConfig(GraphWorkbenchGraphDocument graph)
    {
        var nextBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < graph.Edges.Count; i++)
        {
            GraphWorkbenchEdgeDocument edge = graph.Edges[i];
            if (string.Equals(edge.Role, "next", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(edge.Source) &&
                !string.IsNullOrWhiteSpace(edge.Target) &&
                !nextBySource.ContainsKey(edge.Source))
            {
                nextBySource[edge.Source] = edge.Target;
            }
        }

        var config = new GraphConfig
        {
            Id = graph.Id,
            Kind = string.IsNullOrWhiteSpace(graph.Domain) ? "Graph" : graph.Domain,
            Entry = graph.EntryNodeId
        };

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            GraphWorkbenchNodeDocument node = graph.Nodes[i];
            nextBySource.TryGetValue(node.Id, out string? next);
            config.Nodes.Add(new GraphNodeConfig
            {
                Id = node.Id,
                Op = string.IsNullOrWhiteSpace(node.Op) ? "ConstInt" : node.Op,
                Next = next,
                Inputs = node.Inputs.ToList(),
                IntValue = node.IntValue,
                FloatValue = node.FloatValue,
                BoolValue = node.BoolValue,
                Tag = EmptyToNull(node.Tag),
                Attribute = EmptyToNull(node.Attribute),
                EffectTemplate = EmptyToNull(node.EffectTemplate)
            });
        }

        return config;
    }

    private static bool HasErrors(List<GraphWorkbenchDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (string.Equals(diagnostics[i].Severity, "Error", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequireId(string value, string kind, int index, List<GraphWorkbenchDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        diagnostics.Add(Error("document", string.Empty, "GW0400", $"{kind}[{index}] requires an id."));
        return false;
    }

    private static GraphWorkbenchDiagnostic Error(string documentId, string nodeId, string code, string message) =>
        new("Error", code, documentId, nodeId, message);

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
