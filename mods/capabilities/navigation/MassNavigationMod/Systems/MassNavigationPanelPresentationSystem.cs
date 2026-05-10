using Arch.System;
using Ludots.Core.Engine;
using MassNavigationMod.Runtime;
using MassNavigationMod.UI;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationPanelPresentationSystem : ISystem<float>
{
    private const float PanelRefreshIntervalSeconds = 0.25f;

    private readonly GameEngine _engine;
    private readonly MassNavigationRuntime _runtime;
    private float _refreshAccumulatorSeconds = PanelRefreshIntervalSeconds;

    public MassNavigationPanelPresentationSystem(GameEngine engine, MassNavigationRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            _refreshAccumulatorSeconds = PanelRefreshIntervalSeconds;
            _runtime.RefreshPanel(_engine);
            return;
        }

        _refreshAccumulatorSeconds += dt;
        if (_refreshAccumulatorSeconds < PanelRefreshIntervalSeconds)
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

