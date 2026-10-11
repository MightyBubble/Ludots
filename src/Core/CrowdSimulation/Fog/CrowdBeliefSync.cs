using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>认知槽记账(sim.belief;fog/beliefSync.js 的状态宿)。</summary>
public sealed class CrowdBeliefState
{
    public sealed class Entry
    {
        public required int Slot { get; init; }
        public required uint Key { get; set; }
        /// <summary>认知实体集(bucket 匹配的精确确认,哈希只预筛)。</summary>
        public required HashSet<int> Ids { get; init; }
        /// <summary>乐观迷雾开启时的探索 tile 集(认知的一部分);null = 全知地形。</summary>
        public HashSet<int>? Tiles { get; set; }
        public bool Dormant { get; set; }
    }

    public required int G { get; init; }
    /// <summary>组 → 槽(0 = 真相)。</summary>
    public int[] Slot { get; init; } = null!;
    public Dictionary<uint, List<Entry>> SlotOfKey { get; } = new();
    public Dictionary<int, Entry> Entries { get; } = new();
    public List<Entry> DormantList { get; } = new();
    public long[] LastKey { get; init; } = null!;
    public long[] LastEnt { get; init; } = null!;
    public long[] LastCommit { get; init; } = null!;
    public long LastTruth { get; set; } = -1;
    public bool LastTerrain { get; set; }
    /// <summary>并组:玩家集变了的组,成员必须换导航。</summary>
    public HashSet<int>? Force { get; set; }
    /// <summary>本 tick 原位揭示的组 → 脏 tile 并集。</summary>
    public Dictionary<int, HashSet<int>>? Revealed { get; set; }
    public int Seq { get; set; }
    public int Switches { get; set; }

    // 同步暂存(切换帧/原位揭示帧复用;调用序不嵌套)
    public readonly HashSet<int> ChangedGroups = new();
    public readonly List<CrowdNavGroupSet.Group> RevealedMoved = new();
    public readonly HashSet<CrowdNavGroupSet.Group> RevealedStale = new();
    public readonly List<CrowdNavGroupSet.Group> RetargetMoved = new();
    public readonly HashSet<CrowdOrder> RetargetTouched = new();
    public readonly HashSet<int> RevealFlat = new();
    internal readonly List<(int Id, int TplIndex, CrowdStructureFootprint Fp)> RegisterRows = new();
    internal StableInt[] RetireRank = Array.Empty<StableInt>();
    internal Entry[] RetireEntries = Array.Empty<Entry>();

    public static CrowdBeliefState Create(int g) => new()
    {
        G = g,
        Slot = new int[g],
        LastKey = Enumerable.Repeat(-1L, g).ToArray(),
        LastEnt = Enumerable.Repeat(-1L, g).ToArray(),
        LastCommit = Enumerable.Repeat(long.MinValue / 4, g).ToArray(),
    };
}

/// <summary>认知同步(fog/beliefSync.js 移植):每 tick 一次,在迷雾更新之后。每个视野组在
/// 它的认知所信的导航上规划:槽 0 = 真相(认知集 == 真值集),其余槽由认知集相同的组共享
/// (32 位 key 只做桶预筛,归属永远经集合相等确认)。新槽在此注册;玩家集变了的组换导航并
/// 像重烘一样反应(retarget);没人用的槽休眠,最旧的按容量淘汰。</summary>
public static class CrowdBeliefSync
{
    public static void Sync(CrowdSimSession sim)
    {
        var fog = sim.Fog!;
        var pl = sim.Planner!;
        var b = sim.Belief!;
        bool truthMoved = b.LastTruth != fog.TruthRev || b.LastTerrain != fog.Terrain;
        b.LastTruth = fog.TruthRev;
        b.LastTerrain = fog.Terrain;
        int every = sim.Config.Fog.RevealTicks, tick = sim.TickCount;
        var changed = b.ChangedGroups;
        changed.Clear();
        b.Revealed = null;
        var force = b.Force;
        b.Force = null;
        for (int v = 0; v < fog.G; v++)
        {
            // 被并掉的组没有玩家——停在真相槽,不占变体
            if (fog.Empty(v))
            {
                if (b.Slot[v] != 0)
                {
                    b.Slot[v] = 0;
                    b.LastKey[v] = -1;
                }

                continue;
            }

            if (force != null && force.Contains(v))
            {
                b.LastKey[v] = fog.Terrain ? fog.Rev[v] : fog.EntRev[v];
                b.LastEnt[v] = fog.EntRev[v];
                b.LastCommit[v] = tick;
                b.Slot[v] = SlotFor(sim, v);
                changed.Add(v);
                continue;
            }

            // BV-6:计数器没动才跳过(哈希碰撞藏不住变更);tile 集只在乐观迷雾开启时计数
            long k = fog.Terrain ? fog.Rev[v] : fog.EntRev[v], ent = fog.EntRev[v];
            if (!truthMoved && b.LastKey[v] == k) continue;
            // 仅探索推进:按 fog.revealTicks 批提交(tick 驱动 → 确定)
            if (!truthMoved && b.LastEnt[v] == ent && tick - b.LastCommit[v] < every) continue;
            b.LastKey[v] = k;
            b.LastEnt[v] = ent;
            b.LastCommit[v] = tick;
            int slot = SlotFor(sim, v);
            if (slot != b.Slot[v])
            {
                b.Slot[v] = slot;
                changed.Add(v);
            }
        }

        if (b.Revealed != null)
        {
            // 原位揭示:nav 对象不动;只有流场踩到真变 tile 的组反应(刷新,不重规划)
            var moved = b.RevealedMoved;
            var stale = b.RevealedStale;
            moved.Clear();
            stale.Clear();
            foreach (var g in sim.Groups.Groups)
            {
                if (g == null) continue;
                int v = fog.GroupOf[g.Player];
                if (!b.Revealed.TryGetValue(v, out var d) || g.Flow == null || changed.Contains(v)) continue;
                bool hit = false;
                foreach (int t in d)
                {
                    if (g.Flow.Mask![t] != 0)
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit)
                {
                    moved.Add(g);
                    stale.Add(g);
                }
            }

            var orders = pl.Retarget(moved, stale);
            if (orders.Count > 0) pl.Plan(orders, sim.TickCount);
        }

        if (changed.Count == 0) return;
        var moved2 = b.RetargetMoved;
        var touched = b.RetargetTouched;
        moved2.Clear();
        touched.Clear();
        foreach (var g in sim.Groups.Groups)
        {
            if (g == null || !changed.Contains(fog.GroupOf[g.Player])) continue;
            g.NavId = sim.NavIdFor(g.Player, g.LayerIdx, g.RIdx);
            moved2.Add(g);
            if (g.OrderId != 0)
            {
                var o = sim.Orders.List.FirstOrDefault(x => x.Id == g.OrderId);
                if (o != null) touched.Add(o);
            }
        }

        pl.RelinkLeaders(touched); // 领队踩新导航,不踩已弃变体
        RetireSlots(sim);
        b.Switches++;
        var orders2 = pl.Retarget(moved2);
        if (orders2.Count > 0) pl.Plan(orders2, sim.TickCount);
    }

    private static bool SameSet(ICollection<int> a, IReadOnlyCollection<int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (int id in b)
        {
            if (!a.Contains(id)) return false;
        }

        return true;
    }

    private static int SlotFor(CrowdSimSession sim, int v)
    {
        var fog = sim.Fog!;
        var b = sim.Belief!;
        uint key = fog.BeliefKey(v);
        var bel = fog.Belief[v];
        if (fog.KnowsTerrain(v) && fog.Key[v] == fog.TruthKey && SameSet(bel.Keys, sim.Structures!.EntityIds)) return 0;
        // 探索 tile 集只在乐观迷雾开启时是认知的一部分
        HashSet<int>? tiles = fog.KnowsTerrain(v) ? null : fog.Tiles[v];
        if (b.SlotOfKey.TryGetValue(key, out var bucket))
        {
            foreach (var e in bucket)
            {
                if (SameSet(bel.Keys, e.Ids) && (tiles != null ? e.Tiles != null && tiles.SetEquals(e.Tiles) : e.Tiles == null))
                {
                    return e.Slot;
                }
            }
        }

        if (tiles != null && RevealInPlace(sim, v, key, bel.Keys, tiles)) return b.Slot[v];
        int slot = ++b.Seq;
        var entry = new CrowdBeliefState.Entry
        {
            Slot = slot,
            Key = key,
            Ids = new HashSet<int>(bel.Keys),
            Tiles = tiles == null ? null : new HashSet<int>(tiles),
        };
        if (bucket != null) bucket.Add(entry);
        else b.SlotOfKey[key] = new List<CrowdBeliefState.Entry> { entry };
        b.Entries[slot] = entry;
        var registerOrder = new List<int>(bel.Keys);
        registerOrder.Sort(); // 实体 id 唯一,键即决胜键
        var rows = b.RegisterRows;
        rows.Clear();
        for (int i = 0; i < registerOrder.Count; i++)
        {
            int id = registerOrder[i];
            var info = bel[id];
            rows.Add((id, info.TplIndex, info.Fp));
        }

        sim.Beliefs!.Register(slot, rows, fog.Unexplored(v));
        return slot;
    }

    /// <summary>仅探索推进(实体认知没变)且该组独占此槽:槽原位演化(BeliefNavs.Reveal),
    /// 不开新槽;delta 按升序,两侧同序同 op。</summary>
    private static bool RevealInPlace(CrowdSimSession sim, int v, uint key, ICollection<int> bel, HashSet<int> tiles)
    {
        var b = sim.Belief!;
        int prev = b.Slot[v];
        if (prev <= 0) return false;
        for (int u = 0; u < b.G; u++)
        {
            if (u != v && b.Slot[u] == prev) return false; // 槽被共享,不能原位改
        }

        if (!b.Entries.TryGetValue(prev, out var e) || e.Tiles == null || !SameSet(bel, e.Ids)) return false;
        foreach (int t in e.Tiles)
        {
            if (!tiles.Contains(t)) return false; // 只会前进(忘记走 forget 命令,不走这条)
        }

        var delta = new List<int>();
        foreach (int t in tiles)
        {
            if (!e.Tiles!.Contains(t)) delta.Add(t);
        }

        delta.Sort(); // tile 集合成员唯一
        var bucket = b.SlotOfKey[e.Key];
        bucket.Remove(e);
        if (bucket.Count == 0) b.SlotOfKey.Remove(e.Key);
        e.Tiles = new HashSet<int>(tiles);
        e.Key = key;
        if (b.SlotOfKey.TryGetValue(key, out var nb)) nb.Add(e);
        else b.SlotOfKey[key] = new List<CrowdBeliefState.Entry> { e };
        var dirty = sim.Planner!.RevealBelief(prev, delta);
        // 池化暂存只做并集中转:Revealed 的条目各组独占(两组同 tick 揭示不能别名同一集)
        var flat = b.RevealFlat;
        flat.Clear();
        foreach (var list in dirty.Values) flat.UnionWith(list);
        (b.Revealed ??= new Dictionary<int, HashSet<int>>())[v] = new HashSet<int>(flat);
        return true;
    }

    /// <summary>没人用的槽休眠(变体保留);超过 slotCacheCapacity 或变体数超
    /// variantCapacity 时最旧先 drop;在用的槽永不 drop。</summary>
    private static void RetireSlots(CrowdSimSession sim)
    {
        var b = sim.Belief!;
        var fc = sim.Config.Fog;
        var used = new HashSet<int>(b.Slot);
        // 唤醒 = 在用即出表(参考端 filter 无条件移除在用项);表内条目恒为休眠标志位,
        // 加 "!Dormant" 守卫会让在用槽永远唤不醒——休眠表虚涨,淘汰会误杀在用槽。
        b.DormantList.RemoveAll(e => used.Contains(e.Slot) ? SetAwake(e) : false);
        // 淘汰次序 = 注册序:槽号单调递增不复用,插入序即槽号升序。
        // 字典删键后复用槽位会打乱枚举序,按槽号排,槽号相同时保本次枚举序。
        int retireCount = b.Entries.Count;
        StableOrder.Ensure(ref b.RetireRank, retireCount);
        StableOrder.Ensure(ref b.RetireEntries, retireCount);
        int retireWrite = 0;
        foreach (var entry in b.Entries.Values)
        {
            b.RetireEntries[retireWrite] = entry;
            b.RetireRank[retireWrite] = new StableInt(entry.Slot, retireWrite);
            retireWrite++;
        }

        StableOrder.Sort(b.RetireRank, retireCount);
        for (int retireIndex = 0; retireIndex < retireCount; retireIndex++)
        {
            var e = b.RetireEntries[b.RetireRank[retireIndex].Seq];
            if (used.Contains(e.Slot) || e.Dormant) continue;
            e.Dormant = true;
            b.DormantList.Add(e);
            sim.Beliefs!.Sleep(e.Slot); // 全拷贝变体:显式免操作(裁定 ①)
        }

        while (b.DormantList.Count > fc.SlotCacheCapacity ||
               (b.DormantList.Count > 0 && sim.Beliefs!.VariantCount() > fc.VariantCapacity))
        {
            var e = b.DormantList[0];
            b.DormantList.RemoveAt(0);
            var bucket = b.SlotOfKey[e.Key];
            bucket.Remove(e);
            if (bucket.Count == 0) b.SlotOfKey.Remove(e.Key);
            b.Entries.Remove(e.Slot);
            sim.Beliefs!.Drop(e.Slot);
        }

        static bool SetAwake(CrowdBeliefState.Entry e)
        {
            e.Dormant = false;
            return true;
        }
    }
}
