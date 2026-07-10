namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Sample catalog and paths for the checked-in tooltip descriptor.
/// Ids are generic panel vocabulary only — no game/unit/ability flavor names.
/// </summary>
public static class WebUiTooltipSampleCatalog
{
	public const string DescriptorId = "panel-kit.sample.tooltip";
	public const string Topic = "panel-kit.sample.tooltip";
	public const string EntityInsightProfileId = "profile.insight.generic";
	public const string TemplateId = "template.tooltip.generic";
	public const string LocaleId = "locale.sample";
	public const string AnchorId = "anchor.cursor";

	public const string TitleTokenId = "token.tooltip.title";
	public const string BodyTokenId = "token.tooltip.body";
	public const string BadgeTokenId = "token.tooltip.badge";
	public const string StatTokenId = "token.tooltip.stat";
	public const string TipTokenId = "token.tooltip.tip";
	public const string ActionTitleTokenId = "token.tooltip.action.title";
	public const string ActionBodyTokenId = "token.tooltip.action.body";

	public static WebUiTooltipReferenceCatalog Create(
		Func<string, bool>? isEntityInsightProfileRegistered = null,
		Func<string, bool>? isAbilityPresentationTokenRegistered = null)
	{
		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.Register(EntityInsightProfileId);

		var templates = new WebUiPanelIdRegistry("template");
		templates.RegisterAll([TemplateId, "template.tooltip.section.title", "template.tooltip.section.body"]);

		var locales = new WebUiPanelIdRegistry("locale");
		locales.Register(LocaleId);

		var anchors = new WebUiPanelIdRegistry("anchor");
		anchors.Register(AnchorId);

		var tokens = new HashSet<string>(StringComparer.Ordinal)
		{
			TitleTokenId,
			BodyTokenId,
			BadgeTokenId,
			StatTokenId,
			TipTokenId,
			ActionTitleTokenId,
			ActionBodyTokenId
		};

		return new WebUiTooltipReferenceCatalog(
			profiles,
			templates,
			locales,
			anchors,
			tokenId => tokens.Contains(tokenId),
			(tokenId, localeId) => tokens.Contains(tokenId) && string.Equals(localeId, LocaleId, StringComparison.Ordinal),
			isEntityInsightProfileRegistered ?? (profileId => string.Equals(profileId, EntityInsightProfileId, StringComparison.Ordinal)),
			isAbilityPresentationTokenRegistered);
	}

	public static WebUiTooltipEntityInsightProjection CreateSampleEntityProjection()
	{
		return new WebUiTooltipEntityInsightProjection(
			EntityInsightProfileId,
			TitleTokenId,
			BodyTokenId,
			[BadgeTokenId],
			[StatTokenId],
			[TipTokenId],
			[(ActionTitleTokenId, ActionBodyTokenId)]);
	}

	public static string SampleDescriptorPath()
	{
		string? assemblyDir = Path.GetDirectoryName(typeof(WebUiTooltipSampleCatalog).Assembly.Location);
		if (string.IsNullOrWhiteSpace(assemblyDir))
		{
			throw new InvalidOperationException("Unable to resolve PanelKit assembly directory for sample tooltip descriptor.");
		}

		string path = Path.Combine(assemblyDir, "Samples", "sample_tooltip_descriptor.json");
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Sample tooltip descriptor was not copied to output: '{path}'.", path);
		}

		return path;
	}
}
