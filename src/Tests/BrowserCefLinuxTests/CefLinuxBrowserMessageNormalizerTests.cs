using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.UI.Browser.CefLinux;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCefLinux;

[TestFixture]
public sealed class CefLinuxBrowserMessageNormalizerTests
{
	[Test]
	public void Normalize_WhenPayloadIsDataPlaneEnvelope_ReturnsStandardControlChannel()
	{
		string payload = JsonSerializer.Serialize(new
		{
			schemaVersion = 1,
			sessionId = "session-a",
			requestId = 7,
			kind = "handshake",
			topic = "system",
			payload = new { }
		});

		BrowserScriptMessage message = CefLinuxBrowserMessageNormalizer.Normalize(payload);

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		using JsonDocument document = JsonDocument.Parse(message.Payload);
		Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
	}

	[Test]
	public void Normalize_WhenPayloadWrapsDataPlaneControlChannel_UnwrapsPayload()
	{
		string nested = JsonSerializer.Serialize(new
		{
			schemaVersion = 1,
			sessionId = "session-b",
			requestId = 9,
			kind = "subscribe",
			topic = "webui.entityCollection",
			payload = new { }
		});

		string payload = JsonSerializer.Serialize(new
		{
			channel = BrowserDataPlaneMessageChannels.Control,
			payload = nested
		});

		BrowserScriptMessage message = CefLinuxBrowserMessageNormalizer.Normalize(payload);

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		using JsonDocument document = JsonDocument.Parse(message.Payload);
		Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-b"));
	}

	[Test]
	public void Normalize_WhenPayloadIsApplicationMessage_UsesProviderNeutralApplicationChannel()
	{
		string payload = JsonSerializer.Serialize(new
		{
			source = "browser-ui-showcase",
			payload = "loaded"
		});

		BrowserScriptMessage message = CefLinuxBrowserMessageNormalizer.Normalize(payload);

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.Application));
	}

	[Test]
	public void Normalize_WhenPayloadIsNotJson_UsesApplicationChannelWithRawPayload()
	{
		BrowserScriptMessage message = CefLinuxBrowserMessageNormalizer.Normalize("plain-host-message");

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.Application));
		Assert.That(message.Payload, Is.EqualTo("plain-host-message"));
	}
}
