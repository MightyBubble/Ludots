using Ludots.WebUI.PanelKit;

namespace UiRegionsMod.Runtime;

public sealed class UiRegionsRuntime
{
	private WebUiPanelKitReferenceCatalog? _catalog;

	public WebUiPanelKitReferenceCatalog Catalog =>
		_catalog ?? throw new InvalidOperationException("UiRegions catalog is not installed.");

	/// <summary>
	/// Optional bulletin lines for the generic notification panel (set by showcase directors).
	/// </summary>
	public Func<IReadOnlyList<string>>? BulletinProvider { get; set; }

	public void Install(Func<string, bool> isTopicRegistered)
	{
		_catalog = UiRegionsCatalogFactory.Create(isTopicRegistered);
	}
}
