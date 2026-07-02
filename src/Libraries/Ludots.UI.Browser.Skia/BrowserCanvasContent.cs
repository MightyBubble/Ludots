using System;
using Ludots.UI.Browser;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.UI.Browser.Skia;

public sealed class BrowserCanvasContent : BrowserSurfaceCanvasContent, ISkiaUiCanvasContent
{
	private readonly SkiaBrowserFrameRenderer _renderer;
	private readonly bool _ownsRenderer;

	public BrowserCanvasContent(
		IBrowserSurface surface,
		SkiaBrowserFrameRenderer? renderer = null,
		BrowserSurfaceHitTestOptions? hitTestOptions = null)
		: base(surface, hitTestOptions)
	{
		_renderer = renderer ?? new SkiaBrowserFrameRenderer();
		_ownsRenderer = renderer == null;
	}

	public void Draw(SKCanvas canvas, SKRect rect)
	{
		ArgumentNullException.ThrowIfNull(canvas);
		EnsureSurfaceViewport(rect.Width, rect.Height);
		BrowserFrame? frame = Surface.TryGetLatestFrame();
		if (frame == null)
		{
			return;
		}

		_renderer.DrawFrame(canvas, rect, frame);
	}

	public override void Dispose()
	{
		if (_ownsRenderer)
		{
			_renderer.Dispose();
		}
		base.Dispose();
	}
}
