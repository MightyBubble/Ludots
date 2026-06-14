using System;
using Arch.System;
using Ludots.Core.Engine;
using MassNavigationMod.Runtime;
using MassNavigationMod.UI;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationRuntime _runtime;
    private readonly float _refreshIntervalSeconds;
    private float _refreshAccumulatorSeconds;

    public MassNavigationPanelPresentationSystem(
        GameEngine engine,
        MassNavigationRuntime runtime,
        float refreshIntervalSeconds)
    {
        _engine = engine;
        _runtime = runtime;
        if (!(refreshIntervalSeconds > 0f))
        {
            throw new InvalidOperationException(
                "MassNavigation panel presentation requires scenarioRuntime.panelControls.panelRefreshIntervalSeconds > 0.");
        }

        _refreshIntervalSeconds = refreshIntervalSeconds;
        _refreshAccumulatorSeconds = refreshIntervalSeconds;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            _refreshAccumulatorSeconds = _refreshIntervalSeconds;
            return;
        }

        _refreshAccumulatorSeconds += dt;
        if (_refreshAccumulatorSeconds < _refreshIntervalSeconds)
        {
            return;
        }

        _refreshAccumulatorSeconds = 0f;
        if (_engine.GetService(MassNavigationKeys.SimulationRuntime) is MassNavigationSimulationRuntime simulation)
        {
            simulation.ObservePanelTick();
        }

        _runtime.RefreshPanel(_engine);
    }
}

