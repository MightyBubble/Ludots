using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationAgentMetadataSyncSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, MassNavigationAgentProfile, EntityLayer>()
        .WithNone<SuspendedTag>();
    private static readonly QueryDescription MissingEntityLayerQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, MassNavigationAgentProfile>()
        .WithNone<EntityLayer, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly MassNavigationRuntimeBinding _binding;
    private MassNavigationSimulationRuntime Simulation => _binding.RequireCurrent();
    private readonly HashSet<int> _teamSet = new();
    private int[] _observedTeamIds = Array.Empty<int>();
    private int _metadataTeamCapacity;
    private int _observedRuntimeBindingRevision = -1;
    private int _lastSyncedStructuralChangeFrame = -1;

    public MassNavigationAgentMetadataSyncSystem(GameEngine engine, MassNavigationRuntimeBinding binding)
    {
        _engine = engine;
        _binding = binding;
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

        RefreshRuntimeConfig();

        if (_lastSyncedStructuralChangeFrame == Simulation.StructuralChangeRevision)
        {
            return;
        }

        _lastSyncedStructuralChangeFrame = Simulation.StructuralChangeRevision;

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
                        $"MassNavigation metadata sync required more than runtime.capacity.metadataTeamCapacity {_metadataTeamCapacity} teams.");
                }

                _teamSet.Add(teamId);
                MassNavigationAgentProfile profile = profiles[index];
                EntityLayer layer = layers[index];
                string profileKey = MassNavigationProfileRegistry.GetName(profile.ProfileId);
                MassNavigationAgentProfilePlan runtimeProfile = Simulation.Plan.AgentProfiles.Resolve(profileKey);
                Simulation.MassNavigationFlow.SetUnitRuntimeProfile(
                    agentIndices[index].Value,
                    teamId,
                    profile.Heavy,
                    runtimeProfile.Mass,
                    profile.VisualScale,
                    runtimeProfile.RadiusCm,
                    profile.SpeedCmPerSecond,
                    new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
            }
        }

        if (_teamSet.Count <= 0)
        {
            return;
        }

        int write = 0;
        foreach (int teamId in _teamSet)
        {
            _observedTeamIds[write++] = teamId;
        }

        Array.Sort(_observedTeamIds, 0, write);
        ReadOnlySpan<int> observedTeams = _observedTeamIds.AsSpan(0, write);
        if (!HaveSameTeams(Simulation.TeamIds, observedTeams))
        {
            Simulation.ConfigureTeams(observedTeams);
        }
    }

    private void RefreshRuntimeConfig()
    {
        if (_observedRuntimeBindingRevision == _binding.Revision)
        {
            return;
        }

        _observedRuntimeBindingRevision = _binding.Revision;
        _metadataTeamCapacity = Simulation.Plan.Capacity.MetadataTeamCapacity;
        _teamSet.EnsureCapacity(_metadataTeamCapacity);
        _observedTeamIds = new int[_metadataTeamCapacity];

        _lastSyncedStructuralChangeFrame = -1;
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
