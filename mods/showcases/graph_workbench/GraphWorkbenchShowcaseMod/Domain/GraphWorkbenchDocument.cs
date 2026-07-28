namespace GraphWorkbenchShowcaseMod.Domain;

public sealed class GraphWorkbenchDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public string ActiveGraphId { get; set; } = string.Empty;
    public string ActiveStateMachineId { get; set; } = string.Empty;
    public string ActiveBehaviorTreeId { get; set; } = string.Empty;
    public List<GraphWorkbenchGraphDocument> Graphs { get; set; } = new();
    public List<GraphWorkbenchStateMachineDocument> StateMachines { get; set; } = new();
    public List<GraphWorkbenchBehaviorTreeDocument> BehaviorTrees { get; set; } = new();
}

public sealed class GraphWorkbenchGraphDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string EntryNodeId { get; set; } = string.Empty;
    public List<GraphWorkbenchNodeDocument> Nodes { get; set; } = new();
    public List<GraphWorkbenchEdgeDocument> Edges { get; set; } = new();
}

public sealed class GraphWorkbenchStateMachineDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<GraphWorkbenchNodeDocument> Nodes { get; set; } = new();
    public List<GraphWorkbenchEdgeDocument> Edges { get; set; } = new();
}

public sealed class GraphWorkbenchBehaviorTreeDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<GraphWorkbenchNodeDocument> Nodes { get; set; } = new();
    public List<GraphWorkbenchEdgeDocument> Edges { get; set; } = new();
}

public sealed class GraphWorkbenchNodeDocument
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;
    public string ImplementationGraphId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public int IntValue { get; set; }
    public float FloatValue { get; set; }
    public bool BoolValue { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Attribute { get; set; } = string.Empty;
    public string EffectTemplate { get; set; } = string.Empty;
    public List<string> Inputs { get; set; } = new();
}

public sealed class GraphWorkbenchEdgeDocument
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Role { get; set; } = "next";
}

public sealed record GraphWorkbenchCompileResult(
    bool Success,
    int DraftRevision,
    int AppliedRevision,
    string Summary,
    GraphWorkbenchDiagnostic[] Diagnostics)
{
    public static GraphWorkbenchCompileResult Pending(int draftRevision) =>
        new(false, draftRevision, 0, "Not compiled yet.", Array.Empty<GraphWorkbenchDiagnostic>());
}

public sealed record GraphWorkbenchDiagnostic(
    string Severity,
    string Code,
    string DocumentId,
    string NodeId,
    string Message);
