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
        _time += dt;
        UpdateEnemies();
        for (int i = 0; i < _flash.Length; i++) if (_flash[i] > 0) _flash[i]--;
        for (int i = 0; i < _gx.Length; i++) _target[i] = FindNearestEnemy(i);

        _accum += dt;
        if (_accum >= _config.ThinkPeriodSeconds)
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
            if (tree.StatusOf(i) == BehaviorTreeStatus.Running) yieldingAgents++;
        }

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            yieldingAgents > 0
                ? $"BT L2 tree leaf yielding across think waves ({yieldingAgents} agents) steps={stats.Steps} last={Metrics.LastThinkMs:F3}ms"
                : $"BT L2 tree steps={stats.Steps} last={Metrics.LastThinkMs:F3}ms";
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
        float bestD = _config.SightRadius;
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
