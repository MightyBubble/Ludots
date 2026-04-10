using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

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

        if (!SelectionContextRuntime.TryGetRuntime(_engine.GlobalContext, out SelectionRuntime selection) ||
            !SelectionContextRuntime.TryDescribeCurrentView(_world, _engine.GlobalContext, out _))
        {
            return;
        }
        
        MassNavSelectionSync.SyncIfChanged(_world, _engine.GlobalContext, selection, _simulation);
    }
}
