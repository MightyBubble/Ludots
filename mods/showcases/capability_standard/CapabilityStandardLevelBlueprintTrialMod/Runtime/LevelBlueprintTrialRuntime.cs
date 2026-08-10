using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Level;

namespace CapabilityStandardLevelBlueprintTrialMod.Runtime;

public sealed class LevelBlueprintTrialRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private LevelDirector? _director;
    private float _accum;
    private int _wave;
    private float[] _posX = Array.Empty<float>();
    private float[] _posY = Array.Empty<float>();
    private float[] _phase = Array.Empty<float>();

    public LevelDirector? Director => _director;
    public float[] PosX => _posX;
    public float[] PosY => _posY;
    public int VisibleUnits => _posX.Length;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_level_blueprint_trial" };

    public void EnsureWorld()
    {
        if (_director != null) return;
        _director = LevelBlueprintFactory.CreateTwoPhaseTrial("showcase.level.trial");
        // Visual cohort representing peak units (not 10k dots for clarity; marker stays in metrics).
        int visual = Math.Min(_config.AgentCount, 600);
        _posX = new float[visual];
        _posY = new float[visual];
        _phase = new float[visual];
        for (int i = 0; i < visual; i++)
        {
            _phase[i] = i * 0.02f;
            _posX[i] = MathF.Cos(_phase[i]) * (3f + (i % 20) * 0.2f);
            _posY[i] = MathF.Sin(_phase[i]) * (3f + (i % 20) * 0.2f);
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = "Level-only director trial (moving cohort)";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        float speed = 0.4f + _director!.Phase * 0.5f;
        for (int i = 0; i < _posX.Length; i++)
        {
            float r = 3f + (i % 20) * 0.2f + _director.Phase * 0.8f;
            _phase[i] += speed * dt / r;
            _posX[i] = MathF.Cos(_phase[i]) * r;
            _posY[i] = MathF.Sin(_phase[i]) * r;
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;
        if (_wave == 6) _director.AddCounter(10);
        if (_wave == 10) _director.PulseManual(2);

        var sw = Stopwatch.StartNew();
        LevelThinkStats stats = _director.TickThinkWave();
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"Level-only phase={_director.Phase} signal={_director.LastSignal} fired={stats.Fired} last={Metrics.LastThinkMs:F3}ms";
    }
}
