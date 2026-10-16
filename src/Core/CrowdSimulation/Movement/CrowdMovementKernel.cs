using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Schedulers;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>移动内核的共享上下文:稳定引用 + Fix64 参数包,子步间复用。</summary>
public sealed class CrowdMovementKernel
{
    public required CrowdSimulation.Units.CrowdSimSession Session { get; init; }
    public required CrowdSpatialHash Hash { get; init; }
    /// <summary>意图输出(厘米/秒,按稠密序);马达层读它做一阶响应。</summary>
    public Fix64Vec2[] Intent = Array.Empty<Fix64Vec2>();
    /// <summary>
    /// 避让分离推力(厘米;每子步由分离求解写入,马达读)。
    /// 按下标对齐稠密行,GatherUnits 不重读。退场只走 CrowdSimUnits.ReleaseDense,同一次用 MoveAvoidanceRow 把末行换到空位。
    /// </summary>
    public Fix64Vec2[] Separation = Array.Empty<Fix64Vec2>();
    /// <summary>
    /// 静/动分界:1 = 上次解出的推力低于 restDeadband(睡);新生单位 0(必醒)。
    /// 与 Separation 一样按下标对齐稠密行并活过 GatherUnits,退场时由 MoveAvoidanceRow 换走。
    /// </summary>
    public byte[] Calm = Array.Empty<byte>();
    /// <summary>哈希序聚集缓冲(分离求解的逐格读取面)。</summary>
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
    /// <summary>逐单位的本 tick 接触计数。每 tick 清零后按子步累加到 ushort。超过 65535 回绕。标准配置 maxNeighbors 24、maxSubSteps 8,够不到。</summary>
    public ushort[] Contacts = Array.Empty<ushort>();
    /// <summary>本 tick 接触合计。ScatterUnits 顺带把 ushort 加总,不再另扫一遍。</summary>
    public int ContactSum;

    // 子步地址是稠密下标:空间哈希、分离、接触和并行区间都按下标随机读写。
    // chunk 查询按原型分块,没有跨 chunk 的连续 Span,查询序也不是稠密序(模板组件集可以不同)。
    // 每个 tick 用下面的查询把列扫进 SoA,子步只碰 SoA,tick 末按槽位映回的下标写回 chunk。
    private readonly QueryDescription _unitColumns = new QueryDescription()
        .WithAll<CrowdSimulation.Units.CrowdSimulationUnitState, CrowdSimulation.Units.CrowdSimulationKinematics, Ludots.Core.Components.WorldPositionCm, CrowdSimulationAgent, Ludots.Core.Gameplay.Components.PlayerOwner>()
        .WithNone<Ludots.Core.Presentation.Components.PresentationDestroyPending>();
    public CrowdSimulation.Units.CrowdSimulationUnitState[] States = Array.Empty<CrowdSimulation.Units.CrowdSimulationUnitState>();
    public CrowdSimulation.Units.CrowdSimulationKinematics[] Kins = Array.Empty<CrowdSimulation.Units.CrowdSimulationKinematics>();
    /// <summary>位置(厘米);gather 后由马达子步持续更新,scatter 落回组件。</summary>
    public Fix64Vec2[] Positions = Array.Empty<Fix64Vec2>();
    /// <summary>个人半径(厘米)。生成时已落在无字符串的体型组件上,收拢时按列拷进这张表。</summary>
    public Fix64[] Radii = Array.Empty<Fix64>();
    /// <summary>巡航速度(厘米/秒,Agent 解析值)。</summary>
    public Fix64[] Speeds = Array.Empty<Fix64>();
    /// <summary>推挤优先级(Agent 解析值,不含移动加成——加成依赖子步间的状态,避让层现算)。</summary>
    public Fix64[] PushPriorities = Array.Empty<Fix64>();
    /// <summary>玩家表序下标(PlayerOwner → PairRules.IndexByPlayerId 一次收拢)。</summary>
    public int[] PlayerIdx = Array.Empty<int>();

    // 意图层逐子步的组级备忘:指令簿线性扫与认知槽字典解析只在 tick 边界变,gather 时失效。
    internal Movement.CrowdOrder?[] OrderMemo = Array.Empty<Movement.CrowdOrder?>();
    internal Nav.NavContext?[] NavMemo = Array.Empty<Nav.NavContext?>();
    internal byte[] OrderStamp = Array.Empty<byte>();
    internal byte[] NavStamp = Array.Empty<byte>();
    internal byte GroupMemoEpoch;
    public CrowdWalls.OpenCellCache OpenCache { get; init; } = new();
    /// <summary>流场采样的本帧暂存(方向写出)。</summary>
    public int Tick;
    internal IntentProposal[] Proposals = Array.Empty<IntentProposal>();
    internal int[] OrderGate = Array.Empty<int>();
    internal int[] NavGate = Array.Empty<int>();
    internal CrowdIntentScalars IntentScalars;
    internal CrowdMotorFrame MotorFrame;
    private IndexRangeJob[] _rangeJobs = Array.Empty<IndexRangeJob>();
    private JobHandle[] _rangeHandles = Array.Empty<JobHandle>();

    // avoidHz 相对子步频率降频。rateHz 大于步进频率则抛错。
    public required int Stride { get; set; }
    public int Phase { get; set; }

    // 参数来自 formation、movement 与 avoidance。slotGain = 1/slotTimeConstantSec。
    public required Fix64 BlendRate { get; init; }
    public required Fix64 BlendCommitRatio { get; init; }
    public required int SlotCheckInterval { get; init; }
    public required Fix64 WakeDistanceNavCells { get; init; }
    public required Fix64 SlotArriveNavCells { get; init; }
    public required Fix64 SettleRadiusNavCells { get; init; }
    public required Fix64 StallSpeedRatio { get; init; }
    public required Fix64 StallDecay { get; init; }
    public required Fix64 SettleTimeSeconds { get; init; }
    public required Fix64 SlotSightNavCells { get; init; }
    public required Fix64 SightHysteresisScale { get; init; }
    public required Fix64 LaneSpreadSpeedRatio { get; init; }
    public required Fix64 GoalArriveNavCells { get; init; }
    public required Fix64 SlotGain { get; init; }
    public required Fix64 CatchUpSpeedRatio { get; init; }
    public required Fix64 Accel { get; init; }
    public required Fix64 RestAccelScale { get; init; }
    public required Fix64 RestSeparationScale { get; init; }
    public required Fix64 SeparationWeight { get; init; }
    public required Fix64 RestDeadband { get; init; }
    public required int MaxScan { get; init; }
    public required int MaxNeighbors { get; init; }
    /// <summary>单单位单次求解的推力上限(无量纲)。</summary>
    public required Fix64 MaxPush { get; init; }
    public required Fix64 SmoothingRatio { get; init; }
    public required Fix64 MovingPriorityBonus { get; init; }
    public required Fix64 DominantShare { get; init; }
    public required Fix64 UnitTurnRate { get; init; }
    public required Fix64 StopSpeedRatio { get; init; }
    public required Fix64 SpeedCapRatio { get; init; }
    public required Fix64 LinkSpeedRatio { get; init; }
    public required Fix64 LeaderSpeedRatio { get; init; }
    public required Fix64 LeaderTurnRate { get; init; }
    public required Fix64 FaceTurnScale { get; init; }
    public required Fix64 TurnEpsRad { get; init; }

    private Fix64[] _prevVx = Array.Empty<Fix64>();
    private Fix64[] _prevVy = Array.Empty<Fix64>();
    private sbyte[] _steer = Array.Empty<sbyte>();
    private int _sampleCursor;
    private double _headingSum;
    private int _headingCount;
    private int _flips;
    private int _steerPairs;
    private double _sinEps2 = double.NaN;

    /// <summary>场外采样回调。意图提交时按稠密下标调用，不把提案计算改成串行。</summary>
    public Action<int, int>? OnStray { get; set; }

    public void EnsureCapacity(int unitCapacity)
    {
        if (Intent.Length < unitCapacity) Intent = new Fix64Vec2[unitCapacity];
        if (Proposals.Length < unitCapacity) Proposals = new IntentProposal[unitCapacity];
        if (Separation.Length < unitCapacity) Separation = new Fix64Vec2[unitCapacity];
        if (Positions.Length < unitCapacity) Positions = new Fix64Vec2[unitCapacity];
        if (Radii.Length < unitCapacity) Radii = new Fix64[unitCapacity];
        if (States.Length < unitCapacity) States = new CrowdSimulation.Units.CrowdSimulationUnitState[unitCapacity];
        if (Kins.Length < unitCapacity) Kins = new CrowdSimulation.Units.CrowdSimulationKinematics[unitCapacity];
        if (Speeds.Length < unitCapacity) Speeds = new Fix64[unitCapacity];
        if (PushPriorities.Length < unitCapacity) PushPriorities = new Fix64[unitCapacity];
        if (PlayerIdx.Length < unitCapacity) PlayerIdx = new int[unitCapacity];
        if (_prevVx.Length < unitCapacity)
        {
            Array.Resize(ref _prevVx, unitCapacity);
            Array.Resize(ref _prevVy, unitCapacity);
            Array.Resize(ref _steer, unitCapacity);
        }

        EnsureAvoidanceCapacity(unitCapacity);
    }

    public void BeginMotionTick()
    {
        _headingSum = 0;
        _headingCount = 0;
        _flips = 0;
        _steerPairs = 0;
    }

    public void SampleMotion()
    {
        if (double.IsNaN(_sinEps2))
        {
            double s = Math.Sin((double)TurnEpsRad);
            _sinEps2 = s * s;
        }

        CrowdMotionQuality.Scan(
            Session.Units.Count, _sampleCursor,
            States, Kins, _prevVx, _prevVy, _steer, _sinEps2,
            ref _headingSum, ref _headingCount, ref _flips, ref _steerPairs);
    }

    public void PublishMotion()
    {
        _sampleCursor += CrowdSimulationLimits.MotionQualitySamplesPerTick;
        double heading = _headingCount > 0 ? _headingSum / _headingCount : 0;
        Session.Telemetry.SetMotion(_headingCount, heading, _flips, _steerPairs);
    }

    public void ClearMotionMemory()
    {
        Array.Clear(_prevVx);
        Array.Clear(_prevVy);
        Array.Clear(_steer);
        _sampleCursor = 0;
        _headingSum = 0;
        _headingCount = 0;
        _flips = 0;
        _steerPairs = 0;
    }

    public void ClearMotionSlot(int slot)
    {
        if ((uint)slot >= (uint)_prevVx.Length) return;
        _prevVx[slot] = default;
        _prevVy[slot] = default;
        _steer[slot] = 0;
    }

    /// <summary>tick 首:按 chunk 列收拢到稠密下标。组级备忘失效。</summary>
    public void GatherUnits()
    {
        var session = Session;
        var units = session.Units;
        var world = session.World;
        int n = units.Count;
        EnsureCapacity(units.Capacity);
        session.EnsureFormationScratch(units.Capacity);
        var playerIndex = session.Config.PairRules.IndexByPlayerId;
        var states = States; var kins = Kins; var positions = Positions; var radii = Radii;
        var speeds = Speeds; var push = PushPriorities; var playerIdx = PlayerIdx;
        int seen = 0;
        foreach (ref var chunk in world.Query(in _unitColumns))
        {
            var stateSpan = chunk.GetSpan<CrowdSimulation.Units.CrowdSimulationUnitState>();
            var kinSpan = chunk.GetSpan<CrowdSimulation.Units.CrowdSimulationKinematics>();
            var posSpan = chunk.GetSpan<Ludots.Core.Components.WorldPositionCm>();
            var agentSpan = chunk.GetSpan<CrowdSimulationAgent>();
            var ownerSpan = chunk.GetSpan<Ludots.Core.Gameplay.Components.PlayerOwner>();
            foreach (int index in chunk)
            {
                int slot = stateSpan[index].Slot;
                int dense = units.DenseOfSlot(slot);
                if ((uint)dense >= (uint)n)
                {
                    throw new InvalidOperationException($"单位槽位 {slot} 映到稠密下标 {dense}，超出本 tick 单位数 {n}。");
                }

                states[dense] = stateSpan[index];
                kins[dense] = kinSpan[index];
                positions[dense] = posSpan[index].Value;
                var agent = agentSpan[index];
                speeds[dense] = agent.SpeedCmPerSec;
                radii[dense] = agent.PersonalRadiusCm;
                push[dense] = agent.PushPriority;
                playerIdx[dense] = playerIndex[ownerSpan[index].PlayerId];
                seen++;
            }
        }

        if (seen != n)
        {
            throw new InvalidOperationException($"chunk 收拢到 {seen} 个单位，稠密表有 {n} 个。");
        }

        int gm = session.Groups.Groups.Count;
        bool stampsReplaced = false;
        if (OrderMemo.Length < gm)
        {
            OrderMemo = new Movement.CrowdOrder?[gm];
            NavMemo = new Nav.NavContext?[gm];
            OrderStamp = new byte[gm];
            NavStamp = new byte[gm];
            // 新戳全是 0。世代号若停在 255，下面的 ++ 会回绕成 0，全部被误判为命中。
            GroupMemoEpoch = 0;
            stampsReplaced = true;
        }

        if (OrderGate.Length < gm)
        {
            OrderGate = new int[gm];
            NavGate = new int[gm];
        }

        if (!stampsReplaced && GroupMemoEpoch == byte.MaxValue)
        {
            Array.Clear(OrderStamp, 0, gm);
            Array.Clear(NavStamp, 0, gm);
            GroupMemoEpoch = 0;
        }

        GroupMemoEpoch++;
    }

    /// <summary>tick 末:SoA 按稠密下标写回 chunk 列。写回的是子步终值。</summary>
    public void ScatterUnits()
    {
        var units = Session.Units;
        var world = Session.World;
        int n = units.Count;
        int idle = 0, moving = 0, arrived = 0, unreachable = 0, traversing = 0;
        int contacts = 0;
        ushort[] contactCounts = Contacts;
        int contactN = contactCounts.Length;
        for (int i = 0; i < n; i++)
        {
            if (i < contactN) contacts += contactCounts[i];
            switch (States[i].State)
            {
                case (byte)CrowdUnitState.Idle: idle++; break;
                case (byte)CrowdUnitState.Moving: moving++; break;
                case (byte)CrowdUnitState.Arrived: arrived++; break;
                case (byte)CrowdUnitState.Unreachable: unreachable++; break;
                case (byte)CrowdUnitState.Traversing: traversing++; break;
            }
        }

        int seen = 0;
        foreach (ref var chunk in world.Query(in _unitColumns))
        {
            var stateSpan = chunk.GetSpan<CrowdSimulation.Units.CrowdSimulationUnitState>();
            var kinSpan = chunk.GetSpan<CrowdSimulation.Units.CrowdSimulationKinematics>();
            var posSpan = chunk.GetSpan<Ludots.Core.Components.WorldPositionCm>();
            foreach (int index in chunk)
            {
                int slot = stateSpan[index].Slot;
                int dense = units.DenseOfSlot(slot);
                if ((uint)dense >= (uint)n)
                {
                    throw new InvalidOperationException($"单位槽位 {slot} 映到稠密下标 {dense}，超出本 tick 单位数 {n}。");
                }

                posSpan[index].Value = Positions[dense];
                stateSpan[index] = States[dense];
                kinSpan[index] = Kins[dense];
                seen++;
            }
        }

        if (seen != n)
        {
            throw new InvalidOperationException($"chunk 写回到 {seen} 个单位，稠密表有 {n} 个。");
        }

        ContactSum = contacts;
        Session.Telemetry.SetUnitStates(idle, moving, arrived, unreachable, traversing);
    }

    /// <summary>组级备忘:组的指令对象(指令簿只在 tick 边界变)。</summary>
    internal Movement.CrowdOrder? MemoOrder(int gid, CrowdNavGroupSet.Group g)
    {
        if ((uint)gid >= (uint)OrderGate.Length)
        {
            throw new InvalidOperationException($"导航组 {gid} 超出指令备忘容量 {OrderGate.Length}。");
        }

        if (Volatile.Read(ref OrderStamp[gid]) == GroupMemoEpoch) return OrderMemo[gid];
        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref OrderGate[gid], 1, 0) != 0)
        {
            spin.SpinOnce();
            if (Volatile.Read(ref OrderStamp[gid]) == GroupMemoEpoch) return OrderMemo[gid];
        }

        try
        {
            if (OrderStamp[gid] != GroupMemoEpoch)
            {
                Movement.CrowdOrder? o = null;
                if (g.OrderId != 0)
                {
                    var list = Session.Orders.List;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].Id == g.OrderId) { o = list[i]; break; }
                    }
                }

                OrderMemo[gid] = o;
                Volatile.Write(ref OrderStamp[gid], GroupMemoEpoch);
            }

            return OrderMemo[gid];
        }
        finally
        {
            Volatile.Write(ref OrderGate[gid], 0);
        }
    }

    /// <summary>组级备忘:组的认知槽导航(槽切换只在 tick 边界变)。</summary>
    internal Nav.NavContext MemoNav(int gid, CrowdNavGroupSet.Group g)
    {
        if ((uint)gid >= (uint)NavGate.Length)
        {
            throw new InvalidOperationException($"导航组 {gid} 超出导航备忘容量 {NavGate.Length}。");
        }

        if (Volatile.Read(ref NavStamp[gid]) == GroupMemoEpoch) return NavMemo[gid]!;
        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref NavGate[gid], 1, 0) != 0)
        {
            spin.SpinOnce();
            if (Volatile.Read(ref NavStamp[gid]) == GroupMemoEpoch) return NavMemo[gid]!;
        }

        try
        {
            if (NavStamp[gid] != GroupMemoEpoch)
            {
                NavMemo[gid] = Session.ResolveNavContext(g.NavId);
                Volatile.Write(ref NavStamp[gid], GroupMemoEpoch);
            }

            return NavMemo[gid]!;
        }
        finally
        {
            Volatile.Write(ref NavGate[gid], 0);
        }
    }

    internal int RequireParallelWorkerCount()
    {
        int configured = Session.Config.Sim.ParallelWorkerCount;
        if (configured < 1 || configured > CrowdSimulationLimits.MaxParallelWorkers)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: sim.parallelWorkerCount = {configured}，需为 1~{CrowdSimulationLimits.MaxParallelWorkers} 的整数。");
        }

        return configured;
    }

    /// <summary>
    /// 按下标切成互不重叠的区间。每个区间只写自己的槽；没有按完成先后插入的共享结构，
    /// 所以线程数和调度顺序不改变逐位结果。工人数 1 或长度不足 2 时就地执行。
    /// </summary>
    internal void RunRanges(int length, int phase, Fix64 dt)
    {
        int configured = RequireParallelWorkerCount();
        if (length <= 0) return;
        if (length == 1 || configured == 1)
        {
            long serialWorkBefore = Session.Telemetry.ReadParallelWorkTicks();
            long serialWall0 = Stopwatch.GetTimestamp();
            ExecuteRange(phase, 0, length, 1, dt);
            RecordRangeParallel(phase, serialWall0, serialWorkBefore);
            return;
        }

        int workerCount = Math.Min(configured, length);
        var scheduler = Arch.Core.World.SharedJobScheduler ?? throw new InvalidOperationException(
            $"sim.parallelWorkerCount = {configured} 需要已赋值的 World.SharedJobScheduler。");
        if (!scheduler.IsMainThread)
        {
            throw new InvalidOperationException("群体运动并行步进必须在创建 World.SharedJobScheduler 的线程上调用。");
        }

        if (_rangeJobs.Length < workerCount)
        {
            var jobs = new IndexRangeJob[workerCount];
            for (int i = 0; i < _rangeJobs.Length; i++) jobs[i] = _rangeJobs[i];
            for (int i = _rangeJobs.Length; i < workerCount; i++) jobs[i] = new IndexRangeJob();
            _rangeJobs = jobs;
            _rangeHandles = new JobHandle[workerCount];
        }

        // 分离隔行求解，意图里走流场的单位比已到达的贵。连续切块会把一整段密集下标交给同一个工人。
        // 按工人号交错取值，密集段被摊开；每个下标仍只进一个区间。
        bool spread = phase is RangePhaseAvoidSolve or RangePhaseIntent;
        int baseCount = length / workerCount;
        int remainder = length % workerCount;
        int start = 0;
        long workBefore = Session.Telemetry.ReadParallelWorkTicks();
        long wall0 = Stopwatch.GetTimestamp();
        for (int w = 0; w < workerCount; w++)
        {
            var job = _rangeJobs[w];
            job.Kernel = this;
            job.Phase = phase;
            job.Dt = dt;
            if (spread)
            {
                job.Start = w;
                job.End = length;
                job.Step = workerCount;
            }
            else
            {
                int len = baseCount + (w < remainder ? 1 : 0);
                job.Start = start;
                job.End = start + len;
                job.Step = 1;
                start += len;
            }

            _rangeHandles[w] = scheduler.Schedule(job);
        }

        scheduler.Flush();
        for (int w = 0; w < workerCount; w++) _rangeHandles[w].Complete();
        RecordRangeParallel(phase, wall0, workBefore);
    }

    private void RecordRangeParallel(int phase, long wall0, long workBefore)
    {
        var telemetry = Session.Telemetry;
        long wall = Stopwatch.GetTimestamp() - wall0;
        long work = telemetry.ReadParallelWorkTicks() - workBefore;
        telemetry.AddParallelWallTicks(wall);
        switch (phase)
        {
            case RangePhaseAvoidGather: telemetry.AddGatherParallel(wall, work); break;
            case RangePhaseAvoidSolve: telemetry.AddSolveParallel(wall, work); break;
            case RangePhaseIntent: telemetry.AddIntentParallel(wall, work); break;
            case RangePhaseMotor: telemetry.AddMotorParallel(wall, work); break;
        }
    }

    private void ExecuteRange(int phase, int start, int end, int step, Fix64 dt)
    {
        long t0 = Stopwatch.GetTimestamp();
        switch (phase)
        {
            case RangePhaseAvoidGather:
                CrowdAvoidance.GatherRange(this, start, end);
                break;
            case RangePhaseAvoidSolve:
                CrowdAvoidance.SolveRange(this, start, end, step);
                break;
            case RangePhaseIntent:
                CrowdIntents.WriteRange(this, start, end, step);
                break;
            case RangePhaseMotor:
                CrowdMotor.IntegrateRange(this, start, end, dt);
                break;
            default:
                throw new InvalidOperationException($"未知的群体并行阶段 {phase}。");
        }

        Session.Telemetry.AddParallelWorkTicks(Stopwatch.GetTimestamp() - t0);
    }

    internal const int RangePhaseAvoidGather = 1;
    internal const int RangePhaseAvoidSolve = 2;
    internal const int RangePhaseIntent = 3;
    internal const int RangePhaseMotor = 4;

    private sealed class IndexRangeJob : IJob
    {
        public CrowdMovementKernel Kernel = null!;
        public int Phase;
        public int Start;
        public int End;
        public int Step;
        public Fix64 Dt;

        public void Execute() => Kernel.ExecuteRange(Phase, Start, End, Step, Dt);
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

    /// <summary>稠密表把末行换到空位时,分离与静动标记跟着换。缓冲还没建(长度 0)时没有行可搬。</summary>
    internal void MoveAvoidanceRow(int fromDense, int toDense)
    {
        if (Separation.Length == 0 && Calm.Length == 0) return;
        if ((uint)fromDense >= (uint)Separation.Length || (uint)toDense >= (uint)Separation.Length
            || (uint)fromDense >= (uint)Calm.Length || (uint)toDense >= (uint)Calm.Length)
        {
            throw new InvalidOperationException(
                $"避让行 {fromDense}→{toDense} 超出分离缓冲 {Separation.Length}/{Calm.Length}。");
        }

        Separation[toDense] = Separation[fromDense];
        Separation[fromDense] = Fix64Vec2.Zero;
        Calm[toDense] = Calm[fromDense];
        Calm[fromDense] = 0;
    }

    /// <summary>新单位的槽位不带陈旧推挤,且首次求解前必醒。</summary>
    public void ClearPush(int dense)
    {
        if (dense < Separation.Length) Separation[dense] = Fix64Vec2.Zero;
        if (dense < Calm.Length) Calm[dense] = 0;
    }

    /// <summary>分离求解,然后相位前移。会话在 Step 之前调用。</summary>
    public void SolveSeparation()
    {
        CrowdAvoidance.Solve(this);
        if (Stride > 1) Phase = (Phase + 1) % Stride;
    }

    /// <summary>每子步重建空间哈希(位置/半径取 SoA——gather 已收拢,子步间由马达保持最新;
    /// 无移动快路径在哈希内部)。</summary>
    public void RebuildHash()
    {
        Hash.Build(Positions, Radii, Session.Units.Count);
    }

    /// <summary>Step 只做领队 → 意图 → 马达。分离求解由会话在 Step 之前调用。</summary>
    public void Step(Fix64 dt)
    {
        var telemetry = Session.Telemetry;
        long mark = Stopwatch.GetTimestamp();
        var orders = Session.Orders.List;
        Fix64 turnK = Fix64.Min(Fix64.OneValue, dt * LeaderTurnRate);
        Fix64 faceStep = LeaderTurnRate * FaceTurnScale * dt;
        for (int oi = 0; oi < orders.Count; oi++)
        {
            var leaders = orders[oi].Leaders;
            for (int li = 0; li < leaders.Count; li++)
            {
                leaders[li].Step(
                    leaders[li].PaceCmPerSecond,
                    LeaderSpeedRatio, turnK, faceStep, dt,
                    Session.Config.NavCellSizeCm, Session.Config.NavCellsPerSide);
            }
        }

        telemetry.AddLeaderTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
        CrowdIntents.Compute(this, dt);
        telemetry.AddIntentTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
        CrowdMotor.Integrate(this, dt);
        telemetry.AddMotorTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
        Tick++;
    }

    /// <summary>避让降频步长:max(1, round(stepHz / rateHz))。rateHz 用 double,超过步进频率报错,不再截断或钳位。</summary>
    internal static int AvoidanceStride(int stepHz, double rateHz)
    {
        if (stepHz < 1)
        {
            throw new InvalidOperationException($"避让降频需要步进频率 ≥ 1 Hz（当前 {stepHz}）。");
        }

        if (!(rateHz > 0))
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: avoidance.rateHz = {rateHz.ToString(CultureInfo.InvariantCulture)}，需为 > 0。");
        }

        if (rateHz > stepHz)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: avoidance.rateHz = {rateHz.ToString(CultureInfo.InvariantCulture)} 超过步进频率 {stepHz} Hz。");
        }

        return Math.Max(1, (int)Math.Floor(stepHz / rateHz + 0.5));
    }

    /// <summary>从运行时配置装配内核。降频 stride = max(1, round(stepHz / rateHz)),rateHz 超过步进频率即报错。</summary>
    public static CrowdMovementKernel Create(CrowdSimSession session, CrowdWalls.OpenCellCache? openCache = null)
    {
        var cfg = session.Config;
        var fc = cfg.Formation;
        var mv = cfg.Movement;
        var av = cfg.Avoidance;
        // 哈希格距 = 2 × 最大个人半径 / rings。
        // 两值定点精确传入,不许整数截断
        Fix64 maxRadiusCm = Fix64.Zero;
        foreach (var p in cfg.Profiles) maxRadiusCm = Fix64.Max(maxRadiusCm, p.PersonalRadiusCm);
        Fix64 hashCellCm = Fix64.Max(Fix64.OneValue, maxRadiusCm * 2 / Fix64.FromInt(Math.Max(1, av.HashRings)));
        var hash = new CrowdSpatialHash(cfg.NavCellsPerSide * cfg.NavCellSizeCm, hashCellCm, cfg.Sim.MaxUnits, av.HashRings, maxRadiusCm);
        int stepHz = cfg.FixedHz * session.SubSteps;
        double avoidHz = av.RateHz.ToDouble();
        int stride = AvoidanceStride(stepHz, avoidHz);
        double actualHz = stepHz / (double)stride;
        Log.Info(in LogChannels.Engine,
            $"CrowdSimulation avoidance: configured {avoidHz.ToString(CultureInfo.InvariantCulture)} Hz, stride {stride} substeps, actual {actualHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz.");
        int hashCells = hash.CellsPerSide * hash.CellsPerSide;
        var kernel = new CrowdMovementKernel
        {
            Session = session,
            Hash = hash,
            Awake = new byte[hashCells],
            Stride = stride,
            OpenCache = openCache ?? new CrowdWalls.OpenCellCache(),
            BlendRate = mv.BlendRatePerSec,
            BlendCommitRatio = mv.BlendCommitRatio,
            SlotCheckInterval = mv.SlotCheckIntervalSubSteps,
            WakeDistanceNavCells = mv.WakeDistanceNavCells,
            SlotArriveNavCells = fc.SlotArriveNavCells,
            SettleRadiusNavCells = fc.SettleRadiusNavCells,
            StallSpeedRatio = mv.StallSpeedRatio,
            StallDecay = mv.StallDecayPerSec,
            SettleTimeSeconds = fc.SettleTimeSec,
            SlotSightNavCells = fc.SlotSightNavCells,
            SightHysteresisScale = mv.SightHysteresisScale,
            LaneSpreadSpeedRatio = mv.LaneSpreadSpeedRatio,
            GoalArriveNavCells = fc.GoalArriveNavCells,
            SlotGain = Fix64.OneValue / mv.SlotTimeConstantSec,
            CatchUpSpeedRatio = fc.CatchUpSpeedRatio,
            Accel = av.AccelerationPerSec,
            RestAccelScale = mv.RestAccelScale,
            RestSeparationScale = mv.RestSeparationScale,
            SeparationWeight = av.SeparationWeight,
            RestDeadband = av.RestDeadband,
            MaxScan = av.MaxScan,
            MaxNeighbors = av.MaxNeighbors,
            MaxPush = av.MaxPush,
            SmoothingRatio = av.SmoothingRatio,
            MovingPriorityBonus = cfg.Push.MovingPriorityBonus,
            DominantShare = cfg.Push.DominantShare,
            UnitTurnRate = mv.UnitTurnRateRadPerSec,
            StopSpeedRatio = mv.StopSpeedRatio,
            SpeedCapRatio = mv.SpeedCapRatio,
            // 没有链接种类时没有单位会进入 Traversing 状态，这个倍率不会被读。
            LinkSpeedRatio = cfg.LinkKinds.Count > 0 ? cfg.LinkKinds[0].TraversalSpeedRatio : Fix64.Zero,
            LeaderSpeedRatio = fc.LeaderSpeedRatio,
            LeaderTurnRate = fc.LeaderTurnRatePerSec,
            FaceTurnScale = fc.FaceTurnScale,
            TurnEpsRad = cfg.Telemetry.TurnEpsRad,
        };
        // 结构重烘的唤醒可能早于首次收拢，按稠密下标读这些行。
        kernel.EnsureCapacity(session.Units.Capacity);
        return kernel;
    }
}

internal struct CrowdIntentScalars
{
    public Fix64 Dt;
    public Fix64 Cs;
    public int NavCellSizeCm;
    public Fix64 WorldSizeCm;
    public Fix64 BStep;
    public Fix64 Wake2;
    public Fix64 ArriveSlot;
    public Fix64 Settle;
    public Fix64 StallV2;
    public int NavN;
    public int N2;
    public int Every;
}

internal struct CrowdMotorFrame
{
    public int NavN;
    public int NavCellSizeCm;
    public int N2;
    public Fix64 Dt;
    public Fix64 Cs;
    public Fix64 Lim;
    public Fix64 Response;
    public Fix64 ResponseRest;
    public Fix64 CosT;
    public Fix64 SinT;
    public Fix64 Dead;
    public Fix64 Stop2;
    public Fix64 CapR;
    public bool HasObs;
}
