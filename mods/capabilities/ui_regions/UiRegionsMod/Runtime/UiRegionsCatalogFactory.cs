using Ludots.WebUI.PanelKit;

namespace UiRegionsMod.Runtime;

public static class UiRegionsCatalogFactory
{
	public static WebUiPanelKitReferenceCatalog Create(Func<string, bool> isTopicRegistered)
	{
		ArgumentNullException.ThrowIfNull(isTopicRegistered);

		var surfaceRegions = new WebUiPanelIdRegistry("surface region");
		surfaceRegions.RegisterAll(WebUiNineGridRegions.All);

		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.RegisterAll([
			"profile.resource.generic",
			"profile.command.generic",
			"profile.objective.generic",
			WebUiPanelKitSampleCatalog.ProductionOverviewProfileId,
			WebUiPanelKitSampleCatalog.CommandDeckGlobalProfileId,
			WebUiPanelKitSampleCatalog.CommandDeckEntityProfileId,
			WebUiNotificationPanelDescriptors.GenericProfileId,
			"profile.minimap.composited-overlay",
			WebUiRegionPanelDescriptors.ActivityModalProfileId,
			WebUiRegionPanelDescriptors.ViewFilterProfileId,
			WebUiRegionPanelDescriptors.EntityListProfileId,
			WebUiRegionPanelDescriptors.TimeControlProfileId,
			WebUiRegionPanelDescriptors.EventLogProfileId,
			"profile.entity-insight.generic",
		]);

		var layouts = new WebUiPanelIdRegistry("layout");
		layouts.RegisterAll([
			"layout.bar.horizontal",
			"layout.deck.grid",
			"layout.list.vertical",
			"layout.overview.split",
			WebUiNotificationPanelDescriptors.ToastStackLayoutId,
			"layout.minimap.floating",
			WebUiRegionPanelDescriptors.ActivityModalLayoutId,
			WebUiRegionPanelDescriptors.ViewFilterLayoutId,
			WebUiRegionPanelDescriptors.EntityListLayoutId,
			WebUiRegionPanelDescriptors.TimeControlLayoutId,
			WebUiRegionPanelDescriptors.EventLogLayoutId,
			"layout.insight.card",
		]);

		var densities = new WebUiPanelIdRegistry("density");
		densities.RegisterAll(["density.compact", "density.comfortable"]);

		var inputCapabilities = new WebUiPanelIdRegistry("input capability");
		inputCapabilities.RegisterAll([
			"input.none",
			"input.activate-slot",
			"input.notification-action",
			"input.minimap.focus",
			"input.activity.resolve",
			"input.filter.apply",
			"input.time.control",
			"input.list.select",
		]);

		var visibleConditions = new WebUiPanelIdRegistry("visible condition");
		visibleConditions.RegisterAll(["condition.always", "condition.command-source-nonempty"]);

		return new WebUiPanelKitReferenceCatalog(
			surfaceRegions,
			profiles,
			layouts,
			densities,
			inputCapabilities,
			visibleConditions,
			isTopicRegistered);
	}
}
