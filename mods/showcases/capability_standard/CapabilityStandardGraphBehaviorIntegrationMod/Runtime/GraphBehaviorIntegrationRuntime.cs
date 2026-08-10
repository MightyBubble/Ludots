using System;
using System.Diagnostics;
using System.Numerics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Gameplay.Level;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphBehaviorIntegrationMod.Runtime;

/// <summary>
/// Readable short play: left BT patrol lane, right HFSM gate, top level trigger that spawns a raid enemy.
/// </summary>
public sealed class GraphBehaviorIntegrationRuntime : IBehaviorTreeLeafHost
{
    private readonly GraphShowcaseConfig _config = new();
    private BehaviorTreeWorld? _bt;
    private HfsmWorld? _hfsm;
    private GraphProgramHfsmHost? _hfsmHost;
    private LevelDirector? _level;
    private float _accum;
    private float _time;

    private float[] _gx = Array.Empty<float>();
    private float[] _gy = Array.Empty<float>();
    private int[] _wp = Array.Empty<int>();
    private byte[] _intent = Array.Empty<byte>();
    private int[] _target = Array.Empty<int>();

    private float[] _sx = Array.Empty<float>();
    private float[] _sy = Array.Empty<float>();

    private float _ex;
    private float _ey;
    private bool _enemyAlive;
    private float _markerY = -10f;

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
    public LevelDirector? Level => _level;
    public float EnemyX => _ex;
    public float EnemyY => _ey;
    public bool EnemyAlive => _enemyAlive;
    public float MarkerX => 0f;
    public float MarkerY => _markerY;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_behavior_integration" };

    public void EnsureWorld()
    {
        if (_bt != null) return;
        int guards = 6;
        int sentries = 6;
        _bt = new BehaviorTreeWorld(BehaviorTreeFactory.CreatePatrolChaseAttackTree("integration.bt"), guards);
        _hfsmHost = new GraphProgramHfsmHost(HfsmFactory.CreateSentryScriptPrograms());
        _hfsm = new HfsmWorld(HfsmFactory.CreateSentryHierarchyWithScripts("integration.hfsm"), sentries);
        _level = LevelBlueprintFactory.CreateTwoPhaseTrial("integration.level");

        _gx = new float[guards];
        _gy = new float[guards];
        _wp = new int[guards];
        _intent = new byte[guards];
        _target = new int[guards];
        for (int i = 0; i < guards; i++)
        {
            _bt.AddAgent();
            float t = i / (float)guards;
            _gx[i] = -10f + (i % 3) * 1.2f;
            _gy[i] = -3f + (i / 3) * 3f;
            _wp[i] = i % LeftPatrol.Length;
            _target[i] = -1;
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
        Metrics.Detail = "Integration short play: BT lane + HFSM gate + level trigger";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        if (_markerY < -7.5f) _markerY += 2f * dt;

        // After level phase 1, spawn one raid enemy that walks across
        if (_level!.Phase >= 1 && !_enemyAlive && _time < 20f)
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
        {
            _target[i] = (_enemyAlive && Dist2(_gx[i], _gy[i], _ex, _ey) <= _config.SightRadius * _config.SightRadius)
                ? 0
                : -1;
        }

        for (int i = 0; i < _sx.Length; i++)
        {
            if (_enemyAlive && Dist2(_sx[i], _sy[i], _ex, _ey) <= _config.AlertRadius * _config.AlertRadius)
            {
                _hfsm!.LatchStimulus(i);
            }
        }

        _accum += dt;
        if (_accum >= _config.ThinkPeriodSeconds)
        {
            _accum = 0f;
            if (_level.Phase == 1 && !_enemyAlive && Metrics.ThinkWaves > 8)
            {
                _level.AddCounter(10);
            }

            for (int i = 0; i < _bt!.Count; i++) _bt.RestartThinking(i);
            var sw = Stopwatch.StartNew();
            _bt.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32, this);
            _hfsm!.TickAll(_hfsmHost);
            _level.TickThinkWave();
            sw.Stop();
            Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
            if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
            Metrics.ThinkWaves++;
            Metrics.Detail =
                $"Integration phase={_level.Phase} last={Metrics.LastThinkMs:F3}ms enemy={_enemyAlive}";
        }

        IntegrateGuards(dt);
    }

    private void IntegrateGuards(float dt)
    {
        for (int i = 0; i < _gx.Length; i++)
        {
            if (_intent[i] == 1 && _enemyAlive)
            {
                float dx = _ex - _gx[i];
                float dy = _ey - _gy[i];
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.001f)
                {
                    float step = _config.ChaseSpeed * dt;
                    if (step > len) step = len;
                    _gx[i] += dx / len * step;
                    _gy[i] += dy / len * step;
                }

                continue;
            }

            Vector2 dest = LeftPatrol[_wp[i]];
            float pdx = dest.X - _gx[i];
            float pdy = dest.Y - _gy[i];
            float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
            if (plen < 0.4f)
            {
                _wp[i] = (_wp[i] + 1) % LeftPatrol.Length;
                continue;
            }

            float stepP = _config.PatrolSpeed * dt;
            if (stepP > plen) stepP = plen;
            _gx[i] += pdx / plen * stepP;
            _gy[i] += pdy / plen * stepP;
        }
    }

    public BehaviorTreeStatus EvalCondition(int agentIndex, int bindingId)
    {
        return bindingId switch
        {
            BehaviorTreeHostBindings.SeeEnemy =>
                _target[agentIndex] >= 0 ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure,
            BehaviorTreeHostBindings.InAttackRange =>
                _enemyAlive && Dist2(_gx[agentIndex], _gy[agentIndex], _ex, _ey) <=
                _config.AttackRadius * _config.AttackRadius
                    ? BehaviorTreeStatus.Success
                    : BehaviorTreeStatus.Failure,
            _ => throw new InvalidOperationException($"Unknown condition {bindingId}")
        };
    }

    public BehaviorTreeStatus TickAction(int agentIndex, int bindingId)
    {
        _intent[agentIndex] = bindingId switch
        {
            BehaviorTreeHostBindings.Patrol => (byte)0,
            BehaviorTreeHostBindings.Chase => (byte)1,
            BehaviorTreeHostBindings.Attack => (byte)2,
            _ => throw new InvalidOperationException($"Unknown action {bindingId}")
        };
        return BehaviorTreeStatus.Running;
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return dx * dx + dy * dy;
    }
}
