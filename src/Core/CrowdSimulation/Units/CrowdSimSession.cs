using System;
using System.Collections.Generic;
using ArchWorld = Arch.Core.World;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
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
    private readonly Dictionary<string, RuntimeAgentProfile> _profileById;

    public CrowdSimSession(
        CrowdSimulationRuntimeConfig config,
        ArchWorld world,
        IReadOnlyDictionary<int, NavContext> navs,
        IReadOnlyDictionary<(int Layer, int R), NavContext> navByLayerRadius,
        CrowdSimPresentationWiring? presentation = null)
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
        _profileById = new Dictionary<string, RuntimeAgentProfile>(StringComparer.Ordinal);
        foreach (var p in config.Profiles) _profileById[p.Id] = p;

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
    /// <summary>呈现/交互接线(null = 无头模式:不投影、不镜像选中集合)。</summary>
    public CrowdSimPresentationWiring? Presentation { get; }
    /// <summary>运动内核与规划器(S5;null = 纯部署会话,S4 形状)。</summary>
    public CrowdMovementKernel? Movement { get; private set; }
    public CrowdSimPlanner? Planner { get; private set; }
    /// <summary>到点答复未到时的口径:false = 引擎停摆等下帧(默认);true = 调用线程同步等(无头对拍/回放用)。</summary>
    public bool BlockOnDueReplies { get; set; }
    /// <summary>阻挡盒碰撞索引(宿主按地图阻挡物建;null = 无阻挡碰撞)。</summary>
    public CrowdBlockerColliders? Blockers { get; set; }
    /// <summary>每 tick 的仿真秒数(timeScale/FixedHz)与子步进。</summary>
    public Fix64 SimDt { get; }
    public int SubSteps { get; }
    public Fix64 SubStepDt { get; }

    /// <summary>体型 → 速度(厘米/秒)与半径(厘米),配置常量,不算状态。</summary>
    public Fix64 ProfileSpeedCmPerSecond(string profileId) =>
        Config.AgentTypes[_profileById[profileId].AgentTypeIndex].SpeedCmPerSecond;
    public Fix64 ProfileRadiusCm(string profileId) => _profileById[profileId].RadiusCm;
    /// <summary>体型 → 个人(避让/碰撞)半径 = 半径 × avoidanceRadiusScale(参考实现 units.radius 的口径)。</summary>
    public Fix64 ProfilePersonalRadiusCm(string profileId) => _profileById[profileId].PersonalRadiusCm;
    /// <summary>移动类型(层) → 速度(领队步伐用)。</summary>
    public Fix64 LayerSpeedCmPerSecond(int layerIdx) => Config.AgentTypes[layerIdx].SpeedCmPerSecond;

    public void EnableMovement(CrowdMovementKernel kernel, CrowdSimPlanner planner)
    {
        Movement = kernel;
        Planner = planner;
    }
    public IReadOnlyDictionary<int, NavContext> Navs { get; }
    /// <summary>(移动类型, 半径级) → 导航上下文(deploy / spawnAt 的取上下文入口)。</summary>
    public IReadOnlyDictionary<(int Layer, int R), NavContext> NavByLayerRadius { get; }

    public int TickCount { get; private set; }
    public int SpawnSeq { get; set; }
    public int SelectedCount { get; private set; }
    public int SpawnSkippedTotal { get; private set; }

    /// <summary>会话事件(spawnSkip 等,演示层显示用;不进校验)。</summary>
    public event Action<string>? Notified;

    public NavContext NavFor(int layer, int r) => NavByLayerRadius[(layer, r)];

    /// <summary>
    /// 推进一个 tick:冲到点指令 → 落到点路径答复(未回则停摆,tick 不动) →
    /// 运动内核子步进(领队→意图→马达)× SubSteps → tickCount+1 → 校验码。
    /// 停摆时返回 null(调用方下一帧重试同一 tick;回放对停摆逐帧同构)。
    /// </summary>
    public string? Step()
    {
        Commands.Flush(this, CrowdSimCommands.Exec);
        if (Planner != null && !Planner.ApplyDue(TickCount, BlockOnDueReplies)) return null;
        if (Movement != null)
        {
            for (int s = 0; s < SubSteps; s++)
            {
                Movement.RebuildHash();
                Movement.Step(SubStepDt);
            }
        }

        TickCount++;
        return CrowdSimChecksum.Compute(this);
    }

    /// <summary>快进到目标 tick(停摆不推进;服务故障时抛错而不是死循环)。</summary>
    public void Advance(int ticks, List<string>? checksums = null)
    {
        int guard = ticks * 64 + 64;
        int ran = 0;
        while (ran < ticks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
            string? h = Step();
            if (h == null) continue;
            ran++;
            checksums?.Add(h);
        }
    }

    /// <summary>重置到初始态(回放第一帧之前:单位清空、组清空、指令队列与指令簿清空、计数归零)。</summary>
    public void Reset()
    {
        Units.Clear();
        Groups.Reset();
        Commands.Reset();
        Orders.Clear();
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
