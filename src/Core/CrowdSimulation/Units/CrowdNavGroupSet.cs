using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 导航组注册表(参考实现 sim.groups 移植):组 = (玩家 × 移动类型 × 半径级),
/// 单位按组计数;空组释放进 LIFO 空闲栈,编号在重放中可复现。
/// 组是规划与校验的组织单元(S5 的领队 / 流场按组挂),本阶段只持身份与计数。
/// </summary>
public sealed class CrowdNavGroupSet
{
    public sealed class Group
    {
        public required int Id { get; init; }
        public required int Player { get; init; }
        public required int LayerIdx { get; init; }
        public required int RIdx { get; init; }
        public required int NavId { get; init; }
        public int Count { get; set; }
    }

    private const int MaxGroups = 4096;

    private readonly List<Group?> _groups = new();
    private readonly Stack<int> _free = new();

    public IReadOnlyList<Group?> Groups => _groups;
    public int FreeCount => _free.Count;

    public Group Alloc(int player, int layerIdx, int rIdx, int navId)
    {
        int id = _free.Count > 0 ? _free.Pop() : _groups.Count;
        if (id >= MaxGroups) throw new System.InvalidOperationException($"导航组上限 {MaxGroups}  exceeded。");
        var g = new Group { Id = id, Player = player, LayerIdx = layerIdx, RIdx = rIdx, NavId = navId, Count = 0 };
        if (id == _groups.Count) _groups.Add(g);
        else _groups[id] = g;
        return g;
    }

    /// <summary>释放全部空组(deploy / spawnAt 收尾调用,顺序与参考实现一致)。</summary>
    public void ReleaseEmpty()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i] is { Count: 0 })
            {
                _groups[i] = null;
                _free.Push(i);
            }
        }
    }

    public void Reset()
    {
        _groups.Clear();
        _free.Clear();
    }
}
