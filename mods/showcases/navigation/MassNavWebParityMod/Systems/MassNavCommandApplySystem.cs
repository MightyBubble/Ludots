using System.Diagnostics;
using Arch.System;
using Ludots.Core.Engine;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavCommandApplySystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavCommandApplySystem(GameEngine engine, MassNavSimulationRuntime simulation)
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.ObserveCommandDispatchTick();

        if (_simulation.Commands.PendingCommandCount <= 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        _simulation.Commands.ApplyPending(_simulation);
        _simulation.ObserveCommandApply((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
