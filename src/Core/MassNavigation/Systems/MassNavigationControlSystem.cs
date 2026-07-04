using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.EntityView;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

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
            RefreshSelectedRotation();
        }
    }

    private void RefreshSelectedRotation()
    {
        int count = MassNavigationSelectionAccess.GetCurrentCount(_engine.World, _engine.GlobalContext);
        if (count <= 0)
        {
            _simulation.NavGroupRuntime.RefreshSelectedRotation(
                _engine.World,
                _simulation.AgentState,
                ReadOnlySpan<Arch.Core.Entity>.Empty);
            return;
        }

        Span<Arch.Core.Entity> scratch = _simulation.EnsureSelectionScratch(count);
        int written = MassNavigationSelectionAccess.CopyCurrentSelection(
            _engine.World,
            _engine.GlobalContext,
            _simulation,
            scratch);
        _simulation.NavGroupRuntime.RefreshSelectedRotation(
            _engine.World,
            _simulation.AgentState,
            scratch[..written]);
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
        ClearEntityViewCommandCollections();
        RemovePendingScenarioSpawns();
        _simulation.ResetRuntimeState(_engine.World);
    }

    private void ClearEntityViewCommandCollections()
    {
        EntityViewRuntimeConfig config = _engine.GetService(CoreServiceKeys.EntityViewConfig)
            ?? throw new InvalidOperationException("MassNavigation scene reset requires EntityViewConfig.");
        EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("MassNavigation scene reset requires EntityCollectionStore.");
        EntityViewRuntime.ClearCurrentViewCollections(
            _engine.World,
            _engine.GlobalContext,
            config,
            collections,
            "MassNavigation scene reset");
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
}
