using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Severity of a game notification. Used for profile filtering and stable ordering.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationSeverity : byte
{
	Info = 1,
	Warning = 2,
	Critical = 3
}

/// <summary>
/// Panel presentation kind for a notification profile. Composition only — not gameplay truth.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationPanelKind : byte
{
	ToastStack = 1,
	EventFeed = 2,
	WarningBanner = 3,
	LogReview = 4
}

/// <summary>
/// One clickable action on a notification. <see cref="ActionId"/> must resolve through
/// <see cref="INotificationActionRegistry"/> to a registered WebUI command name.
/// </summary>
public sealed class NotificationAction
{
	public NotificationAction(string actionId, string? labelTokenId = null, JsonElement payload = default)
	{
		ActionId = RequireId(actionId, nameof(actionId));
		LabelTokenId = string.IsNullOrWhiteSpace(labelTokenId) ? null : RequireId(labelTokenId, nameof(labelTokenId));
		Payload = payload.ValueKind == JsonValueKind.Undefined
			? default
			: payload.Clone();
	}

	public string ActionId { get; }
	public string? LabelTokenId { get; }
	public JsonElement Payload { get; }

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

/// <summary>
/// Immutable notification message. Text is always a PresentationText token (WPK-5); never plain fallback copy.
/// </summary>
public sealed class NotificationMessage
{
	private readonly IReadOnlyList<NotificationAction> _actions;

	public NotificationMessage(
		string id,
		string categoryId,
		NotificationSeverity severity,
		string textTokenId,
		string dedupeKey,
		int priority,
		double? ttlSeconds = null,
		IReadOnlyList<NotificationAction>? actions = null,
		double createdAtSeconds = 0d)
	{
		Id = RequireId(id, nameof(id));
		CategoryId = RequireId(categoryId, nameof(categoryId));
		Severity = severity;
		if (!Enum.IsDefined(typeof(NotificationSeverity), severity))
		{
			throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown notification severity.");
		}

		TextTokenId = RequireId(textTokenId, nameof(textTokenId));
		DedupeKey = RequireId(dedupeKey, nameof(dedupeKey));
		Priority = priority;
		if (ttlSeconds is < 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(ttlSeconds), ttlSeconds, "TTL must be null or non-negative seconds.");
		}

		TtlSeconds = ttlSeconds;
		if (double.IsNaN(createdAtSeconds) || double.IsInfinity(createdAtSeconds) || createdAtSeconds < 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(createdAtSeconds), createdAtSeconds, "CreatedAtSeconds must be a finite non-negative value.");
		}

		CreatedAtSeconds = createdAtSeconds;
		IReadOnlyList<NotificationAction> source = actions ?? Array.Empty<NotificationAction>();
		var copy = new NotificationAction[source.Count];
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < source.Count; i++)
		{
			NotificationAction action = source[i] ?? throw new ArgumentException($"actions[{i}] is required.", nameof(actions));
			if (!seen.Add(action.ActionId))
			{
				throw new ArgumentException($"Duplicate notification action id '{action.ActionId}'.", nameof(actions));
			}

			copy[i] = action;
		}

		_actions = new ReadOnlyCollection<NotificationAction>(copy);
	}

	public string Id { get; }
	public string CategoryId { get; }
	public NotificationSeverity Severity { get; }
	public string TextTokenId { get; }
	public string DedupeKey { get; }
	public int Priority { get; }
	public double? TtlSeconds { get; }
	public double CreatedAtSeconds { get; }
	public IReadOnlyList<NotificationAction> Actions => _actions;

	public bool IsExpired(double nowSeconds)
	{
		if (TtlSeconds is null)
		{
			return false;
		}

		return nowSeconds >= CreatedAtSeconds + TtlSeconds.Value;
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
