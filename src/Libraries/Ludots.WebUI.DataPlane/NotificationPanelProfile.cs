using System.Collections.ObjectModel;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Data-driven notification panel profile: which severities/categories appear, panel kind, capacity, and locale.
/// Profile ids are panel-kit vocabulary only — no game flavor names.
/// </summary>
public sealed class NotificationPanelProfile
{
	public const string GenericProfileId = "profile.notification.generic";
	public const string DefaultLocaleId = "locale.sample";

	private readonly HashSet<NotificationSeverity> _includedSeverities;
	private readonly HashSet<string>? _allowedCategories;

	public NotificationPanelProfile(
		string profileId,
		NotificationPanelKind panelKind,
		IReadOnlyList<NotificationSeverity> includedSeverities,
		string localeId,
		int maxVisible,
		IReadOnlyList<string>? allowedCategoryIds = null)
	{
		if (string.IsNullOrWhiteSpace(profileId))
		{
			throw new ArgumentException("Profile id is required.", nameof(profileId));
		}

		if (!Enum.IsDefined(typeof(NotificationPanelKind), panelKind))
		{
			throw new ArgumentOutOfRangeException(nameof(panelKind), panelKind, "Unknown notification panel kind.");
		}

		ArgumentNullException.ThrowIfNull(includedSeverities);
		if (includedSeverities.Count == 0)
		{
			throw new ArgumentException("Notification profile must include at least one severity.", nameof(includedSeverities));
		}

		if (maxVisible <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxVisible), maxVisible, "MaxVisible must be positive.");
		}

		ProfileId = profileId.Trim();
		PanelKind = panelKind;
		LocaleId = RequireId(localeId, nameof(localeId));
		MaxVisible = maxVisible;

		_includedSeverities = new HashSet<NotificationSeverity>();
		foreach (NotificationSeverity severity in includedSeverities)
		{
			if (!Enum.IsDefined(typeof(NotificationSeverity), severity))
			{
				throw new ArgumentOutOfRangeException(
					nameof(includedSeverities),
					severity,
					"Unknown notification severity.");
			}

			_includedSeverities.Add(severity);
		}

		IncludedSeverities = new ReadOnlyCollection<NotificationSeverity>(_includedSeverities.OrderBy(static s => (byte)s).ToArray());
		AllowedCategoryIds = NormalizeOptionalIds(allowedCategoryIds, nameof(allowedCategoryIds));
		_allowedCategories = AllowedCategoryIds == null
			? null
			: new HashSet<string>(AllowedCategoryIds, StringComparer.Ordinal);
	}

	public string ProfileId { get; }
	public NotificationPanelKind PanelKind { get; }
	public string LocaleId { get; }
	public int MaxVisible { get; }
	public IReadOnlyList<NotificationSeverity> IncludedSeverities { get; }
	public IReadOnlyList<string>? AllowedCategoryIds { get; }

	public static NotificationPanelProfile CreateGeneric(
		NotificationPanelKind panelKind = NotificationPanelKind.ToastStack,
		int maxVisible = 8,
		IReadOnlyList<string>? allowedCategoryIds = null,
		string? localeId = null)
	{
		return new NotificationPanelProfile(
			GenericProfileId,
			panelKind,
			[NotificationSeverity.Info, NotificationSeverity.Warning, NotificationSeverity.Critical],
			localeId ?? DefaultLocaleId,
			maxVisible,
			allowedCategoryIds);
	}

	public bool IncludesSeverity(NotificationSeverity severity) => _includedSeverities.Contains(severity);

	public bool IncludesCategory(string categoryId)
	{
		if (_allowedCategories == null)
		{
			return true;
		}

		return !string.IsNullOrWhiteSpace(categoryId) && _allowedCategories.Contains(categoryId.Trim());
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

	private static IReadOnlyList<string>? NormalizeOptionalIds(IReadOnlyList<string>? ids, string paramName)
	{
		if (ids == null)
		{
			return null;
		}

		if (ids.Count == 0)
		{
			throw new ArgumentException($"{paramName} must be null or contain at least one id.", paramName);
		}

		var normalized = new string[ids.Count];
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < ids.Count; i++)
		{
			string id = ids[i];
			if (string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException($"{paramName}[{i}] is required.", paramName);
			}

			string trimmed = id.Trim();
			if (!seen.Add(trimmed))
			{
				throw new ArgumentException($"{paramName} contains duplicate id '{trimmed}'.", paramName);
			}

			normalized[i] = trimmed;
		}

		return normalized;
	}
}
