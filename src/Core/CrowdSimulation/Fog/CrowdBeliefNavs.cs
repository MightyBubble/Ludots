using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>
/// 一个槽一份认知字段。变体号 = 槽号 × 65536 + 真 nav 号。变体克隆真相的逐格数组；
/// tile 条目与 HPA 内层共享，只重烘分歧矩形。Sleep 不丢缓冲。流场缓存键含 nav 号。
/// </summary>
public sealed class CrowdBeliefNavs
{
    public const int BeliefStride = 65536;
    private const int VersionStride = 1 << 20; // 变体版本基,不与真相 / 其他槽相撞

    private readonly CrowdSimSession _sim;
    private readonly Dictionary<int, Slot> _slots = new();
    private readonly HashSet<int> _revealGone = new();
    private readonly List<(int X0, int Y0, int X1, int Y1)> _revealRects = new();
    private BeliefRank[] _beliefRanks = Array.Empty<BeliefRank>();

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
            var field = CrowdStructuresStore.BuildBeliefField(_sim.Config, _sim.Structures!.TerrainSurface(), entities, ref _beliefRanks);
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

    /// <summary>
    /// TryGetMaterialized 自己进 RunExclusive。Get 不加锁；规划侧经 ResolveNavContext 在锁内调用它。
    /// 槽内没有该号时返回 false,不进入 Build。
    /// </summary>
    internal bool TryGetMaterialized(int id, out NavContext nav)
    {
        if (id < BeliefStride)
            throw new ArgumentOutOfRangeException(nameof(id), id, "真相号不走认知注册表。");
        NavContext? found = null;
        bool hit = _sim.Planner!.Service.RunExclusive(() =>
        {
            var slot = SlotOf(id / BeliefStride);
            if (!slot.Navs.TryGetValue(id, out var value)) return false;
            found = value;
            return true;
        });
        nav = found!;
        return hit;
    }

    private NavContext Build(Slot b, NavContext truth, int id, int slot)
    {
        // 结构 op 在阶段 0 的同一互斥域里重烘真相，变体构建不再处理未重烘窗口。
        var v = CloneNav(truth, id);
        var field = FieldFor(b, truth.LayerIndex);
        foreach (var r in DivergenceRects(b))
        {
            CrowdNavRebake.RebakeNav(v, _sim.RebakeSources!, field, r, _sim.NavTileCache!);
        }

        v.Version = slot * VersionStride; // 重烘各 +1 后归槽基
        return v;
    }

    /// <summary>原位揭示：不建新槽，已有变体只重烘给出的 tile。返回值是各变体 ChangedTiles。</summary>
    public Dictionary<int, List<int>> Reveal(int slot, List<int> tiles)
    {
        return _sim.Planner!.Service.RunExclusive(() =>
        {
            var b = SlotOf(slot);
            _revealGone.Clear();
            for (int i = 0; i < tiles.Count; i++) _revealGone.Add(tiles[i]);
            if (b.Unknown != null)
            {
                var unknown = b.Unknown;
                int write = 0;
                for (int i = 0; i < unknown.Count; i++)
                {
                    if (!_revealGone.Contains(unknown[i])) unknown[write++] = unknown[i];
                }

                if (write == 0) b.Unknown = null;
                else if (write != unknown.Count) unknown.RemoveRange(write, unknown.Count - write);
            }

            int navCellsPerSide = b.Field.NavCellsPerSide, navCellsPerTile = _sim.Config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
            _revealRects.Clear();
            for (int i = 0; i < tiles.Count; i++)
            {
                int tile = tiles[i];
                int x = tile % navTilesPerSide * navCellsPerTile;
                int y = tile / navTilesPerSide * navCellsPerTile;
                _revealRects.Add((x, y, x + navCellsPerTile, y + navCellsPerTile));
            }

            foreach (var layer in b.Layers.Values)
            {
                foreach (var (x0, y0, x1, _) in _revealRects)
                {
                    for (int y = y0; y < y0 + navCellsPerTile; y++)
                    {
                        Array.Copy(b.Field.Area, y * navCellsPerSide + x0, layer.Area, y * navCellsPerSide + x0, x1 - x0);
                    }
                }
            }

            b.Masks.Clear();
            var dirty = new Dictionary<int, List<int>>();
            foreach (var (id, v) in b.Navs)
            {
                var field = FieldFor(b, v.LayerIndex);
                foreach (var r in _revealRects)
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
        int navCellsPerSide = b.Field.NavCellsPerSide, navCellsPerTile = _sim.Config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
        int a = AssumedArea(layerIdx);
        var area = (byte[])b.Field.Area.Clone();
        if (a >= 0)
        {
            var terrain = b.Field.TerrainArea;
            foreach (int tile in b.Unknown)
            {
                int x0 = tile % navTilesPerSide * navCellsPerTile, y0 = tile / navTilesPerSide * navCellsPerTile;
                for (int y = y0; y < y0 + navCellsPerTile; y++)
                {
                    for (int x = x0; x < x0 + navCellsPerTile; x++)
                    {
                        int i = y * navCellsPerSide + x;
                        if (area[i] == terrain[i]) area[i] = (byte)a;
                    }
                }
            }
        }

        var layer = CrowdStructuresStore.BuildBeliefField(_sim.Config, b.Field.TerrainSurface(), EntitiesOf(b.Field), ref _beliefRanks);
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
        int navCellsPerSide = b.Field.NavCellsPerSide, navCellSizeCm = b.Field.NavCellSizeCm;
        var res = new List<(int, int, int, int)>();
        void Add(CrowdStructureFootprint fp)
        {
            var r = fp.CellRectOf(navCellSizeCm, navCellsPerSide);
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
            int navCellsPerTile = _sim.Config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
            for (int k = 0; k < b.Unknown.Count;)
            {
                int s = b.Unknown[k], e = s;
                while (k + 1 < b.Unknown.Count && b.Unknown[k + 1] == e + 1 && (e + 1) % navTilesPerSide != 0) e = b.Unknown[++k];
                k++;
                int y = s / navTilesPerSide * navCellsPerTile;
                res.Add((s % navTilesPerSide * navCellsPerTile, y, e % navTilesPerSide * navCellsPerTile + navCellsPerTile, y + navCellsPerTile));
            }
        }

        return res;
    }

    /// <summary>未注册槽（含从未建过的槽 0）差集为空。残影 = 认知挡、真相不挡。</summary>
    public int CopyDivergentCells(int slot, int x0, int y0, int x1, int y1, int step, int[] cells, byte[] ghost)
    {
        if (!_slots.TryGetValue(slot, out var belief)) return 0;
        var truth = _sim.Structures;
        if (truth == null) return 0;
        return DiffCells(belief.Field.Area, belief.Field.Blocked, truth.Area, truth.Blocked, belief.Field.NavCellsPerSide, x0, y0, x1, y1, step, cells, ghost);
    }

    public static int DiffCells(
        byte[] beliefArea,
        byte[] beliefBlocked,
        byte[] truthArea,
        byte[] truthBlocked,
        int navCellsPerSide,
        int x0,
        int y0,
        int x1,
        int y1,
        int step,
        int[] cells,
        byte[] ghost)
    {
        if (step < 1) throw new ArgumentOutOfRangeException(nameof(step));
        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 > navCellsPerSide) x1 = navCellsPerSide;
        if (y1 > navCellsPerSide) y1 = navCellsPerSide;
        int count = 0;
        for (int y = y0; y < y1; y += step)
        {
            int row = y * navCellsPerSide;
            for (int x = x0; x < x1; x += step)
            {
                int i = row + x;
                if (beliefArea[i] == truthArea[i] && beliefBlocked[i] == truthBlocked[i]) continue;
                if (count >= cells.Length || count >= ghost.Length)
                    throw new InvalidOperationException("CrowdSimulationDebugView.json: maxFieldCells 装不下认知分歧格。");
                cells[count] = i;
                ghost[count] = (byte)(beliefBlocked[i] != 0 && truthBlocked[i] == 0 ? 1 : 0);
                count++;
            }
        }

        return count;
    }

    /// <summary>分歧 tile 集(认知视图 / 摘要用)。</summary>
    public List<int> DivergentTiles(int slot)
    {
        var b = SlotOf(slot);
        int navCellsPerSide = b.Field.NavCellsPerSide, navCellsPerTile = _sim.Config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile;
        var s = new HashSet<int>();
        foreach (var (x0, y0, x1, y1) in DivergenceRects(b))
        {
            for (int y = y0; y < y1; y += navCellsPerTile)
            {
                for (int x = x0; x < x1; x += navCellsPerTile)
                {
                    s.Add(y / navCellsPerTile * navTilesPerSide + x / navCellsPerTile);
                }
            }
        }

        var res = new List<int>(s);
        res.Sort(); // 集合成员唯一,键即决胜键
        return res;
    }

    /// <summary>变体现在可能与真相上下文不同的 tile 掩码(C²;分歧矩形外扩净空半径,
    /// 对角步要读两个正交邻)。epoch 缓存——真相实体变更后统一失效。</summary>
    private byte[] DivergenceMask(Slot b, int id, NavContext truth)
    {
        if (b.Masks.TryGetValue(id, out var cached) && cached.Epoch == Epoch) return cached.Mask;
        int navCellsPerSide = truth.NavCellsPerSide, navCellsPerTile = _sim.Config.Hpa.NavCellsPerTile, navTilesPerSide = navCellsPerSide / navCellsPerTile, r = truth.ClearanceNavCells;
        var mask = new byte[navTilesPerSide * navTilesPerSide];
        foreach (var (x0, y0, x1, y1) in DivergenceRects(b))
        {
            int gx0 = Math.Max(0, x0 - r), gy0 = Math.Max(0, y0 - r);
            int gx1 = Math.Min(navCellsPerSide, x1 + r), gy1 = Math.Min(navCellsPerSide, y1 + r);
            for (int ty = gy0 / navCellsPerTile; ty < (gy1 + navCellsPerTile - 1) / navCellsPerTile; ty++)
            {
                for (int tx = gx0 / navCellsPerTile; tx < (gx1 + navCellsPerTile - 1) / navCellsPerTile; tx++)
                {
                    if (ty >= 0 && ty < navTilesPerSide && tx >= 0 && tx < navTilesPerSide) mask[ty * navTilesPerSide + tx] = 1;
                }
            }
        }

        b.Masks[id] = (Epoch, mask);
        return mask;
    }

    /// <summary>真相流场对变体是否精确:走廊包围盒与分歧掩码不相交才成立。Dijkstra 只读走廊格，
    /// 弦拉线可以离开走廊，但不能离开包围盒。这不是纯优化:settled 组保不保留旧流场的判定影响刷新计数。</summary>
    public bool SharesFlow(int id, byte[] corridorMask)
    {
        int slot = id / BeliefStride;
        var b = SlotOf(slot);
        var truth = _sim.Navs[id % BeliefStride];
        var mask = DivergenceMask(b, id, truth);
        int navTilesPerSide = truth.Hpa!.NavTilesPerSide;
        int x0 = navTilesPerSide, y0 = navTilesPerSide, x1 = -1, y1 = -1;
        for (int t = 0; t < corridorMask.Length; t++)
        {
            if (corridorMask[t] == 0) continue;
            int x = t % navTilesPerSide, y = t / navTilesPerSide;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (mask[y * navTilesPerSide + x] != 0) return false;
            }
        }

        return true;
    }

    private static Dictionary<int, HashSet<int>> CopySets(Dictionary<int, HashSet<int>> source)
    {
        var copy = new Dictionary<int, HashSet<int>>(source.Count);
        foreach (var kv in source) copy[kv.Key] = new HashSet<int>(kv.Value);
        return copy;
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
                CompOut = CopySets(t.Links.CompOut),
            };
        }

        return new NavContext
        {
            Id = id,
            LayerIndex = t.LayerIndex,
            ClearanceNavCells = t.ClearanceNavCells,
            NavCellsPerSide = t.NavCellsPerSide,
            Cost = (Fix64[])t.Cost.Clone(),
            SpeedDivisor = (Fix64[])t.SpeedDivisor.Clone(),
            Walk = (byte[])t.Walk.Clone(),
            Passable = (byte[])t.Passable.Clone(),
            Cells = (int[])t.Cells.Clone(),
            CellCount = t.CellCount,
            Comp = (int[])t.Comp.Clone(),
            CompCount = t.CompCount,
            Links = links,
            UpPass = (byte[])t.UpPass.Clone(),
            Portal = (byte[])t.Portal.Clone(),
            UpArea = (byte[])t.UpArea.Clone(),
            UpCost = (Fix64[])t.UpCost.Clone(),
            UpSpeedDivisor = (Fix64[])t.UpSpeedDivisor.Clone(),
            MinCost = t.MinCost,
            Tiles = t.Tiles == null ? null : (NavTileEntry?[])t.Tiles.Clone(),
            UpperTiles = t.UpperTiles == null ? null : new Dictionary<int, UpperTileInfo>(t.UpperTiles),
            Hpa = t.Hpa?.CloneForVariant(),
            UpComp = t.UpComp == null ? null : (int[])t.UpComp.Clone(),
            CompCountTotal = t.CompCountTotal,
            ReachOut = t.ReachOut == null ? null : CopySets(t.ReachOut),
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
