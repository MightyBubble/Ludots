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
    /// <summary>避让分离推力(厘米;每子步由分离求解写入,马达读)。</summary>
    public Fix64Vec2[] Separation = Array.Empty<Fix64Vec2>();
    /// <summary>静/动分界:1 = 上次解出的推力低于 restDeadband(睡);新生单位 0(必醒)。</summary>
    public byte[] Calm = Array.Empty<byte>();
    /// <summary>哈希序聚集缓冲(分离求解的逐格读取面,与参考 sepCtx 同构)。</summary>
    public Fix64[] GatherX = Array.Empty<Fix64>();
    public Fix64[] GatherY = Array.Empty<Fix64>();
    public Fix64[] GatherRadius = Array.Empty<Fix64>();
    public Fix64[] GatherPriority = Array.Empty<Fix64>();
    public byte[] GatherPlayer = Array.Empty<byte>();
    public byte[] GatherLevel = Array.Empty<byte>();
    public byte[] GatherMoving = Array.Empty<byte>();
    public int[] GatherGroup = Array.Empty<int>();
    /// <summary>逐占格唤醒标记(下标 = 哈希 Active 槽位)。</summary>
    public byte[] Awake = Array.Empty<byte>();
    /// <summary>逐单位的本 tick 接触计数(分离求解累加,会话逐 tick 清零;真值 bin 的 u16 同构,
    /// 跨子步累加不回绕)。</summary>
    public ushort[] Contacts = Array.Empty<ushort>();
    public CrowdWalls.OpenCellCache OpenCache { get; init; } = new();
    /// <summary>流场采样的本帧暂存(方向写出)。</summary>
    public int Tick;

    // ── 避让相位(参考 sepCtx.stride/phase):avoidHz 对子步频率的降频错峰 ──
    public required int Stride { get; set; }
    public int Phase { get; set; }

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
    public required int MaxNeighbors { get; init; }
    /// <summary>单单位单次求解的推力上限(sep 与参考同为无量纲公式值,阈值逐字同参考 maxPush)。</summary>
    public required Fix64 MaxPush { get; init; }
    public required Fix64 Smoothing { get; init; }
    public required Fix64 MovingBonus { get; init; }
    public required Fix64 DominantShare { get; init; }
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
        EnsureAvoidanceCapacity(unitCapacity);
    }

    /// <summary>分离求解的缓冲面(容量按单位上限;Awake 按占格数,构造时定尺寸)。</summary>
    public void EnsureAvoidanceCapacity(int unitCapacity)
    {
        if (Calm.Length < unitCapacity) Calm = new byte[unitCapacity];
        if (GatherX.Length < unitCapacity) GatherX = new Fix64[unitCapacity];
        if (GatherY.Length < unitCapacity) GatherY = new Fix64[unitCapacity];
        if (GatherRadius.Length < unitCapacity) GatherRadius = new Fix64[unitCapacity];
        if (GatherPriority.Length < unitCapacity) GatherPriority = new Fix64[unitCapacity];
        if (GatherPlayer.Length < unitCapacity) GatherPlayer = new byte[unitCapacity];
        if (GatherLevel.Length < unitCapacity) GatherLevel = new byte[unitCapacity];
        if (GatherMoving.Length < unitCapacity) GatherMoving = new byte[unitCapacity];
        if (GatherGroup.Length < unitCapacity) GatherGroup = new int[unitCapacity];
        if (Contacts.Length < unitCapacity) Contacts = new ushort[unitCapacity];
    }

    /// <summary>新单位的槽位不带陈旧推挤,且首次求解前必醒。</summary>
    public void ClearPush(int dense)
    {
        if (dense < Separation.Length) Separation[dense] = Fix64Vec2.Zero;
        if (dense < Calm.Length) Calm[dense] = 0;
    }

    /// <summary>分离求解 + 相位前移(参考 tick():哈希重建后、领队之前)。</summary>
    public void SolveSeparation()
    {
        CrowdAvoidance.Solve(this);
        if (Stride > 1) Phase = (Phase + 1) % Stride;
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
            _radii[i] = session.Units.PersonalRadiusCmAt(i);
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

    /// <summary>从运行时配置装配内核(参数包 = formation + movement + slotGain=1/slotTimeConstant + avoidance 局部;空间哈希按体型上限定尺寸)。
    /// sep 是无量纲公式值(restDeadband/maxPush 阈值与参考逐字同数);降频 stride 按参考 setRates 推导:stepHz/min(avoidHz, stepHz) 四舍五入。</summary>
    public static CrowdMovementKernel Create(CrowdSimSession session, CrowdWalls.OpenCellCache? openCache = null)
    {
        var cfg = session.Config;
        var fc = cfg.Formation;
        var mv = cfg.Movement;
        var av = cfg.Avoidance;
        // 哈希几何按最大个人(避让)半径:参考端 reach = max(personal),格距 = 2×reach/rings;
        // 两值定点精确传入,不许整数截断
        Fix64 maxRadiusCm = Fix64.Zero;
        foreach (var p in cfg.Profiles) maxRadiusCm = Fix64.Max(maxRadiusCm, p.PersonalRadiusCm);
        Fix64 hashCellCm = Fix64.Max(Fix64.OneValue, maxRadiusCm * 2 / Fix64.FromInt(Math.Max(1, av.HashRings)));
        var hash = new CrowdSpatialHash(cfg.NavCellCount * cfg.NavCellSizeCm, hashCellCm, cfg.Sim.MaxUnits, av.HashRings, maxRadiusCm);
        int stepHz = cfg.FixedHz * session.SubSteps;
        int avoidHz = (int)av.RateHz.ToDouble();
        int stride = Math.Max(1, (int)(stepHz / Math.Min(avoidHz, (double)stepHz) + 0.5));
        int hashCells = hash.Dim * hash.Dim;
        return new CrowdMovementKernel
        {
            Session = session,
            Hash = hash,
            Awake = new byte[hashCells],
            Stride = stride,
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
            MaxNeighbors = av.MaxNeighbors,
            MaxPush = av.MaxPush,
            Smoothing = av.Smoothing,
            MovingBonus = cfg.Push.MovingBonus,
            DominantShare = cfg.Push.DominantShare,
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
