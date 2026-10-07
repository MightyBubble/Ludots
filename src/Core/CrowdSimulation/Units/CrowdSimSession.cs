using System;
using System.Collections.Generic;
using ArchWorld = Arch.Core.World;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// CrowdSimulation 仿真会话(S4 子集):单位(ECS)、导航组、指令队列与固定帧率推进。
/// 引擎只有一个时钟(Engine/clock.json 的 FixedHz),会话的 tick 就是 FixedHz 的一步:
/// 每步先冲刷到点指令(指令数据决定一切状态变化),再推进 tickCount。
/// 本阶段没有移动与避让:状态只在指令边界变化,回放同一份指令流必然逐位一致。
/// </summary>
public sealed class CrowdSimSession
{
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
    }

    public CrowdSimulationRuntimeConfig Config { get; }
    public ArchWorld World { get; }
    public CrowdSimUnits Units { get; }
    public CrowdNavGroupSet Groups { get; }
    public CrowdCommandQueue Commands { get; }
    /// <summary>呈现/交互接线(null = 无头模式:不投影、不镜像选中集合)。</summary>
    public CrowdSimPresentationWiring? Presentation { get; }
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

    /// <summary>推进一个 tick:先冲刷到点指令,再 +1。返回本帧校验码。</summary>
    public string Step()
    {
        Commands.Flush(this, CrowdSimCommands.Exec);
        TickCount++;
        return CrowdSimChecksum.Compute(this);
    }

    /// <summary>快进到目标 tick(每帧都校验,演示 / 测试直接读)。</summary>
    public void Advance(int ticks, List<string>? checksums = null)
    {
        for (int k = 0; k < ticks; k++)
        {
            string h = Step();
            checksums?.Add(h);
        }
    }

    /// <summary>重置到初始态(回放第一帧之前:单位清空、组清空、指令队列清空、计数归零)。</summary>
    public void Reset()
    {
        Units.Clear();
        Groups.Reset();
        Commands.Reset();
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
