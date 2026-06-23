using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserFrameBufferTests
{
	[Test]
	public void ApplyFullFrame_CopiesPixelsAndMarksWholeViewportDirty()
	{
		var viewport = new BrowserViewport(2, 2);
		var buffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		byte[] source =
		[
			10, 20, 30, 255,
			40, 50, 60, 255,
			70, 80, 90, 255,
			100, 110, 120, 255
		];

		buffer.ApplyFullFrame(source);
		BrowserFrame frame = buffer.Snapshot();

		Assert.That(frame.Sequence, Is.EqualTo(1));
		Assert.That(frame.RowBytes, Is.EqualTo(8));
		Assert.That(frame.DirtyRects, Is.EqualTo(new[] { new BrowserDirtyRect(0, 0, 2, 2) }));
		Assert.That(frame.Pixels.ToArray(), Is.EqualTo(source));
	}

	[Test]
	public void ApplyDirtyFrame_CopiesOnlyDirtyRectPixels()
	{
		var viewport = new BrowserViewport(3, 2);
		var buffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		byte[] initial = new byte[3 * 2 * BrowserFrameBuffer.BytesPerPixel];
		buffer.ApplyFullFrame(initial);

		byte[] source =
		[
			1, 1, 1, 255,
			2, 2, 2, 255,
			3, 3, 3, 255,
			4, 4, 4, 255,
			5, 5, 5, 255,
			6, 6, 6, 255
		];

		buffer.ApplyDirtyFrame(source, 12, new[] { new BrowserDirtyRect(1, 0, 1, 2) });
		BrowserFrame frame = buffer.Snapshot();
		byte[] pixels = frame.Pixels.ToArray();

		Assert.That(frame.Sequence, Is.EqualTo(2));
		Assert.That(frame.DirtyRects, Is.EqualTo(new[] { new BrowserDirtyRect(1, 0, 1, 2) }));
		Assert.That(pixels[4], Is.EqualTo(2));
		Assert.That(pixels[5], Is.EqualTo(2));
		Assert.That(pixels[6], Is.EqualTo(2));
		Assert.That(pixels[16], Is.EqualTo(5));
		Assert.That(pixels[17], Is.EqualTo(5));
		Assert.That(pixels[18], Is.EqualTo(5));
		Assert.That(pixels[0], Is.EqualTo(0));
		Assert.That(pixels[8], Is.EqualTo(0));
		Assert.That(pixels[12], Is.EqualTo(0));
		Assert.That(pixels[20], Is.EqualTo(0));
	}

	[Test]
	public unsafe void ApplyDirtyFrame_FromPointer_CopiesOnlyDirtyRectPixels()
	{
		var viewport = new BrowserViewport(3, 2);
		var buffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		byte[] initial = new byte[3 * 2 * BrowserFrameBuffer.BytesPerPixel];
		buffer.ApplyFullFrame(initial);

		byte[] source =
		[
			1, 1, 1, 255,
			2, 2, 2, 255,
			3, 3, 3, 255,
			4, 4, 4, 255,
			5, 5, 5, 255,
			6, 6, 6, 255
		];

		fixed (byte* sourcePointer = source)
		{
			buffer.ApplyDirtyFrame((IntPtr)sourcePointer, 12, new BrowserDirtyRect(1, 0, 1, 2));
		}

		BrowserFrame frame = buffer.Snapshot();
		byte[] pixels = frame.Pixels.ToArray();

		Assert.That(frame.Sequence, Is.EqualTo(2));
		Assert.That(frame.DirtyRects, Is.EqualTo(new[] { new BrowserDirtyRect(1, 0, 1, 2) }));
		Assert.That(pixels[4], Is.EqualTo(2));
		Assert.That(pixels[5], Is.EqualTo(2));
		Assert.That(pixels[6], Is.EqualTo(2));
		Assert.That(pixels[16], Is.EqualTo(5));
		Assert.That(pixels[17], Is.EqualTo(5));
		Assert.That(pixels[18], Is.EqualTo(5));
		Assert.That(pixels[0], Is.EqualTo(0));
		Assert.That(pixels[8], Is.EqualTo(0));
		Assert.That(pixels[12], Is.EqualTo(0));
		Assert.That(pixels[20], Is.EqualTo(0));
	}

	[Test]
	public void ApplyDirtyFrame_RejectsRectsOutsideViewport()
	{
		var buffer = new BrowserFrameBuffer(new BrowserViewport(2, 2), BrowserPixelFormat.Bgra8888Premultiplied);
		byte[] source = new byte[2 * 2 * BrowserFrameBuffer.BytesPerPixel];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			buffer.ApplyDirtyFrame(source, 8, new[] { new BrowserDirtyRect(1, 1, 2, 1) }));
	}

	[Test]
	public void Constructor_RejectsDefaultViewport()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new BrowserFrameBuffer(default, BrowserPixelFormat.Bgra8888Premultiplied));
	}

	[Test]
	public void BrowserFrame_CopiesDirtyRects()
	{
		var dirtyRects = new List<BrowserDirtyRect>
		{
			new BrowserDirtyRect(0, 0, 1, 1)
		};
		var frame = new BrowserFrame(
			new BrowserViewport(2, 2),
			BrowserPixelFormat.Bgra8888Premultiplied,
			new byte[2 * 2 * BrowserFrameBuffer.BytesPerPixel],
			2 * BrowserFrameBuffer.BytesPerPixel,
			dirtyRects);

		dirtyRects[0] = new BrowserDirtyRect(1, 1, 1, 1);

		Assert.That(frame.DirtyRects, Is.EqualTo(new[] { new BrowserDirtyRect(0, 0, 1, 1) }));
	}

	[Test]
	public void BrowserFrame_RejectsRectsOutsideViewport()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new BrowserFrame(
				new BrowserViewport(2, 2),
				BrowserPixelFormat.Bgra8888Premultiplied,
				new byte[2 * 2 * BrowserFrameBuffer.BytesPerPixel],
				2 * BrowserFrameBuffer.BytesPerPixel,
				new[] { new BrowserDirtyRect(1, 1, 2, 1) }));
	}
}
