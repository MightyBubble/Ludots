using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserScriptMessageNormalizerTests
{
	[Test]
	public void Normalize_HitTestCaptureBooleanPayload_UsesHostControlChannel()
	{
		BrowserScriptMessage message = BrowserScriptMessageNormalizer.Normalize(
			"""{"channel":"ludots.browser.hit-test-capture","payload":true}""");

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.HitTestCapture));
		Assert.That(message.Payload, Is.EqualTo("true"));
	}

	[Test]
	public void Normalize_HitTestCaptureStringPayload_UnwrapsPayload()
	{
		BrowserScriptMessage message = BrowserScriptMessageNormalizer.Normalize(
			"""{"channel":"ludots.browser.hit-test-capture","payload":"false"}""");

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.HitTestCapture));
		Assert.That(message.Payload, Is.EqualTo("false"));
	}

	[Test]
	public void Normalize_HitTestCaptureMissingPayload_StillRoutesToCaptureChannel()
	{
		BrowserScriptMessage message = BrowserScriptMessageNormalizer.Normalize(
			"""{"channel":"ludots.browser.hit-test-capture"}""");

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.HitTestCapture));
		Assert.That(message.Payload, Is.EqualTo(string.Empty));
	}

	[Test]
	public void Normalize_UnknownChannelEnvelope_StaysApplication()
	{
		const string payload = """{"channel":"custom.mod.channel","payload":true}""";

		BrowserScriptMessage message = BrowserScriptMessageNormalizer.Normalize(payload);

		Assert.That(message.Channel, Is.EqualTo(BrowserMessageChannels.Application));
		Assert.That(message.Payload, Is.EqualTo(payload));
	}

	[Test]
	public void Normalize_BareDataPlaneEnvelope_RemainsControlChannel()
	{
		BrowserScriptMessage message = BrowserScriptMessageNormalizer.Normalize(
			"""{"schemaVersion":1,"kind":"handshake","topic":"system"}""");

		Assert.That(message.Channel, Is.EqualTo(BrowserDataPlaneMessageChannels.Control));
		Assert.That(message.Payload, Does.Contain("\"kind\":\"handshake\""));
	}
}
