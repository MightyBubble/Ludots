using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public sealed class BrowserFrame
{
	public BrowserFrame(
		BrowserViewport viewport,
		BrowserPixelFormat pixelFormat,
		ReadOnlyMemory<byte> pixels,
		int rowBytes,
		IReadOnlyList<BrowserDirtyRect>? dirtyRects = null,
		long sequence = 0)
	{
		ValidateViewport(viewport);
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
		DirtyRects = ValidateDirtyRects(viewport, dirtyRects);
		Sequence = sequence;
	}

	public BrowserViewport Viewport { get; }

	public BrowserPixelFormat PixelFormat { get; }

	public ReadOnlyMemory<byte> Pixels { get; }

	public int RowBytes { get; }

	public IReadOnlyList<BrowserDirtyRect> DirtyRects { get; }

	public long Sequence { get; }

	private static IReadOnlyList<BrowserDirtyRect> ValidateDirtyRects(
		BrowserViewport viewport,
		IReadOnlyList<BrowserDirtyRect>? dirtyRects)
	{
		if (dirtyRects == null || dirtyRects.Count == 0)
		{
			return Array.Empty<BrowserDirtyRect>();
		}

		var copy = new BrowserDirtyRect[dirtyRects.Count];
		for (int i = 0; i < dirtyRects.Count; i++)
		{
			BrowserDirtyRect rect = dirtyRects[i];
			if (rect.Width <= 0 || rect.Height <= 0 || rect.Right > viewport.Width || rect.Bottom > viewport.Height)
			{
				throw new ArgumentOutOfRangeException(nameof(dirtyRects), "Dirty rect must fit inside the browser viewport.");
			}
			copy[i] = rect;
		}

		return copy;
	}

	private static void ValidateViewport(BrowserViewport viewport)
	{
		if (viewport.Width <= 0 || viewport.Height <= 0 || viewport.DeviceScaleFactor <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(viewport), "Browser frame viewport must be valid.");
		}
	}
}
