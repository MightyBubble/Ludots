namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Builds a reference catalog suitable for the checked-in sample panel kit manifest.
/// Ids are generic panel-kit vocabulary only — no game/unit/resource names.
/// </summary>
public static class WebUiPanelKitSampleCatalog
{
	public const string ResourceTopic = "panel-kit.sample.resource";
	public const string CommandTopic = "panel-kit.sample.command";
	public const string ObjectiveTopic = "panel-kit.sample.objective";
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
		surfaceRegions.RegisterAll(["region.top-left", "region.top-right", "region.bottom-center", "region.top-center"]);

		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.RegisterAll([
			"profile.resource.generic",
			"profile.command.generic",
			"profile.objective.generic",
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
		visibleConditions.Register("condition.always");

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
