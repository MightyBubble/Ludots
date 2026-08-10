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
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_level_blueprint_trial" };
    public int PeakUnitsMarker { get; private set; }

    public void EnsureWorld()
    {
        if (_director != null) return;
        _director = LevelBlueprintFactory.CreateTwoPhaseTrial("showcase.level.trial");
        PeakUnitsMarker = _config.AgentCount; // peak-unit contract marker for this trial
        Metrics.AgentCount = PeakUnitsMarker;
        Metrics.Detail = "Level-only director trial";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;
        var director = _director!;
        if (_wave == 6) director.AddCounter(10);
        if (_wave == 10) director.PulseManual(2);

        var sw = Stopwatch.StartNew();
        LevelThinkStats stats = director.TickThinkWave();
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"Level-only phase={director.Phase} signal={director.LastSignal} fired={stats.Fired} last={Metrics.LastThinkMs:F3}ms";
    }
}
