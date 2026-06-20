using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.MassCrowd.Runtime;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassNavigationSelectionSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationSelectionSyncSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        _simulation.BeginFrame(dt);
        _simulation.ObserveSelectionSyncTick();

        if (!SelectionContextRuntime.TryGetRuntime(_engine.GlobalContext, out SelectionRuntime selection) ||
            !SelectionContextRuntime.TryDescribeCurrentView(_world, _engine.GlobalContext, out _))
        {
            return;
        }
        
        long start = Stopwatch.GetTimestamp();
        MassNavigationSelectionSync.SyncIfChanged(_world, _engine.GlobalContext, selection, _simulation);
        _simulation.ObserveSelectionSync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
