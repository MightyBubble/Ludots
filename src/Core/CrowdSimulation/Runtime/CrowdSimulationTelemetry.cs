namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 人群仿真遥测(MassNavigationTelemetry 同款形状:类型化 POCO,无字符串键)。
/// 运行时每 tick 观测一次;看板/调试面板直接读属性——呈现侧文本重建是演示层的职责,
/// 这里只做计数与采样。TickMs 用 EMA 平滑(首样本直接落值)。
/// </summary>
public sealed class CrowdSimulationTelemetry
{
    private const double Ema = 0.9;

    public long Ticks { get; private set; }
    public long Units { get; private set; }
    public long ContactsSum { get; private set; }
    public long Rebakes { get; private set; }
    public long FogRefreshes { get; private set; }
    public long PathWaits { get; private set; }
    public long StallTicks { get; private set; }
    public double LastTickMs { get; private set; }
    public double TickMs { get; private set; }
    public double MaxTickMs { get; private set; }

    /// <summary>每 tick 一次:会话步进后调用(tick 墙钟由调用方用秒表环测)。</summary>
    public void BeginFrame(int units, int contactsSum, bool stalled, double tickMs)
    {
        Ticks++;
        Units = units;
        ContactsSum += contactsSum;
        if (stalled) StallTicks++;
        LastTickMs = tickMs;
        TickMs = Ticks == 1 ? tickMs : TickMs * Ema + tickMs * (1 - Ema);
        if (tickMs > MaxTickMs) MaxTickMs = tickMs;
    }

    public void MarkRebake() => Rebakes++;
    public void MarkFogRefresh(int groups) => FogRefreshes += groups;
    public void MarkPathWait() => PathWaits++;
    public void ResetCounters()
    {
        Ticks = Units = ContactsSum = Rebakes = FogRefreshes = PathWaits = StallTicks = 0;
        LastTickMs = TickMs = MaxTickMs = 0;
    }
}
