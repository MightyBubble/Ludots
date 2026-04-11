using Arch.System;
using Ludots.Core.Engine;
using MassNavWebParityMod.Runtime;
using MassNavWebParityMod.UI;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavWebParityRuntime _runtime;

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
        if (_engine.GetService(MassNavWebParityKeys.SimulationRuntime) is MassNavSimulationRuntime simulation &&
            MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            simulation.ObservePanelTick();
        }

        _runtime.RefreshPanel(_engine);
    }
}
