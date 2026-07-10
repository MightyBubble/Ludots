using Ludots.WebUI.PanelKit;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal static class BrowserMinimapCompositedOverlayPanelKitCatalog
{
	public static WebUiPanelKitReferenceCatalog Create(Func<string, bool> isTopicRegistered)
	{
		ArgumentNullException.ThrowIfNull(isTopicRegistered);
		return new WebUiPanelKitReferenceCatalog(
			CreateRegistry("surface region", BrowserMinimapCompositedOverlayPanelKitIds.SurfaceRegionId),
			CreateRegistry("profile", BrowserMinimapCompositedOverlayPanelKitIds.ProfileId),
			CreateRegistry("layout", BrowserMinimapCompositedOverlayPanelKitIds.LayoutId),
			CreateRegistry("density", BrowserMinimapCompositedOverlayPanelKitIds.DensityId),
			CreateRegistry("input capability", BrowserMinimapCompositedOverlayPanelKitIds.InputCapabilityId),
			CreateRegistry("visible condition", BrowserMinimapCompositedOverlayPanelKitIds.VisibleConditionId),
			isTopicRegistered);
	}

	private static WebUiPanelIdRegistry CreateRegistry(string kind, string id)
	{
		var registry = new WebUiPanelIdRegistry(kind);
		registry.Register(id);
		return registry;
	}
}
