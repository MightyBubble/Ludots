using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationMovePlanExecutionSink : IMovePlanExecutionSink
{
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly Entity[] _entitiesByAgentIndex;
    private readonly Vector2[] _lastTargetsByAgentIndex;
    private readonly byte[] _initializedByAgentIndex;
    private int _observedBindingRevision;

    public MassNavigationMovePlanExecutionSink(MassNavigationSimulationRuntime simulation)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        int capacity = simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        _entitiesByAgentIndex = new Entity[capacity];
        _lastTargetsByAgentIndex = new Vector2[capacity];
        _initializedByAgentIndex = new byte[capacity];
        _observedBindingRevision = simulation.AuthoredRuntimeBindingRevision;
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

        InvalidateForBindingRevision();
        MassNavigationAgentIndex agentIndex = world.Get<MassNavigationAgentIndex>(entity);
        int index = agentIndex.Value;
        if ((uint)index >= (uint)_initializedByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"MovePlanning target references MassNavigation agent index {index}, exceeding configured capacity {_initializedByAgentIndex.Length}.");
        }

        Vector2 targetWorldCm = intent.TargetWorldCm;
        if (intent.ResolveNavigableTarget != 0)
        {
            targetWorldCm = _simulation.ResolveAgentNavigableTargetWorldCm(
                index,
                targetWorldCm,
                intent.ProjectionHintWorldCm,
                intent.MinimumClearanceCm);
        }

        bool resetRecovery = _initializedByAgentIndex[index] == 0 ||
                             _entitiesByAgentIndex[index] != entity ||
                             _lastTargetsByAgentIndex[index] != targetWorldCm;
        bool applied = _simulation.SetAgentNavigationTargetWorldCm(
            index,
            targetWorldCm.X,
            targetWorldCm.Y,
            intent.StopRadiusCm,
            resetRecovery);
        _entitiesByAgentIndex[index] = entity;
        _lastTargetsByAgentIndex[index] = targetWorldCm;
        _initializedByAgentIndex[index] = 1;
        return applied || !resetRecovery;
    }

    public void Clear(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        InvalidateForBindingRevision();
        if (!world.IsAlive(entity) ||
            !world.TryGet(entity, out MassNavigationAgentIndex agentIndex))
        {
            return;
        }

        int index = agentIndex.Value;
        if ((uint)index >= (uint)_initializedByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"MovePlanning clear references MassNavigation agent index {index}, exceeding configured capacity {_initializedByAgentIndex.Length}.");
        }

        _entitiesByAgentIndex[index] = Entity.Null;
        _lastTargetsByAgentIndex[index] = default;
        _initializedByAgentIndex[index] = 0;
        _simulation.ReleaseAgentNavigationTarget(index);
    }

    private void InvalidateForBindingRevision()
    {
        int revision = _simulation.AuthoredRuntimeBindingRevision;
        if (revision == _observedBindingRevision)
        {
            return;
        }

        _observedBindingRevision = revision;
        Array.Clear(_entitiesByAgentIndex);
        Array.Clear(_lastTargetsByAgentIndex);
        Array.Clear(_initializedByAgentIndex);
    }
}
