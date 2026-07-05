using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationCommandSourceSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationCommandSourceSyncSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            return;
        }

        _simulation.BeginFrame(dt);
        _simulation.ObserveCommandSourceSyncTick();

        if (_engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
        {
            return;
        }
        
        long start = Stopwatch.GetTimestamp();
        MassNavigationCommandSourceSync.SyncIfChanged(_world, _engine.GlobalContext, collections, _simulation);
        _simulation.ObserveCommandSourceSync((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
