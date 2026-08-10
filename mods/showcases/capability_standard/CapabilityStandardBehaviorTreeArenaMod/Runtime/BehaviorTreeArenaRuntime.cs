using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

public sealed class BehaviorTreeArenaRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private BehaviorTreeWorld? _world;
    private float _accum;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_behavior_tree_arena" };

    public void EnsureWorld()
    {
        if (_world != null) return;
        var tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("showcase.bt.arena", _config.BtLeafCount);
        _world = new BehaviorTreeWorld(tree, _config.AgentCount);
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _world.AddAgent();
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = $"BT-only N_topo={tree.NodeCount}";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var world = _world!;
        for (int i = 0; i < world.Count; i++)
        {
            if (world.Statuses[i] == BehaviorTreeStatus.Success)
            {
                world.RestartThinking(i);
            }
        }

        var sw = Stopwatch.StartNew();
        BehaviorTreeThinkStats stats = world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail = $"BT-only visited={stats.NodesVisited} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms";
    }
}
