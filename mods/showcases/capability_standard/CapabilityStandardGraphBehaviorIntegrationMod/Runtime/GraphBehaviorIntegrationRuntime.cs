using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Gameplay.Level;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

public sealed class GraphBehaviorIntegrationRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private BehaviorTreeWorld? _bt;
    private HfsmWorld? _hfsm;
    private LevelDirector? _level;
    private float _accum;
    private float[] _posX = Array.Empty<float>();
    private float[] _posY = Array.Empty<float>();
    private float[] _phase = Array.Empty<float>();
    private float[] _radius = Array.Empty<float>();

    public float[] PosX => _posX;
    public float[] PosY => _posY;
    public BehaviorTreeWorld? Bt => _bt;
    public HfsmWorld? Hfsm => _hfsm;
    public LevelDirector? Level => _level;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_behavior_integration" };

    public void EnsureWorld()
    {
        if (_bt != null) return;
        var tree = BehaviorTreeFactory.CreateHoldRunningRoot("integration.bt");
        _bt = new BehaviorTreeWorld(tree, _config.AgentCount);
        _hfsm = new HfsmWorld(HfsmFactory.CreateSentryHierarchy("integration.hfsm"), _config.AgentCount);
        _level = LevelBlueprintFactory.CreateTwoPhaseTrial("integration.level");
        _posX = new float[_config.AgentCount];
        _posY = new float[_config.AgentCount];
        _phase = new float[_config.AgentCount];
        _radius = new float[_config.AgentCount];
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _bt.AddAgent();
            _hfsm.AddAgent();
            if ((i % 32) == 0) _hfsm.LatchStimulus(i);
            _radius[i] = 7f + (i % 45) * 0.3f;
            _phase[i] = i * 0.011f;
            _posX[i] = MathF.Cos(_phase[i]) * _radius[i];
            _posY[i] = MathF.Sin(_phase[i]) * _radius[i];
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = "Integration BT+HFSM+Level (separate from solo showcases)";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        for (int i = 0; i < _posX.Length; i++)
        {
            float speed = 1.2f + (_hfsm!.GetLeafState(i) == 4 ? 0.8f : 0f);
            _phase[i] += speed * dt / MathF.Max(0.5f, _radius[i]);
            _posX[i] = MathF.Cos(_phase[i]) * _radius[i];
            _posY[i] = MathF.Sin(_phase[i]) * _radius[i];
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        if (Metrics.ThinkWaves == 5) _level!.AddCounter(10);
        int pulse = Metrics.ThinkWaves * 91;
        for (int i = 0; i < 400; i++) _hfsm!.LatchStimulus((pulse + i) % _hfsm.Count);

        var sw = Stopwatch.StartNew();
        _bt!.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
        _hfsm!.TickAll();
        _level!.TickThinkWave();
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail = $"Integration phase={_level.Phase} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms";
    }
}
