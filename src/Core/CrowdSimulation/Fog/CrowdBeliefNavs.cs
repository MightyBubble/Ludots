using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>
/// 认知变体导航注册表(fog/beliefNav.js 移植):一个认知槽持有 belief 字段(地形 + believed
/// 实体,独立于真值实体)。变体 nav 号 = 槽号 × BeliefStride + 真 nav 号,按上下文懒建。
/// C# 实现取全拷贝变体(裁定 ①,2026-10-09):克隆真相上下文的全部逐格数组,tile 条目经
/// NavTileCache 内容键共享(昂贵部分不重烘),只对分歧矩形(单侧已知实体 + 未探索 tile 行段)
/// 增量重烘——烘焙内容与参考端的 copy-on-write 变体逐位同,内存换机制简单;真机实测后再评 COW。
/// 参考端的 detach/settle(真相重烘前变体私享 tile)在全拷贝下天然成立,为显式免操作;
/// sleep(闲置变体丢派生缓冲)同理——容量上限由 retireSlots 逐旧淘汰保证。
/// 流场缓存键含 nav 号 → 变体的流场自动隔离。</summary>
public sealed class CrowdBeliefNavs
{
    public const int BeliefStride = 65536;
    private const int VersionStride = 1 << 20; // 变体版本基,不与真相 / 其他槽相撞

    private readonly CrowdSimSession _sim;
    private readonly Dictionary<int, Slot> _slots = new();

    public int Epoch { get; private set; }

    private sealed class Slot
    {
        public required CrowdStructuresStore Field;
        /// <summary>未探索 tile 集(乐观迷雾;null = 全知)。</summary>
        public List<int>? Unknown;
        /// <summary>移动类型 → 乐观层视图(未探索 tile 的 assumedArea 覆写)。</summary>
        public required Dictionary<int, CrowdStructuresStore> Layers;
        public required Dictionary<int, NavContext> Navs;
        /// <summary>变体号 → 分歧掩码(epoch 缓存)。</summary>
        public required Dictionary<int, (int Epoch, byte[] Mask)> Masks;
    }

    public CrowdBeliefNavs(CrowdSimSession sim) => _sim = sim;

    public int SlotCount => _slots.Count;

    public int VariantCount()
    {
        int n = 0;
        foreach (var b in _slots.Values) n += b.Navs.Count;
        return n;
    }

    /// <summary>注册认知槽:believed 实体表(带原 id)+ 未探索 tile 集。
    /// 经路径服务的导航互斥锁串行(与在途计算 / 结构重烘互斥;Monitor 同线程可重入)。</summary>
    public void Register(int slot, IReadOnlyList<(int Id, int TplIndex, CrowdStructureFootprint Fp)> entities, List<int>? unknown)
    {
        _sim.Planner!.Service.RunExclusive(() =>
        {
            var field = CrowdStructuresStore.BuildBeliefField(_sim.Config, _sim.Structures!.SnapshotSurface(), entities);
            _slots[slot] = new Slot
            {
                Field = field,
                Unknown = unknown is { Count: > 0 } ? unknown : null,
                Layers = new Dictionary<int, CrowdStructuresStore>(),
                Navs = new Dictionary<int, NavContext>(),
                Masks = new Dictionary<int, (int, byte[])>(),
            };
        });
    }

    public void Drop(int slot) => _sim.Planner!.Service.RunExclusive(() => _slots.Remove(slot));

    /// <summary>闲置槽休眠:全拷贝变体没有可廉价丢弃的派生缓冲,显式免操作(容量由淘汰保证)。</summary>
    public void Sleep(int slot)
    {
    }

    /// <summary>真相实体集变了:所有槽的分歧重推导(epoch 失效掩码)。</summary>
    public void TruthChanged() => Epoch++;

    /// <summary>nav 号解析:&lt; Stride = 真相;否则取/懒建该槽变体。</summary>
    public NavContext Get(int id)
    {
        if (id < BeliefStride) return _sim.Navs[id];
        int slot = id / BeliefStride;
        var b = SlotOf(slot);
        if (!b.Navs.TryGetValue(id, out var v))
        {
            var truth = _sim.Navs[id % BeliefStride];
            v = Build(b, truth, id, slot);
            b.Navs[id] = v;
        }

        return v;
    }

    private NavContext Build(Slot b, NavContext truth, int id, int slot)
    {
        // 参考端 beforeRebake 特例(belief 与真相只差一条未重烘实体)不移植:C# 的结构 op
        // 在阶段 0 同互斥域内立即重烘真相,不存在"未重烘窗口"
        var v = CloneNav(truth, id);
        var field = FieldFor(b, truth.LayerIndex);
        foreach (var r in DivergenceRects(b))
        {
            CrowdNavRebake.RebakeNav(v, _sim.RebakeSources!, field, r, _sim.NavTileCache!);
        }

        v.Version = slot * VersionStride; // 重烘各 +1 后归槽基
        return v;
    }

    /// <summary>原位揭示(INCREMENTAL REVEAL):组的槽只多了探索,实体认知没变且槽不共享——
    /// 不开新槽,已建变体只重烘被揭 tile(与乐观假设同内容的 tile 不脏)。返回 变体号 → 真变
    /// tile 集(ChangedTiles 口径,与参考端 nav.changedTiles 同义)。</summary>
    public Dictionary<int, List<int>> Reveal(int slot, List<int> tiles)
    {
        return _sim.Planner!.Service.RunExclusive(() =>
        {
            var b = SlotOf(slot);
            var gone = new HashSet<int>(tiles);
            if (b.Unknown != null)
            {
                b.Unknown = b.Unknown.Where(t => !gone.Contains(t)).ToList();
                if (b.Unknown.Count == 0) b.Unknown = null;
            }

            int n = b.Field.CellCount, t = _sim.Config.Hpa.ClusterSize;
            var rects = tiles.Select(tile => (tile % (n / t) * t, tile / (n / t) * t, tile % (n / t) * t + t, tile / (n / t) * t + t)).ToList();
            foreach (var layer in b.Layers.Values)
            {
                foreach (var (x0, y0, x1, _) in rects)
                {
                    for (int y = y0; y < y0 + t; y++)
                    {
                        Array.Copy(b.Field.Area, y * n + x0, layer.Area, y * n + x0, x1 - x0);
                    }
                }
            }

            b.Masks.Clear();
            var dirty = new Dictionary<int, List<int>>();
            foreach (var (id, v) in b.Navs)
            {
                var field = FieldFor(b, v.LayerIndex);
                foreach (var r in rects)
                {
                    CrowdNavRebake.RebakeNav(v, _sim.RebakeSources!, field, r, _sim.NavTileCache!);
                }

                dirty[id] = v.ChangedTiles ?? new List<int>();
            }

            return dirty;
        });
    }

    /// <summary>该移动类型最便宜的可跨区域(assumedArea;slopeFree 优先——未知坡度永不挡路)。</summary>
    public int AssumedArea(int layerIdx)
    {
        var row = _sim.Config.AgentTypes[layerIdx].CostByArea;
        int Free(int a) => _sim.Config.NavAreas[a].SlopeFree ? 1 : 0;
        int best = -1;
        for (int a = 0; a < row.Length; a++)
        {
            if (!(row[a] > Fix64.Zero)) continue;
            if (best < 0 || Free(a) > Free(best) || (Free(a) == Free(best) && row[a] < row[best])) best = a;
        }

        return best;
    }

    /// <summary>槽的 belief 字段按移动类型的乐观视图:未探索 tile 上未被 believed 实体覆写的
    /// 格(= 地形源)取该类型的假设可跨区域。</summary>
    private CrowdStructuresStore FieldFor(Slot b, int layerIdx)
    {
        if (b.Unknown == null) return b.Field;
        if (b.Layers.TryGetValue(layerIdx, out var cached)) return cached;
        int n = b.Field.CellCount, t = _sim.Config.Hpa.ClusterSize, c = n / t;
        int a = AssumedArea(layerIdx);
        var area = (byte[])b.Field.Area.Clone();
        if (a >= 0)
        {
            var terrain = b.Field.TerrainArea;
            foreach (int tile in b.Unknown)
            {
                int x0 = tile % c * t, y0 = tile / c * t;
                for (int y = y0; y < y0 + t; y++)
                {
                    for (int x = x0; x < x0 + t; x++)
                    {
                        int i = y * n + x;
                        if (area[i] == terrain[i]) area[i] = (byte)a;
                    }
                }
            }
        }

        var layer = CrowdStructuresStore.BuildBeliefField(_sim.Config, b.Field.SnapshotSurface(), EntitiesOf(b.Field));
        layer.ReplaceArea(area);
        b.Layers[layerIdx] = layer;
        return layer;
    }

    private Slot SlotOf(int slot) =>
        _slots.TryGetValue(slot, out var b) ? b : throw new InvalidOperationException($"认知 slot {slot} 未注册");

    /// <summary>分歧矩形:单侧已知的实体足迹格矩形 + 未探索 tile 的行段合并矩形。</summary>
    private List<(int X0, int Y0, int X1, int Y1)> DivergenceRects(Slot b)
    {
        var truth = _sim.Structures!;
        int n = b.Field.CellCount, cs = b.Field.CellSizeCm;
        var res = new List<(int, int, int, int)>();
        void Add(CrowdStructureFootprint fp)
        {
            var r = fp.CellRectOf(cs, n);
            if (r.X1 > r.X0 && r.Y1 > r.Y0) res.Add(r);
        }

        foreach (int id in truth.EntityIds)
        {
            if (!b.Field.TryGetFootprint(id, out _)) Add(truth.FootprintOf(id));
        }

        foreach (int id in b.Field.EntityIds)
        {
            if (!truth.TryGetFootprint(id, out _)) Add(b.Field.FootprintOf(id));
        }

        if (b.Unknown != null)
        {
            int t = _sim.Config.Hpa.ClusterSize, c = n / t;
            for (int k = 0; k < b.Unknown.Count;)
            {
                int s = b.Unknown[k], e = s;
                while (k + 1 < b.Unknown.Count && b.Unknown[k + 1] == e + 1 && (e + 1) % c != 0) e = b.Unknown[++k];
                k++;
                int y = s / c * t;
                res.Add((s % c * t, y, e % c * t + t, y + t));
            }
        }

        return res;
    }

    /// <summary>分歧 tile 集(认知视图 / 摘要用)。</summary>
    public List<int> DivergentTiles(int slot)
    {
        var b = SlotOf(slot);
        int n = b.Field.CellCount, t = _sim.Config.Hpa.ClusterSize, c = n / t;
        var s = new HashSet<int>();
        foreach (var (x0, y0, x1, y1) in DivergenceRects(b))
        {
            for (int y = y0; y < y1; y += t)
            {
                for (int x = x0; x < x1; x += t)
                {
                    s.Add(y / t * c + x / t);
                }
            }
        }

        return s.OrderBy(v => v).ToList();
    }

    /// <summary>变体现在可能与真相上下文不同的 tile 掩码(C²;分歧矩形外扩净空半径,
    /// 对角步要读两个正交邻)。epoch 缓存——真相实体变更后统一失效。</summary>
    private byte[] DivergenceMask(Slot b, int id, NavContext truth)
    {
        if (b.Masks.TryGetValue(id, out var cached) && cached.Epoch == Epoch) return cached.Mask;
        int n = truth.CellCount, t = _sim.Config.Hpa.ClusterSize, c = n / t, r = truth.ClearanceCells;
        var mask = new byte[c * c];
        foreach (var (x0, y0, x1, y1) in DivergenceRects(b))
        {
            int gx0 = Math.Max(0, x0 - r), gy0 = Math.Max(0, y0 - r);
            int gx1 = Math.Min(n, x1 + r), gy1 = Math.Min(n, y1 + r);
            for (int ty = gy0 / t; ty < (gy1 + t - 1) / t; ty++)
            {
                for (int tx = gx0 / t; tx < (gx1 + t - 1) / t; tx++)
                {
                    if (ty >= 0 && ty < c && tx >= 0 && tx < c) mask[ty * c + tx] = 1;
                }
            }
        }

        b.Masks[id] = (Epoch, mask);
        return mask;
    }

    /// <summary>真相流场对变体是否精确:走廊包围盒与分歧掩码不相交才成立——Dijkstra 只读
    /// 走廊格,但弦拉线在两格间测视线,线段可出走廊、永不出包围盒(sharesFlow 移植)。
    /// 这不是纯优化:settled 组保不保留旧流场的判定影响刷新计数,须与参考端同判。</summary>
    public bool SharesFlow(int id, byte[] corridorMask)
    {
        int slot = id / BeliefStride;
        var b = SlotOf(slot);
        var truth = _sim.Navs[id % BeliefStride];
        var mask = DivergenceMask(b, id, truth);
        int c = truth.Hpa!.ClustersPerSide;
        int x0 = c, y0 = c, x1 = -1, y1 = -1;
        for (int t = 0; t < corridorMask.Length; t++)
        {
            if (corridorMask[t] == 0) continue;
            int x = t % c, y = t / c;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (mask[y * c + x] != 0) return false;
            }
        }

        return true;
    }

    /// <summary>全拷贝变体克隆:逐格数组全拷;tile 条目 / 桥面 tile 信息 / HPA 内层数组共享
    /// (增量重烘只替换条目);链接集换自有 CompOut;可达备忘全新。</summary>
    private static NavContext CloneNav(NavContext t, int id)
    {
        NavLinkSet? links = null;
        if (t.Links != null)
        {
            links = new NavLinkSet
            {
                Count = t.Links.Count,
                From = t.Links.From,
                To = t.Links.To,
                Cost = t.Links.Cost,
                TwoWay = t.Links.TwoWay,
                LengthCells = t.Links.LengthCells,
                OutStart = t.Links.OutStart,
                OutList = t.Links.OutList,
                InStart = t.Links.InStart,
                InList = t.Links.InList,
                CompOut = t.Links.CompOut.ToDictionary(kv => kv.Key, kv => new HashSet<int>(kv.Value)),
            };
        }

        return new NavContext
        {
            Id = id,
            LayerIndex = t.LayerIndex,
            ClearanceCells = t.ClearanceCells,
            CellCount = t.CellCount,
            Cost = (Fix64[])t.Cost.Clone(),
            Walk = (byte[])t.Walk.Clone(),
            Passable = (byte[])t.Passable.Clone(),
            Cells = (int[])t.Cells.Clone(),
            Comp = (int[])t.Comp.Clone(),
            CompCount = t.CompCount,
            Links = links,
            UpPass = (byte[])t.UpPass.Clone(),
            Portal = (byte[])t.Portal.Clone(),
            UpArea = (byte[])t.UpArea.Clone(),
            UpCost = (Fix64[])t.UpCost.Clone(),
            MinCost = t.MinCost,
            Tiles = t.Tiles == null ? null : (NavTileEntry?[])t.Tiles.Clone(),
            UpperTiles = t.UpperTiles == null ? null : new Dictionary<int, UpperTileInfo>(t.UpperTiles),
            Hpa = t.Hpa?.CloneForVariant(),
            UpComp = t.UpComp == null ? null : (int[])t.UpComp.Clone(),
            CompCountTotal = t.CompCountTotal,
            ReachOut = t.ReachOut == null
                ? null
                : t.ReachOut.ToDictionary(kv => kv.Key, kv => new HashSet<int>(kv.Value)),
        };
    }

    private static List<(int Id, int TplIndex, CrowdStructureFootprint Fp)> EntitiesOf(CrowdStructuresStore s)
    {
        var res = new List<(int, int, CrowdStructureFootprint)>();
        foreach (int id in s.EntityIds)
        {
            if (s.TryGetFootprint(id, out var fp) && s.TryGetTemplate(id, out int tpl)) res.Add((id, tpl, fp));
        }

        return res;
    }
}
