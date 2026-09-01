using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

/// <summary>
/// Sentry arena: featured band runs L2 HFSM from AI/hfsm.json (hfsm.sentry.scripted)
/// via HfsmWorld + GraphProgramHfsmHost (leaf Scripts for lifecycle / conditions).
/// Glue latches stimulus when the intruder is in alert radius.
/// The 10k crowd band stays an explicitly labeled no-graph pressure baseline
/// (hfsm.sentry, LifecycleRuns==0).
/// </summary>
public sealed class HfsmSentryArenaRuntime : IDisposable
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private HfsmWorld? _hfsm;
    private GraphProgramHfsmHost? _hfsmHost;
    private HfsmWorld? _crowd;
    private float _accum;
    private float _time;
    private float[] _sx = Array.Empty<float>();
    private float[] _sy = Array.Empty<float>();
    private float _ix;
    private float _iy;
    private bool _intruderAlive;

    public float[] SentryX => _sx;
    public float[] SentryY => _sy;
    public int SentryCount => _sx.Length;
    public float IntruderX => _ix;
    public float IntruderY => _iy;
    public bool IntruderAlive => _intruderAlive;
    public HfsmWorld? FeaturedWorld => _hfsm;
    public HfsmWorld? CrowdWorld => _crowd;
    public bool FeaturedUsesHfsmWorld => _hfsm != null;
    public bool CrowdUsesNoGraphHfsmWorld => _crowd != null;
    public int CrowdAgentCount => _crowd?.Count ?? 0;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_hfsm_sentry_arena" };

    public string GetSentryStateName(int agent)
    {
        if (_hfsm == null || agent < 0 || agent >= _hfsm.Count)
        {
            return "unknown";
        }

        return _hfsm.GetLeafStateName(agent);
    }

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_hfsm != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        int n = _config.FeaturedAgentCount;
        _hfsmHost = new GraphProgramHfsmHost(_programs);
        _hfsm = new HfsmWorld(_behavior.RequireHfsm("hfsm.sentry.scripted"), n);
        _sx = new float[n];
        _sy = new float[n];
        for (int i = 0; i < n; i++)
        {
            _hfsm.AddAgent(_hfsmHost);
            _sx[i] = -6f;
            _sy[i] = -5.5f + i * (11f / Math.Max(1, n - 1));
        }

        if (_config.ShowCrowdBand && _config.CrowdBandCount > 0)
        {
            _crowd = new HfsmWorld(_behavior.RequireHfsm("hfsm.sentry"), _config.CrowdBandCount);
            for (int i = 0; i < _config.CrowdBandCount; i++) _crowd.AddAgent();
        }

        Metrics.AgentCount = n;
        Metrics.Detail = "HFSM L2 hfsm.sentry.scripted + GraphProgramHfsmHost leaf Scripts";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        UpdateIntruder();

        for (int i = 0; i < _sx.Length; i++)
        {
            if (_intruderAlive && Dist2(_sx[i], _sy[i], _ix, _iy) <= _config.AlertRadius * _config.AlertRadius)
            {
                _hfsm!.LatchStimulus(i);
            }
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var sw = Stopwatch.StartNew();
        HfsmThinkStats stats = _hfsm!.TickAll(_hfsmHost);
        HfsmThinkStats? crowdStats = null;
        if (_crowd != null)
        {
            crowdStats = _crowd.TickAll();
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        string crowdPart = crowdStats is { } c
            ? $" crowdAgents={c.Agents} crowdLifecycleRuns={c.LifecycleRuns}"
            : string.Empty;
        Metrics.Detail =
            $"HFSM L2 wave agents={stats.Agents} lifecycleRuns={stats.LifecycleRuns} last={Metrics.LastThinkMs:F3}ms phase0={GetSentryStateName(0)}{crowdPart}";
    }

    public void Dispose()
    {
        // GraphProgramHfsmHost is not IDisposable today.
    }

    private void UpdateIntruder()
    {
        float cycle = _time % 12f;
        if (cycle < 9f)
        {
            _intruderAlive = true;
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

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return dx * dx + dy * dy;
    }
}
