using Arch.Core;
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
    public IReadOnlyList<Entity> ControllableAgents => _controllableAgents;
    public int TotalAgents => _allAgents.Count;
    public int ControllableCount => _controllableAgents.Count;
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

    public void RegisterAgent(Entity entity, bool controllable)
    {
        _spawnedEntities.Add(entity);
        _allAgents.Add(entity);
        if (controllable)
        {
            _controllableIndexByEntityId[entity.Id] = _controllableAgents.Count;
            _controllableAgents.Add(entity);
        }
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

    public void DestroyTracked(World world)
    {
        for (int i = 0; i < _spawnedEntities.Count; i++)
        {
            Entity entity = _spawnedEntities[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (world.Has<PresentationStableId>(entity))
            {
                if (!world.Has<PresentationDestroyPending>(entity))
                {
                    world.Add(entity, new PresentationDestroyPending());
                }

                if (world.Has<PresentationDestroyEventPublished>(entity))
                {
                    world.Remove<PresentationDestroyEventPublished>(entity);
                }

                RemoveMassNavigationRuntimeTags(world, entity);
                if (world.Has<PresentationOwnerHasPerformerPayload>(entity))
                {
                    world.Remove<PresentationOwnerHasPerformerPayload>(entity);
                }
            }
            else
            {
                world.Destroy(entity);
            }
        }

        Reset();
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

