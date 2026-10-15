using SkiaSharp;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 固定的 Skia 光栅工作量：清一张和 HUD 同尺寸的表面，画圆角框和预先建好的文字块。
/// 只用 SkiaSharp，不经过 Ludots.UI，所以 HUD 和 UI 代码变慢不会让它变慢；
/// 同核另一个超线程、缓存和内存带宽被别人占时，它和 HUD 重画一起变慢，用来估计当时的机器速度。
/// 构造时建好所有对象，<see cref="Run"/> 不分配。
/// </summary>
internal sealed class SkiaReferenceRaster : IDisposable
{
    private const int Panels = 12;
    private const int LinesPerPanel = 8;

    private readonly SKSurface _surface;
    private readonly SKPaint _fill = new() { Color = new SKColor(20, 24, 32, 220), IsAntialias = true };
    private readonly SKPaint _stroke = new() { Color = new SKColor(255, 255, 255, 60), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
    private readonly SKPaint _text = new() { Color = SKColors.White, IsAntialias = true };
    private readonly SKFont _font = new(SKTypeface.Default, 13f);
    private readonly SKTextBlob[] _lines = new SKTextBlob[LinesPerPanel];

    public SkiaReferenceRaster(int width, int height)
    {
        _surface = SKSurface.Create(new SKImageInfo(width, height));
        for (int i = 0; i < _lines.Length; i++)
            _lines[i] = SKTextBlob.Create($"tick {1000 + i * 37} units 50000 idle {i * 113} moving {i * 7} p95 {i}.{i * 3} ms", _font);
    }

    public void Run()
    {
        SKCanvas canvas = _surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        for (int p = 0; p < Panels; p++)
        {
            float x = 12f + (p % 4) * 300f;
            float y = 12f + (p / 4) * 180f;
            var rect = new SKRect(x, y, x + 280f, y + 160f);
            canvas.DrawRoundRect(rect, 6f, 6f, _fill);
            canvas.DrawRoundRect(rect, 6f, 6f, _stroke);
            for (int l = 0; l < LinesPerPanel; l++)
                canvas.DrawText(_lines[l], x + 10f, y + 20f + l * 17f, _text);
        }

        canvas.Flush();
    }

    public void Dispose()
    {
        foreach (var line in _lines) line.Dispose();
        _font.Dispose();
        _text.Dispose();
        _stroke.Dispose();
        _fill.Dispose();
        _surface.Dispose();
    }
}
