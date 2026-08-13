namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

internal sealed class GraphOpsNodeGallerySandboxCatalog
{
    public string DisplayTable { get; set; } = "";
    public string BurningTag { get; set; } = "";
    public string MarkedTag { get; set; } = "";
    public int BurningTokenId { get; set; }
    public int MarkedTokenId { get; set; }
    public string BurningCaption { get; set; } = "";
    public string MarkedCaption { get; set; } = "";
    public string MarkEffect { get; set; } = "";
    public string BuffEffect { get; set; } = "";
    public string BuffBlackboardKey { get; set; } = "";
    public string RelationshipType { get; set; } = "";
    public string LoyaltyMetric { get; set; } = "";
    public string TrustedFlag { get; set; } = "";
}
