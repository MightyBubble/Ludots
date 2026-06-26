using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationMovePlanExecutionSink : IMovePlanExecutionSink
{
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly Dictionary<int, Vector2> _lastTargetsByEntityId = new();

    public MassNavigationMovePlanExecutionSink(MassNavigationSimulationRuntime simulation)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public bool TryApply(World world, Entity entity, in MovePlanExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (intent.HasTarget == 0 ||
            !world.IsAlive(entity) ||
            !world.Has<MassNavigationAgentIndex>(entity))
        {
            return false;
        }

        MassNavigationAgentIndex agentIndex = world.Get<MassNavigationAgentIndex>(entity);
        bool resetRecovery = !_lastTargetsByEntityId.TryGetValue(entity.Id, out Vector2 lastTarget) ||
                             lastTarget != intent.TargetWorldCm;
        bool applied = _simulation.SetAgentNavigationTargetWorldCm(
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
        if (!world.IsAlive(entity) ||
            !world.TryGet(entity, out MassNavigationAgentIndex index))
        {
            return;
        }

        _lastTargetsByEntityId.Remove(entity.Id);
        _simulation.ReleaseAgentNavigationTarget(index.Value);
    }
}
