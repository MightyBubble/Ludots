using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Fsm;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

public sealed class HfsmSentryArenaRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private HfsmWorld? _world;
    private GraphProgramHfsmHost? _host;
    private float _accum;
    private int _wave;
    private float[] _posX = Array.Empty<float>();
    private float[] _posY = Array.Empty<float>();
    private float[] _phase = Array.Empty<float>();
    private float[] _radius = Array.Empty<float>();

    public HfsmWorld? World => _world;
    public float[] PosX => _posX;
    public float[] PosY => _posY;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_hfsm_sentry_arena" };

    public void EnsureWorld()
    {
        if (_world != null) return;
        HfsmDefinition def = HfsmFactory.CreateSentryHierarchyWithScripts("showcase.hfsm.sentry");
        _host = new GraphProgramHfsmHost(HfsmFactory.CreateSentryScriptPrograms());
        _world = new HfsmWorld(def, _config.AgentCount);
        _posX = new float[_config.AgentCount];
        _posY = new float[_config.AgentCount];
        _phase = new float[_config.AgentCount];
        _radius = new float[_config.AgentCount];
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _world.AddAgent(_host);
            _radius[i] = 6f + (i % 50) * 0.28f;
            _phase[i] = i * 0.013f;
            _posX[i] = MathF.Cos(_phase[i]) * _radius[i];
            _posY[i] = MathF.Sin(_phase[i]) * _radius[i];
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = "HFSM-only sentry + Script lifecycle (moving)";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        Integrate(dt);
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;

        var world = _world!;
        int pulse = _wave * 97;
        for (int i = 0; i < 800; i++)
        {
            world.LatchStimulus((pulse + i) % world.Count);
        }

        var sw = Stopwatch.StartNew();
        HfsmThinkStats stats = world.TickAll(_host);
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"HFSM+Script taken={stats.TransitionsTaken} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms leaf0={world.GetLeafState(0)}";
    }

    private void Integrate(float dt)
    {
        var world = _world!;
        for (int i = 0; i < world.Count; i++)
        {
            int leaf = world.GetLeafState(i);
            float speed = leaf switch
            {
                1 => 0.9f,  // Idle
                3 => 1.4f,  // Alert
                4 => 2.2f,  // Combat
                5 => 1.1f,  // Retreat
                _ => 1.0f
            };
            _phase[i] += speed * dt / MathF.Max(0.5f, _radius[i]);
            _posX[i] = MathF.Cos(_phase[i]) * _radius[i];
            _posY[i] = MathF.Sin(_phase[i]) * _radius[i];
        }
    }
}
