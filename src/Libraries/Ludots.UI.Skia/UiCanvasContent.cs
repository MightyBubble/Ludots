using System;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

public sealed class UiCanvasContent : IUiCanvasContent, ISkiaUiCanvasContent
{
	private readonly Action<SKCanvas, SKRect> _draw;

	public UiCanvasContent(Action<SKCanvas, SKRect> draw)
	{
		_draw = draw ?? throw new ArgumentNullException("draw");
	}

	public void Draw(SKCanvas canvas, SKRect rect)
	{
		ArgumentNullException.ThrowIfNull(canvas, "canvas");
		_draw(canvas, rect);
	}
}
