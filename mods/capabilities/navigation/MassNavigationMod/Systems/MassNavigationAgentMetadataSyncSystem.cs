using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationAgentMetadataSyncSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgentTag, MassNavigationAgentIndex, Team, MassNavigationAgentProfile>();

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly HashSet<int> _teamSet = new();
    private int[] _teamScratch = new int[8];
    private int _lastSyncedStructuralChangeFrame = -1;

    public MassNavigationAgentMetadataSyncSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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

        if (_lastSyncedStructuralChangeFrame == _simulation.StructuralChangeRevision)
        {
            return;
        }

        _lastSyncedStructuralChangeFrame = _simulation.StructuralChangeRevision;

        _teamSet.Clear();
        _engine.World.Query(in Query, (Entity entity, ref MassNavigationAgentIndex agentIndex, ref Team team, ref MassNavigationAgentProfile profile) =>
        {
            _teamSet.Add(team.Id);
            if (!_engine.World.Has<EntityLayer>(entity))
            {
                throw new System.InvalidOperationException(
                    $"MassNavigation agent entity {entity.Id} requires an explicit EntityLayer component.");
            }

            EntityLayer layer = _engine.World.Get<EntityLayer>(entity);
            _simulation.MassFlow.SetUnitRuntimeProfile(
                agentIndex.Value,
                team.Id,
                profile.Heavy,
                profile.NavMass,
                profile.VisualScale,
                profile.BodyRadiusCm,
                profile.SpeedCmPerSecond,
                new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
        });

        if (_teamSet.Count <= 0)
        {
            return;
        }

        if (_teamScratch.Length < _teamSet.Count)
        {
            _teamScratch = new int[_teamSet.Count];
        }

        int cursor = 0;
        foreach (int teamId in _teamSet)
        {
            _teamScratch[cursor++] = teamId;
        }

        System.Array.Sort(_teamScratch, 0, cursor);
        if (!HaveSameTeams(_simulation.TeamIds, _teamScratch.AsSpan(0, cursor)))
        {
            _simulation.ConfigureScenarioTeams(_teamScratch.AsSpan(0, cursor));
            _simulation.RequestSceneReset();
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


