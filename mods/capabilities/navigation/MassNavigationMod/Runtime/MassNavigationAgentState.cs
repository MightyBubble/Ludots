using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationAgentState
{
    private readonly System.Collections.Generic.List<Entity> _spawnedEntities = new();
    private readonly System.Collections.Generic.List<Entity> _allAgents = new();
    private readonly System.Collections.Generic.List<Entity> _controllableAgents = new();
    private readonly System.Collections.Generic.Dictionary<int, int> _controllableIndexByEntityId = new();

    public IReadOnlyList<Entity> SpawnedEntities => _spawnedEntities;
    public IReadOnlyList<Entity> AllAgents => _allAgents;
    public IReadOnlyList<Entity> ControllableAgentSlots => _controllableAgents;
    public int TotalAgents => _allAgents.Count;
    public int ControllableAgentSlotCount => _controllableAgents.Count;
    public int ControllableAgentCount => _controllableIndexByEntityId.Count;
    public bool HasBoundAgents(int expectedCount)
    {
        if (expectedCount < 0 || _allAgents.Count != expectedCount)
        {
            return false;
        }

        for (int i = 0; i < _allAgents.Count; i++)
        {
            if (_allAgents[i] == Entity.Null)
            {
                return false;
            }
        }

        return true;
    }

    public int BlockerCount { get; private set; }
    public int WorldMarkerCount { get; private set; }

    public void Reset()
    {
        _spawnedEntities.Clear();
        _allAgents.Clear();
        _controllableAgents.Clear();
        _controllableIndexByEntityId.Clear();
        BlockerCount = 0;
        WorldMarkerCount = 0;
    }

    public void RegisterBlocker(Entity entity)
    {
        _spawnedEntities.Add(entity);
        BlockerCount++;
    }

    public void RegisterWorldMarker(Entity entity)
    {
        _spawnedEntities.Add(entity);
        WorldMarkerCount++;
    }

    public bool TryGetControllableIndex(Entity entity, out int index)
    {
        return _controllableIndexByEntityId.TryGetValue(entity.Id, out index);
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
            RemoveMassNavigationRuntimeTags(world, entity);
        }

        Reset();
    }

    public void RegisterAgentAtIndex(Entity entity, int agentIndex, bool controllable)
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

        _spawnedEntities.Add(entity);
        while (_allAgents.Count <= agentIndex)
        {
            _allAgents.Add(Entity.Null);
        }

        _allAgents[agentIndex] = entity;
        if (!controllable)
        {
            return;
        }

        while (_controllableAgents.Count <= agentIndex)
        {
            _controllableAgents.Add(Entity.Null);
        }

        _controllableAgents[agentIndex] = entity;
        _controllableIndexByEntityId[entity.Id] = agentIndex;
    }

    private static void RemoveMassNavigationRuntimeTags(World world, Entity entity)
    {
        if (world.Has<MassNavigationAgentTag>(entity))
        {
            world.Remove<MassNavigationAgentTag>(entity);
        }

        if (world.Has<MassNavigationControllable>(entity))
        {
            world.Remove<MassNavigationControllable>(entity);
        }

        if (world.Has<MassNavigationAgentIndex>(entity))
        {
            world.Remove<MassNavigationAgentIndex>(entity);
        }

        if (world.Has<MassNavigationAgentProfile>(entity))
        {
            world.Remove<MassNavigationAgentProfile>(entity);
        }

        if (world.Has<MassNavigationBlocker>(entity))
        {
            world.Remove<MassNavigationBlocker>(entity);
        }

        if (world.Has<MassNavigationBlockerProfile>(entity))
        {
            world.Remove<MassNavigationBlockerProfile>(entity);
        }

        if (world.Has<MassNavigationHotspotMarker>(entity))
        {
            world.Remove<MassNavigationHotspotMarker>(entity);
        }
    }
}

