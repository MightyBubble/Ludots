using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationAgentMetadataSyncSystem : ISystem<float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, MassNavigationAgentProfile, EntityLayer>()
        .WithNone<SuspendedTag>();
    private static readonly QueryDescription MissingEntityLayerQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, MassNavigationAgentProfile>()
        .WithNone<EntityLayer, SuspendedTag>();

    private readonly GameEngine _engine;
    private readonly HashSet<int> _domainIdSet;
    private readonly int _relationshipDomainCapacity;
    private MassNavigationSimulationRuntime? _lastSimulation;
    private int _lastSyncedStructuralChangeFrame = -1;

    public MassNavigationAgentMetadataSyncSystem(GameEngine engine, MassNavigationConfig config)
    {
        _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
        if (config == null)
        {
            throw new System.ArgumentNullException(nameof(config));
        }

        _relationshipDomainCapacity = config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity;
        _domainIdSet = new HashSet<int>(_relationshipDomainCapacity);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (!ReferenceEquals(_lastSimulation, simulation))
        {
            _lastSimulation = simulation;
            _lastSyncedStructuralChangeFrame = -1;
        }

        if (_lastSyncedStructuralChangeFrame == simulation.StructuralChangeRevision)
        {
            return;
        }

        _lastSyncedStructuralChangeFrame = simulation.StructuralChangeRevision;

        _domainIdSet.Clear();
        ThrowIfAgentMissingEntityLayer();
        foreach (ref var chunk in _engine.World.Query(in Query))
        {
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            Span<MassNavigationAgentProfile> profiles = chunk.GetSpan<MassNavigationAgentProfile>();
            Span<EntityLayer> layers = chunk.GetSpan<EntityLayer>();
            foreach (int index in chunk)
            {
                int domainId = simulation.MassNavigationFlow.GetTeam(agentIndices[index].Value);
                if (!_domainIdSet.Contains(domainId) && _domainIdSet.Count >= _relationshipDomainCapacity)
                {
                    throw new System.InvalidOperationException(
                        $"MassNavigation metadata sync required more than configured scenarioRuntime.runtimeCapacity.relationshipDomainCapacity {_relationshipDomainCapacity} domains.");
                }

                _domainIdSet.Add(domainId);
                MassNavigationAgentProfile profile = profiles[index];
                EntityLayer layer = layers[index];
                string profileKey = MassNavigationProfileRegistry.GetName(profile.ProfileId);
                AgentProfileConfig geometry = simulation.Config.AgentProfiles.ResolveGeometry(profileKey);
                simulation.MassNavigationFlow.SetUnitRuntimeProfile(
                    agentIndices[index].Value,
                    domainId,
                    profile.Heavy,
                    geometry.Mass,
                    profile.VisualScale,
                    geometry.RadiusCm,
                    profile.SpeedCmPerSecond,
                    new MassNavigationAgentLayer(layer.Value.Category, layer.Value.Mask));
            }
        }

        if (_domainIdSet.Count <= 0)
        {
            return;
        }

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

}
