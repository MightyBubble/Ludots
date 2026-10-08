using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>移动内核的共享上下文(参考实现 kernel 对象移植):稳定引用 + Fix64 参数包,子步间复用。</summary>
public sealed class CrowdMovementKernel
{
    public required CrowdSimulation.Units.CrowdSimSession Session { get; init; }
    public required CrowdSpatialHash Hash { get; init; }
    /// <summary>意图输出(厘米/秒,按稠密序);马达层读它做一阶响应。</summary>
    public Fix64Vec2[] Intent = Array.Empty<Fix64Vec2>();
    /// <summary>避让分离推力(S6 才写入;S5 恒零,马达照常读)。</summary>
    public Fix64Vec2[] Separation = Array.Empty<Fix64Vec2>();
    public CrowdWalls.OpenCellCache OpenCache { get; init; } = new();
    /// <summary>流场采样的本帧暂存(方向写出)。</summary>
    public int Tick;

    // ── 参数包(参考实现 p = {...formation, ...movement, slotGain: 1/slotTimeConstant, ...avoidance 局部})──
    public required Fix64 BlendRate { get; init; }
    public required Fix64 BlendCommit { get; init; }
    public required int SlotCheckInterval { get; init; }
    public required Fix64 WakeDistanceCells { get; init; }
    public required Fix64 SlotArriveCells { get; init; }
    public required Fix64 SettleRadiusCells { get; init; }
    public required Fix64 StallSpeedRatio { get; init; }
    public required Fix64 StallDecay { get; init; }
    public required Fix64 SettleTimeSeconds { get; init; }
    public required Fix64 SlotSightCells { get; init; }
    public required Fix64 SightHysteresis { get; init; }
    public required Fix64 LaneSpread { get; init; }
    public required Fix64 GoalArriveCells { get; init; }
    public required Fix64 SlotGain { get; init; }
    public required Fix64 CatchUp { get; init; }
    public required Fix64 Accel { get; init; }
    public required Fix64 RestAccelScale { get; init; }
    public required Fix64 RestSeparationScale { get; init; }
    public required Fix64 SeparationWeight { get; init; }
    public required Fix64 RestDeadband { get; init; }
    public required int MaxScan { get; init; }
    public required Fix64 UnitTurnRate { get; init; }
    public required Fix64 StopSpeedRatio { get; init; }
    public required Fix64 SpeedCapRatio { get; init; }
    public required Fix64 JumpSpeedRatio { get; init; }
    public required Fix64 LeaderSpeed { get; init; }
    public required Fix64 LeaderTurnRate { get; init; }
    public required Fix64 FaceTurnScale { get; init; }
    public required Fix64 TurnEpsRad { get; init; }

    /// <summary>意图层上报走廊外单位( S5-b 接走廊扩展;S5-a 先由领队转向兜底)。</summary>
    public Action<int, int>? OnStray { get; init; }

    public void EnsureCapacity(int unitCapacity)
    {
        if (Intent.Length < unitCapacity) Intent = new Fix64Vec2[unitCapacity];
        if (Separation.Length < unitCapacity) Separation = new Fix64Vec2[unitCapacity];
        if (_positions.Length < unitCapacity) _positions = new Fix64Vec2[unitCapacity];
        if (_radii.Length < unitCapacity) _radii = new Fix64[unitCapacity];
    }

    private Fix64Vec2[] _positions = Array.Empty<Fix64Vec2>();
    private Fix64[] _radii = Array.Empty<Fix64>();

    /// <summary>每子步重建空间哈希(位置/半径从组件收拢;无移动快路径在哈希内部)。</summary>
    public void RebuildHash()
    {
        var session = Session;
        int n = session.Units.Count;
        EnsureCapacity(session.Units.Capacity);
        for (int i = 0; i < n; i++)
        {
            var entity = session.Units.EntityAt(i);
            _positions[i] = session.World.Get<Components.WorldPositionCm>(entity).Value;
            _radii[i] = session.ProfilePersonalRadiusCm(session.Units.ProfileIdAt(i));
        }

        Hash.Build(_positions, _radii, n);
    }

    /// <summary>一个子步:领队 → 意图 → 马达(避让在 S6 才会插到领队之前)。</summary>
    public void Step(Fix64 dt)
    {
        var orders = Session.Orders.List;
        Fix64 turnK = Fix64.Min(Fix64.OneValue, dt * LeaderTurnRate);
        Fix64 faceStep = LeaderTurnRate * FaceTurnScale * dt;
        for (int oi = 0; oi < orders.Count; oi++)
        {
            var leaders = orders[oi].Leaders;
            for (int li = 0; li < leaders.Count; li++)
            {
                leaders[li].Step(
                    Session.LayerSpeedCmPerSecond(leaders[li].LayerIdx),
                    LeaderSpeed, turnK, faceStep, dt,
                    Session.Config.NavCellSizeCm, Session.Config.NavCellCount);
            }
        }

        CrowdIntents.Compute(this, dt);
        CrowdMotor.Integrate(this, dt);
        Tick++;
    }

    /// <summary>从运行时配置装配内核(参数包 = formation + movement + slotGain=1/slotTimeConstant + avoidance 局部;空间哈希按体型上限定尺寸)。</summary>
    public static CrowdMovementKernel Create(CrowdSimSession session, CrowdWalls.OpenCellCache? openCache = null)
    {
        var cfg = session.Config;
        var fc = cfg.Formation;
        var mv = cfg.Movement;
        var av = cfg.Avoidance;
        Fix64 maxRadiusCm = Fix64.Zero;
        foreach (var p in cfg.Profiles) maxRadiusCm = Fix64.Max(maxRadiusCm, p.RadiusCm);
        int hashCellCm = Math.Max(1, (int)(maxRadiusCm * 2).ToLong() / Math.Max(1, av.HashRings));
        return new CrowdMovementKernel
        {
            Session = session,
            Hash = new CrowdSpatialHash(cfg.NavCellCount * cfg.NavCellSizeCm, hashCellCm, cfg.Sim.MaxUnits, av.HashRings, (int)maxRadiusCm.ToLong()),
            OpenCache = openCache ?? new CrowdWalls.OpenCellCache(),
            BlendRate = mv.BlendRate,
            BlendCommit = mv.BlendCommit,
            SlotCheckInterval = mv.SlotCheckInterval,
            WakeDistanceCells = mv.WakeDistanceCells,
            SlotArriveCells = fc.SlotArriveCells,
            SettleRadiusCells = fc.SettleRadiusCells,
            StallSpeedRatio = mv.StallSpeedRatio,
            StallDecay = mv.StallDecay,
            SettleTimeSeconds = fc.SettleTime,
            SlotSightCells = fc.SlotSightCells,
            SightHysteresis = mv.SightHysteresis,
            LaneSpread = mv.LaneSpread,
            GoalArriveCells = fc.GoalArriveCells,
            SlotGain = Fix64.OneValue / mv.SlotTimeConstant,
            CatchUp = fc.CatchUp,
            Accel = av.Acceleration,
            RestAccelScale = mv.RestAccelScale,
            RestSeparationScale = mv.RestSeparationScale,
            SeparationWeight = av.SeparationWeight,
            RestDeadband = av.RestDeadband,
            MaxScan = av.MaxScan,
            UnitTurnRate = mv.UnitTurnRate,
            StopSpeedRatio = mv.StopSpeedRatio,
            SpeedCapRatio = mv.SpeedCapRatio,
            JumpSpeedRatio = mv.JumpSpeedRatio,
            LeaderSpeed = fc.LeaderSpeed,
            LeaderTurnRate = fc.LeaderTurnRate,
            FaceTurnScale = fc.FaceTurnScale,
            TurnEpsRad = cfg.Telemetry.TurnEpsRad,
        };
    }
}
