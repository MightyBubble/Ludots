using System;
using System.Collections.Generic;
using ArchWorld = Arch.Core.World;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// CrowdSimulation 仿真会话(S4 部署 + S5 移动):单位(ECS)、导航组、指令队列、
/// 指令簿(领队/阵型)、规划器与固定帧率推进。
/// 引擎只有一个时钟(Engine/clock.json 的 FixedHz),会话的 tick 就是 FixedHz 的一步:
/// 每步先冲刷到点指令(指令数据决定一切状态变化),再落到点的路径答复,再推进运动内核。
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

        // 子步数:单步位移不超过 maxStepCells 格(封顶 maxSubSteps)——与参考实现同推导
        Fix64 maxSpeed = Fix64.Zero;
        foreach (var a in config.AgentTypes) maxSpeed = Fix64.Max(maxSpeed, a.SpeedCmPerSecond);
        Fix64 simDt = config.Sim.TimeScale / Fix64.FromInt(config.FixedHz);
        SimDt = simDt;
        int needed = (int)Fix64.Ceiling(maxSpeed * config.Movement.SpeedCapRatio * simDt /
            (Fix64.FromInt(config.NavCellSizeCm) * config.Movement.MaxStepCells)).ToLong();
        SubSteps = Math.Clamp(Math.Max(1, needed), 1, config.Movement.MaxSubSteps);
        SubStepDt = simDt / SubSteps;
    }

    public CrowdSimulationRuntimeConfig Config { get; }
    public ArchWorld World { get; }
    public CrowdSimUnits Units { get; }
    public CrowdNavGroupSet Groups { get; }
    public CrowdCommandQueue Commands { get; }
    /// <summary>指令簿(S5):移动指令聚合与虚拟领队。</summary>
    public CrowdOrderBook Orders { get; }
    /// <summary>呈现/交互接线(空选中属主的派生接线 = 回放:不投影、不镜像选中集合)。</summary>
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
    /// <summary>跨 tick 推进的重烘收尾 job(RT-04 切片;null = 无在途变更)。</summary>
    public CrowdRebakeJob? RebakeJob { get; set; }
    /// <summary>最近一次已完结的结构变更报告(遥测 / 对拍)。</summary>
    public CrowdRebakeReport? LastRebakeReport { get; set; }
    /// <summary>最近一次挤离中无处安放的单位数(计数,进重烘报告)。</summary>
    public int EvictStuck { get; set; }
    /// <summary>调试开关:每次结构变更后跑增量 vs 全量一致性检查(DB-03;对拍免谈,验收必跑)。</summary>
    public bool VerifyIncrementalNav { get; set; }
    /// <summary>每 tick 的仿真秒数(timeScale/FixedHz)与子步进。</summary>
    public Fix64 SimDt { get; }
    public int SubSteps { get; }
    public Fix64 SubStepDt { get; }

    /// <summary>移动类型(层) → 速度(领队步伐用)。</summary>
    public Fix64 LayerSpeedCmPerSecond(int layerIdx) => Config.AgentTypes[layerIdx].SpeedCmPerSecond;

    public void EnableMovement(CrowdMovementKernel kernel, CrowdSimPlanner planner)
    {
        Movement = kernel;
        Planner = planner;
        // 迷雾/认知随运动内核装配(F02):参考端 Fog 在仿真构造期常驻;C# 只在运动会话里启用,
        // 纯部署会话(S4 形状)不建迷雾。LOS 开启需要高度场(装配缺口即报)。
        if (Structures != null)
        {
            Fog = new CrowdFog(Config, Config.Relations, Structures);
            if (Fog.Los) Fog.EnsureHeights(RebakeSources!.HeightS);
            Beliefs = new CrowdBeliefNavs(this);
            Belief = CrowdBeliefState.Create(Fog.G);
        }
    }

    /// <summary>迷雾状态(F02;null = 无头部署会话/未启用运动)。马达永远走真相,规划经认知槽。</summary>
    public CrowdFog? Fog { get; private set; }
    /// <summary>认知变体导航注册表(F02)。</summary>
    public CrowdBeliefNavs? Beliefs { get; private set; }
    /// <summary>认知槽记账(F02)。</summary>
    public CrowdBeliefState? Belief { get; private set; }
    /// <summary>truth 冻结口径(同参考端 __S7_TRUTH_NAV__ 导出补丁):跳过迷雾更新与认知同步,
    /// 组停在真相槽——旧真值(S5/S7)在迷雾内核落地后原样全等的保证;新场景不置。</summary>
    public bool TruthNavFrozen { get; set; }

    /// <summary>nav 号解析(&lt; Stride = 真相字典;否则认知注册表取/懒建变体)。
    /// 变体构建经路径服务的导航互斥锁串行(Monitor 同线程可重入,worker 侧安全)。</summary>
    public NavContext ResolveNavContext(int navId)
    {
        if (navId < CrowdBeliefNavs.BeliefStride) return Navs[navId];
        var beliefs = Beliefs ?? throw new InvalidOperationException($"变体 nav {navId}:会话没有认知注册表(迷雾未启用)。");
        return Planner!.Service.RunExclusive(() => beliefs.Get(navId));
    }

    /// <summary>组的规划句柄解析(参考 navFor):玩家视野组当前槽的变体号(0 = 真相号)。</summary>
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

    /// <summary>回放会话换装全新烘焙的导航上下文(参考端回放 = 全新仿真:结构 op 会改写共享上下文,
    /// 带着已生效的变更从头跑必然分歧)。</summary>
    public void ReplaceNavs(IReadOnlyDictionary<int, NavContext> navs) => Navs = navs;

    // 选中镜像、下令重组、阵型排槽的复用缓冲。挂在会话上:回放会话各自一份,不跨线程共享。
    internal readonly List<Arch.Core.Entity> SelectionMembers = new();
    internal readonly Dictionary<int, CrowdNavGroupSet.Group> IssueGroups = new();
    internal readonly List<CrowdNavGroupSet.Group> IssueGroupOrder = new();
    internal readonly Dictionary<int, (Fix64 X, Fix64 Y, int N)> IssueLayerSums = new();
    internal readonly CrowdOrder[] IssueBatch = new CrowdOrder[1];
    internal Fix64[] FormationDiameter = Array.Empty<Fix64>();
    internal Fix64[] FormationForward = Array.Empty<Fix64>();
    internal Fix64[] FormationLateral = Array.Empty<Fix64>();
    internal CrowdFormations.FormationRank[] FormationRanks = Array.Empty<CrowdFormations.FormationRank>();
    internal int[] FormationOrder = Array.Empty<int>();
    internal CrowdFormations.RowRank[] FormationRows = Array.Empty<CrowdFormations.RowRank>();

    public int TickCount { get; private set; }
    public int SpawnSeq { get; set; }
    public int SelectedCount { get; private set; }
    public int SpawnSkippedTotal { get; private set; }

    /// <summary>会话事件(spawnSkip 等,演示层显示用;不进校验)。</summary>
    public event Action<string>? Notified;

    /// <summary>结构变更重烘报告发布(遥测日志:rebake 报告与缓存命中计数,真机冒烟的重量证据)。</summary>
    public event Action<CrowdRebakeReport>? RebakeReported;

    public void NotifyRebake(CrowdRebakeReport report)
    {
        RebakeReported?.Invoke(report);
        Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
            $"CrowdSimulation rebake: kind={(report.Kind == CrowdRebakeReport.KindPlace ? "place" : "remove")} " +
            $"tiles={report.Tiles} contexts={report.Contexts} costOnly={report.CostOnly} " +
            $"cacheHits={report.Hits} misses={report.Misses} orders={report.Orders} refreshes={report.Refreshes} " +
            $"evicted={report.Evicted} stuck={report.Stuck} (tick {report.ExecTick}→{report.ReportTick})");
    }

    public NavContext NavFor(int layer, int r) => NavByLayerRadius[(layer, r)];

    /// <summary>
    /// 推进一个 tick:冲到点指令(指令内含结构 op 与其重烘阶段 0)→ 重烘切片推进 →
    /// 寿命到期拆除 → 落到点路径答复(未回则停摆,tick 不动)→ 运动内核子步进
    /// (领队→意图→马达)× SubSteps → tickCount+1 → 流场刷新排队 → 校验码。
    /// 返回 false = 本 tick 停摆(调用方下一帧重试同一 tick;回放对停摆逐帧同构),
    /// checksum 仅在 true 时有效——tick 路径零字符串分配。
    /// </summary>
    public bool Step(out ulong checksum)
    {
        Commands.Flush(this, CrowdSimCommands.Exec);
        if (RebakeJob != null) CrowdStructureOps.StepRebake(this, all: false);
        if (Structures != null) CrowdStructureOps.ExpireStructures(this);
        if (Planner != null && !Planner.ApplyDue(TickCount, BlockOnDueReplies))
        {
            checksum = 0;
            return false;
        }
        if (Movement != null)
        {
            if (Movement.Contacts.Length > 0) Array.Clear(Movement.Contacts);
            Movement.GatherUnits();
            for (int s = 0; s < SubSteps; s++)
            {
                // 子步序与参考 tick() 一致:哈希重建 → 分离求解(+相位前移) → 领队 → 意图 → 马达
                Movement.RebuildHash();
                Movement.SolveSeparation();
                Movement.Step(SubStepDt);
            }

            Movement.ScatterUnits();
        }

        // F02:运动后、tick 计数前——迷雾更新 + 认知同步(与参考端 advance 的挂点逐位同;
        // truth 冻结口径跳过两者,等价参考端 __S7_TRUTH_NAV__ 导出补丁)
        if (Fog != null && !TruthNavFrozen)
        {
            Fog.Update(TickCount, this);
            CrowdBeliefSync.Sync(this);
        }

        TickCount++;
        Planner?.ProcessRefreshes(TickCount);
        checksum = CrowdSimChecksum.ComputeValue(this);
        return true;
    }

    /// <summary>快进到目标 tick(停摆不推进;服务故障时抛错而不是死循环)。</summary>
    public void Advance(int ticks, List<ulong>? checksums = null)
    {
        int guard = ticks * 64 + 64;
        int ran = 0;
        while (ran < ticks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
            if (!Step(out var h)) continue;
            ran++;
            checksums?.Add(h);
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
        SelectedCount = 0;
        SpawnSkippedTotal = 0;
    }

    public void NotifySpawnSkip(int skipped, int requested)
    {
        SpawnSkippedTotal += skipped;
        Notified?.Invoke($"spawnSkip: 请求 {requested} 个,容量不足跳过 {skipped} 个(上限 {Config.Sim.MaxUnits})");
    }

    public void SetSelectedCount(int count) => SelectedCount = count;
}
