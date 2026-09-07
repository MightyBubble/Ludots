using System;
using System.Diagnostics;
using System.Numerics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

public sealed class GraphBehaviorIntegrationRuntime : IBehaviorTreeSensorFeed
{
    private const float EnemyFirstWaveSeconds = 0.6f;
    private const int NoTargetDistanceCm = 100_000;

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private BehaviorTreeWorld? _bt;
    private HfsmWorld? _hfsm;
    private GraphProgramHfsmHost? _hfsmHost;
    private float _accum;
    private float _time;
    private float[] _gx = Array.Empty<float>();
    private float[] _gy = Array.Empty<float>();
    private int[] _wp = Array.Empty<int>();
    private byte[] _intent = Array.Empty<byte>();
    private int[] _target = Array.Empty<int>();
    private float[] _sx = Array.Empty<float>();
    private float[] _sy = Array.Empty<float>();
    private float _ex, _ey, _markerY = -10f;
    private bool _enemyAlive;
    private int _seeId, _rangeId;
    private bool _paused;
    private bool _l2Enabled = true;
    private bool _stimulusEnabled = true;
    private float _sensorRadius = 5.5f;
    private float _thinkPeriodSeconds = 0.2f;
    private string _status = "两队已就位，等待同一名入侵者穿过防线。";

    public static readonly Vector2[] LeftPatrol =
    {
        new(-10f, -4f), new(-4f, -4f), new(-4f, 4f), new(-10f, 4f)
    };

    public float[] GuardX => _gx;
    public float[] GuardY => _gy;
    public int GuardCount => _gx.Length;
    public byte[] Intent => _intent;
    public int[] TargetIndex => _target;
    public float[] SentryX => _sx;
    public float[] SentryY => _sy;
    public int SentryCount => _sx.Length;
    public HfsmWorld? Hfsm => _hfsm;
    public bool EnemyStaged => _time >= EnemyFirstWaveSeconds;
    public float EnemyX => _ex;
    public float EnemyY => _ey;
    public bool EnemyAlive => _enemyAlive;
    public float MarkerX => 0f;
    public float MarkerY => _markerY;
    public bool Paused => _paused;
    public bool L2Enabled => _l2Enabled;
    public bool StimulusEnabled => _stimulusEnabled;
    public float SensorRadius => _sensorRadius;
    public float ThinkPeriodSeconds => _thinkPeriodSeconds;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_behavior_integration" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_bt != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        int guards = 6, sentries = 6;
        _seeId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.seeEnemy", GraphActionHost.BehaviorTree);
        _rangeId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.inAttackRange", GraphActionHost.BehaviorTree);
        _bt = new BehaviorTreeWorld(_behavior.RequireTree("bt.patrolChaseAttack"), guards);
        _hfsmHost = new GraphProgramHfsmHost(_programs);
        _hfsm = new HfsmWorld(_behavior.RequireHfsm("hfsm.sentry.scripted"), sentries);

        _gx = new float[guards];
        _gy = new float[guards];
        _wp = new int[guards];
        _intent = new byte[guards];
        _target = new int[guards];
        for (int i = 0; i < guards; i++)
        {
            _bt.AddAgent();
            _gx[i] = -10f + (i % 3) * 1.2f;
            _gy[i] = -3f + (i / 3) * 3f;
            _wp[i] = i % LeftPatrol.Length;
        }

        _sx = new float[sentries];
        _sy = new float[sentries];
        for (int i = 0; i < sentries; i++)
        {
            _hfsm.AddAgent(_hfsmHost);
            _sx[i] = 6f;
            _sy[i] = -4.5f + i * 1.6f;
        }

        Metrics.AgentCount = guards + sentries;
        Metrics.Detail = "Integration L2: BehaviorTreeWorld + HfsmWorld/GraphProgramHfsmHost";
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
        _status = _paused ? "已暂停；点“单步”比较两套 L2 决策的下一波结果。" : "继续自动运行。";
    }

    public void Step()
    {
        if (!_paused)
        {
            _status = "单步只在暂停时可用；先点“暂停”。";
            return;
        }

        Advance(_thinkPeriodSeconds, forceThink: true);
        _status = _l2Enabled ? "BT 与 HFSM 各推进了一次 L2 决策。" : "世界已推进，但两套 L2 决策当前关闭。";
    }

    public void ToggleL2()
    {
        _l2Enabled = !_l2Enabled;
        if (!_l2Enabled) Array.Clear(_intent);
        _status = _l2Enabled
            ? "BT 与 HFSM 的 L2 决策都已恢复。"
            : "两套 L2 决策都已关闭；入侵者继续穿场，守卫不再响应。";
    }

    public void ToggleStimulus()
    {
        _stimulusEnabled = !_stimulusEnabled;
        _time = 0f;
        if (!_stimulusEnabled) _enemyAlive = false;
        _status = _stimulusEnabled ? "入侵者已重新进入场景。" : "入侵者已移除；两队不再收到新刺激。";
    }

    public void IncreaseSensorRadius()
    {
        _sensorRadius = MathF.Min(10f, _sensorRadius + 1.5f);
        _status = $"两队感知半径扩大到 {_sensorRadius:0.0} 米。";
    }

    public void DecreaseSensorRadius()
    {
        _sensorRadius = MathF.Max(1.5f, _sensorRadius - 1.5f);
        _status = $"两队感知半径缩小到 {_sensorRadius:0.0} 米。";
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
        _sensorRadius = _config.SightRadius;
        _thinkPeriodSeconds = _config.ThinkPeriodSeconds;
        _accum = 0f;
        _time = 0f;
        _markerY = -10f;
        _enemyAlive = false;
        _bt = null;
        _hfsm = null;
        _hfsmHost = null;
        Metrics.LastThinkMs = 0;
        Metrics.MaxThinkMs = 0;
        Metrics.ThinkWaves = 0;
        EnsureWorld();
        _status = "场景已重置；两套 L2 决策、入侵者和自动运行均已开启。";
    }

    public GraphShowcaseControlState BuildControlState()
    {
        int patrol = 0, chase = 0, attack = 0;
        for (int i = 0; i < _intent.Length; i++)
        {
            switch (_intent[i])
            {
                case 1: chase++; break;
                case 2: attack++; break;
                default: patrol++; break;
            }
        }

        int idle = 0, alert = 0, combat = 0, retreat = 0;
        if (_hfsm != null)
        {
            for (int i = 0; i < _hfsm.Count; i++)
            {
                switch (_hfsm.GetLeafStateName(i))
                {
                    case "alert": alert++; break;
                    case "combat": combat++; break;
                    case "retreat": retreat++; break;
                    default: idle++; break;
                }
            }
        }

        string detail =
            $"BT 巡逻 {patrol}/追击 {chase}/攻击 {attack}；HFSM 待命 {idle}/警戒 {alert}/战斗 {combat}/撤退 {retreat}；" +
            $"决策波 {Metrics.ThinkWaves}；本波 {Metrics.LastThinkMs:0.000}ms。";
        return new GraphShowcaseControlState(
            "BT + HFSM 联合演武场",
            "同一名入侵者穿过两道防线：左边行为树追击，右边分层状态机切换状态。",
            _status,
            detail,
            "左侧 BehaviorTreeWorld；右侧 HfsmWorld + GraphProgramHfsmHost。",
            _paused,
            _l2Enabled,
            _stimulusEnabled,
            _sensorRadius,
            _thinkPeriodSeconds,
            GuardCount + SentryCount);
    }

    private void Advance(float dt, bool forceThink)
    {
        _time += dt;
        if (_markerY < -7.5f) _markerY += 2f * dt;
        if (_stimulusEnabled && !_enemyAlive && _time >= EnemyFirstWaveSeconds && _time < 20f)
        {
            _enemyAlive = true;
            _ex = 0f;
            _ey = 8f;
        }

        if (_enemyAlive)
        {
            _ey -= 2.2f * dt;
            if (_ey < -8f) _enemyAlive = false;
        }

        for (int i = 0; i < _gx.Length; i++)
            _target[i] = (_enemyAlive && Dist2(_gx[i], _gy[i], _ex, _ey) <= _sensorRadius * _sensorRadius) ? 0 : -1;
        for (int i = 0; _l2Enabled && i < _sx.Length; i++)
        {
            if (_enemyAlive && Dist2(_sx[i], _sy[i], _ex, _ey) <= _sensorRadius * _sensorRadius)
                _hfsm!.LatchStimulus(i);
        }

        _accum += dt;
        if (_l2Enabled && (forceThink || _accum >= _thinkPeriodSeconds))
        {
            _accum = 0f;
            _bt!.RestartAllThinking();
            var sw = Stopwatch.StartNew();
            _bt.TickAll(_programs, 32, this);
            _hfsm!.TickAll(_hfsmHost);
            sw.Stop();
            for (int i = 0; i < _bt.Count; i++) _intent[i] = (byte)_bt.LastScriptReturns[i];
            Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
            if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
            Metrics.ThinkWaves++;
            Metrics.Detail = $"Integration L2 last={Metrics.LastThinkMs:F3}ms (BehaviorTreeWorld+HfsmWorld)";
        }

        IntegrateGuards(dt);
    }

    public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
    {
        if (graphId != _seeId && graphId != _rangeId) return;
        if (!_enemyAlive)
        {
            ints[0] = NoTargetDistanceCm;
            return;
        }

        float dx = _ex - _gx[agentIndex];
        float dy = _ey - _gy[agentIndex];
        ints[0] = (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy) * 100f);
    }

    private void IntegrateGuards(float dt)
    {
        for (int i = 0; i < _gx.Length; i++)
        {
            if (_intent[i] == 1 && _enemyAlive)
            {
                float dx = _ex - _gx[i], dy = _ey - _gy[i];
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.001f)
                {
                    float step = MathF.Min(_config.ChaseSpeed * dt, len);
                    _gx[i] += dx / len * step;
                    _gy[i] += dy / len * step;
                }

                continue;
            }

            Vector2 dest = LeftPatrol[_wp[i]];
            float pdx = dest.X - _gx[i], pdy = dest.Y - _gy[i];
            float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
            if (plen < 0.4f) { _wp[i] = (_wp[i] + 1) % LeftPatrol.Length; continue; }
            float stepP = MathF.Min(_config.PatrolSpeed * dt, plen);
            _gx[i] += pdx / plen * stepP;
            _gy[i] += pdy / plen * stepP;
        }
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return dx * dx + dy * dy;
    }
}
