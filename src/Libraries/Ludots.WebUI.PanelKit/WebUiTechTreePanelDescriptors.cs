namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Stable ids for the reusable TechTree / Progression panel (WPK-9).
/// Composition only — gameplay truth stays in Progression registries / state buffer / evaluator.
/// </summary>
public static class WebUiTechTreePanelDescriptors
{
	public const string PanelType = "techtree";
	public const string GenericProfileId = "profile.techtree.generic";
	public const string TreeLayoutId = "layout.techtree.tree";
	public const string SampleTopic = WebUiPanelKitSampleCatalog.TechTreeTopic;
	public const string ResearchCommandName = "progression.research";
}
