using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardHfsmSentryArenaMod.Runtime;

/// <summary>
/// FSM-1 closeout arena: the featured sentry band runs Graph.FSM.Sentry (FsmState sugar)
/// through GraphFsmHost — one Script dispatch slice per agent per think wave. Glue feeds
/// intruder distance into I[0]; phase lives in the per-agent map variable sentry.phase.
/// The 10k crowd band is an explicitly labeled no-graph pressure baseline: C# HfsmWorld
/// over hfsm.sentry with zero lifecycle Script hosts (builtin Stimulus/Always predicates
/// only). A 10k GraphFsmHost crowd would claim graph semantics the CI envelope cannot
/// honestly carry (same decision class as the BT arena crowd band).
/// </summary>
public sealed class HfsmSentryArenaRuntime : IDisposable
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private GraphFsmHost? _fsmHost;
    private HfsmWorld? _crowd;
    private DistanceSensorFeed? _feed;
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
    public GraphFsmHost? FsmHost => _fsmHost;
    public HfsmWorld? CrowdWorld => _crowd;
    public bool FeaturedUsesGraphFsmHost => _fsmHost != null;
    public bool CrowdUsesNoGraphHfsmWorld => _crowd != null;
    public int CrowdAgentCount => _crowd?.Count ?? 0;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_hfsm_sentry_arena" };

    public string GetSentryStateName(int agent)
    {
        if (_fsmHost == null || agent < 0 || agent >= _fsmHost.Count)
        {
            return "unknown";
        }

        return _fsmHost.PhaseOf(agent) switch
        {
            0 => "idle",
            1 => "alert",
            2 => "combat",
            3 => "retreat",
            _ => "unknown"
        };
    }

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_fsmHost != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        int n = _config.FeaturedAgentCount;
        _fsmHost = new GraphFsmHost(_programs, GraphIdRegistry.GetId("Graph.FSM.Sentry"), n, "sentry.phase");
        _feed = new DistanceSensorFeed(this);
        _sx = new float[n];
        _sy = new float[n];
        for (int i = 0; i < n; i++)
        {
            _fsmHost.AddAgent();
            _sx[i] = -6f;
            _sy[i] = -5.5f + i * (11f / Math.Max(1, n - 1));
        }

        if (_config.ShowCrowdBand && _config.CrowdBandCount > 0)
        {
            _crowd = new HfsmWorld(_behavior.RequireHfsm("hfsm.sentry"), _config.CrowdBandCount);
            for (int i = 0; i < _config.CrowdBandCount; i++) _crowd.AddAgent();
        }

        Metrics.AgentCount = n;
        Metrics.Detail = "HFSM sentry FSM graph (FsmState sugar) + labeled no-graph crowd band";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        UpdateIntruder();

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var sw = Stopwatch.StartNew();
        GraphFsmThinkStats stats = _fsmHost!.ThinkWave(budgetSteps: 128, sensors: _feed);
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
            $"HFSM sentry FSM wave agents={stats.Agents} steps={stats.Steps} last={Metrics.LastThinkMs:F3}ms phase0={GetSentryStateName(0)}{crowdPart}";
    }

    public void Dispose()
    {
        _fsmHost?.Dispose();
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

    private sealed class DistanceSensorFeed : IBehaviorTreeSensorFeed
    {
        private readonly HfsmSentryArenaRuntime _runtime;

        public DistanceSensorFeed(HfsmSentryArenaRuntime runtime)
        {
            _runtime = runtime;
        }

        public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
        {
            if (!_runtime._intruderAlive)
            {
                ints[0] = int.MaxValue;
                return;
            }

            float dx = _runtime._ix - _runtime._sx[agentIndex];
            float dy = _runtime._iy - _runtime._sy[agentIndex];
            ints[0] = (int)(MathF.Sqrt(dx * dx + dy * dy) * 100f);
        }
    }
}
