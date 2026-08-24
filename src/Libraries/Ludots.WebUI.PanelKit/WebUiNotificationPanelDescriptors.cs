namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Stable ids for the reusable Notification panel (WPK-7).
/// Composition only — gameplay message truth stays in NotificationRuntime / DataPlane producer.
/// Independent of NarrativeFrontend, Task, and showcase toast private state.
/// </summary>
public static class WebUiNotificationPanelDescriptors
{
	public const string PanelType = "notification";
	public const string GenericProfileId = "profile.notification.generic";
	public const string SampleTopic = WebUiPanelKitSampleCatalog.NotificationTopic;
	public const string ToastStackLayoutId = "layout.toast.stack";
	public const string OpenPanelActionId = "action.notification.open-panel";
	public const string OpenPanelCommandName = "notification.openPanel";
}
