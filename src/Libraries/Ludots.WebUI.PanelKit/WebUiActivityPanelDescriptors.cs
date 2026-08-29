namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Stable ids for the reusable Activity event panel. Composition only — gameplay
/// truth stays in ActivityRuntimeService / the DataPlane activity producer; the
/// confirm button routes through a named command, never a second progress copy.
/// </summary>
public static class WebUiActivityPanelDescriptors
{
    public const string PanelType = "activity";
    public const string GenericProfileId = "profile.activity.generic";
    public const string SampleTopic = WebUiPanelKitSampleCatalog.ActivityTopic;
    public const string VerticalListLayoutId = "layout.list.vertical";
    public const string ConfirmCommandName = "activity.confirm";
    public const string TriggerCommandName = "activity.showcase.trigger";
}
