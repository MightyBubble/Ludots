using Arch.System;
using Ludots.Core.Engine;
using MassNavPlaygroundMod.Runtime;
using MassNavPlaygroundMod.UI;

namespace MassNavPlaygroundMod.Systems;

internal sealed class MassNavPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavPlaygroundPanelController _controller;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavPanelPresentationSystem(GameEngine engine, MassNavPlaygroundPanelController controller, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _controller = controller;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _controller.MountOrSync(_engine, _simulation);
    }
}
