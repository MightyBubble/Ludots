namespace OwnershipCascadeShowcaseMod;

public static class OwnershipCascadeIds
{
    public const string ShowcaseMapId = "ownership_cascade_showcase";
    public const string InstalledKey = "OwnershipCascadeShowcase.Installed";
    public const string RuntimeServiceKey = "OwnershipCascadeShowcase.Runtime";
    public const string CaptureActionId = "OwnershipCascade.Capture";
    public const string ReclaimActionId = "OwnershipCascade.Reclaim";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, ShowcaseMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
