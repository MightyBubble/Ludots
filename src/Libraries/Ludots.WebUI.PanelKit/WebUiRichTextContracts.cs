using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Semantic role of one rich-text run. Browser maps roles to presentation; payloads never carry HTML.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebUiRichTextRunRole
{
	Text = 1,
	Emphasis = 2,
	Token = 3,
	Icon = 4,
	Value = 5,
	State = 6
}

/// <summary>
/// One semantic run inside a rich-text block. Exactly one content channel is active per role.
/// </summary>
public sealed class WebUiRichTextRun
{
	public WebUiRichTextRun(
		WebUiRichTextRunRole role,
		string? text = null,
		string? tokenId = null,
		string? iconId = null,
		string? valueId = null,
		string? stateId = null)
	{
		Role = role;
		switch (role)
		{
			case WebUiRichTextRunRole.Text:
			case WebUiRichTextRunRole.Emphasis:
				Text = RequireContent(text, nameof(text), role);
				RejectOthers(tokenId, iconId, valueId, stateId, role);
				break;
			case WebUiRichTextRunRole.Token:
				TokenId = RequireId(tokenId, nameof(tokenId));
				RejectOthers(text, iconId, valueId, stateId, role);
				break;
			case WebUiRichTextRunRole.Icon:
				IconId = RequireId(iconId, nameof(iconId));
				RejectOthers(text, tokenId, valueId, stateId, role);
				break;
			case WebUiRichTextRunRole.Value:
				ValueId = RequireId(valueId, nameof(valueId));
				RejectOthers(text, tokenId, iconId, stateId, role);
				break;
			case WebUiRichTextRunRole.State:
				StateId = RequireId(stateId, nameof(stateId));
				RejectOthers(text, tokenId, iconId, valueId, role);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown rich-text run role.");
		}
	}

	public WebUiRichTextRunRole Role { get; }
	public string? Text { get; }
	public string? TokenId { get; }
	public string? IconId { get; }
	public string? ValueId { get; }
	public string? StateId { get; }

	private static string RequireContent(string? value, string paramName, WebUiRichTextRunRole role)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required for rich-text role '{role}'.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		WebUiRichTextGuard.RejectHtml(trimmed, paramName);
		return trimmed;
	}

	private static string RequireId(string? value, string paramName)
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

	private static void RejectOthers(
		string? a,
		string? b,
		string? c,
		string? d,
		WebUiRichTextRunRole role)
	{
		if (!string.IsNullOrWhiteSpace(a) ||
		    !string.IsNullOrWhiteSpace(b) ||
		    !string.IsNullOrWhiteSpace(c) ||
		    !string.IsNullOrWhiteSpace(d))
		{
			throw new ArgumentException($"Rich-text role '{role}' must not carry unrelated content channels.");
		}
	}
}

/// <summary>
/// Ordered block of semantic runs. Used by tooltip and other WebUI text panels.
/// </summary>
public sealed class WebUiRichTextBlock
{
	private readonly IReadOnlyList<WebUiRichTextRun> _runs;

	public WebUiRichTextBlock(string blockId, IReadOnlyList<WebUiRichTextRun> runs)
	{
		BlockId = RequireId(blockId, nameof(blockId));
		ArgumentNullException.ThrowIfNull(runs);
		if (runs.Count == 0)
		{
			throw new ArgumentException($"Rich-text block '{BlockId}' must declare at least one run.", nameof(runs));
		}

		_runs = new ReadOnlyCollection<WebUiRichTextRun>(runs.ToArray());
	}

	public string BlockId { get; }
	public IReadOnlyList<WebUiRichTextRun> Runs => _runs;

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
/// Fail-fast guards shared by rich-text builders and tooltip producers.
/// </summary>
public static class WebUiRichTextGuard
{
	public static void RejectHtml(string text, string path)
	{
		if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0)
		{
			throw new InvalidOperationException(
				$"{path} must not contain HTML markup; rich text uses semantic runs, not HTML strings. Offending text: '{text}'.");
		}
	}

	public static WebUiRichTextRunRole ParseRole(string? value, string path)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"{path} must declare a rich-text run role.");
		}

		string normalized = value.Trim();
		if (Enum.TryParse(normalized, ignoreCase: true, out WebUiRichTextRunRole role) &&
		    Enum.IsDefined(typeof(WebUiRichTextRunRole), role))
		{
			return role;
		}

		throw new InvalidOperationException($"{path} references unknown rich-text run role '{value}'.");
	}

	public static void RequireRegisteredToken(Func<string, bool> isTokenRegistered, string tokenId, string path)
	{
		ArgumentNullException.ThrowIfNull(isTokenRegistered);
		if (!isTokenRegistered(tokenId))
		{
			throw new InvalidOperationException($"{path} references unknown text token '{tokenId}'.");
		}
	}

	public static void RequireLocaleCoverage(
		Func<string, string, bool> hasLocaleTemplate,
		string tokenId,
		string localeId,
		string path)
	{
		ArgumentNullException.ThrowIfNull(hasLocaleTemplate);
		if (!hasLocaleTemplate(tokenId, localeId))
		{
			throw new InvalidOperationException(
				$"{path} token '{tokenId}' has no template for locale '{localeId}'.");
		}
	}
}
