using Ludots.UI.Skia;
using SkiaSharp;

namespace CrowdSimulationUiMod.UI;

/// <summary>一条遥测曲线画在一个画布节点上。绘制时直接读 <see cref="CrowdHudState"/> 的样本，不建节点、不分配。</summary>
internal sealed class CrowdSparkCanvas
{
    private readonly CrowdHudState _state;
    private readonly int _series;
    private readonly int _line;
    private readonly SKPaint _paint;
    private readonly SKPaint _hotPaint;

    public CrowdSparkCanvas(CrowdHudState state, int series, int line, int[] color, int[] hotColor)
    {
        _state = state;
        _series = series;
        _line = line;
        _paint = Stroke(color);
        _hotPaint = Stroke(hotColor);
        Content = new UiCanvasContent(Draw);
    }

    public UiCanvasContent Content { get; }

    private void Draw(SKCanvas canvas, SKRect rect)
    {
        int count = _state.SparkCount;
        float[] values = _state.Spark[_series];
        if (count < 2 || rect.Width <= 0f || rect.Height <= 2f) return;
        SKPaint paint = _state.TelemetryHot[_line] ? _hotPaint : _paint;
        float step = rect.Width / (count - 1);
        float usable = rect.Height - 2f;
        float x0 = rect.Left;
        float y0 = rect.Bottom - 1f - values[0] * usable;
        for (int i = 1; i < count; i++)
        {
            float x1 = rect.Left + i * step;
            float y1 = rect.Bottom - 1f - values[i] * usable;
            canvas.DrawLine(x0, y0, x1, y1, paint);
            x0 = x1;
            y0 = y1;
        }
    }

    private static SKPaint Stroke(int[] rgba) => new()
    {
        Color = new SKColor((byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3]),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        IsAntialias = true,
    };
}
