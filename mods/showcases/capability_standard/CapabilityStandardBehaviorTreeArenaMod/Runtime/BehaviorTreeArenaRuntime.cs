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
/// Real-graph BT arena: the featured patrol/chase/attack tree is one compiled Script program
/// (Graph.BT.Tree.PatrolChaseAttack, BtSequence/BtSelector sugar) driven per agent by
/// GraphBehaviorTreeHost. Glue feeds the distance measurement into the ambient I[0]
/// sensor slot; thresholds, branch structure, and the intent code (pinned I[3]) live in
/// the graph. IntegrateMotion is a pure executor over the intent register.
/// The 10k crowd band is an explicitly labeled no-graph pressure baseline: a C#
/// BehaviorTreeWorld topology with zero Script participation (measured 2026-08-24:
/// a 10k real-graph crowd costs 9.5-15.8ms per think wave on this box and breaks the
/// 25ms CI envelope combined with the featured tree; the C# band stays under it).
/// </summary>
public sealed class BehaviorTreeArenaRuntime : IBehaviorTreeSensorFeed
{
    private const int IntentPin = 3;
    private const int NoTargetDistanceCm = 100_000;
    private const int TreeThinkBudgetSteps = 128;
    private const int CrowdThinkBudgetSteps = 8;

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private GraphBehaviorTreeHost? _host;
    private BehaviorTreeWorld? _crowd;
    private float _accum;
    private float _time;

    private float[] _gx = Array.Empty<float>();
    private float[] _gy = Array.Empty<float>();
    private int[] _wp = Array.Empty<int>();
    private byte[] _intent = Array.Empty<byte>();
    private byte[] _flash = Array.Empty<byte>();
    private int[] _target = Array.Empty<int>();
    private float[] _ex = Array.Empty<float>();
    private float[] _ey = Array.Empty<float>();
    private bool[] _eAlive = Array.Empty<bool>();
    private int _treeGraphId;

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
    public GraphBehaviorTreeHost? TreeHost => _host;
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
        if (_host != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        _treeGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.tree.patrolChaseAttack", GraphActionHost.BehaviorTree);
        int n = _config.FeaturedAgentCount;
        _host = new GraphBehaviorTreeHost(_programs, _treeGraphId, n);
        _gx = new float[n];
        _gy = new float[n];
        _wp = new int[n];
        _intent = new byte[n];
        _flash = new byte[n];
        _target = new int[n];

        for (int i = 0; i < n; i++)
        {
            _host.AddAgent();
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
        Metrics.Detail = "BT Script tree from ActionLib";
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
        var host = _host!;
        host.RestartFinishedAgents();
        var sw = Stopwatch.StartNew();
        GraphBehaviorTreeThinkStats stats = host.ThinkWave(TreeThinkBudgetSteps, sensors: this);
        if (_crowd != null)
        {
            _crowd.RestartFinishedThinking();
            _crowd.TickAll(CrowdThinkBudgetSteps);
        }

        sw.Stop();
        int yieldingAgents = 0;
        for (int i = 0; i < host.Count; i++)
        {
            _intent[i] = (byte)host.ReadInt(i, IntentPin);
            if (_intent[i] == 2) _flash[i] = 10;
            if (host.StatusOf(i) == BehaviorTreeStatus.Running) yieldingAgents++;
        }

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            yieldingAgents > 0
                ? $"BT Script graph tree patrol leaf yielding across think waves ({yieldingAgents} agents) steps={stats.Steps} last={Metrics.LastThinkMs:F3}ms"
                : $"BT Script graph tree steps={stats.Steps} last={Metrics.LastThinkMs:F3}ms";
    }

    /// <summary>Glue feed: the raw distance measurement (cm, ceiling) into the ambient I[0] slot; the graph owns the thresholds.</summary>
    public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
    {
        if (graphId != _treeGraphId) return;
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

    /// <summary>Pure executor: consumes the graph's intent register; no chase/attack decision logic lives here.</summary>
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
