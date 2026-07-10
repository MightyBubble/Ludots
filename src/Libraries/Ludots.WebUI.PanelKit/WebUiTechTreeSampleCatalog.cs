namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Sample catalog and paths for the checked-in TechTree / Progression descriptor.
/// Ids are generic panel vocabulary only — no tech/era/tradition/edict flavor names.
/// </summary>
public static class WebUiTechTreeSampleCatalog
{
	public const string DescriptorId = "panel-kit.sample.techtree";
	public const string Topic = "panel-kit.sample.techtree";
	public const string ProfileId = "profile.techtree.generic";
	public const string LayoutId = "layout.tree.generic";
	public const string LocaleId = "locale.sample";

	public const string RootProgressionId = "progression.sample.root";
	public const string BranchProgressionId = "progression.sample.branch";
	public const string RootRequirementId = "requirement.sample.root.unlock";
	public const string BranchRequirementId = "requirement.sample.branch.unlock";
	public const string ScopeKeyId = "scope.sample.host";
	public const string ResearchActionId = "action.progression.research";
	public const string TooltipDescriptorId = "panel-kit.sample.tooltip";

	public const string TitleTokenRoot = "token.techtree.node.root.title";
	public const string BodyTokenRoot = "token.techtree.node.root.body";
	public const string EffectTokenRoot = "token.techtree.node.root.effect";
	public const string BlockedTokenRoot = "token.techtree.node.root.blocked";
	public const string TitleTokenBranch = "token.techtree.node.branch.title";
	public const string BodyTokenBranch = "token.techtree.node.branch.body";
	public const string EffectTokenBranch = "token.techtree.node.branch.effect";
	public const string BlockedTokenBranch = "token.techtree.node.branch.blocked";

	public static WebUiTechTreeReferenceCatalog Create(
		Func<string, bool>? isProgressionRegistered = null,
		Func<string, bool>? isRequirementRegistered = null,
		Func<string, bool>? isScopeKeyRegistered = null,
		Func<string, bool>? isActionRegistered = null,
		Func<string, bool>? isTooltipDescriptorRegistered = null)
	{
		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.Register(ProfileId);

		var layouts = new WebUiPanelIdRegistry("layout");
		layouts.RegisterAll([LayoutId, "layout.grid.generic", "layout.layered.generic"]);

		var locales = new WebUiPanelIdRegistry("locale");
		locales.Register(LocaleId);

		var displayTokens = new WebUiPanelIdRegistry("display token");
		displayTokens.RegisterAll([
			TitleTokenRoot,
			BodyTokenRoot,
			EffectTokenRoot,
			BlockedTokenRoot,
			TitleTokenBranch,
			BodyTokenBranch,
			EffectTokenBranch,
			BlockedTokenBranch
		]);

		var progressions = new HashSet<string>(StringComparer.Ordinal)
		{
			RootProgressionId,
			BranchProgressionId
		};
		var requirements = new HashSet<string>(StringComparer.Ordinal)
		{
			RootRequirementId,
			BranchRequirementId
		};
		var scopes = new HashSet<string>(StringComparer.Ordinal) { ScopeKeyId };
		var actions = new HashSet<string>(StringComparer.Ordinal) { ResearchActionId };

		return new WebUiTechTreeReferenceCatalog(
			profiles,
			layouts,
			locales,
			displayTokens,
			isProgressionRegistered ?? (id => progressions.Contains(id)),
			isRequirementRegistered ?? (id => requirements.Contains(id)),
			isScopeKeyRegistered ?? (id => scopes.Contains(id)),
			isActionRegistered ?? (id => actions.Contains(id)),
			(tokenId, localeId) =>
				displayTokens.Contains(tokenId) &&
				string.Equals(localeId, LocaleId, StringComparison.Ordinal),
			isTooltipDescriptorRegistered ??
			(id => string.Equals(id, TooltipDescriptorId, StringComparison.Ordinal)));
	}

	public static string SampleDescriptorPath()
	{
		string? assemblyDir = Path.GetDirectoryName(typeof(WebUiTechTreeSampleCatalog).Assembly.Location);
		if (string.IsNullOrWhiteSpace(assemblyDir))
		{
			throw new InvalidOperationException("Unable to resolve PanelKit assembly directory for sample TechTree descriptor.");
		}

		string path = Path.Combine(assemblyDir, "Samples", "sample_techtree_descriptor.json");
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Sample TechTree descriptor was not copied to output: '{path}'.", path);
		}

		return path;
	}
}
