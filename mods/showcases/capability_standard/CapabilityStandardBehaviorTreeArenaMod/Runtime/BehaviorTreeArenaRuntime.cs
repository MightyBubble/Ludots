using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GraphBrains;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

/// <summary>
/// BT arena: featured band runs bt.patrolChaseAttack via ECS entities carrying
/// GraphActionBrain{BtId} + BtState, driven by BtBrainHostSystem. Sensor data
/// (distance to nearest enemy) is written to entity blackboards (Sensor.DistanceCm).
/// Zero parallel world. The 10k crowd band is a labeled no-graph pressure baseline.
/// </summary>
public sealed class BehaviorTreeArenaRuntime : IDisposable
{
    private const int NoTargetDistanceCm = 100_000;
    private const string DistanceKey = "Sensor.DistanceCm";

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphBehaviorCatalog? _behavior;
    private World? _world;
    private BtBrainHostSystem? _brain;
    private Entity[] _guards = Array.Empty<Entity>();
    private int _distanceKeyId;
    private float _accum;
    private float _time;
    private bool _paused;
    private bool _l2Enabled = true;
    private bool _stimulusEnabled = true;
    private float _sightRadius = 5.5f;
    private float _thinkPeriodSeconds = 0.2f;
    private string _status = "Patrol started.";

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
    public bool Paused => _paused;
    public bool L2Enabled => _l2Enabled;
    public bool StimulusEnabled => _stimulusEnabled;
    public float SightRadius => _sightRadius;
    public float ThinkPeriodSeconds => _thinkPeriodSeconds;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_behavior_tree_arena" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions, GraphBehaviorCatalog behavior)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _behavior == null) throw new InvalidOperationException("Bind required.");

        _world = World.Create();
        _distanceKeyId = ConfigKeyRegistry.Register(DistanceKey);
        var api = new GasGraphRuntimeApi(_world);

        int n = _config.FeaturedAgentCount;
        _guards = new Entity[n];
        _gx = new float[n]; _gy = new float[n]; _wp = new int[n];
        _intent = new byte[n]; _flash = new byte[n]; _target = new int[n];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            int seg = (int)(t * PatrolPath.Length) % PatrolPath.Length;
            Vector2 a = PatrolPath[seg];
            Vector2 b = PatrolPath[(seg + 1) % PatrolPath.Length];
            float u = (t * PatrolPath.Length) - seg;
            _gx[i] = a.X + (b.X - a.X) * u;
            _gy[i] = a.Y + (b.Y - a.Y) * u;
            _wp[i] = (seg + 1) % PatrolPath.Length;
            _target[i] = -1;

            _guards[i] = _world.Create(
                new GraphActionBrain { BtId = "bt.patrolChaseAttack", ThinkEveryNTicks = 1 },
                new BtState(),
                new PlayerOwner { PlayerId = 1 },
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());
        }

        _brain = new BtBrainHostSystem(_world, _programs, api, new AlwaysAdvanceGate(), _behavior);
        _ex = new float[2]; _ey = new float[2]; _eAlive = new bool[2];
        Metrics.AgentCount = n;
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        if (_paused) return;
        Advance(dt);
    }

    public void Dispose() { _world?.Dispose(); _world = null; }
    public void TogglePaused() => _paused = !_paused;
    public void Step() { if (_paused) Advance(_thinkPeriodSeconds); }
    public void ToggleL2() { _l2Enabled = !_l2Enabled; if (!_l2Enabled) Array.Clear(_intent); }
    public void ToggleStimulus() { _stimulusEnabled = !_stimulusEnabled; _time = 0f; if (!_stimulusEnabled) Array.Clear(_eAlive); }
    public void IncreaseSightRadius() => _sightRadius = MathF.Min(10f, _sightRadius + 1.5f);
    public void DecreaseSightRadius() => _sightRadius = MathF.Max(1.5f, _sightRadius - 1.5f);
    public void IncreaseThinkPeriod() => _thinkPeriodSeconds = MathF.Min(1f, _thinkPeriodSeconds * 1.5f);
    public void DecreaseThinkPeriod() => _thinkPeriodSeconds = MathF.Max(0.05f, _thinkPeriodSeconds / 1.5f);
    public void ResetScenario()
    {
        _paused = false; _l2Enabled = true; _stimulusEnabled = true;
        _sightRadius = _config.SightRadius; _thinkPeriodSeconds = _config.ThinkPeriodSeconds;
        _accum = 0f; _time = 0f;
        Metrics.LastThinkMs = 0; Metrics.MaxThinkMs = 0; Metrics.ThinkWaves = 0;
    }

    public GraphShowcaseControlState BuildControlState()
    {
        int patrol = 0, chase = 0, attack = 0, alive = 0;
        for (int i = 0; i < _intent.Length; i++)
            { switch (_intent[i]) { case 1: chase++; break; case 2: attack++; break; default: patrol++; break; } }
        for (int i = 0; i < _eAlive.Length; i++) if (_eAlive[i]) alive++;
        return new GraphShowcaseControlState(
            "BT Arena", "Component-driven behavior tree showcase.",
            _status,
            $"P{patrol}/C{chase}/A{attack} E{alive} W{Metrics.ThinkWaves} {Metrics.LastThinkMs:0.000}ms",
            "BtBrainHostSystem (component-based)",
            _paused, _l2Enabled, _stimulusEnabled, _sightRadius, _thinkPeriodSeconds, GuardCount);
    }

    private void Advance(float dt)
    {
        _time += dt;
        UpdateEnemies();
        for (int i = 0; i < _flash.Length; i++) if (_flash[i] > 0) _flash[i]--;
        for (int i = 0; i < _gx.Length; i++) _target[i] = FindNearestEnemy(i);

        _accum += dt;
        if (_l2Enabled && _accum >= _thinkPeriodSeconds)
        {
            _accum = 0f;
            ThinkWave();
        }

        IntegrateMotion(dt);
    }

    private void ThinkWave()
    {
        if (_world == null || _brain == null || _guards.Length == 0) return;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < _guards.Length; i++)
        {
            ref BlackboardIntBuffer bb = ref _world.Get<BlackboardIntBuffer>(_guards[i]);
            bb.Set(_distanceKeyId, DistanceToTargetCm(i));
        }

        _brain.Update(1f / 60f);

        for (int i = 0; i < _guards.Length; i++)
        {
            _intent[i] = _world.Get<BtState>(_guards[i]).Status == 2 ? (byte)1 : (byte)0;
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
    }

    private int DistanceToTargetCm(int guard)
    {
        int e = _target[guard];
        if (e < 0 || !_eAlive[e]) return NoTargetDistanceCm;
        float dx = _ex[e] - _gx[guard], dy = _ey[e] - _gy[guard];
        return (int)MathF.Ceiling(MathF.Sqrt(dx*dx + dy*dy) * 100f);
    }

    private void UpdateEnemies()
    {
        if (!_stimulusEnabled) { Array.Clear(_eAlive); return; }
        float c = _time % 8f;
        if (c < 6f) { _eAlive[0] = true; _ex[0] = 12f - c*3.5f; _ey[0] = MathF.Sin(c*1.2f)*2f; } else _eAlive[0] = false;
        float c2 = (_time+4f) % 10f;
        if (c2 < 5f) { _eAlive[1] = true; _ex[1] = -12f + c2*4f; _ey[1] = 3f; } else _eAlive[1] = false;
    }

    private int FindNearestEnemy(int guard)
    {
        int best = -1; float bd = _sightRadius;
        for (int e = 0; e < _eAlive.Length; e++)
        {
            if (!_eAlive[e]) continue;
            float d = MathF.Sqrt((_ex[e]-_gx[guard])*(_ex[e]-_gx[guard]) + (_ey[e]-_gy[guard])*(_ey[e]-_gy[guard]));
            if (d <= bd) { bd = d; best = e; }
        }
        return best;
    }

    private void IntegrateMotion(float dt)
    {
        for (int i = 0; i < _gx.Length; i++)
        {
            if (_intent[i] == 2) continue;
            bool ch = _intent[i] == 1 && _target[i] >= 0 && _eAlive[_target[i]];
            Vector2 d; float sp;
            if (ch) { int e = _target[i]; d = new(_ex[e], _ey[e]); sp = _config.ChaseSpeed; }
            else
            {
                d = PatrolPath[_wp[i]];
                if (MathF.Sqrt((d.X-_gx[i])*(d.X-_gx[i])+(d.Y-_gy[i])*(d.Y-_gy[i])) < 0.35f)
                { _wp[i] = (_wp[i]+1)%PatrolPath.Length; d = PatrolPath[_wp[i]]; }
                sp = _config.PatrolSpeed;
            }
            float dx = d.X-_gx[i], dy = d.Y-_gy[i], l = MathF.Sqrt(dx*dx+dy*dy);
            if (l > 0.001f) { float s = MathF.Min(sp*dt, l); _gx[i]+=dx/l*s; _gy[i]+=dy/l*s; }
        }
    }

    private sealed class AlwaysAdvanceGate : IGameplayAdvanceGate { public bool CanAdvanceGameplay => true; }
}
