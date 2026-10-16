using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 里的矢量图元：火花线、滑条、比例条。坐标按画布内容框归一化，颜色取 hud.css 主题同一组 rgba。
/// </summary>
internal static class CrowdHudCanvas
{
    public static readonly UiColor Primary = new(132, 31, 48, 255);
    public static readonly UiColor Destructive = new(181, 62, 48, 255);
    public static readonly UiColor Success = new(93, 118, 76, 255);
    public static readonly UiColor MutedDot = new(130, 124, 115, 102);
    public static readonly UiColor Secondary = new(239, 236, 230, 255);
    public static readonly UiColor PrimaryTrack = new(132, 31, 48, 51);
    public static readonly UiColor PrimaryThumb = new(132, 31, 48, 128);
    public static readonly UiColor Background = new(234, 231, 225, 255);

    public const float SliderHeightPx = 16f;
    private const float TrackPx = 6f;
    private const float ThumbOuterPx = 8f;
    private const float ThumbInnerPx = 6f;
    private const float ThumbInset = 0.03f;

    public static UiVectorDrawList Spark(int history) => new(1, history);

    public static UiVectorDrawList Slider() => new(4, 0);

    public static UiVectorDrawList Bar(int segments) => new(segments + 1, 0);

    /// <summary>
    /// 原型 Sparkline：上界 max(数据, 1e-6)，下界 min(数据, 0)，纵向上下各留 1px。
    /// 样本少于两个时画空。<paramref name="ring"/> 从 <paramref name="start"/> 起按时间顺序读 <paramref name="count"/> 个。
    /// </summary>
    public static void DrawSpark(UiVectorDrawList list, double[] ring, int start, int count, float heightPx, bool alert)
    {
        list.Clear();
        if (count < 2) return;
        int n = ring.Length;
        double max = 1e-6, min = 0d;
        for (int i = 0; i < count; i++)
        {
            double v = ring[(start + i) % n];
            if (v > max) max = v;
            if (v < min) min = v;
        }

        double span = max - min;
        if (span <= 0) span = 1;
        list.BeginPolyline(alert ? Destructive : Primary, 1f);
        for (int i = 0; i < count; i++)
        {
            double v = ring[(start + i) % n];
            double y = heightPx - (v - min) / span * (heightPx - 2) - 1;
            list.AddPoint(i / (float)(count - 1), (float)(y / heightPx));
        }

        list.EndPath();
    }

    /// <summary>滑条：6px 浅轨、已选段、两层圆点的拇指。<paramref name="t"/> 为 0..1，NaN 只画轨。</summary>
    public static void DrawSlider(UiVectorDrawList list, double t)
    {
        list.Clear();
        float top = (SliderHeightPx - TrackPx) / 2f / SliderHeightPx;
        float h = TrackPx / SliderHeightPx;
        list.FillRect(0f, top, 1f, h, PrimaryTrack);
        if (double.IsNaN(t)) return;
        float x = ThumbX(Math.Clamp(t, 0d, 1d));
        list.FillRect(0f, top, x, h, Primary);
        list.FillCircle(x, 0.5f, ThumbOuterPx, PrimaryThumb);
        list.FillCircle(x, 0.5f, ThumbInnerPx, Background);
    }

    /// <summary>点击位置（相对内容框的 0..1）换回滑条取值比例，与 <see cref="DrawSlider"/> 的拇指位置互逆。</summary>
    public static double SliderValueAt(float fraction) => Math.Clamp((fraction - ThumbInset) / (1f - 2f * ThumbInset), 0f, 1f);

    public static void DrawRatio(UiVectorDrawList list, double ratio)
    {
        list.Clear();
        list.FillRect(0f, 0f, 1f, 1f, Secondary);
        if (!double.IsFinite(ratio) || ratio <= 0) return;
        list.FillRect(0f, 0f, (float)Math.Min(1d, ratio), 1f, Primary);
    }

    /// <summary>原型 UnitStates 的堆叠条：移动中、已抵达、不可达、待命，宽度按占总数的比例。</summary>
    public static void DrawAgents(UiVectorDrawList list, int moving, int arrived, int unreachable, int idle, int total)
    {
        list.Clear();
        list.FillRect(0f, 0f, 1f, 1f, Secondary);
        if (total <= 0) total = 1;
        float x = 0f;
        x = Segment(list, x, moving, total, Primary);
        x = Segment(list, x, arrived, total, Success);
        x = Segment(list, x, unreachable, total, Destructive);
        Segment(list, x, idle, total, MutedDot);
    }

    private static float Segment(UiVectorDrawList list, float x, int value, int total, UiColor color)
    {
        if (value <= 0 || x >= 1f) return x;
        float w = Math.Min(1f - x, value / (float)total);
        list.FillRect(x, 0f, w, 1f, color);
        return x + w;
    }

    private static float ThumbX(double t) => (float)(ThumbInset + t * (1d - 2d * ThumbInset));
}
