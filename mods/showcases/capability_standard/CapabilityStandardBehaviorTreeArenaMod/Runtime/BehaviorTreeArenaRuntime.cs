using System;
using System.Diagnostics;
using System.Numerics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

/// <summary>
/// BT arena: featured band runs L2 topology from AI/behavior_trees.json
/// (bt.patrolChaseAttack) via BehaviorTreeWorld; leaves are ActionLib Script graphs.
/// Glue feeds distance into I[0] for seeEnemy / inAttackRange leaf graphs.
/// The 10k crowd band stays an explicitly labeled no-graph pressure baseline
/// (bt.arenaCrowd, AlwaysSuccess, ScriptSlices==0).
/// </summary>
public sealed class BehaviorTreeArenaRuntime : IBehaviorTreeSensorFeed
{
    private const int NoTargetDistanceCm = 100_000;
    private const int TreeThinkBudgetSteps = 128;
    private const int CrowdThinkBudgetSteps = 8;

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private BehaviorTreeWorld? _tree;
    private BehaviorTreeWorld? _crowd;
    private float _accum;
    private float _time;
    private int _seeId;
    private int _rangeId;
    private bool _paused;
    private bool _l2Enabled = true;
    private bool _stimulusEnabled = true;
    private float _sightRadius = 5.5f;
    private float _thinkPeriodSeconds = 0.2f;
    private string _status = "巡逻已开始，等待入侵者进入视野。";

    private float[] _gx = Array.Empty<float>();
    private float[] _gy = Array.Empty<float>();
    private int[] _wp = Array.Empty<int>();
    private byte[] _intent = Array.Empty<byte>();
    private byte[] _flash = Array.Empty<byte>();
    private int[] _target = Array.Empty<int>();
    private float[] _ex = Array.Empty<float>();
    private float[] _ey = Array.Empty<float>();
    private bool[] _eAlive = Array.Empty<bool>();

    public static readonly Vector2[] PatrolPath =
    {
        new(-8f, -6f), new(8f, -6f), new(8f, 6f), new(-8f, 6f)
    };

    public float[] GuardX => _gx;
    public float[] GuardY => _gy;
    public int GuardCount => _gx.Length;
    public byte[] Intent => _intent;
    public byte[] Flash => _flash;
    public int[] TargetIndex => _target;
    public float[] EnemyX => _ex;
    public float[] EnemyY => _ey;
    public bool[] EnemyAlive => _eAlive;
    public int EnemyCount => _ex.Length;
    public BehaviorTreeWorld? TreeWorld => _tree;
    public BehaviorTreeWorld? CrowdWorld => _crowd;
    public bool Paused => _paused;
    public bool L2Enabled => _l2Enabled;
    public bool StimulusEnabled => _stimulusEnabled;
    public float SightRadius => _sightRadius;
    public float ThinkPeriodSeconds => _thinkPeriodSeconds;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_behavior_tree_arena" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_tree != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        _seeId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.seeEnemy", GraphActionHost.BehaviorTree);
        _rangeId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.inAttackRange", GraphActionHost.BehaviorTree);
        int n = _config.FeaturedAgentCount;
        _tree = new BehaviorTreeWorld(_behavior.RequireTree("bt.patrolChaseAttack"), n);
        _gx = new float[n];
        _gy = new float[n];
        _wp = new int[n];
        _intent = new byte[n];
        _flash = new byte[n];
        _target = new int[n];

        for (int i = 0; i < n; i++)
        {
            _tree.AddAgent();
            float t = i / (float)n;
            int seg = (int)(t * PatrolPath.Length) % PatrolPath.Length;
            Vector2 a = PatrolPath[seg];
            Vector2 b = PatrolPath[(seg + 1) % PatrolPath.Length];
            float u = (t * PatrolPath.Length) - seg;
            _gx[i] = a.X + (b.X - a.X) * u;
            _gy[i] = a.Y + (b.Y - a.Y) * u;
            _wp[i] = (seg + 1) % PatrolPath.Length;
            _target[i] = -1;
        }

        _ex = new float[2];
        _ey = new float[2];
        _eAlive = new bool[2];

        if (_config.ShowCrowdBand && _config.CrowdBandCount > 0)
        {
            BehaviorTreeDefinition crowdTree = _behavior.RequireTree("bt.arenaCrowd");
            _crowd = new BehaviorTreeWorld(crowdTree, _config.CrowdBandCount);
            for (int i = 0; i < _config.CrowdBandCount; i++) _crowd.AddAgent();
        }

        Metrics.AgentCount = n;
        Metrics.Detail = "BT L2 tree bt.patrolChaseAttack + ActionLib leaf Scripts";
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
        _status = _paused ? "已暂停；点“单步”观察下一次决策。" : "继续自动运行。";
    }

    public void Step()
    {
        if (!_paused)
        {
            _status = "单步只在暂停时可用；先点“暂停”。";
            return;
        }

        Advance(_thinkPeriodSeconds, forceThink: true);
        _status = _l2Enabled ? "已推进一次世界更新和一次 L2 决策。" : "世界已推进，但 L2 决策当前关闭。";
    }

    public void ToggleL2()
    {
        _l2Enabled = !_l2Enabled;
        if (_tree != null)
        {
            for (int i = 0; i < _tree.Count; i++) _tree.ResetAgent(i);
        }

        if (!_l2Enabled) Array.Clear(_intent);
        _status = _l2Enabled
            ? "L2 行为树已恢复；守卫会按树做巡逻、追击和攻击决策。"
            : "L2 行为树已关闭；同场只保留巡逻执行作对照。";
    }

    public void ToggleStimulus()
    {
        _stimulusEnabled = !_stimulusEnabled;
        _time = 0f;
        if (!_stimulusEnabled) Array.Clear(_eAlive);
        _status = _stimulusEnabled ? "入侵者已重新进入场景。" : "入侵者已移除；守卫应回到巡逻。";
    }

    public void IncreaseSightRadius()
    {
        _sightRadius = MathF.Min(10f, _sightRadius + 1.5f);
        _status = $"视野扩大到 {_sightRadius:0.0} 米。";
    }

    public void DecreaseSightRadius()
    {
        _sightRadius = MathF.Max(1.5f, _sightRadius - 1.5f);
        _status = $"视野缩小到 {_sightRadius:0.0} 米。";
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
        _sightRadius = _config.SightRadius;
        _thinkPeriodSeconds = _config.ThinkPeriodSeconds;
        _accum = 0f;
        _time = 0f;
        _tree = null;
        _crowd = null;
        Metrics.LastThinkMs = 0;
        Metrics.MaxThinkMs = 0;
        Metrics.ThinkWaves = 0;
        EnsureWorld();
        _status = "场景已重置；L2 行为树、入侵者和自动运行均已开启。";
    }

    public GraphShowcaseControlState BuildControlState()
    {
        int patrol = 0, chase = 0, attack = 0, aliveEnemies = 0;
        for (int i = 0; i < _intent.Length; i++)
        {
            switch (_intent[i])
            {
                case 1: chase++; break;
                case 2: attack++; break;
                default: patrol++; break;
            }
        }

        for (int i = 0; i < _eAlive.Length; i++) if (_eAlive[i]) aliveEnemies++;
        string detail =
            $"巡逻 {patrol} / 追击 {chase} / 攻击 {attack}；入侵者 {aliveEnemies}；" +
            $"决策波 {Metrics.ThinkWaves}；本波 {Metrics.LastThinkMs:0.000}ms；万人段 ScriptSlices=0。";
        return new GraphShowcaseControlState(
            "行为树演武场",
            "让入侵者穿过巡逻区，观察同一批守卫如何从巡逻切到追击和攻击。关闭 L2 可直接比较。",
            _status,
            detail,
            "BehaviorTreeWorld 读取 bt.patrolChaseAttack，叶子来自 ActionLib。",
            _paused,
            _l2Enabled,
            _stimulusEnabled,
            _sightRadius,
            _thinkPeriodSeconds,
            GuardCount);
    }

    private void Advance(float dt, bool forceThink)
    {
        _time += dt;
        UpdateEnemies();
        for (int i = 0; i < _flash.Length; i++) if (_flash[i] > 0) _flash[i]--;
        for (int i = 0; i < _gx.Length; i++) _target[i] = FindNearestEnemy(i);

        _accum += dt;
        if (_l2Enabled && (forceThink || _accum >= _thinkPeriodSeconds))
        {
            _accum = 0f;
            ThinkWave();
        }

        IntegrateMotion(dt);
    }

    private void ThinkWave()
    {
        var tree = _tree!;
        tree.RestartFinishedThinking();
        var sw = Stopwatch.StartNew();
        BehaviorTreeThinkStats stats = tree.TickAll(_programs, TreeThinkBudgetSteps, this);
        if (_crowd != null)
        {
            _crowd.RestartFinishedThinking();
            _crowd.TickAll(CrowdThinkBudgetSteps);
        }

        sw.Stop();
        int yieldingAgents = 0;
        for (int i = 0; i < tree.Count; i++)
        {
            _intent[i] = (byte)tree.LastScriptReturns[i];
            if (_intent[i] == 2) _flash[i] = 10;
            if (tree.Statuses[i] == BehaviorTreeStatus.Running) yieldingAgents++;
        }

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            yieldingAgents > 0
                ? $"BT L2 tree leaf yielding across think waves ({yieldingAgents} agents) steps={stats.ScriptSteps} last={Metrics.LastThinkMs:F3}ms"
                : $"BT L2 tree steps={stats.ScriptSteps} last={Metrics.LastThinkMs:F3}ms";
    }

    /// <summary>Glue feed: distance (cm) into I[0] for Condition leaf Scripts only.</summary>
    public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
    {
        if (graphId != _seeId && graphId != _rangeId) return;
        ints[0] = DistanceToTargetCm(agentIndex);
    }

    private int DistanceToTargetCm(int guard)
    {
        int e = _target[guard];
        if (e < 0 || !_eAlive[e]) return NoTargetDistanceCm;
        float dx = _ex[e] - _gx[guard];
        float dy = _ey[e] - _gy[guard];
        return (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy) * 100f);
    }

    private void UpdateEnemies()
    {
        if (!_stimulusEnabled)
        {
            Array.Clear(_eAlive);
            return;
        }

        float cycle = _time % 8f;
        if (cycle < 6f) { _eAlive[0] = true; _ex[0] = 12f - cycle * 3.5f; _ey[0] = MathF.Sin(cycle * 1.2f) * 2f; }
        else _eAlive[0] = false;
        float c2 = (_time + 4f) % 10f;
        if (c2 < 5f) { _eAlive[1] = true; _ex[1] = -12f + c2 * 4f; _ey[1] = 3f; }
        else _eAlive[1] = false;
    }

    private int FindNearestEnemy(int guard)
    {
        int best = -1;
        float bestD = _sightRadius;
        for (int e = 0; e < _eAlive.Length; e++)
        {
            if (!_eAlive[e]) continue;
            float dx = _ex[e] - _gx[guard];
            float dy = _ey[e] - _gy[guard];
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= bestD) { bestD = d; best = e; }
        }

        return best;
    }

    /// <summary>Pure executor: consumes leaf Script intent returns; no chase/attack decisions here.</summary>
    private void IntegrateMotion(float dt)
    {
        for (int i = 0; i < _gx.Length; i++)
        {
            if (_intent[i] == 2) continue;

            bool chasing = _intent[i] == 1 && _target[i] >= 0 && _eAlive[_target[i]];
            Vector2 dest;
            float speed;
            if (chasing)
            {
                int e = _target[i];
                dest = new Vector2(_ex[e], _ey[e]);
                speed = _config.ChaseSpeed;
            }
            else
            {
                dest = PatrolPath[_wp[i]];
                if (Distance(_gx[i], _gy[i], dest.X, dest.Y) < 0.35f)
                {
                    _wp[i] = (_wp[i] + 1) % PatrolPath.Length;
                    dest = PatrolPath[_wp[i]];
                }

                speed = _config.PatrolSpeed;
            }

            float dx = dest.X - _gx[i];
            float dy = dest.Y - _gy[i];
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.001f)
            {
                float step = MathF.Min(speed * dt, len);
                _gx[i] += dx / len * step;
                _gy[i] += dy / len * step;
            }
        }
    }

    private static float Distance(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
