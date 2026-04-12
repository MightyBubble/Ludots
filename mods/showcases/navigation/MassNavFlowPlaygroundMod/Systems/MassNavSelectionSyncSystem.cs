using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using MassNavFlowPlaygroundMod.Runtime;

namespace MassNavFlowPlaygroundMod.Systems;

internal sealed class MassNavSelectionSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavSelectionSyncSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _world = engine.World;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _simulation.BeginFrame(dt);
        if (MassNavFlowPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            _simulation.ObserveSelectionSyncTick();
        }

        if (!SelectionContextRuntime.TryGetRuntime(_engine.GlobalContext, out SelectionRuntime selection) ||
            !SelectionContextRuntime.TryDescribeCurrentView(_world, _engine.GlobalContext, out _))
        {
            return;
        }
        
        long start = Stopwatch.GetTimestamp();
        if (MassNavSelectionSync.SyncIfChanged(_world, _engine.GlobalContext, selection, _simulation))
        {
            _simulation.FlowSimulation.SyncSelectionTints(_world);
        }
        _simulation.ObserveSelectionSync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
