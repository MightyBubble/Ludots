using Ludots.UI.Skia;
using SkiaSharp;

namespace CrowdSimulationUiMod.UI;

/// <summary>一条遥测曲线画在一个画布节点上。绘制时直接读 <see cref="CrowdHudState"/> 的环形样本，不建节点、不分配。</summary>
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
        _paint = Fill(color);
        _hotPaint = Fill(hotColor);
        Content = new UiCanvasContent(Draw);
    }

    public UiCanvasContent Content { get; }

    private void Draw(SKCanvas canvas, SKRect rect)
    {
        int count = _state.SparkCount;
        float[] values = _state.Spark[_series];
        if (count < 2 || rect.Width <= 0f || rect.Height <= 0f) return;
        float slot = rect.Width / values.Length;
        float bar = MathF.Max(1f, slot - 1f);
        float left = rect.Right - count * slot;
        SKPaint paint = _state.TelemetryHot[_line] ? _hotPaint : _paint;
        for (int i = 0; i < count; i++)
        {
            float h = MathF.Max(1f, values[i] * rect.Height);
            canvas.DrawRect(left + i * slot, rect.Bottom - h, bar, h, paint);
        }
    }

    private static SKPaint Fill(int[] rgba) => new()
    {
        Color = new SKColor((byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3]),
        Style = SKPaintStyle.Fill,
        IsAntialias = false,
    };
}
