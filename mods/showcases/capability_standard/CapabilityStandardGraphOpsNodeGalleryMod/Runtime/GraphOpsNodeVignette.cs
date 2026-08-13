namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public sealed class GraphOpsNodeVignette
{
    public string Op { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Title { get; set; } = "";
    public string Beat { get; set; } = "";
    public string DetailTemplate { get; set; } = "";
    public string[] AssertDetailContains { get; set; } = Array.Empty<string>();
    public string FeaturedNodeId { get; set; } = "";
    public string GraphKind { get; set; } = "";
    public GraphOpsNodeActor[] Actors { get; set; } = Array.Empty<GraphOpsNodeActor>();
    public GraphOpsNodeLinearOptions? Linear { get; set; }
}

public sealed class GraphOpsNodeActor
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public string Template { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Health { get; set; } = 100f;
    public float HealthMax { get; set; } = 100f;
}

public sealed class GraphOpsNodeLinearOptions
{
    public string ResultKind { get; set; } = "float";
    public string ApplyTo { get; set; } = "none";
}
