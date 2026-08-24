namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Independent notification SSOT. Accepts domain projections as messages; does not own Task,
/// NarrativeFrontend, or showcase toast private state.
/// </summary>
public sealed class NotificationRuntime
{
	private readonly Dictionary<string, NotificationMessage> _byId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _dedupeToId = new(StringComparer.Ordinal);
	private readonly INotificationTextValidator _textValidator;
	private readonly INotificationActionRegistry _actionRegistry;
	private readonly Func<double> _clockSeconds;
	private readonly string? _localeId;
	private uint _revision = 1;

	public NotificationRuntime(
		INotificationTextValidator textValidator,
		INotificationActionRegistry actionRegistry,
		Func<double>? clockSeconds = null,
		string? localeId = null)
	{
		_textValidator = textValidator ?? throw new ArgumentNullException(nameof(textValidator));
		_actionRegistry = actionRegistry ?? throw new ArgumentNullException(nameof(actionRegistry));
		_clockSeconds = clockSeconds ?? (() => 0d);
		_localeId = string.IsNullOrWhiteSpace(localeId) ? null : localeId.Trim();
	}

	public uint Revision => _revision;

	public INotificationActionRegistry ActionRegistry => _actionRegistry;

	/// <summary>
	/// Publishes or replaces a notification. Same <see cref="NotificationMessage.DedupeKey"/> replaces
	/// the previous message and bumps revision. Unknown text tokens / actions fail fast.
	/// </summary>
	public void Publish(NotificationMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);
		ValidateMessage(message);

		double now = _clockSeconds();
		if (message.IsExpired(now))
		{
			throw new InvalidOperationException(
				$"Notification '{message.Id}' is already expired at publish time (ttl={message.TtlSeconds}).");
		}

		if (_dedupeToId.TryGetValue(message.DedupeKey, out string? existingId) &&
		    !string.Equals(existingId, message.Id, StringComparison.Ordinal))
		{
			_byId.Remove(existingId);
		}

		_byId[message.Id] = message;
		_dedupeToId[message.DedupeKey] = message.Id;
		BumpRevision();
	}

	public bool TryDismiss(string notificationId)
	{
		if (string.IsNullOrWhiteSpace(notificationId))
		{
			throw new ArgumentException("Notification id is required.", nameof(notificationId));
		}

		string id = notificationId.Trim();
		if (!_byId.TryGetValue(id, out NotificationMessage? message))
		{
			return false;
		}

		_byId.Remove(id);
		if (_dedupeToId.TryGetValue(message.DedupeKey, out string? mapped) &&
		    string.Equals(mapped, id, StringComparison.Ordinal))
		{
			_dedupeToId.Remove(message.DedupeKey);
		}

		BumpRevision();
		return true;
	}

	public bool TryGet(string notificationId, out NotificationMessage? message)
	{
		if (string.IsNullOrWhiteSpace(notificationId))
		{
			message = null;
			return false;
		}

		return _byId.TryGetValue(notificationId.Trim(), out message);
	}

	/// <summary>
	/// Resolves a notification action to its registered WebUI command name. Unknown action fails fast.
	/// </summary>
	public string ResolveActionCommand(string notificationId, string actionId)
	{
		if (string.IsNullOrWhiteSpace(notificationId))
		{
			throw new ArgumentException("Notification id is required.", nameof(notificationId));
		}

		string id = notificationId.Trim();
		if (!_byId.TryGetValue(id, out NotificationMessage? message))
		{
			throw new InvalidOperationException($"Notification '{id}' is not present in the runtime.");
		}

		string action = RequireId(actionId, nameof(actionId));
		bool found = false;
		for (int i = 0; i < message.Actions.Count; i++)
		{
			if (string.Equals(message.Actions[i].ActionId, action, StringComparison.Ordinal))
			{
				found = true;
				break;
			}
		}

		if (!found)
		{
			throw new InvalidOperationException(
				$"Notification '{id}' does not declare action '{action}'.");
		}

		return _actionRegistry.RequireCommandName(action);
	}

	public IReadOnlyList<NotificationMessage> SnapshotActive(NotificationPanelProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ExpireDue();

		var rows = new List<NotificationMessage>(_byId.Count);
		foreach (NotificationMessage message in _byId.Values)
		{
			if (!profile.IncludesSeverity(message.Severity))
			{
				continue;
			}

			if (!profile.IncludesCategory(message.CategoryId))
			{
				continue;
			}

			rows.Add(message);
		}

		rows.Sort(static (a, b) =>
		{
			int byPriority = b.Priority.CompareTo(a.Priority);
			if (byPriority != 0)
			{
				return byPriority;
			}

			int bySeverity = ((byte)b.Severity).CompareTo((byte)a.Severity);
			if (bySeverity != 0)
			{
				return bySeverity;
			}

			int byCreated = a.CreatedAtSeconds.CompareTo(b.CreatedAtSeconds);
			if (byCreated != 0)
			{
				return byCreated;
			}

			return string.CompareOrdinal(a.Id, b.Id);
		});

		if (rows.Count > profile.MaxVisible)
		{
			rows.RemoveRange(profile.MaxVisible, rows.Count - profile.MaxVisible);
		}

		return rows;
	}

	public int ExpireDue()
	{
		double now = _clockSeconds();
		var expired = new List<string>();
		foreach (KeyValuePair<string, NotificationMessage> pair in _byId)
		{
			if (pair.Value.IsExpired(now))
			{
				expired.Add(pair.Key);
			}
		}

		if (expired.Count == 0)
		{
			return 0;
		}

		for (int i = 0; i < expired.Count; i++)
		{
			NotificationMessage message = _byId[expired[i]];
			_byId.Remove(expired[i]);
			if (_dedupeToId.TryGetValue(message.DedupeKey, out string? mapped) &&
			    string.Equals(mapped, expired[i], StringComparison.Ordinal))
			{
				_dedupeToId.Remove(message.DedupeKey);
			}
		}

		BumpRevision();
		return expired.Count;
	}

	private void ValidateMessage(NotificationMessage message)
	{
		_textValidator.Validate(message.Id, message.TextTokenId, _localeId);

		for (int i = 0; i < message.Actions.Count; i++)
		{
			NotificationAction action = message.Actions[i];
			_actionRegistry.RequireCommandName(action.ActionId);
			if (!string.IsNullOrWhiteSpace(action.LabelTokenId))
			{
				_textValidator.Validate(message.Id, action.LabelTokenId!, _localeId);
			}
		}
	}

	private void BumpRevision()
	{
		_revision = unchecked(_revision + 1);
		if (_revision == 0)
		{
			_revision = 1;
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
