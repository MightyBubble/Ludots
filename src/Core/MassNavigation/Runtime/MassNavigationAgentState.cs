using Arch.Core;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassNavigation.Runtime;

internal sealed class MassNavigationAgentState
{
    private readonly System.Collections.Generic.List<Entity> _spawnedEntities;
    private readonly System.Collections.Generic.HashSet<Entity> _spawnedEntitySet;
    private readonly System.Collections.Generic.List<Entity> _allAgents;
    private readonly System.Collections.Generic.List<Entity> _controllableAgents;
    private readonly System.Collections.Generic.Dictionary<Entity, int> _controllableIndexByEntity;
    private int _boundAgentCount;
    private int _controllableAgentSlotCount;

    public MassNavigationAgentState(int agentCapacity)
    {
        if (agentCapacity <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(agentCapacity));
        }

        _spawnedEntities = new System.Collections.Generic.List<Entity>(agentCapacity);
        _spawnedEntitySet = new System.Collections.Generic.HashSet<Entity>(agentCapacity);
        _allAgents = new System.Collections.Generic.List<Entity>(agentCapacity);
        _controllableAgents = new System.Collections.Generic.List<Entity>(agentCapacity);
        _controllableIndexByEntity = new System.Collections.Generic.Dictionary<Entity, int>(agentCapacity);
    }

    public IReadOnlyList<Entity> SpawnedEntities => _spawnedEntities;
    public IReadOnlyList<Entity> AllAgents => _allAgents;
    public IReadOnlyList<Entity> ControllableAgentSlots => _controllableAgents;
    public int TotalAgents => _allAgents.Count;
    public int ControllableAgentSlotCount => _controllableAgentSlotCount;
    public int ControllableAgentCount => _controllableIndexByEntity.Count;
    public bool HasBoundAgents(int expectedCount)
    {
        if (expectedCount < 0)
        {
            return false;
        }

        return _allAgents.Count == expectedCount && _boundAgentCount == expectedCount;
    }

    public int BlockerCount { get; private set; }
    public int WorldMarkerCount { get; private set; }

    public void Reset()
    {
        _spawnedEntities.Clear();
        _spawnedEntitySet.Clear();
        _allAgents.Clear();
        _controllableAgents.Clear();
        _controllableIndexByEntity.Clear();
        _boundAgentCount = 0;
        _controllableAgentSlotCount = 0;
        BlockerCount = 0;
        WorldMarkerCount = 0;
    }

    public void RegisterBlocker(Entity entity)
    {
        TrackSpawnedEntity(entity);
        BlockerCount++;
    }

    public void RegisterWorldMarker(Entity entity)
    {
        TrackSpawnedEntity(entity);
        WorldMarkerCount++;
    }

    public bool TryGetControllableIndex(Entity entity, out int index)
    {
        return _controllableIndexByEntity.TryGetValue(entity, out index);
    }

    public bool TryGetControllableEntity(int agentIndex, out Entity entity)
    {
        if ((uint)agentIndex >= (uint)_controllableAgents.Count)
        {
            entity = Entity.Null;
            return false;
        }

        entity = _controllableAgents[agentIndex];
        return entity != Entity.Null;
    }

    public bool TryGetAgentEntity(int agentIndex, out Entity entity)
    {
        if ((uint)agentIndex >= (uint)_allAgents.Count)
        {
            entity = Entity.Null;
            return false;
        }

        entity = _allAgents[agentIndex];
        return entity != Entity.Null;
    }

    public void DestroyTracked(World world)
    {
        for (int i = 0; i < _spawnedEntities.Count; i++)
        {
            Entity entity = _spawnedEntities[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            PresentationEntityLifecycle.RequestDestroy(
                world,
                entity,
                $"MassNavigationAgentState tracked entity {entity.Id}");
            RemoveMassNavigationRuntimeBindings(world, entity);
        }

        Reset();
    }

    public void ClearRuntimeBindings(World world)
    {
        for (int i = 0; i < _spawnedEntities.Count; i++)
        {
            Entity entity = _spawnedEntities[i];
            if (world.IsAlive(entity))
            {
                RemoveMassNavigationRuntimeBindings(world, entity);
            }
        }

        Reset();
    }

    public void RegisterAgentAtIndex(Entity entity, int agentIndex, bool controllable)
    {
        ValidateAgentRegistration(agentIndex, controllable);

        TrackSpawnedEntity(entity);
        while (_allAgents.Count <= agentIndex)
        {
            _allAgents.Add(Entity.Null);
        }

        _allAgents[agentIndex] = entity;
        _boundAgentCount++;
        if (!controllable)
        {
            return;
        }

        while (_controllableAgents.Count <= agentIndex)
        {
            _controllableAgents.Add(Entity.Null);
        }

        _controllableAgents[agentIndex] = entity;
        _controllableIndexByEntity[entity] = agentIndex;
        _controllableAgentSlotCount++;
    }

    public void ValidateAgentRegistration(int agentIndex, bool controllable)
    {
        if (agentIndex < 0)
        {
            throw new System.InvalidOperationException("MassNavigationAgentState requires non-negative agent indices.");
        }

        if ((uint)agentIndex < (uint)_allAgents.Count &&
            _allAgents[agentIndex] != Entity.Null)
        {
            throw new System.InvalidOperationException($"MassNavigationAgentState agent index {agentIndex} is already registered.");
        }

        if (controllable &&
            (uint)agentIndex < (uint)_controllableAgents.Count &&
            _controllableAgents[agentIndex] != Entity.Null)
        {
            throw new System.InvalidOperationException($"MassNavigationAgentState controllable index {agentIndex} is already registered.");
        }
    }

    private static void RemoveMassNavigationRuntimeBindings(World world, Entity entity)
    {
        if (world.Has<MassNavigationAgentIndex>(entity))
        {
            world.Remove<MassNavigationAgentIndex>(entity);
        }

        if (world.Has<MassNavigationAgentProfile>(entity))
        {
            world.Remove<MassNavigationAgentProfile>(entity);
        }

        if (world.Has<MassNavigationBlockerProfile>(entity))
        {
            world.Remove<MassNavigationBlockerProfile>(entity);
        }

        if (world.Has<MovePlanExecutionIntent>(entity))
        {
            world.Remove<MovePlanExecutionIntent>(entity);
        }

        if (world.Has<MovePlanExecutionResult>(entity))
        {
            world.Remove<MovePlanExecutionResult>(entity);
        }
    }

    public void ClearEnvironmentCounts()
    {
        BlockerCount = 0;
        WorldMarkerCount = 0;
    }

    private void TrackSpawnedEntity(Entity entity)
    {
        if (_spawnedEntitySet.Add(entity))
        {
            _spawnedEntities.Add(entity);
        }
    }
}
