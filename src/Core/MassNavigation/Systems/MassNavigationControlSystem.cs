using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationControlSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationControlSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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

        _simulation.ObserveControlTick();

        if (_simulation.ConsumeSceneResetRequest())
        {
            if (_simulation.Config.ScenarioRuntime.AutoSpawnConfiguredScenario)
            {
                ResetConfiguredScenario();
            }
            else
            {
                ResetRuntimeState();
                _simulation.MarkSceneResetExecuted();
                _simulation.MarkStructuralChange();
            }
        }
        else
        {
            _simulation.NavGroupRuntime.RefreshCommandSourceRotation(_engine.World, _simulation.AgentState, _simulation.CommandSourceEntities);
        }
    }

    private void ResetConfiguredScenario()
    {
        ResetRuntimeState();
        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            _engine,
            _simulation,
            _engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigation runtime requires TeamEntityLookup."));

        _simulation.MarkSceneResetExecuted();
        _simulation.MarkStructuralChange();
    }

    private void ResetRuntimeState()
    {
        ClearCommandSources();
        RemovePendingScenarioSpawns();
        _simulation.ResetRuntimeState(_engine.World);
    }

    private void RemovePendingScenarioSpawns()
    {
        RuntimeEntitySpawnQueue spawnQueue = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavigation runtime requires RuntimeEntitySpawnQueue.");
        if (_engine.CurrentMapSession != null)
        {
            spawnQueue.RemoveForMap(_engine.CurrentMapSession.MapId);
        }
    }

    private void ClearCommandSources()
    {
        EntityCollectionStore? collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
        if (collections == null)
        {
            return;
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localPlayerObj) ||
            localPlayerObj is not Arch.Core.Entity owner ||
            !_engine.World.IsAlive(owner))
        {
            return;
        }

        collections.Remove(owner, EntityCollectionKeys.CommandSource);
    }
}
