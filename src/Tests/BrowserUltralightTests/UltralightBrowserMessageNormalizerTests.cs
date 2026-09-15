using Ludots.UI.Browser;
using Ludots.UI.Browser.Ultralight;
using NUnit.Framework;

namespace Ludots.Tests.BrowserUltralight;

[TestFixture]
public sealed class UltralightBrowserMessageNormalizerTests
{
	[Test]
	public void Normalize_BareDataPlaneEnvelope_BecomesControlChannel()
	{
		const string envelope =
			"""{"schemaVersion":1,"kind":"subscribe","topic":"ludots.panel.panel.fireball.status","sessionId":"fireball-web-skin"}""";

		BrowserScriptMessage message = UltralightBrowserMessageNormalizer.Normalize(envelope);

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		Assert.That(message.Payload, Does.Contain("\"kind\":\"subscribe\""));
		Assert.That(message.Payload, Does.Contain("ludots.panel.panel.fireball.status"));
	}

	[Test]
	public void Normalize_WrappedControlEnvelope_UnwrapsPayload()
	{
		const string wrapped =
			"""{"channel":"ludots.dataplane.control","payload":"{\"schemaVersion\":1,\"kind\":\"handshake\"}"}""";

		BrowserScriptMessage message = UltralightBrowserMessageNormalizer.Normalize(wrapped);

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		Assert.That(message.Payload, Does.Contain("\"kind\":\"handshake\""));
	}

	[Test]
	public void Normalize_PlainApplicationText_StaysApplication()
	{
		BrowserScriptMessage message = UltralightBrowserMessageNormalizer.Normalize("not-json");

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.Application));
		Assert.That(message.Payload, Is.EqualTo("not-json"));
	}
}
