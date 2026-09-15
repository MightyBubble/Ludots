using System;
using System.Text.Json;

namespace Ludots.UI.Browser;

public static class BrowserScriptMessageNormalizer
{
	public static BrowserScriptMessage Normalize(string payload)
	{
		if (TryCreateRoutedMessage(payload, out BrowserScriptMessage routed))
		{
			return routed;
		}

		return new BrowserScriptMessage(BrowserMessageChannels.Application, payload);
	}

	private static bool TryCreateRoutedMessage(string payload, out BrowserScriptMessage message)
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

			if (!root.TryGetProperty("channel", out JsonElement channel))
			{
				return false;
			}

			string? channelName = channel.GetString();
			if (string.Equals(channelName, BrowserDataPlaneMessageChannels.Control, StringComparison.Ordinal))
			{
				string nested = ReadNestedPayload(root);
				if (string.IsNullOrWhiteSpace(nested))
				{
					return false;
				}

				message = new BrowserScriptMessage(BrowserDataPlaneMessageChannels.Control, nested);
				return true;
			}

			if (string.Equals(channelName, BrowserMessageChannels.HitTestCapture, StringComparison.Ordinal))
			{
				message = new BrowserScriptMessage(
					BrowserMessageChannels.HitTestCapture,
					ReadNestedPayload(root));
				return true;
			}
		}

		return false;
	}

	private static string ReadNestedPayload(JsonElement root)
	{
		if (!root.TryGetProperty("payload", out JsonElement nestedPayload))
		{
			return string.Empty;
		}

		return nestedPayload.ValueKind == JsonValueKind.String
			? nestedPayload.GetString() ?? string.Empty
			: nestedPayload.GetRawText();
	}
}
