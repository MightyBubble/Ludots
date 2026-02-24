using SkiaSharp;

namespace Ludots.UI.Widgets;

public class Label : Widget
{
    public string Text { get; set; } = "";
    public float FontSize { get; set; } = 20;
    public SKColor TextColor { get; set; } = SKColors.White;
    
    protected override void OnRender(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var paint = new SKPaint
        {
            Color = TextColor,
            IsAntialias = true
        };
        
        // Draw text at (0, FontSize) to account for baseline
        canvas.DrawText(Text, 0, FontSize, SKTextAlign.Left, font, paint);
    }
}
