namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeDriverCatalog
{
    public static IGraphOpsNodeDriver Create(string driver)
    {
        if (string.IsNullOrWhiteSpace(driver))
        {
            throw new InvalidOperationException("Vignette driver is required.");
        }

        return driver switch
        {
            "linear" => new Drivers.LinearNodeDriver(),
            "attr" => new Drivers.AttrNodeDriver(),
            "script" => new Drivers.ScriptNodeDriver(),
            "sandbox" => new Drivers.SandboxNodeDriver(),
            "spatial" => new Drivers.SpatialNodeDriver(),
            "event" => new Drivers.EventNodeDriver(),
            "entryPayload" => new Drivers.EntryPayloadNodeDriver(),
            "invokeGraph" => new Drivers.InvokeGraphNodeDriver(),
            "placedEntity" => new Drivers.PlacedEntityNodeDriver(),
            "placedRegion" => new Drivers.PlacedRegionNodeDriver(),
            "blackboard" => new Drivers.BlackboardNodeDriver(),
            "rel" => new Drivers.RelNodeDriver(),
            "query" => new Drivers.QueryNodeDriver(),
            _ => throw new InvalidOperationException($"Unknown GraphOps node driver '{driver}'.")
        };
    }
}
