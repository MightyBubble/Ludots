using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Fsm;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

public sealed class HfsmSentryArenaRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private HfsmWorld? _world;
    private float _accum;
    private int _wave;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_hfsm_sentry_arena" };

    public void EnsureWorld()
    {
        if (_world != null) return;
        HfsmDefinition def = HfsmFactory.CreateSentryHierarchy("showcase.hfsm.sentry");
        _world = new HfsmWorld(def, _config.AgentCount);
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _world.AddAgent();
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = "HFSM-only sentry hierarchy";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;

        var world = _world!;
        // Pulse a rotating cohort so hierarchy transitions stay visible without graph-layer stagger.
        int pulse = _wave * 97;
        for (int i = 0; i < 512; i++)
        {
            world.LatchStimulus((pulse + i) % world.Count);
        }

        var sw = Stopwatch.StartNew();
        HfsmThinkStats stats = world.TickAll();
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"HFSM-only taken={stats.TransitionsTaken} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms leaf0={world.GetLeafState(0)}";
    }
}
