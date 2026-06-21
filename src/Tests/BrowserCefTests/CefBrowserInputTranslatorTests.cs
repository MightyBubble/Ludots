using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserInputTranslatorTests
{
	[Test]
	public void ToCefWheelDelta_TranslatesDomWheelDirectionToNativeCefDirection()
	{
		var wheel = new BrowserWheelEvent(
			X: 10f,
			Y: 20f,
			DeltaX: 36f,
			DeltaY: 120f);

		CefWheelDelta delta = CefBrowserInputTranslator.ToCefWheelDelta(wheel);

		Assert.That(delta.DeltaX, Is.EqualTo(-36));
		Assert.That(delta.DeltaY, Is.EqualTo(-120));
	}

	[Test]
	public void ToCefWheelDelta_RoundsAfterDirectionTranslation()
	{
		var wheel = new BrowserWheelEvent(
			X: 10f,
			Y: 20f,
			DeltaX: -12.4f,
			DeltaY: -80.6f);

		CefWheelDelta delta = CefBrowserInputTranslator.ToCefWheelDelta(wheel);

		Assert.That(delta.DeltaX, Is.EqualTo(12));
		Assert.That(delta.DeltaY, Is.EqualTo(81));
	}
}
