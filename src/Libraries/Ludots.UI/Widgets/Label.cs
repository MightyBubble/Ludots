using SkiaSharp;

namespace Ludots.UI.Widgets;

public class Label : Widget
{
	public string Text { get; set; } = "";

	public float FontSize { get; set; } = 20f;

	public SKColor TextColor { get; set; } = SKColors.White;

	protected override void OnRender(SKCanvas canvas)
	{
		using SKFont font = new SKFont(SKTypeface.Default, FontSize);
		using SKPaint paint = new SKPaint
		{
			Color = TextColor,
			IsAntialias = true
		};
		canvas.DrawText(Text, 0f, FontSize, SKTextAlign.Left, font, paint);
	}
}
