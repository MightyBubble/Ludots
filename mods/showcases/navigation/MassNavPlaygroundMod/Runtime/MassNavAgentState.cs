using Arch.Core;

namespace MassNavPlaygroundMod.Runtime;

public sealed class MassNavAgentState
{
    private readonly System.Collections.Generic.List<Entity> _allAgents = new();
    private readonly System.Collections.Generic.List<Entity> _controllableAgents = new();

    public IReadOnlyList<Entity> AllAgents => _allAgents;
    public IReadOnlyList<Entity> ControllableAgents => _controllableAgents;
    public int TotalAgents => _allAgents.Count;
    public int ControllableCount => _controllableAgents.Count;
    public int BlockerCount { get; private set; }

    public void Reset()
    {
        _allAgents.Clear();
        _controllableAgents.Clear();
        BlockerCount = 0;
    }

    public void RegisterAgent(Entity entity, bool controllable)
    {
        _allAgents.Add(entity);
        if (controllable)
        {
            _controllableAgents.Add(entity);
        }
    }

    public void RegisterBlocker()
    {
        BlockerCount++;
    }

}
