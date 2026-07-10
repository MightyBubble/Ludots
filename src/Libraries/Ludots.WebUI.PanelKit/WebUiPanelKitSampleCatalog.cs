namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Builds a reference catalog suitable for the checked-in sample panel kit manifest.
/// Ids are generic panel-kit vocabulary only â€?no game/unit/resource names.
/// </summary>
public static class WebUiPanelKitSampleCatalog
{
	public const string ResourceTopic = "panel-kit.sample.resource";
	public const string CommandTopic = "panel-kit.sample.command";
	public const string ObjectiveTopic = "panel-kit.sample.objective";
	public const string ProductionTopic = "panel-kit.sample.production";

	public const string CommandDeckGlobalProfileId = "profile.command-deck.global";
	public const string CommandDeckEntityProfileId = "profile.command-deck.entity";
	public const string CommandDeckAggregateProfileId = "profile.command-deck.aggregate";
	public const string CommandDeckPinnedProfileId = "profile.command-deck.conditional-pinned";
	public const string ProductionOverviewProfileId = "profile.production-overview.generic";
	public const string NotificationTopic = "panel-kit.sample.notification";

	/// <summary>
	/// Every topic declared by <see cref="SampleManifestPath"/>. Callers that load the full sample
	/// manifest must register each of these before catalog validation (fail-fast, no silent skip).
	/// </summary>
	public static IReadOnlyList<string> SampleTopics { get; } =
	[
		ResourceTopic,
		CommandTopic,
		ObjectiveTopic,
		NotificationTopic
	];

	public static WebUiPanelKitReferenceCatalog Create(Func<string, bool> isTopicRegistered)
	{
		ArgumentNullException.ThrowIfNull(isTopicRegistered);

		var surfaceRegions = new WebUiPanelIdRegistry("surface region");
		surfaceRegions.RegisterAll(["region.top-left", "region.top-right", "region.bottom-center", "region.bottom-left"]);
		surfaceRegions.RegisterAll(["region.top-left", "region.top-right", "region.bottom-center", "region.top-center"]);

		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.RegisterAll([
			"profile.resource.generic",
			"profile.command.generic",
			"profile.objective.generic",
			ProductionOverviewProfileId,
			CommandDeckGlobalProfileId,
			CommandDeckEntityProfileId,
			CommandDeckAggregateProfileId,
			CommandDeckPinnedProfileId
		]);

		var layouts = new WebUiPanelIdRegistry("layout");
		layouts.RegisterAll(["layout.bar.horizontal", "layout.deck.grid", "layout.list.vertical", "layout.overview.split"]);
			WebUiNotificationPanelDescriptors.GenericProfileId
		]);

		var layouts = new WebUiPanelIdRegistry("layout");
		layouts.RegisterAll([
			"layout.bar.horizontal",
			"layout.deck.grid",
			"layout.list.vertical",
			WebUiNotificationPanelDescriptors.ToastStackLayoutId
		]);

		var densities = new WebUiPanelIdRegistry("density");
		densities.RegisterAll(["density.compact", "density.comfortable"]);

		var inputCapabilities = new WebUiPanelIdRegistry("input capability");
		inputCapabilities.RegisterAll(["input.none", "input.activate-slot", "input.notification-action"]);

		var visibleConditions = new WebUiPanelIdRegistry("visible condition");
		visibleConditions.RegisterAll(["condition.always", "condition.binding-flag"]);

		return new WebUiPanelKitReferenceCatalog(
			surfaceRegions,
			profiles,
			layouts,
			densities,
			inputCapabilities,
			visibleConditions,
			isTopicRegistered);
	}

	public static string SampleManifestPath()
	{
		string? assemblyDir = Path.GetDirectoryName(typeof(WebUiPanelKitSampleCatalog).Assembly.Location);
		if (string.IsNullOrWhiteSpace(assemblyDir))
		{
			throw new InvalidOperationException("Unable to resolve PanelKit assembly directory for sample manifest.");
		}

		string path = Path.Combine(assemblyDir, "Samples", "sample_panel_kit_manifest.json");
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Sample panel kit manifest was not copied to output: '{path}'.", path);
		}

		return path;
	}
}
