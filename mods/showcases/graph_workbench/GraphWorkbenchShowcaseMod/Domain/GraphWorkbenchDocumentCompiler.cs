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
        ValidateGraphPortEdges(graph, diagnostics);
        ValidateGraphOutputs(graph, diagnostics);
        var nodeIds = new HashSet<string>(graph.Nodes.Select(static node => node.Id), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(graph.EntryNodeId) && !nodeIds.Contains(graph.EntryNodeId))
        {
            diagnostics.Add(Error(graph.Id, graph.EntryNodeId, "GW0101", $"Graph entry node '{graph.EntryNodeId}' is missing."));
        }
    }

    private static void ValidateGraphPortEdges(GraphWorkbenchGraphDocument graph, List<GraphWorkbenchDiagnostic> diagnostics)
    {
        var nodesById = graph.Nodes
            .Where(static node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(static node => node.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var connectedInputs = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < graph.Edges.Count; i++)
        {
            GraphWorkbenchEdgeDocument edge = graph.Edges[i];
            bool isInputEdge = IsInputPort(edge.TargetPort);
            bool isExecEdge = string.Equals(edge.SourcePort, "exec:next", StringComparison.Ordinal) ||
                              string.Equals(edge.TargetPort, "exec:in", StringComparison.Ordinal);

            if (isInputEdge)
            {
                ValidateGraphInputEdge(graph, edge, nodesById, connectedInputs, diagnostics);
                continue;
            }

            if (isExecEdge)
            {
                if (!string.Equals(edge.SourcePort, "exec:next", StringComparison.Ordinal) ||
                    !string.Equals(edge.TargetPort, "exec:in", StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(graph.Id, edge.Target, "GW0314", $"Graph exec edge '{edge.Id}' must connect exec:next to exec:in."));
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(edge.SourcePort) || !string.IsNullOrWhiteSpace(edge.TargetPort))
            {
                diagnostics.Add(Error(graph.Id, edge.Target, "GW0315", $"Graph edge '{edge.Id}' uses unsupported ports '{edge.SourcePort}' -> '{edge.TargetPort}'."));
            }
        }

        for (int n = 0; n < graph.Nodes.Count; n++)
        {
            GraphWorkbenchNodeDocument node = graph.Nodes[n];
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(node.Inputs[i]))
                {
                    continue;
                }

                if (!connectedInputs.Contains(InputConnectionKey(node.Id, i)))
                {
                    diagnostics.Add(Error(graph.Id, node.Id, "GW0316", $"Node '{node.Id}' input[{i}] has no visible edge to target port in:{i}."));
                }
            }
        }
    }

    private static void ValidateGraphInputEdge(
        GraphWorkbenchGraphDocument graph,
        GraphWorkbenchEdgeDocument edge,
        Dictionary<string, GraphWorkbenchNodeDocument> nodesById,
        HashSet<string> connectedInputs,
        List<GraphWorkbenchDiagnostic> diagnostics)
    {
        if (!nodesById.TryGetValue(edge.Target, out GraphWorkbenchNodeDocument? targetNode))
        {
            return;
        }

        if (!TryParseInputPort(edge.TargetPort, out int inputIndex))
        {
            diagnostics.Add(Error(graph.Id, edge.Target, "GW0310", $"Graph input edge '{edge.Id}' has invalid target port '{edge.TargetPort}'."));
            return;
        }

        if (!IsValueSourcePort(edge.SourcePort))
        {
            diagnostics.Add(Error(graph.Id, edge.Target, "GW0311", $"Graph input edge '{edge.Id}' must connect from a value output port."));
            return;
        }

        if (inputIndex < 0 || inputIndex >= targetNode.Inputs.Count)
        {
            diagnostics.Add(Error(graph.Id, targetNode.Id, "GW0312", $"Graph input edge '{edge.Id}' targets input[{inputIndex}], but node '{targetNode.Id}' declares {targetNode.Inputs.Count} inputs."));
            return;
        }

        string key = InputConnectionKey(targetNode.Id, inputIndex);
        if (!connectedInputs.Add(key))
        {
            diagnostics.Add(Error(graph.Id, targetNode.Id, "GW0313", $"Node '{targetNode.Id}' input[{inputIndex}] has more than one visible edge."));
            return;
        }

        if (!nodesById.TryGetValue(edge.Source, out GraphWorkbenchNodeDocument? sourceNode))
        {
            return;
        }

        if (!TryResolveSourceValue(edge, sourceNode, out string sourceValue, out string error))
        {
            diagnostics.Add(Error(graph.Id, sourceNode.Id, "GW0317", error));
            return;
        }

        string expected = targetNode.Inputs[inputIndex];
        if (!string.Equals(expected, sourceValue, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(graph.Id, targetNode.Id, "GW0318", $"Node '{targetNode.Id}' input[{inputIndex}] expects '{expected}', but visible edge '{edge.Id}' carries '{sourceValue}'."));
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

    private static void ValidateGraphOutputs(GraphWorkbenchGraphDocument graph, List<GraphWorkbenchDiagnostic> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < graph.Outputs.Count; i++)
        {
            GraphWorkbenchGraphOutputDocument output = graph.Outputs[i];
            if (!RequireId(output.Id, "output", i, diagnostics) || !ids.Add(output.Id))
            {
                diagnostics.Add(Error(graph.Id, output.Id, "GW0110", $"Duplicate or missing graph output id '{output.Id}'."));
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

    public static GraphConfig ToGraphConfig(GraphWorkbenchGraphDocument graph)
    {
        var nextBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        var inputsByTarget = BuildInputsByTarget(graph);
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
                Inputs = inputsByTarget.TryGetValue(node.Id, out List<string>? visibleInputs)
                    ? visibleInputs
                    : node.Inputs?.ToList() ?? new List<string>(),
                IntValue = node.IntValue,
                FloatValue = node.FloatValue,
                BoolValue = node.BoolValue,
                Tag = EmptyToNull(node.Tag),
                Attribute = EmptyToNull(node.Attribute),
                Template = EmptyToNull(node.Template),
                CollectionKey = EmptyToNull(node.CollectionKey),
                EffectTemplate = EmptyToNull(node.EffectTemplate),
                BlackboardKey = EmptyToNull(node.BlackboardKey),
                ConfigKey = EmptyToNull(node.ConfigKey),
                ValidOutput = EmptyToNull(node.ValidOutput),
                DroppedOutput = EmptyToNull(node.DroppedOutput),
                QueryCapacityPolicy = EmptyToNull(node.QueryCapacityPolicy),
                RadiusCm = node.RadiusCm,
                RangeCm = node.RangeCm,
                DirectionDeg = node.DirectionDeg,
                HalfAngleDeg = node.HalfAngleDeg,
                LengthCm = node.LengthCm,
                HalfWidthCm = node.HalfWidthCm,
                HalfHeightCm = node.HalfHeightCm,
                RotationDeg = node.RotationDeg,
                HexRadius = node.HexRadius,
                LayerMask = node.LayerMask,
                RelationshipMode = EmptyToNull(node.RelationshipMode),
                Limit = node.Limit,
                TeamId = node.TeamId,
                Sort = EmptyToNull(node.Sort),
                RelationshipType = EmptyToNull(node.RelationshipType),
                Metric = EmptyToNull(node.Metric),
                Flag = EmptyToNull(node.Flag),
                Reason = EmptyToNull(node.Reason),
                PayloadPreset = EmptyToNull(node.PayloadPreset),
                BuiltinHandler = EmptyToNull(node.BuiltinHandler),
                Descending = node.Descending,
                Slot = node.Slot
            });
        }

        for (int i = 0; i < graph.Outputs.Count; i++)
        {
            GraphWorkbenchGraphOutputDocument output = graph.Outputs[i];
            config.Outputs.Add(new GraphOutputConfig
            {
                Id = output.Id,
                Destination = output.Destination,
                Type = output.Type,
                Source = output.Source,
                Key = output.Key,
                CollectionKey = output.CollectionKey,
                Role = output.Role,
                Title = output.Title,
                Summary = output.Summary
            });
        }

        return config;
    }

    private static Dictionary<string, List<string>> BuildInputsByTarget(GraphWorkbenchGraphDocument graph)
    {
        var nodesById = graph.Nodes
            .Where(static node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(static node => node.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (int i = 0; i < graph.Edges.Count; i++)
        {
            GraphWorkbenchEdgeDocument edge = graph.Edges[i];
            if (!TryParseInputPort(edge.TargetPort, out int inputIndex) ||
                !nodesById.TryGetValue(edge.Source, out GraphWorkbenchNodeDocument? sourceNode) ||
                !TryResolveSourceValue(edge, sourceNode, out string sourceValue, out _))
            {
                continue;
            }

            if (!result.TryGetValue(edge.Target, out List<string>? inputs))
            {
                inputs = new List<string>();
                result[edge.Target] = inputs;
            }

            while (inputs.Count <= inputIndex)
            {
                inputs.Add(string.Empty);
            }

            inputs[inputIndex] = sourceValue;
        }

        return result;
    }

    private static bool TryResolveSourceValue(
        GraphWorkbenchEdgeDocument edge,
        GraphWorkbenchNodeDocument sourceNode,
        out string sourceValue,
        out string error)
    {
        sourceValue = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(edge.SourcePort) ||
            string.Equals(edge.SourcePort, "out:value", StringComparison.Ordinal))
        {
            sourceValue = sourceNode.Id;
            return true;
        }

        if (string.Equals(edge.SourcePort, "out:valid", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(sourceNode.ValidOutput))
            {
                error = $"Node '{sourceNode.Id}' source port out:valid requires validOutput.";
                return false;
            }

            sourceValue = sourceNode.ValidOutput;
            return true;
        }

        if (string.Equals(edge.SourcePort, "out:dropped", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(sourceNode.DroppedOutput))
            {
                error = $"Node '{sourceNode.Id}' source port out:dropped requires droppedOutput.";
                return false;
            }

            sourceValue = sourceNode.DroppedOutput;
            return true;
        }

        error = $"Node '{sourceNode.Id}' uses unsupported value source port '{edge.SourcePort}'.";
        return false;
    }

    private static bool IsInputPort(string port) =>
        TryParseInputPort(port, out _);

    private static bool TryParseInputPort(string port, out int inputIndex)
    {
        inputIndex = -1;
        const string Prefix = "in:";
        return !string.IsNullOrWhiteSpace(port) &&
               port.StartsWith(Prefix, StringComparison.Ordinal) &&
               int.TryParse(port.AsSpan(Prefix.Length), out inputIndex) &&
               inputIndex >= 0;
    }

    private static bool IsValueSourcePort(string port) =>
        string.IsNullOrWhiteSpace(port) ||
        string.Equals(port, "out:value", StringComparison.Ordinal) ||
        string.Equals(port, "out:valid", StringComparison.Ordinal) ||
        string.Equals(port, "out:dropped", StringComparison.Ordinal);

    private static string InputConnectionKey(string nodeId, int inputIndex) =>
        $"{nodeId}\u001F{inputIndex}";

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
