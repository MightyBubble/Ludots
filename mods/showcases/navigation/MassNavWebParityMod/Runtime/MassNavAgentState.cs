using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavAgentState
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

    public void Reset()
    {
        _spawnedEntities.Clear();
        _allAgents.Clear();
        _controllableAgents.Clear();
        _controllableIndexByEntityId.Clear();
        BlockerCount = 0;
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

    public bool TryGetControllableIndex(Entity entity, out int index)
    {
        return _controllableIndexByEntityId.TryGetValue(entity.Id, out index);
    }

    public void DestroyTracked(World world)
    {
        for (int i = 0; i < _spawnedEntities.Count; i++)
        {
            Entity entity = _spawnedEntities[i];
            if (world.IsAlive(entity))
            {
                world.Destroy(entity);
            }
        }

        Reset();
    }
}
