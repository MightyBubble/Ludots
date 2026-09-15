using System;
using System.Text.Json;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Ultralight;

/// <summary>
/// Mirrors CEF's CefBrowserMessageNormalizer: bare DataPlane envelopes
/// (schemaVersion/kind/topic) must become Control-channel BrowserScriptMessage
/// or BrowserMessageBridgeDataTransport rejects the uplink.
/// </summary>
internal static class UltralightBrowserMessageNormalizer
{
	public static BrowserScriptMessage Normalize(string payload)
	{
		if (TryCreateDataPlaneMessage(payload, out BrowserScriptMessage dataPlaneMessage))
		{
			return dataPlaneMessage;
		}

		return new BrowserScriptMessage(BrowserMessageChannels.Application, payload);
	}

	private static bool TryCreateDataPlaneMessage(string payload, out BrowserScriptMessage message)
	{
		message = new BrowserScriptMessage(BrowserMessageChannels.Application, payload);
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
}
