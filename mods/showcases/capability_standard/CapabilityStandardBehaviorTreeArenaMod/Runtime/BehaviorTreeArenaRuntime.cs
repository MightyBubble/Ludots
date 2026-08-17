using System;
using System.Diagnostics;
using System.Numerics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

public sealed class BehaviorTreeArenaRuntime : IBehaviorTreeSensorFeed
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private GraphBehaviorCatalog? _behavior;
    private BehaviorTreeWorld? _world;
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
    private int _seeGraphId;
    private int _rangeGraphId;

    public static readonly Vector2[] PatrolPath =
    {
        new(-8f, -6f), new(8f, -6f), new(8f, 6f), new(-8f, 6f)
    };

    public BehaviorTreeWorld? World => _world;
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
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_behavior_tree_arena" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _actions == null || _behavior == null)
        {
            throw new InvalidOperationException("Bind(Registry, ActionCatalog, BehaviorCatalog) required.");
        }

        BehaviorTreeDefinition tree = _behavior.RequireTree("bt.patrolChaseAttack");
        int n = _config.FeaturedAgentCount;
        _world = new BehaviorTreeWorld(tree, n);
        _gx = new float[n];
        _gy = new float[n];
        _wp = new int[n];
        _intent = new byte[n];
        _flash = new byte[n];
        _target = new int[n];

        for (int i = 0; i < n; i++)
        {
            _world.AddAgent();
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
            BehaviorTreeDefinition crowdTree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("showcase.bt.crowd", 7);
            _crowd = new BehaviorTreeWorld(crowdTree, _config.CrowdBandCount);
            for (int i = 0; i < _config.CrowdBandCount; i++) _crowd.AddAgent();
        }

        _seeGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.seeEnemy", GraphActionHost.BehaviorTree);
        _rangeGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.inAttackRange", GraphActionHost.BehaviorTree);
        Metrics.AgentCount = n;
        Metrics.Detail = "BT Script leaves from ActionLib";
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
        var world = _world!;
        world.RestartAllThinking();
        var sw = Stopwatch.StartNew();
        BehaviorTreeThinkStats stats = world.TickAll(_programs, 32, this);
        if (_crowd != null)
        {
            _crowd.RestartFinishedThinking();
            _crowd.TickAll(8);
        }

        sw.Stop();
        int yieldingAgents = 0;
        for (int i = 0; i < world.Count; i++)
        {
            int ret = world.LastScriptReturns[i];
            _intent[i] = (byte)ret;
            if (ret == 2) _flash[i] = 10;
            if (world.Statuses[i] == BehaviorTreeStatus.Running) yieldingAgents++;
        }

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            yieldingAgents > 0
                ? $"BT Script patrol leaf yielding across think waves ({yieldingAgents} agents) slices={stats.ScriptSlices} last={Metrics.LastThinkMs:F3}ms"
                : $"BT Script vignette slices={stats.ScriptSlices} last={Metrics.LastThinkMs:F3}ms";
    }

    public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
    {
        if (graphId == _seeGraphId) ints[0] = _target[agentIndex] >= 0 ? 1 : 0;
        else if (graphId == _rangeGraphId) ints[0] = InAttackRange(agentIndex) ? 1 : 0;
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

    private void IntegrateMotion(float dt)
    {
        for (int i = 0; i < _gx.Length; i++)
        {
            if (_intent[i] == 2) continue;
            if (_intent[i] == 1 && _target[i] >= 0 && _eAlive[_target[i]])
            {
                int e = _target[i];
                float dx = _ex[e] - _gx[i];
                float dy = _ey[e] - _gy[i];
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.001f)
                {
                    float step = MathF.Min(_config.ChaseSpeed * dt, len);
                    _gx[i] += dx / len * step;
                    _gy[i] += dy / len * step;
                }

                continue;
            }

            Vector2 dest = PatrolPath[_wp[i]];
            float pdx = dest.X - _gx[i];
            float pdy = dest.Y - _gy[i];
            float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
            if (plen < 0.35f)
            {
                _wp[i] = (_wp[i] + 1) % PatrolPath.Length;
                dest = PatrolPath[_wp[i]];
                pdx = dest.X - _gx[i];
                pdy = dest.Y - _gy[i];
                plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
            }

            if (plen > 0.001f)
            {
                float step = MathF.Min(_config.PatrolSpeed * dt, plen);
                _gx[i] += pdx / plen * step;
                _gy[i] += pdy / plen * step;
            }
        }
    }

    private bool InAttackRange(int agentIndex)
    {
        int e = _target[agentIndex];
        if (e < 0 || !_eAlive[e]) return false;
        float dx = _ex[e] - _gx[agentIndex];
        float dy = _ey[e] - _gy[agentIndex];
        return dx * dx + dy * dy <= _config.AttackRadius * _config.AttackRadius;
    }
}
