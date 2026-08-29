using Ludots.WebUI.PanelKit;

namespace ActivityDispatchShowcaseMod;

internal static class ActivityDispatchPanelKitCatalog
{
    public static WebUiPanelKitReferenceCatalog Create(Func<string, bool> isTopicRegistered)
    {
        ArgumentNullException.ThrowIfNull(isTopicRegistered);
        return new WebUiPanelKitReferenceCatalog(
            CreateRegistry("surface region", ActivityDispatchShowcaseIds.SurfaceRegionId),
            CreateRegistry("profile", ActivityDispatchShowcaseIds.ProfileId),
            CreateRegistry("layout", ActivityDispatchShowcaseIds.LayoutId),
            CreateRegistry("density", ActivityDispatchShowcaseIds.DensityId),
            CreateRegistry("input capability", ActivityDispatchShowcaseIds.InputCapabilityId),
            CreateRegistry("visible condition", ActivityDispatchShowcaseIds.VisibleConditionId),
            isTopicRegistered);
    }

    private static WebUiPanelIdRegistry CreateRegistry(string kind, string id)
    {
        var registry = new WebUiPanelIdRegistry(kind);
        registry.Register(id);
        return registry;
    }
}
