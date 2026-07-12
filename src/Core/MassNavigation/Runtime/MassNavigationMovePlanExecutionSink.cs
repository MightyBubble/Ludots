using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MovePlanning;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly record struct MassNavigationPreparedMovePlanExecution(
    Entity Entity,
    int AgentIndex,
    Vector2 TargetWorldCm,
    float StopRadiusCm,
    bool ResetRecovery,
    int BindingRevision);

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

        MassNavigationPreparedMovePlanExecution prepared = PrepareApply(world, entity, in intent);
        ApplyPrepared(world, in prepared);
        return true;
    }

    public MassNavigationPreparedMovePlanExecution PrepareApply(
        World world,
        Entity entity,
        in MovePlanExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (intent.HasTarget == 0)
        {
            throw new InvalidOperationException("MovePlanning execution preparation requires an explicit target.");
        }

        int bindingRevision = _simulation.AuthoredRuntimeBindingRevision;
        bool bindingRevisionChanged = bindingRevision != _observedBindingRevision;
        int index = ValidateBinding(world, entity);

        if (!float.IsFinite(intent.TargetWorldCm.X) ||
            !float.IsFinite(intent.TargetWorldCm.Y) ||
            !float.IsFinite(intent.StopRadiusCm) ||
            intent.StopRadiusCm < 0f)
        {
            throw new InvalidOperationException(
                $"MovePlanning target for agent index {index} requires finite WorldCm coordinates and stopRadiusCm >= 0.");
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

        bool resetRecovery = bindingRevisionChanged ||
                             _initializedByAgentIndex[index] == 0 ||
                             _entitiesByAgentIndex[index] != entity ||
                             _lastTargetsByAgentIndex[index] != targetWorldCm;
        return new MassNavigationPreparedMovePlanExecution(
            entity,
            index,
            targetWorldCm,
            intent.StopRadiusCm,
            resetRecovery,
            bindingRevision);
    }

    public int ValidateBinding(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"MovePlanning execution preparation requires live entity {entity.Id}.");
        }

        if (!world.TryGet(entity, out MassNavigationAgentIndex agentIndex))
        {
            throw new InvalidOperationException(
                $"MovePlanning execution preparation requires entity {entity.Id} to have MassNavigationAgentIndex.");
        }

        int index = agentIndex.Value;
        if ((uint)index >= (uint)_initializedByAgentIndex.Length)
        {
            throw new InvalidOperationException(
                $"MovePlanning target references MassNavigation agent index {index}, exceeding configured capacity {_initializedByAgentIndex.Length}.");
        }

        if (!_simulation.AgentState.TryGetAgentEntity(index, out Entity boundEntity) || boundEntity != entity)
        {
            throw new InvalidOperationException(
                $"MovePlanning target entity {entity.Id} is not the committed MassNavigation binding for agent index {index}.");
        }

        return index;
    }

    public void ApplyPrepared(World world, in MassNavigationPreparedMovePlanExecution prepared)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_simulation.AuthoredRuntimeBindingRevision != prepared.BindingRevision)
        {
            throw new InvalidOperationException(
                $"MovePlanning prepared target for entity {prepared.Entity.Id} was invalidated by a MassNavigation binding revision change.");
        }

        if (!world.IsAlive(prepared.Entity) ||
            !world.TryGet(prepared.Entity, out MassNavigationAgentIndex currentIndex) ||
            currentIndex.Value != prepared.AgentIndex ||
            !_simulation.AgentState.TryGetAgentEntity(prepared.AgentIndex, out Entity boundEntity) ||
            boundEntity != prepared.Entity)
        {
            throw new InvalidOperationException(
                $"MovePlanning prepared target for entity {prepared.Entity.Id} no longer matches agent index {prepared.AgentIndex}.");
        }

        if (_observedBindingRevision != prepared.BindingRevision)
        {
            InvalidateForBindingRevision();
        }

        _simulation.SetAgentNavigationTargetWorldCm(
            prepared.AgentIndex,
            prepared.TargetWorldCm.X,
            prepared.TargetWorldCm.Y,
            prepared.StopRadiusCm,
            prepared.ResetRecovery);
        int index = prepared.AgentIndex;
        Entity entity = prepared.Entity;
        _entitiesByAgentIndex[index] = entity;
        _lastTargetsByAgentIndex[index] = prepared.TargetWorldCm;
        _initializedByAgentIndex[index] = 1;
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
