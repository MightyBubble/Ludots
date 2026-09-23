namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Sample catalog and paths for the checked-in TechTree / Progression descriptor.
/// Ids are generic panel vocabulary only — no game/tech flavor names.
/// </summary>
public static class WebUiTechTreeSampleCatalog
{
	public const string DescriptorId = "panel-kit.sample.techtree";
	public const string Topic = WebUiPanelKitSampleCatalog.TechTreeTopic;
	public const string ProfileId = WebUiTechTreePanelDescriptors.GenericProfileId;
	public const string LayoutId = WebUiTechTreePanelDescriptors.TreeLayoutId;
	public const string ScopeKey = "scope.sample";
	public const string RootProgressionId = "progression.sample.root";
	public const string ChildProgressionId = "progression.sample.child";
	public const string ChildRequirementId = "requirement.sample.child";
	public const string ResearchCommandId = WebUiTechTreePanelDescriptors.ResearchCommandName;
	public const string RootDisplayTokenId = "token.techtree.node.root";
	public const string ChildDisplayTokenId = "token.techtree.node.child";
	public const string BlockedReasonTokenId = "token.techtree.blocked.requirement";

	public static WebUiTechTreeReferenceCatalog Create(
		Func<string, bool> isProgressionRegistered,
		Func<string, bool> isRequirementRegistered,
		Func<string, bool> isScopeKeyRegistered,
		Func<string, bool> isCommandRegistered)
	{
		ArgumentNullException.ThrowIfNull(isProgressionRegistered);
		ArgumentNullException.ThrowIfNull(isRequirementRegistered);
		ArgumentNullException.ThrowIfNull(isScopeKeyRegistered);
		ArgumentNullException.ThrowIfNull(isCommandRegistered);

		var displayTokens = new WebUiPanelIdRegistry("display token");
		displayTokens.RegisterAll([RootDisplayTokenId, ChildDisplayTokenId]);

		var blockedReasonTokens = new WebUiPanelIdRegistry("blocked reason token");
		blockedReasonTokens.Register(BlockedReasonTokenId);

		var profiles = new WebUiPanelIdRegistry("profile");
		profiles.Register(ProfileId);

		var layouts = new WebUiPanelIdRegistry("layout");
		layouts.Register(LayoutId);

		return new WebUiTechTreeReferenceCatalog(
			displayTokens,
			blockedReasonTokens,
			profiles,
			layouts,
			isProgressionRegistered,
			isRequirementRegistered,
			isScopeKeyRegistered,
			isCommandRegistered);
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
