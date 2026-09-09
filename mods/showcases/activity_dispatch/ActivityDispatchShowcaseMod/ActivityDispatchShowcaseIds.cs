namespace ActivityDispatchShowcaseMod;

internal static class ActivityDispatchShowcaseIds
{
    public const string AssetManifestPath = "ActivityDispatchShowcaseMod:Assets/PanelKit/panel_manifest.json";
    public const string AssetIndexPath = "ActivityDispatchShowcaseMod:Assets/activity-app/index.html";
    public const string ManifestId = "wpk.activity.dispatch-showcase";
    public const string HostOwnerId = "ActivityDispatch.Showcase";
    public const string PanelId = "panel.activity.events";
    public const string PanelType = "activity";
    public const string SurfaceRegionId = "region.bottom-right";
    public const string ProfileId = "profile.activity.showcase";
    public const string LayoutId = "layout.list.vertical";
    public const string DensityId = "density.comfortable";
    public const string InputCapabilityId = "input.activity-confirm";
    public const string VisibleConditionId = "condition.always";
    public const string Topic = "wpk.activity.dispatch";
    public const string SessionId = "activity-dispatch-showcase";
    public const string ConfirmCommand = "activity.confirm";
    public const string TriggerCommand = "activity.showcase.trigger";
    public const string SetAttributeCommand = "activity.showcase.setAttribute";
    public const string MapId = "activity_dispatch";
    public const string ScopeInstanceId = "council";

    public const string TriggerEventForced = "ActivityShowcase.Forced";
    public const string TriggerEventPooled = "ActivityShowcase.Pooled";
    public const string TriggerEventAutomatic = "ActivityShowcase.Automatic";

    public static readonly string[] TriggerEvents =
    [
        TriggerEventForced,
        TriggerEventPooled,
        TriggerEventAutomatic
    ];
}
