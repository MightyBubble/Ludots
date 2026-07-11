using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationMovePlanExecutionSink : IMovePlanExecutionSink
{
    private readonly MassNavigationRuntimeBinding _binding;
    private readonly Dictionary<int, Vector2> _lastTargetsByEntityId = new();
    private int _observedBindingRevision = -1;

    public MassNavigationMovePlanExecutionSink(MassNavigationRuntimeBinding binding)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public bool TryApply(World world, Entity entity, in MovePlanExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (intent.HasTarget == 0 ||
            !world.IsAlive(entity) ||
            !world.Has<MassNavigationAgentIndex>(entity) ||
            !TryResolveSimulation(out MassNavigationSimulationRuntime simulation))
        {
            return false;
        }

        MassNavigationAgentIndex agentIndex = world.Get<MassNavigationAgentIndex>(entity);
        bool resetRecovery = !_lastTargetsByEntityId.TryGetValue(entity.Id, out Vector2 lastTarget) ||
                             lastTarget != intent.TargetWorldCm;
        bool applied = simulation.SetAgentNavigationTargetWorldCm(
            agentIndex.Value,
            intent.TargetWorldCm.X,
            intent.TargetWorldCm.Y,
            intent.StopRadiusCm,
            resetRecovery);
        _lastTargetsByEntityId[entity.Id] = intent.TargetWorldCm;
        return applied || !resetRecovery;
    }

    public void Clear(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        _lastTargetsByEntityId.Remove(entity.Id);
        if (!world.IsAlive(entity) ||
            !world.TryGet(entity, out MassNavigationAgentIndex index) ||
            !TryResolveSimulation(out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        simulation.ReleaseAgentNavigationTarget(index.Value);
    }

    private bool TryResolveSimulation(out MassNavigationSimulationRuntime simulation)
    {
        if (_observedBindingRevision != _binding.Revision)
        {
            _lastTargetsByEntityId.Clear();
            _observedBindingRevision = _binding.Revision;
        }

        if (_binding.Current is not MassNavigationSimulationRuntime current)
        {
            simulation = null!;
            return false;
        }

        simulation = current;
        return true;
    }
}
