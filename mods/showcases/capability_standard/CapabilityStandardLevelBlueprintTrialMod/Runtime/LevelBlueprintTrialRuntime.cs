using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Level;

namespace CapabilityStandardLevelBlueprintTrialMod.Runtime;

public sealed class LevelBlueprintTrialRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private LevelDirector? _director;
    private GraphProgramLevelHost? _scriptHost;
    private float _accum;
    private float _time;
    private float _markerX;
    private float _markerY;
    private float[] _mx = Array.Empty<float>();
    private float[] _my = Array.Empty<float>();
    private bool[] _mAlive = Array.Empty<bool>();
    private int _aliveCount;
    private bool _spawned;
    private bool _clearedReported;
    private bool _manualScriptFired;

    public LevelDirector? Director => _director;
    public float MarkerX => _markerX;
    public float MarkerY => _markerY;
    public float[] MobX => _mx;
    public float[] MobY => _my;
    public bool[] MobAlive => _mAlive;
    public int MobSlotCount => _mx.Length;
    public int AliveMobs => _aliveCount;
    public bool GateOpen => _director != null && _director.Phase >= 2;
    public int LastScriptGraphId => _scriptHost?.LastRanGraphId ?? 0;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_level_blueprint_trial" };

    public void EnsureWorld()
    {
        if (_director != null) return;
        _director = LevelBlueprintFactory.CreateTwoPhaseTrial("showcase.level.trial");
        _scriptHost = new GraphProgramLevelHost(LevelScriptPrograms.CreateTwoPhaseTrialPrograms());
        _markerX = 0f;
        _markerY = -10f;
        _mx = new float[6];
        _my = new float[6];
        _mAlive = new bool[6];
        Metrics.AgentCount = _config.CrowdBandCount;
        Metrics.Detail = "Level Script: walk in → spawn → clear → open gate";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        MoveMarker(dt);

        _accum += dt;
        if (_accum >= _config.ThinkPeriodSeconds)
        {
            _accum = 0f;
            if (_spawned && _aliveCount == 0 && !_clearedReported)
            {
                _director!.AddCounter(10);
                _clearedReported = true;
            }

            var sw = Stopwatch.StartNew();
            LevelThinkStats stats = _director!.TickThinkWave(_scriptHost);
            if (_director.Phase >= 2 && !_manualScriptFired)
            {
                _director.PulseManual(2, _scriptHost);
                _manualScriptFired = true;
            }

            sw.Stop();
            Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
            if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
            Metrics.ThinkWaves++;
            Metrics.Detail =
                $"Level Script vignette phase={_director.Phase} script={LastScriptGraphId} fired={stats.Fired} last={Metrics.LastThinkMs:F3}ms";

            if (_director.Phase >= 1 && !_spawned)
            {
                SpawnWave();
            }
        }

        SimulateCombat();
    }

    private void MoveMarker(float dt)
    {
        if (_director!.Phase < 2)
        {
            // Walk into the trigger circle and wait out the fight
            if (_markerY < -8f) _markerY += 2.4f * dt;
            else _markerY = -7.8f;
        }
        else if (_markerY < 7f)
        {
            _markerY += 3f * dt;
        }
    }

    private void SpawnWave()
    {
        _spawned = true;
        for (int i = 0; i < _mx.Length; i++)
        {
            _mAlive[i] = true;
            _mx[i] = (i % 2 == 0 ? -4f : 4f);
            _my[i] = (i / 2) * 1.4f;
        }

        _aliveCount = _mx.Length;
    }

    private void SimulateCombat()
    {
        if (!_spawned || _aliveCount == 0) return;
        int idx = (int)(_time / 0.85f);
        if (idx < _mx.Length && _mAlive[idx])
        {
            _mAlive[idx] = false;
            _aliveCount--;
        }
    }
}
