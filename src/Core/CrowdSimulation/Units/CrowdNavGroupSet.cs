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
        /// <summary>所属指令 id(0 = 无指令);组不复制指令数据。</summary>
        public int OrderId { get; set; }
        /// <summary>该组的虚拟领队(同指令同移动类型的组共享一个实例)。</summary>
        public Movement.CrowdLeader? Leader { get; set; }
        /// <summary>该组的流场(S3-c FlowField;null = 路径答复未回/无指令)。</summary>
        public Nav.FlowField? Flow { get; set; }
        /// <summary>目标格(-1 = 无)与目标连通域(-2 = 不可达)。</summary>
        public int Goal { get; set; } = -1;
        public int GoalComp { get; set; } = -2;
        /// <summary>路径答复在途(重发指令途中单位保持当前速度,不刹车)。</summary>
        public bool Planning { get; set; }
        /// <summary>槽位视线全员判定(规划器在阵型落定帧置位一次,意图层读后清零)。</summary>
        public bool LosAll { get; set; }
        /// <summary>上一任未完成的领队(重发指令时供 D61/D62 继承;答复落帧后清空)。</summary>
        public Movement.CrowdLeader? PrevLeader { get; set; }
        /// <summary>组状态序:每次规划 / 流场刷新提交时 +1;到点答复按它判陈旧(被更新请求取代的静默丢弃)。</summary>
        public int StateSeq { get; set; }
    }

    private const int MaxGroups = 4096;

    private readonly List<Group?> _groups = new();
    private readonly Stack<int> _free = new();

    public IReadOnlyList<Group?> Groups => _groups;
    public int FreeCount => _free.Count;

    public bool TryGet(int id, out Group group)
    {
        if (id >= 0 && id < _groups.Count && _groups[id] is { } g)
        {
            group = g;
            return true;
        }

        group = null!;
        return false;
    }

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
