using Ludots.WebUI.PanelKit;

namespace UiRegionsMod.Runtime;

public sealed class UiRegionsRuntime
{
	private WebUiPanelKitReferenceCatalog? _catalog;

	public WebUiPanelKitReferenceCatalog Catalog =>
		_catalog ?? throw new InvalidOperationException("UiRegions catalog is not installed.");

	public void Install(Func<string, bool> isTopicRegistered)
	{
		_catalog = UiRegionsCatalogFactory.Create(isTopicRegistered);
	}
}
