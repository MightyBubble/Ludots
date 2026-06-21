using SkiaSharp;

namespace Ludots.UI.Skia;

public interface ISkiaUiCanvasContent
{
	void Draw(SKCanvas canvas, SKRect rect);
}
