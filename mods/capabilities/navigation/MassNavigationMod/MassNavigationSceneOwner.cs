using System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Scripting;

namespace MassNavigationMod;

internal sealed class MassNavigationSceneOwner : IMassNavigationSceneController
{
    private readonly MapId _mapId;
    private readonly MassNavigationSceneAuthoringConfig _authoring;

    public MassNavigationSceneOwner(
        MapId mapId,
        MassNavigationSceneAuthoringConfig authoring)
    {
        _mapId = mapId;
        _authoring = authoring ?? throw new ArgumentNullException(nameof(authoring));
    }

    public void Activate(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.MapId != _mapId)
        {
            throw new InvalidOperationException(
                $"MassNavigation scene owner for '{_mapId.Value}' cannot activate runtime '{simulation.MapId.Value}'.");
        }

        if (!_authoring.AutoSpawnConfiguredScenario)
        {
            Deactivate(engine);
            return;
        }

        TeamManager.LoadConfig(_authoring.TeamRelationships!.CreateTeamConfig());
        engine.SetService(MassNavigationKeys.SceneController, this);
        if (simulation.AgentState.TotalAgents == 0)
        {
            PopulateScene(engine, simulation);
        }
    }

    public void PopulateScene(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.MapId != _mapId)
        {
            throw new InvalidOperationException(
                $"MassNavigation scene owner is not bound to active map '{simulation.MapId.Value}'.");
        }

        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            engine,
            simulation,
            _authoring,
            engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigation scene owner requires TeamEntityLookup."));
    }

    public void Deactivate(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (ReferenceEquals(engine.GetService(MassNavigationKeys.SceneController), this))
        {
            engine.RemoveService(MassNavigationKeys.SceneController);
        }
    }
}
