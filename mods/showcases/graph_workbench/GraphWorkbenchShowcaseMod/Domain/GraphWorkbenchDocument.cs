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
    public List<GraphWorkbenchGraphOutputDocument> Outputs { get; set; } = new();
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
    public string Template { get; set; } = string.Empty;
    public string CollectionKey { get; set; } = string.Empty;
    public string EffectTemplate { get; set; } = string.Empty;
    public string BlackboardKey { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;
    public string ValidOutput { get; set; } = string.Empty;
    public string DroppedOutput { get; set; } = string.Empty;
    public string QueryCapacityPolicy { get; set; } = string.Empty;
    public float RadiusCm { get; set; }
    public float RangeCm { get; set; }
    public int DirectionDeg { get; set; }
    public int HalfAngleDeg { get; set; }
    public int LengthCm { get; set; }
    public int HalfWidthCm { get; set; }
    public int HalfHeightCm { get; set; }
    public int RotationDeg { get; set; }
    public int HexRadius { get; set; }
    public uint LayerMask { get; set; }
    public string RelationshipMode { get; set; } = string.Empty;
    public int Limit { get; set; }
    public int TeamId { get; set; }
    public string Sort { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PayloadPreset { get; set; } = string.Empty;
    public string BuiltinHandler { get; set; } = string.Empty;
    public bool Descending { get; set; }
    public int Slot { get; set; }
    public List<string> Inputs { get; set; } = new();
}

public sealed class GraphWorkbenchEdgeDocument
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Role { get; set; } = "next";
    public string SourcePort { get; set; } = string.Empty;
    public string TargetPort { get; set; } = string.Empty;
}

public sealed class GraphWorkbenchGraphOutputDocument
{
    public string Id { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string CollectionKey { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
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
