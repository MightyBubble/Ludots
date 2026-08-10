using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Fsm;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

public sealed class HfsmSentryArenaRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private HfsmWorld? _world;
    private HfsmWorld? _crowd;
    private GraphProgramHfsmHost? _host;
    private float _accum;
    private float _time;
    private float[] _sx = Array.Empty<float>();
    private float[] _sy = Array.Empty<float>();
    private float _ix;
    private float _iy;
    private bool _intruderAlive;

    public HfsmWorld? World => _world;
    public float[] SentryX => _sx;
    public float[] SentryY => _sy;
    public int SentryCount => _sx.Length;
    public float IntruderX => _ix;
    public float IntruderY => _iy;
    public bool IntruderAlive => _intruderAlive;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_hfsm_sentry_arena" };

    public void EnsureWorld()
    {
        if (_world != null) return;
        HfsmDefinition def = HfsmFactory.CreateSentryHierarchyWithScripts("showcase.hfsm.sentry");
        _host = new GraphProgramHfsmHost(HfsmFactory.CreateSentryScriptPrograms());
        int n = _config.FeaturedAgentCount;
        _world = new HfsmWorld(def, n);
        _sx = new float[n];
        _sy = new float[n];
        for (int i = 0; i < n; i++)
        {
            _world.AddAgent(_host);
            _sx[i] = -6f;
            _sy[i] = -5.5f + i * (11f / Math.Max(1, n - 1));
        }

        if (_config.ShowCrowdBand && _config.CrowdBandCount > 0)
        {
            HfsmDefinition crowdDef = HfsmFactory.CreateSentryHierarchy("showcase.hfsm.crowd");
            _crowd = new HfsmWorld(crowdDef, _config.CrowdBandCount);
            for (int i = 0; i < _config.CrowdBandCount; i++) _crowd.AddAgent();
        }

        Metrics.AgentCount = n;
        Metrics.Detail = "HFSM gate: idle→alert→combat→retreat";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        UpdateIntruder();

        var world = _world!;
        for (int i = 0; i < world.Count; i++)
        {
            float dx = _ix - _sx[i];
            float dy = _iy - _sy[i];
            float d2 = dx * dx + dy * dy;
            if (_intruderAlive && d2 <= _config.AlertRadius * _config.AlertRadius)
            {
                world.LatchStimulus(i);
            }
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var sw = Stopwatch.StartNew();
        HfsmThinkStats stats = world.TickAll(_host);
        if (_crowd != null)
        {
            if ((Metrics.ThinkWaves % 5) == 0)
            {
                for (int i = 0; i < 200 && i < _crowd.Count; i++)
                {
                    _crowd.LatchStimulus((Metrics.ThinkWaves * 17 + i) % _crowd.Count);
                }
            }

            _crowd.TickAll();
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"HFSM vignette sentries={world.Count} taken={stats.TransitionsTaken} last={Metrics.LastThinkMs:F3}ms leaf0={world.GetLeafState(0)}";
    }

    private void UpdateIntruder()
    {
        float cycle = _time % 12f;
        if (cycle < 9f)
        {
            _intruderAlive = true;
            // Approach from +x, pass the gate line, then leave
            _ix = 10f - cycle * 2.2f;
            _iy = MathF.Sin(cycle * 0.7f) * 1.5f;
        }
        else
        {
            _intruderAlive = false;
            _ix = 20f;
            _iy = 0f;
        }
    }
}
