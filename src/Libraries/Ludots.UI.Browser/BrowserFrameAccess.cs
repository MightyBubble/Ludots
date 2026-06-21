using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public readonly struct BrowserFrameAccess
{
	public BrowserFrameAccess(
		BrowserViewport viewport,
		BrowserPixelFormat pixelFormat,
		ReadOnlyMemory<byte> pixels,
		int rowBytes,
		IReadOnlyList<BrowserDirtyRect>? dirtyRects,
		long sequence)
	{
		if (rowBytes < checked(viewport.Width * BrowserFrameBuffer.BytesPerPixel))
		{
			throw new ArgumentOutOfRangeException(nameof(rowBytes), "Frame row bytes are smaller than the viewport width.");
		}

		int requiredBytes = checked(rowBytes * viewport.Height);
		if (pixels.Length < requiredBytes)
		{
			throw new ArgumentException("Frame pixel buffer is smaller than rowBytes * height.", nameof(pixels));
		}

		Viewport = viewport;
		PixelFormat = pixelFormat;
		Pixels = pixels;
		RowBytes = rowBytes;
		DirtyRects = dirtyRects ?? Array.Empty<BrowserDirtyRect>();
		Sequence = sequence;
	}

	public BrowserViewport Viewport { get; }

	public BrowserPixelFormat PixelFormat { get; }

	public ReadOnlyMemory<byte> Pixels { get; }

	public int RowBytes { get; }

	public IReadOnlyList<BrowserDirtyRect> DirtyRects { get; }

	public long Sequence { get; }

	public static BrowserFrameAccess FromFrame(BrowserFrame frame)
	{
		ArgumentNullException.ThrowIfNull(frame);
		return new BrowserFrameAccess(
			frame.Viewport,
			frame.PixelFormat,
			frame.Pixels,
			frame.RowBytes,
			frame.DirtyRects,
			frame.Sequence);
	}
}
