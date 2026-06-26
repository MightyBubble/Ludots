using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationAgentMetadataSyncSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, MassNavigationAgentProfile, EntityLayer>();
    private static readonly QueryDescription MissingEntityLayerQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, MassNavigationAgentProfile>()
        .WithNone<EntityLayer>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly HashSet<int> _teamSet;
    private readonly int[] _configuredTeamIds;
    private readonly int _metadataTeamCapacity;
    private int _lastSyncedStructuralChangeFrame = -1;

    public MassNavigationAgentMetadataSyncSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
        _metadataTeamCapacity = simulation.Config.ScenarioRuntime.RuntimeCapacity.MetadataTeamCapacity;
        _teamSet = new HashSet<int>(_metadataTeamCapacity);
        MassNavigationScenarioTeamConfig[] configuredTeams = simulation.Config.Scenario.Teams;
        if (configuredTeams.Length > _metadataTeamCapacity)
        {
            throw new System.InvalidOperationException(
                $"MassNavigation metadata sync configured scenario team count {configuredTeams.Length} exceeds scenarioRuntime.runtimeCapacity.metadataTeamCapacity {_metadataTeamCapacity}.");
        }

        _configuredTeamIds = new int[configuredTeams.Length];
        for (int i = 0; i < configuredTeams.Length; i++)
        {
            _configuredTeamIds[i] = configuredTeams[i].Id;
        }
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

        if (_lastSyncedStructuralChangeFrame == _simulation.StructuralChangeRevision)
        {
            return;
        }

        _lastSyncedStructuralChangeFrame = _simulation.StructuralChangeRevision;

        _teamSet.Clear();
        ThrowIfAgentMissingEntityLayer();
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            Span<Team> teams = chunk.GetSpan<Team>();
            Span<MassNavigationAgentProfile> profiles = chunk.GetSpan<MassNavigationAgentProfile>();
            Span<EntityLayer> layers = chunk.GetSpan<EntityLayer>();
            foreach (int index in chunk)
            {
                int teamId = teams[index].Id;
                if (!_teamSet.Contains(teamId) && _teamSet.Count >= _metadataTeamCapacity)
                {
                    throw new System.InvalidOperationException(
                        $"MassNavigation metadata sync required more than configured scenarioRuntime.runtimeCapacity.metadataTeamCapacity {_metadataTeamCapacity} teams.");
                }

                _teamSet.Add(teamId);
                MassNavigationAgentProfile profile = profiles[index];
                EntityLayer layer = layers[index];
                string profileKey = MassNavigationProfileRegistry.GetName(profile.ProfileId);
                AgentProfileConfig geometry = _simulation.Config.AgentProfiles.ResolveGeometry(profileKey);
                _simulation.MassNavigationFlow.SetUnitRuntimeProfile(
                    agentIndices[index].Value,
                    teamId,
                    profile.Heavy,
                    geometry.Mass,
                    profile.VisualScale,
                    geometry.RadiusCm,
                    profile.SpeedCmPerSecond,
                    new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
            }
        }

        if (_teamSet.Count <= 0)
        {
            return;
        }

        foreach (int teamId in _teamSet)
        {
            if (!IsConfiguredTeam(teamId))
            {
                throw new System.InvalidOperationException(
                    $"MassNavigation metadata sync observed team {teamId}, but that team is not authored in scenario.teams.");
            }
        }

        if (!HaveSameTeams(_simulation.TeamIds, _configuredTeamIds))
        {
            _simulation.ConfigureScenarioTeams(_configuredTeamIds);
            _simulation.RequestSceneReset();
        }
    }

    private bool IsConfiguredTeam(int teamId)
    {
        for (int i = 0; i < _configuredTeamIds.Length; i++)
        {
            if (_configuredTeamIds[i] == teamId)
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfAgentMissingEntityLayer()
    {
        foreach (ref var chunk in _engine.World.Query(in MissingEntityLayerQuery))
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref entityFirst, index);
                throw new System.InvalidOperationException(
                    $"MassNavigation agent entity {entity.Id} requires an explicit EntityLayer component.");
            }
        }
    }

    private static bool HaveSameTeams(ReadOnlySpan<int> existing, ReadOnlySpan<int> next)
    {
        if (existing.Length != next.Length)
        {
            return false;
        }

        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != next[i])
            {
                return false;
            }
        }

        return true;
    }
}
