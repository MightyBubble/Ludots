namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Generic panelType ids for UIR function regions that are not covered by WPK-1..10 samples.
/// Implementations may live in capability mods; ids are library SSOT.
/// </summary>
public static class WebUiRegionPanelDescriptors
{
	public const string ActivityModalPanelType = "activity-modal";
	public const string ActivityModalProfileId = "profile.activity-modal.generic";
	public const string ActivityModalLayoutId = "layout.modal.choice";

	public const string ViewFilterPanelType = "view-filter";
	public const string ViewFilterProfileId = "profile.view-filter.generic";
	public const string ViewFilterLayoutId = "layout.filter.chips";

	public const string EntityListPanelType = "entity-list";
	public const string EntityListProfileId = "profile.entity-list.generic";
	public const string EntityListLayoutId = "layout.list.tabbed";

	public const string TimeControlPanelType = "time-control";
	public const string TimeControlProfileId = "profile.time-control.generic";
	public const string TimeControlLayoutId = "layout.time.strip";

	public const string EventLogPanelType = "event-log";
	public const string EventLogProfileId = "profile.event-log.generic";
	public const string EventLogLayoutId = "layout.log.vertical";
}
