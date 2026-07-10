namespace Ludots.WebUI.PanelKit;

/// <summary>
/// CommandDeck-specific panel kit descriptor (WPK-3). Extends the generic panel declaration with
/// display-mode and source/profile references. Does not carry gameplay truth — only stable ids.
/// </summary>
public sealed class CommandDeckPanelDescriptor
{
	public CommandDeckPanelDescriptor(
		string panelId,
		string displayMode,
		string sourceKind,
		string sourceRef,
		string commandPanelSourceId,
		string topic,
		string profileId,
		string layoutId,
		string filterProfileId = "",
		string aggregationProfileId = "",
		string routeProfileId = "",
		string visibilityConditionId = "",
		string categoryTagPrefix = "")
	{
		PanelId = RequireId(panelId, nameof(panelId));
		DisplayMode = RequireId(displayMode, nameof(displayMode));
		SourceKind = RequireId(sourceKind, nameof(sourceKind));
		SourceRef = sourceRef?.Trim() ?? string.Empty;
		CommandPanelSourceId = RequireId(commandPanelSourceId, nameof(commandPanelSourceId));
		Topic = RequireId(topic, nameof(topic));
		ProfileId = RequireId(profileId, nameof(profileId));
		LayoutId = RequireId(layoutId, nameof(layoutId));
		FilterProfileId = filterProfileId?.Trim() ?? string.Empty;
		AggregationProfileId = aggregationProfileId?.Trim() ?? string.Empty;
		RouteProfileId = routeProfileId?.Trim() ?? string.Empty;
		VisibilityConditionId = visibilityConditionId?.Trim() ?? string.Empty;
		CategoryTagPrefix = categoryTagPrefix?.Trim() ?? string.Empty;

		ValidateMode();
	}

	public string PanelId { get; }
	public string DisplayMode { get; }
	public string SourceKind { get; }
	public string SourceRef { get; }
	public string CommandPanelSourceId { get; }
	public string Topic { get; }
	public string ProfileId { get; }
	public string LayoutId { get; }
	public string FilterProfileId { get; }
	public string AggregationProfileId { get; }
	public string RouteProfileId { get; }
	public string VisibilityConditionId { get; }
	public string CategoryTagPrefix { get; }

	public const string PanelType = "command-deck";
	public const string DisplayModeGlobal = "global";
	public const string DisplayModeEntity = "entity";
	public const string DisplayModeAggregateFiltered = "aggregateFiltered";
	public const string DisplayModeConditionalPinned = "conditionalPinned";

	private void ValidateMode()
	{
		switch (DisplayMode)
		{
			case DisplayModeGlobal:
			case DisplayModeEntity:
				break;
			case DisplayModeAggregateFiltered:
				if (string.IsNullOrWhiteSpace(AggregationProfileId))
				{
					throw new InvalidOperationException(
						$"CommandDeck panel '{PanelId}' aggregateFiltered mode requires aggregationProfileId.");
				}

				if (string.IsNullOrWhiteSpace(RouteProfileId))
				{
					throw new InvalidOperationException(
						$"CommandDeck panel '{PanelId}' aggregateFiltered mode requires routeProfileId.");
				}

				break;
			case DisplayModeConditionalPinned:
				if (string.IsNullOrWhiteSpace(VisibilityConditionId))
				{
					throw new InvalidOperationException(
						$"CommandDeck panel '{PanelId}' conditionalPinned mode requires visibilityConditionId.");
				}

				break;
			default:
				throw new InvalidOperationException(
					$"CommandDeck panel '{PanelId}' has unknown displayMode '{DisplayMode}'.");
		}
	}

	private static string RequireId(string value, string paramName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		return trimmed;
	}
}
