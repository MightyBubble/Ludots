using System;
using System.Text.Json;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal static class CefBrowserMessageNormalizer
{
	private const string ProviderPrivateChannel = "cefsharp";

	public static BrowserScriptMessage Normalize(object? message)
	{
		string payload = NormalizePayload(message);
		if (TryCreateDataPlaneMessage(payload, out BrowserScriptMessage dataPlaneMessage))
		{
			return dataPlaneMessage;
		}

		return new BrowserScriptMessage(ProviderPrivateChannel, payload);
	}

	private static bool TryCreateDataPlaneMessage(string payload, out BrowserScriptMessage message)
	{
		message = new BrowserScriptMessage(ProviderPrivateChannel, payload);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(payload);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			if (root.TryGetProperty("schemaVersion", out JsonElement schemaVersion) &&
				schemaVersion.ValueKind == JsonValueKind.Number)
			{
				message = new BrowserScriptMessage(
					BrowserDataPlaneMessageChannels.Control,
					root.GetRawText());
				return true;
			}

			if (root.TryGetProperty("channel", out JsonElement channel) &&
				string.Equals(channel.GetString(), BrowserDataPlaneMessageChannels.Control, StringComparison.Ordinal) &&
				root.TryGetProperty("payload", out JsonElement nestedPayload))
			{
				string nested = nestedPayload.ValueKind == JsonValueKind.String
					? nestedPayload.GetString() ?? string.Empty
					: nestedPayload.GetRawText();
				if (!string.IsNullOrWhiteSpace(nested))
				{
					message = new BrowserScriptMessage(BrowserDataPlaneMessageChannels.Control, nested);
					return true;
				}
			}
		}

		return false;
	}

	private static string NormalizePayload(object? message)
	{
		if (message == null)
		{
			return string.Empty;
		}

		return message switch
		{
			string text => text,
			JsonElement json => json.GetRawText(),
			_ => JsonSerializer.Serialize(message)
		};
	}
}
