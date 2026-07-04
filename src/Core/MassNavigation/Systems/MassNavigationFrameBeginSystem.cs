using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationFrameBeginSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationFrameBeginSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        _simulation.BeginFrame(dt);
    }
}
