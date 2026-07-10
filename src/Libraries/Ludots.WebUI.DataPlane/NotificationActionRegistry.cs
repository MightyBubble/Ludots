namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Maps notification action ids to registered WebUI command names.
/// Unknown action ids fail fast; there is no silent ignore path.
/// </summary>
public interface INotificationActionRegistry
{
	void Register(string actionId, string commandName);

	bool TryGetCommandName(string actionId, out string commandName);

	string RequireCommandName(string actionId);

	bool Contains(string actionId);

	IReadOnlyCollection<string> ActionIds { get; }
}

/// <summary>
/// Default notification action registry. Action ids are panel vocabulary;
/// command names must match handlers registered on <see cref="WebUiCommandRouter"/>.
/// </summary>
public sealed class NotificationActionRegistry : INotificationActionRegistry
{
	private readonly Dictionary<string, string> _actions = new(StringComparer.Ordinal);
	private readonly Func<string, bool>? _isCommandRegistered;

	public NotificationActionRegistry(Func<string, bool>? isCommandRegistered = null)
	{
		_isCommandRegistered = isCommandRegistered;
	}

	public void Register(string actionId, string commandName)
	{
		string key = RequireId(actionId, nameof(actionId));
		string command = RequireId(commandName, nameof(commandName));
		if (_actions.ContainsKey(key))
		{
			throw new InvalidOperationException($"Notification action '{key}' is already registered.");
		}

		if (_isCommandRegistered != null && !_isCommandRegistered(command))
		{
			throw new InvalidOperationException(
				$"Notification action '{key}' references unknown WebUI command '{command}'.");
		}

		_actions[key] = command;
	}

	public bool TryGetCommandName(string actionId, out string commandName)
	{
		if (string.IsNullOrWhiteSpace(actionId))
		{
			commandName = string.Empty;
			return false;
		}

		return _actions.TryGetValue(actionId.Trim(), out commandName!);
	}

	public string RequireCommandName(string actionId)
	{
		string key = RequireId(actionId, nameof(actionId));
		if (!_actions.TryGetValue(key, out string? commandName))
		{
			throw new InvalidOperationException($"Unknown notification action '{key}'.");
		}

		return commandName;
	}

	public bool Contains(string actionId)
	{
		return !string.IsNullOrWhiteSpace(actionId) && _actions.ContainsKey(actionId.Trim());
	}

	public IReadOnlyCollection<string> ActionIds => _actions.Keys;

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
