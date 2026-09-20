namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Stable ids for the reusable Objective / Task tracker panel (WPK-6).
/// Composition only — gameplay truth stays in TaskRuntimeService / DataPlane producer.
/// </summary>
public static class WebUiTaskObjectivePanelDescriptors
{
	public const string PanelType = "objective";
	public const string GenericProfileId = "profile.objective.generic";
	public const string SampleTopic = WebUiPanelKitSampleCatalog.ObjectiveTopic;
	public const string VerticalListLayoutId = "layout.list.vertical";
}
