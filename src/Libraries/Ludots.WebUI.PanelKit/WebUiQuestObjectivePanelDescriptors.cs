namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Stable ids for the reusable Objective / Quest tracker panel (WPK-6).
/// Composition only — gameplay truth stays in QuestRuntimeService / DataPlane producer.
/// </summary>
public static class WebUiQuestObjectivePanelDescriptors
{
	public const string PanelType = "objective";
	public const string GenericProfileId = "profile.objective.generic";
	public const string SampleTopic = WebUiPanelKitSampleCatalog.ObjectiveTopic;
	public const string VerticalListLayoutId = "layout.list.vertical";
}
