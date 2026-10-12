namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 人群仿真遥测(MassNavigationTelemetry 同款 POCO,无字符串键)。
/// 本 tick 快照(赋值覆盖):Units、Contacts、Idle/Moving/Arrived/Unreachable/Jumping、LastTickMs。
/// 会话累计:Ticks、Rebakes、FogRefreshes、SubStepClamped、EvictStuck、Plans,以及 PathWaits 与 StallTicks(同一次停摆各加一,无头 Step/Advance 也计)。
/// TickMs 是墙钟 EMA,MaxTickMs 是墙钟峰值。
/// </summary>
public sealed class CrowdSimulationTelemetry
{
    private const double Ema = 0.9;

    public long Ticks { get; private set; }
    public long Units { get; private set; }
    public long Contacts { get; private set; }
    public long Rebakes { get; private set; }
    public long FogRefreshes { get; private set; }
    public long PathWaits { get; private set; }
    public long StallTicks { get; private set; }
    public long SubStepClamped { get; private set; }
    public long EvictStuck { get; private set; }
    public long Plans { get; private set; }
    public int Idle { get; private set; }
    public int Moving { get; private set; }
    public int Arrived { get; private set; }
    public int Unreachable { get; private set; }
    public int Jumping { get; private set; }
    public double LastTickMs { get; private set; }
    public double TickMs { get; private set; }
    public double MaxTickMs { get; private set; }

    /// <summary>每 tick 一次:会话步进后调用(tick 墙钟由调用方用秒表环测)。Contacts 是本 tick 读数,停摆帧传 0。</summary>
    public void BeginFrame(int units, int contacts, bool stalled, double tickMs)
    {
        Ticks++;
        Units = units;
        Contacts = stalled ? 0 : contacts;
        LastTickMs = tickMs;
        TickMs = Ticks == 1 ? tickMs : TickMs * Ema + tickMs * (1 - Ema);
        if (tickMs > MaxTickMs) MaxTickMs = tickMs;
    }

    public void MarkRebake() => Rebakes++;
    public void MarkFogRefresh(int groups) => FogRefreshes += groups;

    /// <summary>路径答复未到。PathWaits 与 StallTicks 是同一次停摆,一起加,无头 Step 也走这里。</summary>
    public void MarkPathWait()
    {
        PathWaits++;
        StallTicks++;
    }
    public void MarkSubStepClamped() => SubStepClamped++;
    public void MarkEvictStuck(int stuck) => EvictStuck += stuck;
    public void MarkPlans(int applied) => Plans += applied;

    public void SetUnitStates(int idle, int moving, int arrived, int unreachable, int jumping)
    {
        Idle = idle;
        Moving = moving;
        Arrived = arrived;
        Unreachable = unreachable;
        Jumping = jumping;
    }

    public void ResetCounters()
    {
        Ticks = Units = Contacts = Rebakes = FogRefreshes = PathWaits = StallTicks = 0;
        SubStepClamped = EvictStuck = Plans = 0;
        Idle = Moving = Arrived = Unreachable = Jumping = 0;
        LastTickMs = TickMs = MaxTickMs = 0;
    }
}
