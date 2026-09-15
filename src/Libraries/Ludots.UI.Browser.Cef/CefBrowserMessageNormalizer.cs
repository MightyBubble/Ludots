using System.Text.Json;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal static class CefBrowserMessageNormalizer
{
	public static BrowserScriptMessage Normalize(object? message)
	{
		return BrowserScriptMessageNormalizer.Normalize(NormalizePayload(message));
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
