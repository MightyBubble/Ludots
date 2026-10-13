using System.Diagnostics;
using System.Threading;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 一个 tick 内各阶段的秒表 tick 累计。单位是 <see cref="Stopwatch.GetTimestamp"/> 的 tick,不是毫秒。
/// 子步多次发生的阶段在本 tick 内相加。ParallelWork 是各区间执行时长之和,ParallelWall 是调度墙钟之和,两者不计入阶段合计。
/// </summary>
public struct CrowdSimulationPhaseTicks
{
    public long Command;
    public long Rebake;
    public long Plan;
    public long Gather;
    public long Hash;
    public long Separation;
    public long Leader;
    public long Intent;
    public long Motor;
    public long Scatter;
    public long Fog;
    public long Belief;
    public long Refresh;
    public long Checksum;
    public long ParallelWork;
    public long ParallelWall;
    public long GatherWall;
    public long GatherWork;
    public long SolveWall;
    public long SolveWork;
    public long IntentWall;
    public long IntentWork;
    public long MotorWall;
    public long MotorWork;

    public readonly long Accounted =>
        Command + Rebake + Plan + Gather + Hash + Separation + Leader + Intent + Motor + Scatter + Fog + Belief + Refresh + Checksum;

    public void Accumulate(in CrowdSimulationPhaseTicks sample)
    {
        Command += sample.Command;
        Rebake += sample.Rebake;
        Plan += sample.Plan;
        Gather += sample.Gather;
        Hash += sample.Hash;
        Separation += sample.Separation;
        Leader += sample.Leader;
        Intent += sample.Intent;
        Motor += sample.Motor;
        Scatter += sample.Scatter;
        Fog += sample.Fog;
        Belief += sample.Belief;
        Refresh += sample.Refresh;
        Checksum += sample.Checksum;
        ParallelWork += sample.ParallelWork;
        ParallelWall += sample.ParallelWall;
        GatherWall += sample.GatherWall;
        GatherWork += sample.GatherWork;
        SolveWall += sample.SolveWall;
        SolveWork += sample.SolveWork;
        IntentWall += sample.IntentWall;
        IntentWork += sample.IntentWork;
        MotorWall += sample.MotorWall;
        MotorWork += sample.MotorWork;
    }
}

/// <summary>
/// 人群仿真遥测(MassNavigationTelemetry 同款 POCO,无字符串键)。
/// 本 tick 快照(赋值覆盖):Units、Contacts、Idle/Moving/Arrived/Unreachable/Jumping、LastTickMs、LastPhaseTicks。
/// 会话累计:Ticks、Rebakes、FogRefreshes、SubStepClamped、EvictStuck、Plans,以及 PathWaits 与 StallTicks(同一次停摆各加一,无头 Step/Advance 也计)。
/// TickMs 是墙钟 EMA,MaxTickMs 是墙钟峰值。SumPhaseTicks 是各阶段秒表 tick 的会话累计。
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
    /// <summary>本 tick 计入航向均值的抽样次数。0 表示没有可比较的相邻步。</summary>
    public int MotionSamples { get; private set; }
    /// <summary>移动单位相邻步航向变化的绝对值均值(度)。没有样本时为 0。</summary>
    public double HeadingDeg { get; private set; }
    public int FlipCount { get; private set; }
    public int SteerPairs { get; private set; }
    /// <summary>左右转向反复率，0..<see cref="CrowdSimulationLimits.FullPercent"/>。没有转向对时为 0。</summary>
    public int FlipPct { get; private set; }
    public double LastTickMs { get; private set; }
    public double TickMs { get; private set; }
    public double MaxTickMs { get; private set; }
    public CrowdSimulationPhaseTicks LastPhaseTicks => _lastPhaseTicks;
    public CrowdSimulationPhaseTicks SumPhaseTicks => _sumPhaseTicks;

    private CrowdSimulationPhaseTicks _phaseFrame;
    private CrowdSimulationPhaseTicks _lastPhaseTicks;
    private CrowdSimulationPhaseTicks _sumPhaseTicks;

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

    /// <summary>本 tick 运动质量。反复率用整数百分数，分母为 0 时记 0。</summary>
    public void SetMotion(int samples, double headingDeg, int flips, int steerPairs)
    {
        MotionSamples = samples;
        HeadingDeg = headingDeg;
        FlipCount = flips;
        SteerPairs = steerPairs;
        FlipPct = steerPairs > 0
            ? (int)((long)CrowdSimulationLimits.FullPercent * flips / steerPairs)
            : 0;
    }

    public void ResetCounters()
    {
        Ticks = Units = Contacts = Rebakes = FogRefreshes = PathWaits = StallTicks = 0;
        SubStepClamped = EvictStuck = Plans = 0;
        Idle = Moving = Arrived = Unreachable = Jumping = 0;
        MotionSamples = FlipCount = SteerPairs = FlipPct = 0;
        HeadingDeg = 0;
        LastTickMs = TickMs = MaxTickMs = 0;
        _phaseFrame = _lastPhaseTicks = _sumPhaseTicks = default;
    }

    internal static double TicksToMs(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    internal void BeginPhaseFrame() => _phaseFrame = default;

    internal void CommitPhaseFrame()
    {
        _lastPhaseTicks = _phaseFrame;
        _sumPhaseTicks.Accumulate(in _phaseFrame);
    }

    internal void AddCommandTicks(long ticks) => _phaseFrame.Command += ticks;
    internal void AddRebakeTicks(long ticks) => _phaseFrame.Rebake += ticks;
    internal void AddPlanTicks(long ticks) => _phaseFrame.Plan += ticks;
    internal void AddGatherTicks(long ticks) => _phaseFrame.Gather += ticks;
    internal void AddHashTicks(long ticks) => _phaseFrame.Hash += ticks;
    internal void AddSeparationTicks(long ticks) => _phaseFrame.Separation += ticks;
    internal void AddLeaderTicks(long ticks) => _phaseFrame.Leader += ticks;
    internal void AddIntentTicks(long ticks) => _phaseFrame.Intent += ticks;
    internal void AddMotorTicks(long ticks) => _phaseFrame.Motor += ticks;
    internal void AddScatterTicks(long ticks) => _phaseFrame.Scatter += ticks;
    internal void AddFogTicks(long ticks) => _phaseFrame.Fog += ticks;
    internal void AddBeliefTicks(long ticks) => _phaseFrame.Belief += ticks;
    internal void AddRefreshTicks(long ticks) => _phaseFrame.Refresh += ticks;
    internal void AddChecksumTicks(long ticks) => _phaseFrame.Checksum += ticks;
    internal void AddParallelWallTicks(long ticks) => _phaseFrame.ParallelWall += ticks;

    internal long ReadParallelWorkTicks() => Volatile.Read(ref _phaseFrame.ParallelWork);

    internal void AddGatherParallel(long wall, long work)
    {
        _phaseFrame.GatherWall += wall;
        _phaseFrame.GatherWork += work;
    }

    internal void AddSolveParallel(long wall, long work)
    {
        _phaseFrame.SolveWall += wall;
        _phaseFrame.SolveWork += work;
    }

    internal void AddIntentParallel(long wall, long work)
    {
        _phaseFrame.IntentWall += wall;
        _phaseFrame.IntentWork += work;
    }

    internal void AddMotorParallel(long wall, long work)
    {
        _phaseFrame.MotorWall += wall;
        _phaseFrame.MotorWork += work;
    }

    /// <summary>区间在工人线程上执行,只累加这一格。</summary>
    internal void AddParallelWorkTicks(long ticks) => Interlocked.Add(ref _phaseFrame.ParallelWork, ticks);

    internal static long TakeTicks(ref long mark)
    {
        long now = Stopwatch.GetTimestamp();
        long elapsed = now - mark;
        mark = now;
        return elapsed;
    }
}
