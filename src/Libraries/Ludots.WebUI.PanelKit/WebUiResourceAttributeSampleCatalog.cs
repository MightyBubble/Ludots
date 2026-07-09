namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Sample catalog and paths for the checked-in resource attribute descriptor.
/// Ids are generic panel vocabulary only — no game/resource flavor names.
/// </summary>
public static class WebUiResourceAttributeSampleCatalog
{
	public const string DescriptorId = "panel-kit.sample.resource-attribute";
	public const string Topic = WebUiPanelKitSampleCatalog.ResourceTopic;

	public static WebUiResourceAttributeReferenceCatalog Create(
		Func<string, bool> isAttributeRegistered,
		Func<string, bool>? isGraphOutputKeyRegistered = null)
	{
		ArgumentNullException.ThrowIfNull(isAttributeRegistered);

		var displayTokens = new WebUiPanelIdRegistry("display token");
		displayTokens.RegisterAll([
			"token.resource.primary",
			"token.resource.secondary",
			"token.resource.capacity"
		]);

		var unitTokens = new WebUiPanelIdRegistry("unit token");
		unitTokens.RegisterAll([
			"unit.none",
			"unit.count",
			"unit.rate"
		]);

		return new WebUiResourceAttributeReferenceCatalog(
			displayTokens,
			unitTokens,
			isAttributeRegistered,
			isGraphOutputKeyRegistered);
	}

	public static string SampleDescriptorPath()
	{
		string? assemblyDir = Path.GetDirectoryName(typeof(WebUiResourceAttributeSampleCatalog).Assembly.Location);
		if (string.IsNullOrWhiteSpace(assemblyDir))
		{
			throw new InvalidOperationException("Unable to resolve PanelKit assembly directory for sample resource descriptor.");
		}

		string path = Path.Combine(assemblyDir, "Samples", "sample_resource_attribute_descriptor.json");
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Sample resource attribute descriptor was not copied to output: '{path}'.", path);
		}

		return path;
	}
}
