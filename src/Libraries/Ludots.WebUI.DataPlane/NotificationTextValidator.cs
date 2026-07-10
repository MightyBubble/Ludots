namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Validates notification text tokens against the WPK-5 PresentationText / localization contract.
/// Missing token or locale coverage fails fast with concrete ids — no plain-text fallback.
/// </summary>
public interface INotificationTextValidator
{
	void Validate(string notificationId, string textTokenId, string? localeId = null);
}

public sealed class NotificationTextValidator : INotificationTextValidator
{
	private readonly Func<string, bool> _isTokenRegistered;
	private readonly Func<string, string, bool>? _hasLocaleTemplate;

	public NotificationTextValidator(
		Func<string, bool> isTokenRegistered,
		Func<string, string, bool>? hasLocaleTemplate = null)
	{
		_isTokenRegistered = isTokenRegistered ?? throw new ArgumentNullException(nameof(isTokenRegistered));
		_hasLocaleTemplate = hasLocaleTemplate;
	}

	public void Validate(string notificationId, string textTokenId, string? localeId = null)
	{
		if (string.IsNullOrWhiteSpace(notificationId))
		{
			throw new ArgumentException("Notification id is required.", nameof(notificationId));
		}

		if (string.IsNullOrWhiteSpace(textTokenId))
		{
			throw new InvalidOperationException(
				$"Notification '{notificationId}' is missing text token; plain-text fallback is forbidden.");
		}

		string token = textTokenId.Trim();
		if (!_isTokenRegistered(token))
		{
			throw new InvalidOperationException(
				$"Notification '{notificationId}' references unknown text token '{token}'.");
		}

		if (!string.IsNullOrWhiteSpace(localeId))
		{
			if (_hasLocaleTemplate == null)
			{
				throw new InvalidOperationException(
					$"Notification '{notificationId}' requires locale '{localeId.Trim()}' coverage for token '{token}', " +
					"but no WPK-5 locale template hook is configured.");
			}

			string locale = localeId.Trim();
			if (!_hasLocaleTemplate(token, locale))
			{
				throw new InvalidOperationException(
					$"Notification '{notificationId}' token '{token}' has no template for locale '{locale}'.");
			}
		}
	}
}
