using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserHitMaskCompositeTests
{
	[Test]
	public void WriteVisualBgra_MaskPixel_ClearsVisualAlpha()
	{
		Span<byte> pixel = stackalloc byte[4];
		BrowserHitMaskColor mask = BrowserHitMaskColor.Default;

		BrowserHitMaskComposite.WriteVisualBgra(pixel, mask.B, mask.G, mask.R, 255, mask);

		Assert.That(pixel.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
	}

	[Test]
	public void WriteVisualBgra_ColoredPixel_KeepsPremultipliedColor()
	{
		Span<byte> pixel = stackalloc byte[4];

		BrowserHitMaskComposite.WriteVisualBgra(pixel, 10, 20, 200, 255, BrowserHitMaskColor.Default);

		Assert.That(pixel.ToArray(), Is.EqualTo(new byte[] { 10, 20, 200, 255 }));
	}

	[Test]
	public void ApplyToBgraBuffer_OnlyClearsMaskPixels()
	{
		BrowserHitMaskColor mask = BrowserHitMaskColor.Default;
		byte[] pixels =
		{
			mask.B, mask.G, mask.R, 255,
			0, 20, 200, 255
		};

		BrowserHitMaskComposite.ApplyToBgraBuffer(pixels, 8, 2, 1, mask);

		Assert.That(pixels, Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 20, 200, 255 }));
	}
}
