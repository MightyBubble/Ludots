using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationCommandApplySystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationCommandApplySystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        _simulation.ObserveCommandDispatchTick();

        if (_simulation.Commands.PendingCommandCount <= 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        _simulation.Commands.ApplyPending(_engine.World, _simulation);
        _simulation.ObserveCommandApply((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}

