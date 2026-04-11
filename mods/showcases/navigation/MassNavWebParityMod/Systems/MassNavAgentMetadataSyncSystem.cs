using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavAgentMetadataSyncSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavAgentTag, MassNavAgentIndex, Team, MassNavAgentProfile>();

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private readonly HashSet<int> _teamSet = new();
    private int[] _teamScratch = new int[8];

    public MassNavAgentMetadataSyncSystem(GameEngine engine, MassNavSimulationRuntime simulation)
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

        _teamSet.Clear();
        _engine.World.Query(in Query, (ref MassNavAgentIndex agentIndex, ref Team team, ref MassNavAgentProfile profile) =>
        {
            _teamSet.Add(team.Id);
            _simulation.WebParity.SetUnitRuntimeProfile(agentIndex.Value, team.Id, profile.NavMass, profile.VisualScale);
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
