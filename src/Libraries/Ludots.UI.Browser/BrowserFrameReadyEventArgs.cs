using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public sealed class BrowserFrameReadyEventArgs : EventArgs
{
	public BrowserFrameReadyEventArgs(
		BrowserViewport viewport,
		BrowserPixelFormat pixelFormat,
		IReadOnlyList<BrowserDirtyRect>? dirtyRects,
		long sequence)
	{
		Viewport = viewport;
		PixelFormat = pixelFormat;
		DirtyRects = dirtyRects ?? Array.Empty<BrowserDirtyRect>();
		Sequence = sequence;
	}

	public BrowserViewport Viewport { get; }

	public BrowserPixelFormat PixelFormat { get; }

	public IReadOnlyList<BrowserDirtyRect> DirtyRects { get; }

	public long Sequence { get; }
}
