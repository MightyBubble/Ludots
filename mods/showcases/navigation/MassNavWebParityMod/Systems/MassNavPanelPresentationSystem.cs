using Arch.System;
using Ludots.Core.Engine;
using MassNavWebParityMod.Runtime;
using MassNavWebParityMod.UI;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavPanelPresentationSystem : ISystem<float>
{
    private const float PanelRefreshIntervalSeconds = 0.25f;

    private readonly GameEngine _engine;
    private readonly MassNavWebParityRuntime _runtime;
    private float _refreshAccumulatorSeconds = PanelRefreshIntervalSeconds;

    public MassNavPanelPresentationSystem(GameEngine engine, MassNavWebParityRuntime runtime)
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
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
        if (_engine.GetService(MassNavWebParityKeys.SimulationRuntime) is MassNavSimulationRuntime simulation)
        {
            simulation.ObservePanelTick();
        }

        _runtime.RefreshPanel(_engine);
    }
}
