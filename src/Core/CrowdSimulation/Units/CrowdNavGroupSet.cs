using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 导航组注册表。组 = (玩家 × 移动类型 × 半径级),
/// 单位按组计数;空组释放进 LIFO 空闲栈,编号在重放中可复现。
/// 组持有领队、流场、目标格、规划中、视线全员、上一任领队、状态序，以及规划句柄和身体句柄。
/// </summary>
public sealed class CrowdNavGroupSet
{
    public sealed class Group
    {
        public int Id { get; set; }
        public int Player { get; set; }
        public int LayerIdx { get; set; }
        public int RIdx { get; set; }
        /// <summary>当前规划句柄(双句柄之"认知"):真相号或认知变体号(槽切换时重挂)。
        /// 意图/领队/规划读它;马达物理读 BodyNavId(永远真相)。</summary>
        public int NavId { get; set; }
        /// <summary>真相句柄(双句柄之"身体"):生成时定,不再变。</summary>
        public int BodyNavId { get; set; }
        public int Count { get; set; }
        /// <summary>所属指令 id(0 = 无指令);组不复制指令数据。</summary>
        public int OrderId { get; set; }
        /// <summary>该组的虚拟领队(同指令同移动类型的组共享一个实例)。</summary>
        public Movement.CrowdLeader? Leader { get; set; }
        /// <summary>该组的流场。null = 路径答复未回，或没有指令。</summary>
        public Nav.FlowField? Flow { get; set; }
        /// <summary>目标格(-1 = 无)与目标连通域(-2 = 不可达)。</summary>
        public int Goal { get; set; } = -1;
        public int GoalComp { get; set; } = -2;
        /// <summary>路径答复在途(重发指令途中单位保持当前速度,不刹车)。</summary>
        public bool Planning { get; set; }
        /// <summary>流场刷新或走廊扩展答复在途。在途时组不再搭新的刷新,也不另提扩展。</summary>
        public bool Extending { get; set; }
        /// <summary>槽位视线全员判定(规划器在阵型落定帧置位一次,意图层读后清零)。</summary>
        public bool LosAll { get; set; }
        /// <summary>上一任未完成的领队(重发指令时供反转/朝向继承;答复落帧后清空)。</summary>
        public Movement.CrowdLeader? PrevLeader { get; set; }
        /// <summary>组状态序:每次规划,或反应取代在途刷新时 +1;到点答复按它判陈旧(被取代的静默丢弃)。</summary>
        public int StateSeq { get; set; }
    }

    internal const int MaxGroups = 4096;

    /// <summary>空组放回流场时走规划器的换场入口。流场只由规划器挂上，所以持有流场的组必然有这个入口。</summary>
    internal Action<Group>? ReleaseFlow { get; set; }

    private readonly List<Group?> _groups = new();
    private readonly Stack<int> _free = new();
    private readonly Stack<Group> _objects = new();

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
        if (!_objects.TryPop(out var g)) g = new Group();
        g.Id = id;
        g.Player = player;
        g.LayerIdx = layerIdx;
        g.RIdx = rIdx;
        g.NavId = navId;
        g.BodyNavId = navId;
        g.Count = 0;
        g.OrderId = 0;
        g.Leader = null;
        g.Flow = null;
        g.Goal = -1;
        g.GoalComp = -2;
        g.Planning = false;
        g.Extending = false;
        g.LosAll = false;
        g.PrevLeader = null;
        g.StateSeq = 0;
        if (id == _groups.Count) _groups.Add(g);
        else _groups[id] = g;
        return g;
    }

    /// <summary>释放全部空组。deploy / spawnAt 收尾调用，按组号升序。</summary>
    public void ReleaseEmpty()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i] is { Count: 0 } empty)
            {
                if (empty.Flow != null)
                {
                    var release = ReleaseFlow
                        ?? throw new InvalidOperationException("组持有流场但没有接上规划器的换场入口。");
                    release(empty);
                }

                _groups[i] = null;
                _free.Push(i);
                _objects.Push(empty);
            }
        }
    }

    public void Reset()
    {
        _groups.Clear();
        _free.Clear();
    }
}
