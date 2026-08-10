using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Gameplay.Level;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

/// <summary>
/// Separate integration demo: BT + HFSM + Level in one think wave.
/// Not a replacement for the solo arenas — those remain single-capability.
/// </summary>
public sealed class GraphBehaviorIntegrationRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private BehaviorTreeWorld? _bt;
    private HfsmWorld? _hfsm;
    private LevelDirector? _level;
    private float _accum;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_behavior_integration" };

    public void EnsureWorld()
    {
        if (_bt != null) return;
        var tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("integration.bt", _config.BtLeafCount);
        _bt = new BehaviorTreeWorld(tree, _config.AgentCount);
        _hfsm = new HfsmWorld(HfsmFactory.CreateSentryHierarchy("integration.hfsm"), _config.AgentCount);
        _level = LevelBlueprintFactory.CreateTwoPhaseTrial("integration.level");
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _bt.AddAgent();
            _hfsm.AddAgent();
            if ((i % 32) == 0) _hfsm.LatchStimulus(i);
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = "Integration BT+HFSM+Level (separate from solo showcases)";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var bt = _bt!;
        for (int i = 0; i < bt.Count; i++)
        {
            if (bt.Statuses[i] == BehaviorTreeStatus.Success) bt.RestartThinking(i);
        }

        if (Metrics.ThinkWaves == 5) _level!.AddCounter(10);

        var sw = Stopwatch.StartNew();
        bt.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
        _hfsm!.TickAll();
        _level!.TickThinkWave();
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"Integration phase={_level.Phase} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms";
    }
}
