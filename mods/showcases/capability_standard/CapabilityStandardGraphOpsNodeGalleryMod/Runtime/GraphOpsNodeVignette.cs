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
    public string Field { get; set; } = "";
    public GraphOpsNodeCollection[] Collections { get; set; } = Array.Empty<GraphOpsNodeCollection>();
    public GraphOpsNodeLink[] Links { get; set; } = Array.Empty<GraphOpsNodeLink>();
    public string ConfigEffectId { get; set; } = "";
    public GraphOpsNodeLinearOptions? Linear { get; set; }
    /// <summary>Map variable declarations copied verbatim into the generated gallery map JSON (map-var ops).</summary>
    public GraphOpsNodeVignetteVariable[] Variables { get; set; } = Array.Empty<GraphOpsNodeVignetteVariable>();
}

public sealed class GraphOpsNodeVignetteVariable
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "int";
    public double Initial { get; set; }
    public bool Phase { get; set; }
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
    public int Team { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public sealed class GraphOpsNodeCollection
{
    public string Key { get; set; } = "";
    public string[] Members { get; set; } = Array.Empty<string>();
}

public sealed class GraphOpsNodeLink
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Type { get; set; } = "";
    public string Metric { get; set; } = "";
    public int MetricValue { get; set; }
    public string[] Flags { get; set; } = Array.Empty<string>();
}

public sealed class GraphOpsNodeLinearOptions
{
    public string ResultKind { get; set; } = "float";
    public string ApplyTo { get; set; } = "none";
}
