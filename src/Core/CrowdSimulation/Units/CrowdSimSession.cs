using System;
using System.Collections.Generic;
using System.Diagnostics;
using ArchWorld = Arch.Core.World;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// CrowdSimulation 仿真会话(S4 部署 + S5 移动):单位(ECS)、导航组、指令队列、
/// 指令簿(领队/阵型)、规划器与固定帧率推进。
/// 引擎只有一个时钟(Engine/clock.json 的 FixedHz),会话的 tick 就是 FixedHz 的一步:
/// 每步先冲刷到点指令，再落到点的路径答复，再推进运动内核。
/// 未冻结时，在散回单位之后、TickCount++ 之前做 Fog.Update 和 CrowdBeliefSync.Sync。TruthNavFrozen 跳过这两步。
/// 答复未按生效帧返回时本 tick 停摆(固定生效帧语义,结果不降级)。
/// </summary>
public sealed class CrowdSimSession
{
    public CrowdSimSession(
        CrowdSimulationRuntimeConfig config,
        ArchWorld world,
        IReadOnlyDictionary<int, NavContext> navs,
        IReadOnlyDictionary<(int Layer, int R), NavContext> navByLayerRadius,
        CrowdSimPresentationWiring presentation)
    {
        Config = config;
        World = world;
        Navs = navs;
        NavByLayerRadius = navByLayerRadius;
        Presentation = presentation;
        Units = new CrowdSimUnits(world, config.Sim.MaxUnits, presentation);
        Groups = new CrowdNavGroupSet();
        Commands = new CrowdCommandQueue();
        Orders = new CrowdOrderBook();
        MemberStamp = new int[Units.Capacity];
        MemberCapture = new uint[Units.Capacity];

        // 子步数取本图实际绑定模板的最大速度。单步位移不超过 maxStepCells 格(封顶 maxSubSteps)。
        Fix64 maxSpeed = Fix64.Zero;
        for (int i = 0; i < config.Profiles.Count; i++)
        {
            maxSpeed = Fix64.Max(maxSpeed, config.Profiles[i].SpeedCmPerSecond);
        }
        Fix64 simDt = config.Sim.SimSecondsPerTickSecond / Fix64.FromInt(config.FixedHz);
        SimDt = simDt;
        int needed = (int)Fix64.Ceiling(maxSpeed * config.Movement.SpeedCapRatio * simDt /
            (Fix64.FromInt(config.NavCellSizeCm) * config.Movement.MaxStepNavCells)).ToLong();
        int unclamped = Math.Max(1, needed);
        int cap = config.Movement.MaxSubSteps;
        SubSteps = Math.Clamp(unclamped, 1, cap);
        if (unclamped > cap) Telemetry.MarkSubStepClamped();
        SubStepDt = simDt / SubSteps;
    }

    /// <summary>类型化遥测。事件点就地加计数;呈现只读。</summary>
    public CrowdSimulationTelemetry Telemetry { get; } = new();

    public CrowdSimulationRuntimeConfig Config { get; }
    public ArchWorld World { get; }
    public CrowdSimUnits Units { get; }
    public CrowdNavGroupSet Groups { get; }
    public CrowdCommandQueue Commands { get; }
    /// <summary>指令簿(S5):移动指令聚合与虚拟领队。</summary>
    public CrowdOrderBook Orders { get; }
    /// <summary>呈现/交互接线。回放派生接线不持有集合写者,不改活集合。</summary>
    public CrowdSimPresentationWiring Presentation { get; }
    /// <summary>运动内核与规划器(S5;null = 纯部署会话,S4 形状)。</summary>
    public CrowdMovementKernel? Movement { get; private set; }
    public CrowdSimPlanner? Planner { get; private set; }
    /// <summary>到点答复未到时的口径:false = 引擎停摆等下帧(默认);true = 调用线程同步等(无头对拍/回放用)。</summary>
    public bool BlockOnDueReplies { get; set; }
    /// <summary>阻挡盒碰撞索引:有结构仓时跟随仓的活 CSR(每次结构变更整体重建,新实例),
    /// 否则用宿主按地图阻挡物建的静态索引(null = 无阻挡碰撞)。</summary>
    public CrowdBlockerColliders? Blockers => Structures?.Colliders ?? _staticBlockers;

    /// <summary>静态阻挡盒索引(无结构仓的会话用;S5 冻结口径)。</summary>
    private CrowdBlockerColliders? _staticBlockers;

    /// <summary>无结构仓会话的静态阻挡盒装配入口(有仓时无效——仓是唯一真相)。</summary>
    public void SetStaticBlockers(CrowdBlockerColliders? blockers) => _staticBlockers = blockers;
    /// <summary>结构实体仓(S7;null = 无结构动态的会话,结构指令不可用)。</summary>
    public CrowdStructuresStore? Structures { get; set; }
    /// <summary>增量重烘的烘焙输入(与初始烘焙同源);null = 结构指令不可用。</summary>
    public Nav.CrowdRebakeSources? RebakeSources { get; set; }
    /// <summary>导航 tile 内容键缓存(初始烘焙所用的同一份;增量重烘靠它命中复用)。</summary>
    public Nav.NavTileCache? NavTileCache { get; set; }
    /// <summary>跨 tick 推进的重烘收尾 job(null = 无在途变更)。</summary>
    public CrowdRebakeJob? RebakeJob { get; set; }
    internal CrowdRebakeJob? RebakeScratch { get; set; }
    internal CrowdRebakeReport? ReportScratch { get; set; }
    internal readonly HashSet<int> RebakeUnionSeen = new(32);
    /// <summary>最近一次已完结的结构变更报告(遥测 / 对拍)。</summary>
    public CrowdRebakeReport? LastRebakeReport { get; set; }
    /// <summary>最近一次挤离中无处安放的单位数(计数,进重烘报告)。</summary>
    public int EvictStuck { get; set; }
    /// <summary>调试开关:每次结构变更后跑增量 vs 全量一致性检查(对拍不跑,验收必跑)。</summary>
    public bool VerifyIncrementalNav { get; set; }
    /// <summary>每 tick 的仿真秒数(simSecondsPerTickSecond/FixedHz)与子步进。</summary>
    public Fix64 SimDt { get; }
    public int SubSteps { get; }
    public Fix64 SubStepDt { get; }

    private FogLayerRegistry? _visionLayers;
    private FogFieldStore? _visionFields;

    public void BindVision(FogLayerRegistry layers, FogFieldStore fields)
    {
        _visionLayers = layers ?? throw new ArgumentNullException(nameof(layers));
        _visionFields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public void EnableMovement(CrowdMovementKernel kernel, CrowdSimPlanner planner)
    {
        Movement = kernel;
        Planner = planner;
        // EnableMovement 只有已有结构仓才建 Fog、Beliefs、Belief。没有结构仓的运动会话也不建。纯部署不调用这里。盖章前要先绑定雾层和雾场。有迷雾就注入高度表。
        if (Structures != null)
        {
            if (_visionLayers == null || _visionFields == null)
            {
                throw new InvalidOperationException("迷雾盖章需要先绑定雾层注册表和雾场。");
            }

            Fog = new CrowdFog(Config, Config.PairRules, Structures, _visionLayers, _visionFields);
            Fog.EnsureHeights(RebakeSources!.HeightS);
            Beliefs = new CrowdBeliefNavs(this);
            Belief = CrowdBeliefState.Create(Fog.VisionGroupCount);
        }
    }

    /// <summary>迷雾状态。null = 没有结构仓，或没有调用 EnableMovement。马达永远走真相，规划经认知槽。</summary>
    public CrowdFog? Fog { get; private set; }
    public CrowdBeliefNavs? Beliefs { get; private set; }
    public CrowdBeliefState? Belief { get; private set; }
    /// <summary>truth 冻结:跳过迷雾更新与认知同步,组停在真相槽。
    /// 旧真值(S5/S7)在迷雾内核落地后仍要逐位相同;新场景不置。</summary>
    public bool TruthNavFrozen { get; set; }

    private readonly struct BeliefNavLookup
    {
        public readonly CrowdBeliefNavs Beliefs;
        public readonly int NavId;
        public BeliefNavLookup(CrowdBeliefNavs beliefs, int navId)
        {
            Beliefs = beliefs;
            NavId = navId;
        }
    }

    private static readonly Func<BeliefNavLookup, NavContext> ReadBeliefNav = static lookup => lookup.Beliefs.Get(lookup.NavId);

    /// <summary>nav 号解析(&lt; Stride = 真相字典;否则认知注册表取/懒建变体)。
    /// 变体构建经路径服务的导航互斥锁串行(Monitor 同线程可重入,worker 侧安全)。
    /// 慢路径用静态委托，不在 tick 里捕获 navId。</summary>
    public NavContext ResolveNavContext(int navId)
    {
        if (navId < CrowdBeliefNavs.BeliefStride) return Navs[navId];
        var beliefs = Beliefs ?? throw new InvalidOperationException($"变体 nav {navId}:会话没有认知注册表(迷雾未启用)。");
        return Planner!.Service.RunExclusive(new BeliefNavLookup(beliefs, navId), ReadBeliefNav);
    }

    /// <summary>
    /// 呈现只读解析。真相号直接取 <see cref="Navs"/>;认知号只取已物化变体。
    /// 未物化返回 false,不 Build。
    /// </summary>
    public bool TryGetMaterializedNav(int navId, out NavContext nav)
    {
        if (navId < CrowdBeliefNavs.BeliefStride)
        {
            if (!Navs.TryGetValue(navId, out var truth))
                throw new InvalidOperationException($"真相 nav {navId} 不在会话字典。");
            nav = truth;
            return true;
        }

        var beliefs = Beliefs ?? throw new InvalidOperationException($"变体 nav {navId}:会话没有认知注册表(迷雾未启用)。");
        if (beliefs.TryGetMaterialized(navId, out var variant))
        {
            nav = variant;
            return true;
        }

        nav = null!;
        return false;
    }

    /// <summary>组的规划句柄：玩家视野组当前槽的变体号。槽 0 是真相号。</summary>
    public int NavIdFor(int playerIndex, int layerIdx, int rIdx)
    {
        int truth = NavByLayerRadius[(layerIdx, rIdx)].Id;
        if (Fog == null || Belief == null || TruthNavFrozen) return truth;
        int slot = Belief.Slot[Fog.GroupOf[playerIndex]];
        return slot == 0 ? truth : slot * CrowdBeliefNavs.BeliefStride + truth;
    }

    /// <summary>组的规划句柄挂到其玩家视野组当前槽(生成时与槽切换时;马达句柄 BodyNavId 恒真相)。</summary>
    public void AttachGroupNav(CrowdNavGroupSet.Group g) => g.NavId = NavIdFor(g.Player, g.LayerIdx, g.RIdx);
    public IReadOnlyDictionary<int, NavContext> Navs { get; private set; }
    /// <summary>(移动类型, 半径级) → 导航上下文(deploy / spawnAt 的取上下文入口)。</summary>
    public IReadOnlyDictionary<(int Layer, int R), NavContext> NavByLayerRadius { get; }

    /// <summary>回放会话换装全新烘焙的导航上下文。结构 op 会改写共享上下文，带着已生效的变更从头跑会分歧。</summary>
    public void ReplaceNavs(IReadOnlyDictionary<int, NavContext> navs) => Navs = navs;

    // 下令重组、阵型排槽的复用缓冲。挂在会话上:回放会话各自一份,不跨线程共享。
    // MemberStamp 用世代标记本条指令的成员槽。
    internal readonly int[] MemberStamp;
    internal readonly uint[] MemberCapture;
    internal int MemberEpoch;
    internal readonly Dictionary<int, CrowdNavGroupSet.Group> IssueGroups = new(16);
    internal readonly List<CrowdNavGroupSet.Group> IssueGroupOrder = new(16);
    internal readonly Dictionary<int, (Fix64 XCm, Fix64 YCm, int Count)> IssueLayerSums = new(8);
    internal readonly CrowdOrder[] IssueBatch = new CrowdOrder[1];
    internal Fix64[] FormationDiameter = Array.Empty<Fix64>();
    internal Fix64[] FormationForward = Array.Empty<Fix64>();
    internal Fix64[] FormationLateral = Array.Empty<Fix64>();
    internal CrowdFormations.FormationRank[] FormationRanks = Array.Empty<CrowdFormations.FormationRank>();
    internal int[] FormationOrder = Array.Empty<int>();
    internal CrowdFormations.RowRank[] FormationRows = Array.Empty<CrowdFormations.RowRank>();

    internal void EnsureFormationScratch(int count)
    {
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationDiameter, count);
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationForward, count);
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationLateral, count);
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationRanks, count);
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationOrder, count);
        Ludots.Core.CrowdSimulation.StableOrder.Ensure(ref FormationRows, count);
    }

    public int TickCount { get; private set; }

    /// <summary>本 tick 的分区摘要。只在 <see cref="Step"/> 返回 true 时更新。</summary>
    public CrowdSimStateDigest LastDigest { get; private set; }

    /// <summary>
    /// 诊断分区（groups / jump / commands）。默认关，这三个词为 0，不进 <see cref="CrowdSimStateDigest.Total"/>。
    /// 打开后每 tick 用各自的 Mix 计算。自动回放武装时运行时从第一 tick 打开，回放会话复制同一开关。
    /// </summary>
    public bool DiagnosticDigest { get; set; }
    public int SpawnSeq { get; set; }
    public int SpawnSkippedTotal { get; private set; }

    /// <summary>会话事件(spawnSkip 等,演示层显示用;不进校验)。</summary>
    public event Action<string>? Notified;

    /// <summary>结构变更重烘报告发布(遥测日志:rebake 报告与缓存命中计数,真机冒烟的重量证据)。</summary>
    public event Action<CrowdRebakeReport>? RebakeReported;

    public void NotifyRebake(CrowdRebakeReport report)
    {
        Telemetry.MarkRebake();
        LastRebakeReport = report;
        // 会话上的报告对象下一次重烘会原地改写。有监听者时交出当时的值，没有监听者不分配。
        if (RebakeReported != null) RebakeReported.Invoke(CopyRebakeReport(report));
        Ludots.Core.Diagnostics.Log.Dbg(in Ludots.Core.Diagnostics.LogChannels.Engine,
            $"CrowdSimulation rebake: kind={(report.Kind == CrowdRebakeReport.KindPlace ? "place" : "remove")} " +
            $"tiles={report.Tiles} contexts={report.Contexts} costOnly={report.CostOnly} " +
            $"cacheHits={report.Hits} misses={report.Misses} orders={report.Orders} refreshes={report.Refreshes} " +
            $"evicted={report.Evicted} stuck={report.Stuck} (tick {report.ExecTick}→{report.ReportTick})");
    }

    private static CrowdRebakeReport CopyRebakeReport(CrowdRebakeReport report)
    {
        var rows = new List<CrowdNavRebakeContextResult>(report.ContextResults.Count);
        for (int i = 0; i < report.ContextResults.Count; i++)
        {
            var row = report.ContextResults[i];
            rows.Add(new CrowdNavRebakeContextResult
            {
                NavId = row.NavId,
                Kind = row.Kind,
                DirtyTiles = new List<int>(row.DirtyTiles),
            });
        }

        return new CrowdRebakeReport
        {
            ExecTick = report.ExecTick,
            ReportTick = report.ReportTick,
            Kind = report.Kind,
            Tiles = report.Tiles,
            Contexts = report.Contexts,
            CostOnly = report.CostOnly,
            Hits = report.Hits,
            Misses = report.Misses,
            Orders = report.Orders,
            Refreshes = report.Refreshes,
            Evicted = report.Evicted,
            Stuck = report.Stuck,
            Bbox = report.Bbox,
            ContextResults = rows,
            UnionTiles = new List<int>(report.UnionTiles),
        };
    }

    public NavContext NavFor(int layer, int r) => NavByLayerRadius[(layer, r)];

    /// <summary>
    /// 推进一个 tick:冲到点指令(指令内含结构 op 与其重烘阶段 0)→ 重烘切片推进 →
    /// 寿命到期拆除 → 落到点路径答复(未回则停摆,tick 不动)→ 运动内核子步进
    /// (领队→意图→马达)× SubSteps → 散回单位 → 未冻结时 Fog.Update 然后 CrowdBeliefSync.Sync
    /// → tickCount+1 → 流场刷新排队 → 校验码。TruthNavFrozen 跳过迷雾和认知同步。
    /// 返回 false = 本 tick 停摆(调用方下一帧重试同一 tick;回放对停摆逐帧同构),
    /// checksum 与 <see cref="LastDigest"/> 仅在 true 时有效。
    /// </summary>
    public bool Step(out ulong checksum)
    {
        var telemetry = Telemetry;
        telemetry.BeginPhaseFrame();
        long mark = Stopwatch.GetTimestamp();
        try
        {
            Commands.Flush(this, ExecCommand);
            Units.MaterializePending();
            telemetry.AddCommandTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
            if (RebakeJob != null) CrowdStructureOps.StepRebake(this, all: false);
            if (Structures != null) CrowdStructureOps.ExpireStructures(this);
            telemetry.AddRebakeTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
            if (Planner != null && !Planner.ApplyDue(TickCount, BlockOnDueReplies))
            {
                telemetry.AddPlanTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                // Flush 可能改了成员,scatter 还没跑;成员数没变就留着上一份直方。
                if (Units.Count != _stateHistogramMembers) RecountUnitStates();
                Telemetry.MarkPathWait();
                checksum = 0;
                return false;
            }

            telemetry.AddPlanTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));

            if (Movement != null)
            {
                if (Movement.Contacts.Length > 0) Array.Clear(Movement.Contacts);
                Movement.GatherUnits();
                telemetry.AddGatherTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                for (int s = 0; s < SubSteps; s++)
                {
                    // 子步序：哈希重建，分离求解（含相位前移），领队，意图，马达。
                    Movement.RebuildHash();
                    telemetry.AddHashTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                    Movement.SolveSeparation();
                    telemetry.AddSeparationTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                    Movement.Step(SubStepDt);
                    mark = Stopwatch.GetTimestamp();
                }

                Movement.ScatterUnits();
                telemetry.AddScatterTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                _stateHistogramMembers = Units.Count;
            }

            // 运动之后、TickCount++ 之前做迷雾更新和认知同步。truth 冻结时跳过两者。
            if (Fog != null && !TruthNavFrozen)
            {
                Fog.Update(TickCount, this);
                telemetry.AddFogTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
                CrowdBeliefSync.Sync(this);
                telemetry.AddBeliefTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
            }

            TickCount++;
            Planner?.ProcessRefreshes(TickCount);
            telemetry.AddRefreshTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
            LastDigest = CrowdSimChecksum.Compute(this);
            checksum = LastDigest.Total;
            telemetry.AddChecksumTicks(CrowdSimulationTelemetry.TakeTicks(ref mark));
            return true;
        }
        finally
        {
            telemetry.CommitPhaseFrame();
        }
    }

    /// <summary>快进到目标 tick(停摆不推进;服务故障时抛错而不是死循环)。</summary>
    public void Advance(int ticks, List<ulong>? checksums = null, List<CrowdSimStateDigest>? digests = null)
    {
        int guard = ticks * CrowdSimulationLimits.AdvanceStallGuardFactor + CrowdSimulationLimits.AdvanceStallGuardFactor;
        int ran = 0;
        while (ran < ticks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
            if (!Step(out var h)) continue;
            ran++;
            checksums?.Add(h);
            digests?.Add(LastDigest);
        }
    }

    /// <summary>重置到初始态(回放第一帧之前:单位清空、组清空、指令队列与指令簿清空、计数归零;
    /// 在途重烘 job 作废——回放是全新仿真,结构仓不重置,静态地图状态即初始态)。</summary>
    public void Reset()
    {
        Units.Clear();
        Groups.Reset();
        Commands.Reset();
        Orders.Clear();
        RebakeJob = null;
        TickCount = 0;
        SpawnSeq = 0;
        SpawnSkippedTotal = 0;
        _stateHistogramMembers = 0;
        Telemetry.SetUnitStates(0, 0, 0, 0, 0);
    }

    /// <summary>
    /// 把成员句柄收成槽位世代标记。空集、失效句柄、重复句柄、玩家不符都在这里拒收,不改写成员集。
    /// </summary>
    internal void BindMemberSet(ReadOnlySpan<uint> members, int player)
    {
        if (members.IsEmpty)
        {
            throw new InvalidOperationException("order: 成员集为空。");
        }

        if (MemberEpoch == int.MaxValue)
        {
            Array.Clear(MemberStamp);
            MemberEpoch = 0;
        }

        int epoch = ++MemberEpoch;
        for (int i = 0; i < members.Length; i++)
        {
            if (!Units.TryResolveHandle(members[i], out int dense))
            {
                throw new InvalidOperationException("order: 成员句柄无效。");
            }

            var entity = Units.EntityAt(dense);
            if (World.Get<Gameplay.Components.PlayerOwner>(entity).PlayerId != player)
            {
                throw new InvalidOperationException("order: 成员不属于该玩家。");
            }

            int slot = Units.SlotAt(dense);
            if (MemberStamp[slot] == epoch)
            {
                throw new InvalidOperationException("order: 成员句柄重复。");
            }

            MemberStamp[slot] = epoch;
        }
    }

    /// <summary>玩家代表 selected 集合的成员数。没有接线或还没写过集合时为 0,不扫描单位。</summary>
    public int SelectedCollectionCount(int playerId)
    {
        var reps = Presentation.PlayerReps;
        if (reps == null || (uint)playerId >= (uint)reps.Length) return 0;
        var rep = reps[playerId];
        if (rep == Arch.Core.Entity.Null || !World.IsAlive(rep)) return 0;
        if (!Presentation.Collections.TryGet(rep, Presentation.SelectedCollectionKeyId, out var handle)) return 0;
        return Presentation.Collections.TryGetView(handle, out var view) ? view.Count : 0;
    }

    public void NotifySpawnSkip(int skipped, int requested)
    {
        SpawnSkippedTotal += skipped;
        Notified?.Invoke($"spawnSkip: 请求 {requested} 个,该玩家没有可用的类型×半径组合,跳过 {skipped} 个");
    }
    /// <summary>把本 tick 的接触读数交给遥测。停摆帧传 0,不把上一成功帧再写一遍。</summary>
    internal void PublishTelemetryFrame(bool stalled, double tickMs)
    {
        int contacts = stalled || Movement == null ? 0 : Movement.ContactSum;
        Telemetry.BeginFrame(Units.Count, contacts, stalled, tickMs);
    }

    private static readonly Func<CrowdSimSession, CrowdSimCommand, object?> ExecCommand = CrowdSimCommands.Exec;

    private int _stateHistogramMembers;

    private void RecountUnitStates()
    {
        int idle = 0, moving = 0, arrived = 0, unreachable = 0, jumping = 0;
        int n = Units.Count;
        for (int i = 0; i < n; i++)
        {
            switch (World.Get<CrowdSimulationUnitState>(Units.EntityAt(i)).State)
            {
                case (byte)CrowdUnitState.Idle: idle++; break;
                case (byte)CrowdUnitState.Moving: moving++; break;
                case (byte)CrowdUnitState.Arrived: arrived++; break;
                case (byte)CrowdUnitState.Unreachable: unreachable++; break;
                case (byte)CrowdUnitState.Jump: jumping++; break;
            }
        }

        Telemetry.SetUnitStates(idle, moving, arrived, unreachable, jumping);
        _stateHistogramMembers = n;
    }
}
