using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserMessageNormalizerTests
{
	[Test]
	public void Normalize_WhenPayloadIsDataPlaneEnvelope_ReturnsStandardControlChannel()
	{
		BrowserScriptMessage message = CefBrowserMessageNormalizer.Normalize(new
		{
			schemaVersion = 1,
			sessionId = "session-a",
			requestId = 7,
			kind = "handshake",
			topic = "system",
			payload = new { }
		});

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		using JsonDocument document = JsonDocument.Parse(message.Payload);
		Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
	}

	[Test]
	public void Normalize_WhenPayloadWrapsDataPlaneControlChannel_UnwrapsPayload()
	{
		string payload = JsonSerializer.Serialize(new
		{
			schemaVersion = 1,
			sessionId = "session-b",
			requestId = 9,
			kind = "subscribe",
			topic = "webui.entityCollection",
			payload = new { }
		});

		BrowserScriptMessage message = CefBrowserMessageNormalizer.Normalize(new
		{
			channel = BrowserDataPlaneMessageChannels.Control,
			payload
		});

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		using JsonDocument document = JsonDocument.Parse(message.Payload);
		Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-b"));
	}

	[Test]
	public void Normalize_WhenPayloadIsProviderLocalMessage_KeepsProviderPrivateChannel()
	{
		BrowserScriptMessage message = CefBrowserMessageNormalizer.Normalize(new
		{
			source = "browser-ui-showcase",
			payload = "loaded"
		});

		Assert.That(message.Channel, Is.EqualTo("cefsharp"));
		Assert.That(message.Payload, Does.Contain("browser-ui-showcase"));
	}
}
