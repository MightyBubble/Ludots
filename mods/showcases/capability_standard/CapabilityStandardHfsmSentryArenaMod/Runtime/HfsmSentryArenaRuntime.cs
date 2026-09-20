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
    private bool _paused;
    private bool _l2Enabled = true;
    private bool _stimulusEnabled = true;
    private float _alertRadius = 5f;
    private float _thinkPeriodSeconds = 0.2f;
    private string _status = "岗哨已就位，等待入侵者进入警戒圈。";

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
    public bool Paused => _paused;
    public bool L2Enabled => _l2Enabled;
    public bool StimulusEnabled => _stimulusEnabled;
    public float AlertRadius => _alertRadius;
    public float ThinkPeriodSeconds => _thinkPeriodSeconds;
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
        if (_paused) return;
        Advance(dt, forceThink: false);
    }

    public void TogglePaused()
    {
        _paused = !_paused;
        _status = _paused ? "已暂停；点“单步”观察下一次状态转换。" : "继续自动运行。";
    }

    public void Step()
    {
        if (!_paused)
        {
            _status = "单步只在暂停时可用；先点“暂停”。";
            return;
        }

        Advance(_thinkPeriodSeconds, forceThink: true);
        _status = _l2Enabled ? "已推进一次世界更新和一次 L2 状态转换。" : "世界已推进，但 L2 决策当前关闭。";
    }

    public void ToggleL2()
    {
        _l2Enabled = !_l2Enabled;
        _status = _l2Enabled
            ? "L2 HFSM 已恢复；岗哨会按状态机响应入侵者。"
            : "L2 HFSM 已关闭；岗哨不再响应入侵者。";
    }

    public void ToggleStimulus()
    {
        _stimulusEnabled = !_stimulusEnabled;
        _time = 0f;
        if (!_stimulusEnabled) _intruderAlive = false;
        _status = _stimulusEnabled ? "入侵者已重新进入场景。" : "入侵者已移除；不再产生新警报。";
    }

    public void IncreaseAlertRadius()
    {
        _alertRadius = MathF.Min(10f, _alertRadius + 1.5f);
        _status = $"警戒半径扩大到 {_alertRadius:0.0} 米。";
    }

    public void DecreaseAlertRadius()
    {
        _alertRadius = MathF.Max(1.5f, _alertRadius - 1.5f);
        _status = $"警戒半径缩小到 {_alertRadius:0.0} 米。";
    }

    public void IncreaseThinkPeriod()
    {
        _thinkPeriodSeconds = MathF.Min(1f, _thinkPeriodSeconds * 1.5f);
        _status = $"思考放慢到每 {_thinkPeriodSeconds:0.00} 秒一次。";
    }

    public void DecreaseThinkPeriod()
    {
        _thinkPeriodSeconds = MathF.Max(0.05f, _thinkPeriodSeconds / 1.5f);
        _status = $"思考加快到每 {_thinkPeriodSeconds:0.00} 秒一次。";
    }

    public void ResetScenario()
    {
        _paused = false;
        _l2Enabled = true;
        _stimulusEnabled = true;
        _alertRadius = _config.AlertRadius;
        _thinkPeriodSeconds = _config.ThinkPeriodSeconds;
        _accum = 0f;
        _time = 0f;
        _hfsm = null;
        _hfsmHost = null;
        _crowd = null;
        Metrics.LastThinkMs = 0;
        Metrics.MaxThinkMs = 0;
        Metrics.ThinkWaves = 0;
        EnsureWorld();
        _status = "场景已重置；L2 HFSM、入侵者和自动运行均已开启。";
    }

    public GraphShowcaseControlState BuildControlState()
    {
        int idle = 0, alert = 0, combat = 0, retreat = 0;
        for (int i = 0; i < SentryCount; i++)
        {
            switch (GetSentryStateName(i))
            {
                case "alert": alert++; break;
                case "combat": combat++; break;
                case "retreat": retreat++; break;
                default: idle++; break;
            }
        }

        string detail =
            $"待命 {idle} / 警戒 {alert} / 战斗 {combat} / 撤退 {retreat}；" +
            $"决策波 {Metrics.ThinkWaves}；本波 {Metrics.LastThinkMs:0.000}ms；万人段 LifecycleRuns=0。";
        return new GraphShowcaseControlState(
            "HFSM 岗哨演武场",
            "让入侵者穿过岗哨线，观察待命、警戒、战斗和撤退的层级状态变化。关闭 L2 可直接比较。",
            _status,
            detail,
            "HfsmWorld 读取 hfsm.sentry.scripted，生命周期叶子由 GraphProgramHfsmHost 执行。",
            _paused,
            _l2Enabled,
            _stimulusEnabled,
            _alertRadius,
            _thinkPeriodSeconds,
            SentryCount);
    }

    private void Advance(float dt, bool forceThink)
    {
        _time += dt;
        UpdateIntruder();

        for (int i = 0; _l2Enabled && i < _sx.Length; i++)
        {
            if (_intruderAlive && Dist2(_sx[i], _sy[i], _ix, _iy) <= _alertRadius * _alertRadius)
            {
                _hfsm!.LatchStimulus(i);
            }
        }

        _accum += dt;
        if (!_l2Enabled || (!forceThink && _accum < _thinkPeriodSeconds)) return;
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
        if (!_stimulusEnabled)
        {
            _intruderAlive = false;
            return;
        }

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
