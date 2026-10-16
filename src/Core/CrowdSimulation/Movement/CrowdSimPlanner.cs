using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 指令规划器:指令 → 逐组(目标格/可达替代/成员/出发簇)→
/// 路径服务请求(固定生效帧)→ 答复落帧时建领队 + 排阵型槽位 + 挂流场。
/// 本层含重烘反应、认知重定向、原位揭示、流场缓存和走廊扩展。
/// 答复未回则本 tick 停摆；领队路径答复不可达时，非穿越中成员标不可达。
/// </summary>
public sealed class CrowdSimPlanner
{
    private const int GroupScratch = CrowdSimulationLimits.HotPathSpanReserve;
    private const int LeaderPoolCount = 32;
    private const int PathCapacity = CrowdSimulationLimits.HotPathSpanReserve;
    private const int OriginBufferCount = 16;
    private const int StrayRecordCapacity = CrowdSimulationLimits.HotPathSlotReserve;

    private readonly CrowdSimSession _session;
    private readonly PathQueryService _service;
    private readonly List<PendingPlan> _pending = new();
    // 列表逐 tick Clear 后复用。容量不够时这一次仍会分配。
    private readonly List<CrowdNavGroupSet.Group> _staleScratch = new();
    private readonly List<PendingRefresh> _pendingRefreshes = new();
    /// <summary>待刷新流场的组队列(每 tick 预算 maxRefreshesPerTick)。</summary>
    private readonly List<CrowdNavGroupSet.Group> _refreshQueue = new();
    private StableInt[] _liveRank = Array.Empty<StableInt>();
    private StableInt[] _touchRank = Array.Empty<StableInt>();
    private CrowdOrder[] _touchOrders = Array.Empty<CrowdOrder>();
    private int _seq;
    private readonly FlowFieldCache _cache;
    private readonly long[] _lruKeys;
    private readonly byte[] _maskA;
    private readonly StrayRecord?[] _strays = new StrayRecord?[CrowdNavGroupSet.MaxGroups];
    private readonly Stack<StrayRecord> _strayPool;
    private readonly int[] _extendByGroup;
    private readonly int[] _extendDist;
    private readonly Stack<byte[]> _maskCopies;
    private readonly List<PendingRefresh> _maskHold;
    private readonly int[] _retiredIds;
    private readonly Stack<int[]> _originBuffers;
    private readonly int[] _originStamp;
    private int _originGen;
    private int _strayCursor;
    private int _tilesPerSide;
    private bool _loggedFirstPlan;
    private bool _loggedFirstApply;

    // 规划与反应的暂存都在构造时留够。调用不嵌套:Plan / FinishOrder / React 串行,用完即还。
    private readonly int[] _compTally = new int[1 << 16];
    private readonly List<int> _compsInOrder = new(16);
    private readonly Dictionary<int, List<int>> _membersByGroup = new(GroupScratch);
    private readonly Stack<List<int>> _memberLists = new(GroupScratch);
    private readonly List<List<int>> _memberListsInUse = new(GroupScratch);
    private readonly Stack<PendingOrder> _orderPool = new(8);
    private readonly Stack<PendingGroup> _groupPool = new(GroupScratch);
    private readonly Stack<PendingLeader> _leaderReqPool = new(32);
    private readonly Stack<PendingPlan> _planPool = new(4);
    private readonly Stack<PendingRefresh> _refreshPool = new(16);
    private readonly Stack<CrowdLeader> _leaderPool = new(LeaderPoolCount);
    private readonly List<PendingOrder> _pendingOrders = new(8);
    private readonly Dictionary<int, List<int>> _byLayer = new(8);
    private readonly List<int> _layersInOrder = new(8);
    private readonly Stack<List<int>> _layerBuckets = new(8);
    private readonly List<List<int>> _layerBucketsInUse = new(8);
    private readonly Dictionary<int, LiveGroup> _live = new(GroupScratch);
    private readonly List<(Fix64 Width, IReadOnlyList<int> Members)> _blocks = new(8);
    private readonly List<int> _bucketIds = new(16);
    private readonly List<Fix64Vec2> _pathScratch = new(PathCapacity);
    private readonly Dictionary<int, List<int>> _dirtyByNav = new(16);
    private readonly List<List<int>> _dirtyTileLists = new(16);
    private readonly HashSet<int> _underway = new(GroupScratch);
    private readonly Dictionary<int, Fix64> _reachByGroup = new(GroupScratch);
    private readonly List<CrowdOrder> _reactOrders = new(8);
    private readonly HashSet<int> _orderSet = new(8);
    private readonly List<CrowdNavGroupSet.Group> _hit = new(GroupScratch);
    private readonly HashSet<int> _reachWant = new(GroupScratch);
    private readonly HashSet<int> _reachLost = new(GroupScratch);
    private readonly Dictionary<long, bool> _reachPairMemo = new(GroupScratch);
    private readonly Dictionary<int, bool> _nearestOk = new(CrowdSimulationLimits.HotPathSlotReserve);
    private int _nrBest;
    private long _nrBd;
    private int _nrTx;
    private int _nrTy;
    private int _nrFrom;
    private int _nrSide;
    private NavContext _nrNav = null!;
    private byte[] _nrPass = null!;

    /// <summary>路径服务(结构变更与在途路径计算共用同一份导航态,经它的互斥锁串行)。</summary>
    public PathQueryService Service => _service;
    /// <summary>当前待刷新流场的队列长度(重烘报告字段)。</summary>
    public int RefreshQueueCount => _refreshQueue.Count;
    /// <summary>组的流场刷新或走廊扩展排队中或在途:答复落帧前组仍持旧场。</summary>
    public bool Refreshing(CrowdNavGroupSet.Group g) => g.Extending || _refreshQueue.Contains(g) || HasPending(g);

    private struct LiveGroup
    {
        public PendingGroup Pg;
    }

    private sealed class PendingPlan
    {
        public int DueTick;
        public readonly List<PendingOrder> Orders = new(8);
    }

    private sealed class PendingOrder
    {
        public PendingOrder()
        {
            Job = new PathPlanJob(Groups);
        }

        public CrowdOrder Order = null!;
        public readonly List<PendingGroup> Groups = new(8);
        public readonly List<PendingLeader> Leaders = new(4);
        public readonly PathPlanJob Job;
        public int RequestId;
    }

    private sealed class PendingGroup : PathPlanGroup
    {
        public int GroupId;
        public int PlanSeq;
        /// <summary>答复落帧时导航版本或导航号变了，组进入 AfterStalePlan。</summary>
        public int Version;
        /// <summary>规划时钉住的缓存场,整份规划应用完才放开。</summary>
        public FlowField? Pin;
        public bool Ok;
        public bool Stale;
        /// <summary>应用时这一组最后拿到的场,给引用它的 Same 组用。</summary>
        public FlowField? Resolved;
    }

    /// <summary>流场刷新或走廊扩展的在途请求:领队与槽位不动,答复落帧时换场。</summary>
    private sealed class PendingRefresh
    {
        public int RequestId;
        public int DueTick;
        public int NavId;
        public int Version;
        public bool Extend;
        /// <summary>同导航同目标、搭这一次刷新的组。扩展只有一组。</summary>
        public readonly List<Rider> Riders = new(8);
        /// <summary>请求时钉住的缓存场,答复应用完才放开。</summary>
        public FlowField? Cached;
        /// <summary>交给工人的走廊副本。工人退休前不能回到池里。</summary>
        public byte[]? MaskCopy;
        /// <summary>交给工人的走失记录,和走廊副本同时回池。</summary>
        public StrayRecord? Strays;
    }

    /// <summary>一组待扩展的走失格:每个 tile 记第一次碰到的格,按记下的先后排。
    /// 记录跨 tick 累积,下一次扩展整份带走。</summary>
    private sealed class StrayRecord
    {
        public StrayRecord(int tileCount) => Has = new byte[tileCount];

        public CrowdNavGroupSet.Group? Owner;
        public readonly byte[] Has;
        public int[] Cells = new int[StrayRecordCapacity];
        public int[] Tiles = new int[StrayRecordCapacity];
        public int Count;

        public void Add(int tile, int cell)
        {
            if ((uint)tile >= (uint)Has.Length || Has[tile] != 0) return;
            if (Count == Cells.Length)
            {
                Array.Resize(ref Cells, Count * 2);
                Array.Resize(ref Tiles, Count * 2);
            }

            Has[tile] = 1;
            Cells[Count] = cell;
            Tiles[Count] = tile;
            Count++;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++) Has[Tiles[i]] = 0;
            Count = 0;
            Owner = null;
        }
    }

    private struct Rider
    {
        public CrowdNavGroupSet.Group Group;
        public int GroupId;
        /// <summary>请求时的组状态号。之后被重规划或被反应取代就对不上,答复跳过这一组。</summary>
        public int Seq;
        public bool Live;
    }

    private sealed class PendingLeader
    {
        public int StrictestGroupId;
        public int[] BucketGroupIds = new int[8];
        public int BucketCount;
        public int StartCell;
        public int RequestId;
        public CrowdLeader? Prev;
    }

    public CrowdSimPlanner(CrowdSimSession session, PathQueryService service)
    {
        _session = session;
        _service = service;
        _membersByGroup.EnsureCapacity(GroupScratch);
        _live.EnsureCapacity(GroupScratch);
        _underway.EnsureCapacity(GroupScratch);
        _reachByGroup.EnsureCapacity(GroupScratch);
        _reachWant.EnsureCapacity(GroupScratch);
        _reachLost.EnsureCapacity(GroupScratch);
        _reachPairMemo.EnsureCapacity(GroupScratch);
        _nearestOk.EnsureCapacity(CrowdSimulationLimits.HotPathSlotReserve);
        _dirtyByNav.EnsureCapacity(16);
        for (int i = 0; i < GroupScratch; i++)
        {
            _groupPool.Push(new PendingGroup());
            _memberLists.Push(new List<int>(8));
        }

        for (int i = 0; i < LeaderPoolCount; i++) _leaderPool.Push(new CrowdLeader(PathCapacity));
        for (int i = 0; i < 8; i++)
        {
            _orderPool.Push(new PendingOrder());
            _layerBuckets.Push(new List<int>(8));
            _planPool.Push(new PendingPlan());
        }

        for (int i = 0; i < 32; i++) _leaderReqPool.Push(new PendingLeader());
        for (int i = 0; i < 16; i++) _dirtyTileLists.Add(new List<int>(8));
        int refreshInFlight = InFlight(session.Config.Planning.MaxRefreshesPerTick, session.Config.Planning.RefreshLatencyTicks);
        int extendInFlight = InFlight(session.Config.Flowfield.MaxRebuildsPerTick, session.Config.Planning.LatencyTicks);
        int pendingSlots = refreshInFlight + extendInFlight;
        for (int i = 0; i < pendingSlots; i++) _refreshPool.Push(new PendingRefresh());
        _pendingRefreshes.EnsureCapacity(pendingSlots);
        int n2 = session.Config.NavCellsPerSide * session.Config.NavCellsPerSide;
        _cache = new FlowFieldCache(session.Config.Flowfield.CacheCapacity, n2);
        _lruKeys = new long[session.Config.Flowfield.CacheCapacity];
        int perTile = session.Config.Hpa.NavCellsPerTile;
        _tilesPerSide = perTile > 0 ? session.Config.NavCellsPerSide / perTile : 1;
        int tileCount = _tilesPerSide * _tilesPerSide;
        if (tileCount < 1) tileCount = 1;
        _maskA = new byte[tileCount];
        foreach (var pooled in _groupPool) pooled.Corridor = new byte[tileCount];
        _strayPool = new Stack<StrayRecord>(GroupScratch + extendInFlight);
        for (int i = 0; i < GroupScratch + extendInFlight; i++) _strayPool.Push(new StrayRecord(tileCount));
        _extendByGroup = new int[CrowdNavGroupSet.MaxGroups];
        _extendDist = new int[_tilesPerSide + 1];
        _maskCopies = new Stack<byte[]>(pendingSlots);
        for (int i = 0; i < pendingSlots; i++) _maskCopies.Push(new byte[tileCount]);
        _maskHold = new List<PendingRefresh>(pendingSlots);
        _retiredIds = new int[pendingSlots];
        _originStamp = new int[tileCount * 2];
        _originBuffers = new Stack<int[]>(GroupScratch);
        for (int i = 0; i < OriginBufferCount; i++) _originBuffers.Push(new int[tileCount * 4]);
        session.Groups.OnRelease = ReleaseGroup;
    }

    private static int InFlight(int perTick, int latencyTicks)
    {
        int n = perTick * latencyTicks;
        return n < 1 ? 1 : n;
    }

    public int PendingCount => _pending.Count;

    /// <summary>
    /// 拖线线宽已经是厘米。按这一块的成员占比切开，截成整厘米。
    /// 再乘每米厘米数会把行宽放大一百倍。
    /// </summary>
    internal static Fix64 FaceWidthShareCm(Fix64 faceWidthCm, int memberCount, int total)
        => faceWidthCm > Fix64.Zero
            ? Fix64.FromInt((int)faceWidthCm.ToLong()) * memberCount / total
            : Fix64.Zero;

    /// <summary>
    /// 回收组号会留下指向空槽或已改派组的链接。组的 OrderId 才表示链接仍属于这条指令。
    /// </summary>
    private bool TryOrderedGroup(CrowdOrder order, CrowdOrderGroupLink link, out CrowdNavGroupSet.Group group)
    {
        if (_session.Groups.TryGet(link.GroupId, out var found) && found.OrderId == order.Id)
        {
            group = found;
            return true;
        }

        group = null!;
        return false;
    }

    /// <summary>为一批指令起规划:组目标/可达替代/成员标记/每 (tile, 层) 一个起点,再发请求。</summary>
    public void Plan(IReadOnlyList<CrowdOrder> orders, int tick)
    {
        var session = _session;
        var cfg = session.Config;
        int navCellsPerSide = cfg.NavCellsPerSide;
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            for (int li = 0; li < o.Leaders.Count; li++) _leaderPool.Push(o.Leaders[li]);
            o.Leaders.Clear();
            foreach (var link in o.Groups)
            {
                if (!TryOrderedGroup(o, link, out var g)) continue;
                g.Goal = CrowdDeployment.NearestPassable(session.ResolveNavContext(g.NavId), link.GoalCell);
                g.GoalComp = g.Goal >= 0 ? session.ResolveNavContext(g.NavId).Comp[g.Goal] : -2;
                if (g.Flow != null) RecycleFlow(g);
                DropStrays(g);
                g.Leader = null;
                g.Planning = true;
                g.Extending = false;
                g.StateSeq++;
            }
        }

        // 目标不可达 → 换最近的可达格:按组员多数所在连通域判定
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            foreach (var link in o.Groups)
            {
                if (!TryOrderedGroup(o, link, out var g)) continue;
                var compsInOrder = _compsInOrder;
                compsInOrder.Clear();
                var units = session.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
                    if (st.GroupId != g.Id) continue;
                    int cell = CellOfUnit(session, i, st);
                    int c = CompAtLevel(session, g, cell, st.Level);
                    int slot = TallySlot(c);
                    if (_compTally[slot] == 0) compsInOrder.Add(c);
                    _compTally[slot]++;
                }

                int best = -1, bestN = 0;
                foreach (var c in compsInOrder)
                {
                    int cnt = _compTally[TallySlot(c)];
                    if (c >= 0 && cnt > bestN) { best = c; bestN = cnt; }
                }

                for (int ci = 0; ci < compsInOrder.Count; ci++) _compTally[TallySlot(compsInOrder[ci])] = 0;

                if (best < 0 || (g.Goal >= 0 && session.ResolveNavContext(g.NavId).CanReach(best, g.GoalComp))) continue;
                g.Goal = NearestReachable(session.ResolveNavContext(g.NavId), best, link.GoalCell, navCellsPerSide);
                g.GoalComp = g.Goal >= 0 ? session.ResolveNavContext(g.NavId).Comp[g.Goal] : -2;
                link.GoalCell = g.Goal;
            }
        }

        // 逐单位:成员/可达性/状态;穿越链接中的单位从落点出发规划
        var membersByGroup = _membersByGroup;
        ReturnMembers();
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            foreach (var link in o.Groups)
            {
                if (!TryOrderedGroup(o, link, out var g)) continue;
                membersByGroup[g.Id] = RentMembers();
            }
        }

        var unitsAll = session.Units;
        for (int i = 0; i < unitsAll.Count; i++)
        {
            var entity = unitsAll.EntityAt(i);
            var st = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!membersByGroup.TryGetValue(st.GroupId, out var mem)) continue;
            var g = session.Groups.Groups[st.GroupId]!;
            bool traversing = st.State == (byte)CrowdUnitState.Traversing;
            if (!traversing)
            {
                var kin = session.World.Get<CrowdSimulationKinematics>(entity);
                st.Mode = 0;
                kin.Blend = Fix64.Zero;
                kin.StallSeconds = Fix64.Zero;
                session.World.Set(entity, kin);
            }

            int cell = CellOfUnit(session, i, st);
            if (g.Goal < 0 || !session.ResolveNavContext(g.NavId).CanReach(CompAtLevel(session, g, cell, st.Level), g.GoalComp))
            {
                if (!traversing)
                {
                    st.State = (byte)CrowdUnitState.Unreachable;
                    st.Order = 0;
                    session.World.Set(entity, st);
                }

                continue;
            }

            if (!traversing && st.State != (byte)CrowdUnitState.Arrived)
            {
                st.State = (byte)CrowdUnitState.Moving;
            }

            st.Order = (uint)g.OrderId; // 组的指令字段是唯一真相(扫指令簿会被回收组号的陈旧链接命中)
            session.World.Set(entity, st);
            mem.Add(i);
        }

        // 发请求:组流场(各起点走廊并集→目标)+ 每(指令×移动类型)一条领队路径(最严净空导航,联合质心起点)
        var pendingOrders = _pendingOrders;
        pendingOrders.Clear();
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            var po = RentOrder();
            po.Order = o;
            var byLayer = _byLayer;
            var layersInOrder = _layersInOrder;
            ReturnLayers();
            foreach (var link in o.Groups)
            {
                if (!TryOrderedGroup(o, link, out var g)) continue;
                var mem = membersByGroup[g.Id];
                if (mem.Count == 0)
                {
                    g.Planning = false;
                    continue;
                }

                var pg = RentGroup();
                pg.GroupId = g.Id;
                pg.PlanSeq = ++_seq;
                pg.NavId = g.NavId;
                pg.Goal = g.Goal;
                pg.Version = session.ResolveNavContext(g.NavId).Version;
                pg.Origins = RentOrigins();
                pg.OriginCount = CollectOrigins(session, mem, pg.Origins);
                pg.LeaderStart = -1;
                pg.Pin = PinCached(g.NavId, g.Goal);
                pg.CachedMask = pg.Pin?.Mask;
                po.Groups.Add(pg);
                if (!byLayer.TryGetValue(g.LayerIdx, out var bucket))
                {
                    byLayer[g.LayerIdx] = bucket = RentLayer();
                    layersInOrder.Add(g.LayerIdx);
                }

                bucket.Add(g.Id);
            }

            foreach (int layerIdx in layersInOrder)
            {
                var bucket = byLayer[layerIdx];
                int strictestId = bucket[0];
                foreach (int gid in bucket)
                {
                    if (session.ResolveNavContext(session.Groups.Groups[gid]!.NavId).ClearanceNavCells >
                        session.ResolveNavContext(session.Groups.Groups[strictestId]!.NavId).ClearanceNavCells)
                    {
                        strictestId = gid;
                    }
                }

                var strictest = session.Groups.Groups[strictestId]!;
                Fix64 sx = Fix64.Zero, sy = Fix64.Zero;
                int cnt = 0;
                foreach (int gid in bucket)
                {
                    foreach (int i in membersByGroup[gid])
                    {
                        var p = session.World.Get<Components.WorldPositionCm>(unitsAll.EntityAt(i)).Value;
                        sx += p.X;
                        sy += p.Y;
                        cnt++;
                    }
                }

                var nav = session.ResolveNavContext(strictest.NavId);
                int lc = CrowdDeployment.NearestPassable(nav,
                    CrowdSimulationSpace.CellAtStrict(sx / cnt, sy / cnt, cfg.NavCellSizeCm, navCellsPerSide));
                int strictestIndex = PendingGroupIndex(po, strictestId);
                var strictestPg = po.Groups[strictestIndex];
                if (lc < 0 || !nav.CanReach(nav.Comp[lc], strictest.GoalComp)) lc = RepresentativeStart(strictestPg);
                strictestPg.LeaderStart = lc;
                po.Job.AddLeader(strictestIndex);
                CrowdLeader? prev = null;
                foreach (int gid in bucket)
                {
                    if (prev == null && session.Groups.Groups[gid]!.PrevLeader is { } l) prev = l;
                }

                foreach (int gid in bucket) session.Groups.Groups[gid]!.PrevLeader = null;
                var pl = RentLeaderReq();
                pl.StrictestGroupId = strictestId;
                if (bucket.Count > pl.BucketGroupIds.Length) pl.BucketGroupIds = new int[Math.Max(bucket.Count, pl.BucketGroupIds.Length * 2)];
                for (int bi = 0; bi < bucket.Count; bi++) pl.BucketGroupIds[bi] = bucket[bi];
                pl.BucketCount = bucket.Count;
                pl.StartCell = lc;
                pl.Prev = prev;
                po.Leaders.Add(pl);
            }

            if (po.Groups.Count > 0)
            {
                var first = po.Groups[0];
                po.RequestId = _service.Request(new PathQuery(first.NavId, -1, first.Goal, 0) { Plan = po.Job }, tick);
            }

            for (int li = 0; li < po.Leaders.Count; li++)
            {
                var pl = po.Leaders[li];
                var strictest = session.Groups.Groups[pl.StrictestGroupId]!;
                pl.RequestId = _service.Request(new PathQuery(strictest.NavId, pl.StartCell, strictest.Goal, 0), tick);
            }

            ReturnLayers();
            if (po.Groups.Count > 0) pendingOrders.Add(po);
            else RecycleOrder(po);
        }

        ReturnMembers();
        if (pendingOrders.Count > 0)
        {
            var plan = RentPlan();
            plan.DueTick = tick + _session.Config.Planning.LatencyTicks;
            plan.Orders.Clear();
            for (int pi = 0; pi < pendingOrders.Count; pi++) plan.Orders.Add(pendingOrders[pi]);
            _pending.Add(plan);
            if (!_loggedFirstPlan)
            {
                _loggedFirstPlan = true;
                int groups = 0, leaders = 0;
                foreach (var po in pendingOrders) { groups += po.Groups.Count; leaders += po.Leaders.Count; }
                Ludots.Core.Diagnostics.Log.Dbg(in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"CrowdSimulation first plan at tick {tick}: orders={pendingOrders.Count} groups={groups} leaders={leaders}.");
            }
        }
    }

    /// <summary>答复落帧:到点的按请求序应用;有一个没回来,仿真停摆(返回 false)。
    /// block=true:同步等答复(AwaitDue,超时即故障),进入时已经故障也抛。
    /// block=false:故障后一直返回 false,仿真停在这一帧,不改到主线程重算。</summary>
    public bool ApplyDue(int tick, bool block)
    {
        ReclaimRetired();
        if ((_pending.Count > 0 || _pendingRefreshes.Count > 0) && _service.Faulted)
        {
            if (block) throw new PathServiceFaultException(_service.FaultMessage ?? "路径服务故障(原因未记录)");
            return false;
        }

        int k = 0, rk = 0;
        if (!block)
        {
            while (k < _pending.Count && _pending[k].DueTick <= tick)
            {
                var plan = _pending[k];
                foreach (var po in plan.Orders)
                {
                    if (!_service.Ready(po.RequestId)) return false;
                    foreach (var pl in po.Leaders)
                    {
                        if (!_service.Ready(pl.RequestId)) return false;
                    }
                }

                k++;
            }

            while (rk < _pendingRefreshes.Count && _pendingRefreshes[rk].DueTick <= tick)
            {
                if (!_service.Ready(_pendingRefreshes[rk].RequestId)) return false;
                rk++;
            }
        }
        else
        {
            while (k < _pending.Count && _pending[k].DueTick <= tick) k++;
            while (rk < _pendingRefreshes.Count && _pendingRefreshes[rk].DueTick <= tick) rk++;
        }

        // 成员复用:每 tick 必经路径,不逐次 new(AfterStalePlan 只读遍历)
        var stale = _staleScratch;
        stale.Clear();
        for (int i = 0; i < k; i++)
        {
            ApplyPlan(_pending[i], stale);
            RecyclePlan(_pending[i]);
        }

        for (int i = 0; i < rk; i++)
        {
            ApplyRefresh(_pendingRefreshes[i]);
            _refreshPool.Push(_pendingRefreshes[i]);
        }

        if (k > 0) _pending.RemoveRange(0, k);
        if (rk > 0) _pendingRefreshes.RemoveRange(0, rk);
        if (stale.Count > 0)
        {
            // 在途期间导航变了的规划已按答复应用。无场或目标被挡则重规划;否则再看连通域和领队剩余线。
            var orders = AfterStalePlan(stale);
            if (orders.Count > 0) Plan(orders, tick);
        }

        if (k > 0) _session.Telemetry.MarkPlans(k);
        if (k > 0 && !_loggedFirstApply)
        {
            _loggedFirstApply = true;
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation first plan applied at tick {tick}.");
        }

        return true;
    }

    private void ApplyPlan(PendingPlan plan, List<CrowdNavGroupSet.Group> stale)
    {
        foreach (var po in plan.Orders)
        {
            var o = po.Order;
            var live = _live;
            live.Clear();
            _service.ReleaseReply(_service.AwaitDue(po.RequestId));
            foreach (var pg in po.Groups)
            {
                pg.Ok = _session.Groups.TryGet(pg.GroupId, out var g) && g.OrderId == o.Id;
                pg.Stale = false;
                pg.Resolved = null;
                if (!pg.Ok) continue;
                // 陈旧答复(请求后导航重烘过):场先挂上(刷新落地前组用它),组进裁决清单。
                pg.Stale = _session.ResolveNavContext(g.NavId).Version != pg.Version || g.NavId != pg.NavId;
                live[pg.GroupId] = new LiveGroup { Pg = pg };
                if (!pg.Stale) continue;
                stale.Add(g);
                StalePlanApplies++;
            }

            // Same 可能指向组表里排在后面的组,先落自己建的场和缓存场。
            ApplyPlanFlows(po, false, stale);
            ApplyPlanFlows(po, true, stale);
            foreach (var pg in po.Groups)
            {
                if (pg.Ok && _session.Groups.TryGet(pg.GroupId, out var g)) g.Planning = false;
            }

            FinishOrder(po, live);
        }

        foreach (var po in plan.Orders)
        {
            foreach (var pg in po.Groups) UnpinPlanned(pg);
        }
    }

    /// <summary>
    /// 新建场:陈旧组挂上但不进缓存,免得旧代价被后来的命中复用;
    /// 组已经不在这条指令上时,导航没变的场照样进缓存,变了就回池。
    /// 组没拿到场(它引用的组的场回池了)就进裁决清单重规划。
    /// </summary>
    private void ApplyPlanFlows(PendingOrder po, bool same, List<CrowdNavGroupSet.Group> stale)
    {
        for (int j = 0; j < po.Groups.Count; j++)
        {
            var pg = po.Groups[j];
            if ((pg.Reply == PathPlanReply.Same) != same) continue;
            FlowField? flow;
            bool admit = false;
            switch (pg.Reply)
            {
                case PathPlanReply.New:
                    flow = pg.Flow;
                    pg.Flow = null;
                    if (!pg.Stale)
                    {
                        if (!pg.Ok && !PlannedNavUnchanged(pg))
                        {
                            RecycleFlow(flow!);
                            continue;
                        }

                        admit = true;
                    }

                    break;
                case PathPlanReply.Cached:
                    flow = pg.Pin;
                    PlanCachedReplies++;
                    break;
                case PathPlanReply.Same:
                    flow = po.Groups[pg.SameAs].Resolved;
                    PlanSameReplies++;
                    break;
                default:
                    throw new InvalidOperationException($"指令 {po.Order.Id} 的组 {pg.GroupId} 没有规划答复。");
            }

            pg.Resolved = flow;
            int revision = pg.Reply == PathPlanReply.New ? po.RequestId : 0;
            if (!pg.Ok)
            {
                if (!admit) continue;
                flow!.BuildRevision = revision;
                _cache.Admit(flow);
                continue;
            }

            if (!_session.Groups.TryGet(pg.GroupId, out var g)) continue;
            if (flow != null) SetFlow(g, flow, admit, revision);
            else if (g.Flow == null) stale.Add(g);
        }
    }

    private bool PlannedNavUnchanged(PendingGroup pg)
    {
        if (_session.Groups.TryGet(pg.GroupId, out var g))
            return g.NavId == pg.NavId && _session.ResolveNavContext(g.NavId).Version == pg.Version;
        return _session.TryGetMaterializedNav(pg.NavId, out var nav) && nav.Version == pg.Version;
    }

    /// <summary>
    /// 钉住 (导航, 目标) 的缓存场,按键取,不看覆盖。认知变体没有自己的场时,
    /// 真相场的走廊不碰任何分歧 tile 就借真相场。
    /// </summary>
    private FlowField? PinCached(int navId, int goal)
    {
        var flow = _cache.Get(_cache.KeyOf(navId, goal));
        if (flow == null && navId >= CrowdBeliefNavs.BeliefStride)
        {
            var truth = _cache.Get(_cache.KeyOf(navId % CrowdBeliefNavs.BeliefStride, goal));
            if (truth != null && _session.Beliefs!.SharesFlow(navId, truth.Mask!))
            {
                flow = truth;
                BeliefFlowShares++;
            }
        }

        if (flow != null) flow.Refs++;
        return flow;
    }

    private void UnpinPlanned(PendingGroup pg)
    {
        var pin = pg.Pin;
        pg.Pin = null;
        pg.CachedMask = null;
        pg.Resolved = null;
        if (pin == null) return;
        if (pin.Refs > 0) pin.Refs--;
        _cache.Release(pin);
    }

    /// <summary>领队 + 阵型槽位 + 到达足印(质心与朝向继承按落帧时刻的成员位置)。</summary>
    private void FinishOrder(PendingOrder po, Dictionary<int, LiveGroup> live)
    {
        var o = po.Order;
        var session = _session;
        var cfg = session.Config;
        var fc = cfg.Formation;
        int navCellSizeCm = cfg.NavCellSizeCm;
        Fix64 navCellCm = Fix64.FromInt(navCellSizeCm);

        // 成员名单按落帧时刻重收(请求后单位可能移动)
        var membersByGroup = _membersByGroup;
        ReturnMembers();
        var units = session.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
            if (st.Order == o.Id && st.GroupId >= 0 && live.ContainsKey(st.GroupId))
            {
                if (!membersByGroup.TryGetValue(st.GroupId, out var m))
                {
                    m = RentMembers();
                    membersByGroup[st.GroupId] = m;
                }

                m.Add(i);
            }
        }

        Fix64 area = Fix64.Zero;
        int total = 0;
        // 顺序无关:Fix64 精确加法/计数,交换律成立。升序只是把遍历序写成合同。
        int liveCount = live.Count;
        StableOrder.Ensure(ref _liveRank, liveCount);
        int liveWrite = 0;
        foreach (int gid in live.Keys)
        {
            _liveRank[liveWrite] = new StableInt(gid, liveWrite);
            liveWrite++;
        }

        StableOrder.Sort(_liveRank, liveCount);
        for (int liveIndex = 0; liveIndex < liveCount; liveIndex++)
        {
            int gid = _liveRank[liveIndex].Key;
            if (!membersByGroup.TryGetValue(gid, out var mem)) continue;
            foreach (var i in mem)
            {
                Fix64 d = units.PersonalRadiusCmAt(i) * 2 * fc.SpacingScale;
                area += d * d;
            }

            total += mem.Count;
        }

        if (total == 0)
        {
            ReturnMembers();
            return;
        }

        var shape = CrowdFormations.ShapeOf(cfg.Formations, o.ShapeId);
        var blocks = _blocks;
        blocks.Clear();
        Fix64 ext = Fix64.Zero;
        foreach (var pl in po.Leaders)
        {
            var bucket = _bucketIds;
            bucket.Clear();
            for (int bi = 0; bi < pl.BucketCount; bi++)
            {
                int gid = pl.BucketGroupIds[bi];
                if (live.ContainsKey(gid)) bucket.Add(gid);
            }

            if (bucket.Count == 0) continue;
            var leaderResult = _service.AwaitDue(pl.RequestId);
            // 请求发出后该组可能已被抽空。领队改挂桶里仍活着、净空最严的组。
            if (!session.Groups.TryGet(pl.StrictestGroupId, out var strictest) || !live.ContainsKey(pl.StrictestGroupId))
            {
                strictest = null!;
                int bestClearance = -1;
                for (int bi = 0; bi < bucket.Count; bi++)
                {
                    if (!session.Groups.TryGet(bucket[bi], out var candidate) || !live.ContainsKey(candidate.Id)) continue;
                    int clearance = session.ResolveNavContext(candidate.NavId).ClearanceNavCells;
                    if (clearance > bestClearance)
                    {
                        bestClearance = clearance;
                        strictest = candidate;
                    }
                }

                if (bestClearance < 0)
                {
                    Recycle(leaderResult);
                    continue;
                }
            }

            if (!leaderResult.Reachable || leaderResult.Points == null)
            {
                // 非穿越中成员标不可达并清掉指令号;穿越链接中的单位保持穿越。组目标清成 -1。
                foreach (int gid in bucket)
                {
                    if (!session.Groups.TryGet(gid, out var g)) continue;
                    foreach (int i in membersByGroup[gid])
                    {
                        var e = units.EntityAt(i);
                        var st = session.World.Get<CrowdSimulationUnitState>(e);
                        if (st.State != (byte)CrowdUnitState.Traversing)
                        {
                            st.State = (byte)CrowdUnitState.Unreachable;
                            st.Order = 0;
                            session.World.Set(e, st);
                        }
                    }

                    RecycleFlow(g);
                    g.Goal = -1;
                    g.GoalComp = -2;
                    DropStrays(g);
                }

                Recycle(leaderResult);
                continue;
            }

            Fix64 pace = Fix64.MaxValue;
            int paced = 0;
            foreach (int gid in bucket)
            {
                foreach (int i in membersByGroup[gid])
                {
                    Fix64 speed = session.World.Get<CrowdSimulationAgent>(units.EntityAt(i)).SpeedCmPerSec;
                    if (speed < pace)
                    {
                        pace = speed;
                    }

                    paced++;
                }
            }

            if (paced == 0)
            {
                throw new InvalidOperationException("领队路径没有成员，无法取模板速度。");
            }

            var leader = RentLeader();
            leader.Reset(
                ToVec2Path(leaderResult, navCellSizeCm), strictest.LayerIdx, session.ResolveNavContext(strictest.NavId),
                o.Mode == CrowdOrderMode.Preserve, o.Face, fc.LeaderLookAheadNavCells * navCellCm, pace);
            Recycle(leaderResult);

            // 质心取落帧时刻;起点格是请求时刻的,接受其 8 邻域
            Fix64 cx = Fix64.Zero, cy = Fix64.Zero;
            int cn = 0;
            foreach (int gid in bucket)
            {
                foreach (int i in membersByGroup[gid])
                {
                    var p = session.World.Get<Components.WorldPositionCm>(units.EntityAt(i)).Value;
                    cx += p.X;
                    cy += p.Y;
                    cn++;
                }
            }

            if (cn > 0)
            {
                cx /= cn;
                cy /= cn;
                int c = CrowdSimulationSpace.CellAtStrict(cx, cy, navCellSizeCm, cfg.NavCellsPerSide);
                if (c >= 0 && strictestPassable(session.ResolveNavContext(strictest.NavId), c) &&
                    Math.Abs(c % cfg.NavCellsPerSide - pl.StartCell % cfg.NavCellsPerSide) <= 1 &&
                    Math.Abs(c / cfg.NavCellsPerSide - pl.StartCell / cfg.NavCellsPerSide) <= 1)
                {
                    leader.SetStart(cx, cy);
                }
            }

            // 反转继承 mirror;同向继承朝向(近似同向才继,真转弯立刻按新朝向排槽)
            if (pl.Prev is { } prev)
            {
                leader.InheritFrom(prev, fc.MirrorFlipDot, fc.HeadingInheritDot);
            }

            leader.RecordWalls(leader.Nav, navCellSizeCm, cfg.NavCellsPerSide);
            o.Leaders.Add(leader);
            var mem = RentMembers();
            foreach (int gid in bucket)
            {
                if (!session.Groups.TryGet(gid, out var g)) continue;
                g.Leader = leader;
                g.LosAll = true; // 首个意图趟全员重查槽位视线
                foreach (int i in membersByGroup[gid])
                {
                    mem.Add(i);
                    var e = units.EntityAt(i);
                    var st = session.World.Get<CrowdSimulationUnitState>(e);
                    if (st.State == (byte)CrowdUnitState.Arrived)
                    {
                        st.State = (byte)CrowdUnitState.Moving; // 路径落地才离站
                        session.World.Set(e, st);
                    }
                }
            }

            if (mem.Count == 0) continue;
            if (o.Mode == CrowdOrderMode.Converge)
            {
                foreach (int i in mem)
                {
                    var e = units.EntityAt(i);
                    var kin = session.World.Get<CrowdSimulationKinematics>(e);
                    kin.SlotOffsetCm = Fix64Vec2.Zero;
                    session.World.Set(e, kin);
                }
            }
            else if (o.Mode == CrowdOrderMode.Preserve)
            {
                ext = Fix64.Max(ext, CrowdFormations.RelativeSlots(session, mem, leader.Hx, leader.Hy));
            }
            else
            {
                Fix64 widthShare = FaceWidthShareCm(o.FaceWidthCm, mem.Count, total);
                blocks.Add((CrowdFormations.AssignSlots(session, mem, leader.Hx, leader.Hy, fc.SpacingScale, shape, widthShare, leader.Mirror), mem));
            }
        }

        if (blocks.Count > 1) CrowdFormations.LayoutSideBySide(session, blocks);
        // reachCells:聚拢按触碰判到达只认这个半径。槽位视线门槛是 max(slotSightNavCells, reachCells),它把门槛抬高。
        if (o.Mode == CrowdOrderMode.Converge)
        {
            o.ReachCells = (int)Fix64.Max(fc.MinClumpNavCells, (fc.ClumpSlack * Fix64Math.Sqrt(area / Fix64.Pi)) / navCellCm).ToLong();
        }
        else if (o.Mode == CrowdOrderMode.Preserve)
        {
            o.ReachCells = (int)(ext / navCellCm + fc.SettleRadiusNavCells).ToLong();
        }
        else
        {
            Fix64 w2 = Fix64.Zero;
            foreach (var b in blocks) w2 += b.Width;
            o.ReachCells = (int)((w2 / 2 + Fix64Math.Sqrt(area)) / navCellCm + fc.SettleRadiusNavCells).ToLong();
        }

        ReturnMembers();
    }

    private static bool strictestPassable(NavContext nav, int cell) => nav.Passable[cell] != 0;

    /// <summary>PathResult.Points 是格系坐标(格心 +0.5);领队走世界厘米,出服务边界处 ×navCellSizeCm 换算。只读活前缀。</summary>
    private List<Fix64Vec2> ToVec2Path(PathResult result, int navCellSizeCm)
    {
        Fix64 navCellCm = Fix64.FromInt(navCellSizeCm);
        var path = _pathScratch;
        path.Clear();
        var points = result.Points;
        int n = result.PointCount;
        if (points == null) return path;
        for (int i = 0; i + 1 < n; i += 2) path.Add(new Fix64Vec2(points[i] * navCellCm, points[i + 1] * navCellCm));
        // 起终点落在同一点时漏斗只留下起点。领队用一段零长度走到同一点，这一步报到达。
        if (path.Count == 1) path.Add(path[0]);
        return path;
    }

    private void Recycle(PathResult result)
    {
        if (result.Flow is { } f)
        {
            result.Flow = null;
            RecycleFlow(f);
        }

        _service.ReleaseReply(result);
    }

    private void RecycleFlow(CrowdNavGroupSet.Group g) => SetFlow(g, null, false, 0);

    private void RecycleFlow(FlowField f) => _cache.Release(f);

    /// <summary>
    /// 被重规划或被反应取代的组跳过。还在的组清掉刷新标记:请求后导航变了就重新排刷新,
    /// 否则挂上答复的场。新建场只有在至少一组导航没变时才进缓存,否则回池。
    /// </summary>
    private void ApplyRefresh(PendingRefresh pr)
    {
        var result = _service.AwaitDue(pr.RequestId);
        ReleaseMask(pr);
        bool own = false;
        var riders = pr.Riders;
        for (int i = 0; i < riders.Count; i++)
        {
            var rider = riders[i];
            rider.Live = _session.Groups.TryGet(rider.GroupId, out var live) && live == rider.Group && live.StateSeq == rider.Seq;
            riders[i] = rider;
            if (rider.Live && !own) own = RefreshNavUnchanged(pr, rider.Group);
        }

        var flow = pr.Cached;
        if (result.Cached) RefreshCachedReplies++;
        else
        {
            flow = result.Flow;
            result.Flow = null;
            if (own && flow != null)
            {
                flow.BuildRevision = pr.RequestId;
                _cache.Admit(flow);
            }
            else
            {
                if (flow != null) RecycleFlow(flow);
                flow = null;
            }
        }

        _service.ReleaseReply(result);
        for (int i = 0; i < riders.Count; i++)
        {
            if (!riders[i].Live) continue;
            var g = riders[i].Group;
            g.Extending = false;
            if (!RefreshNavUnchanged(pr, g)) RefreshFlow(g);
            else if (flow != null) SetFlow(g, flow, false, 0);
        }

        Unpin(pr);
        riders.Clear();
    }

    private bool RefreshNavUnchanged(PendingRefresh pr, CrowdNavGroupSet.Group g) =>
        g.NavId == pr.NavId && _session.ResolveNavContext(g.NavId).Version == pr.Version;

    /// <summary>队伍反应:对看到本次变更的组逐个判定——目标被挡 → 重规划指令;
    /// 行进中且变更落在剩余路线上 → 领队线/成员可达被切断则重规划,否则同走廊刷新流场;
    /// 驻扎组不动槽位。返回需要重规划的指令清单。</summary>
    public List<CrowdOrder> ReactRebake(IReadOnlyList<CrowdNavRebakeContextResult> dirty)
    {
        var session = _session;
        var dirtyByNav = _dirtyByNav;
        for (int i = 0; i < _dirtyTileLists.Count; i++) _dirtyTileLists[i].Clear();
        dirtyByNav.Clear();
        int tileList = 0;
        for (int ri = 0; ri < dirty.Count; ri++)
        {
            var r = dirty[ri];
            if (r.DirtyTiles.Count == 0) continue;
            List<int> copy;
            if (tileList < _dirtyTileLists.Count) copy = _dirtyTileLists[tileList];
            else
            {
                copy = new List<int>(r.DirtyTiles.Count);
                _dirtyTileLists.Add(copy);
            }

            tileList++;
            for (int ti = 0; ti < r.DirtyTiles.Count; ti++) copy.Add(r.DirtyTiles[ti]);
            dirtyByNav[r.NavId] = copy;
        }

        foreach (var pair in dirtyByNav) _cache.EvictTouching(pair.Key, pair.Value);

        int navCellsPerSide = session.Config.NavCellsPerSide;
        var units = session.Units;
        var underway = _underway;
        var reach = _reachByGroup;
        underway.Clear();
        reach.Clear();
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            var st = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!session.Groups.TryGet(st.GroupId, out var g0)) continue;
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Traversing) underway.Add(g0.Id);
            if (g0.Flow == null) continue;
            int cell = CellOfUnit(session, i, st);
            var len = g0.Flow.Len[(st.Level != 0 ? navCellsPerSide * navCellsPerSide : 0) + cell];
            if (!reach.TryGetValue(g0.Id, out var cur) || len > cur) reach[g0.Id] = len;
        }

        var orders = _reactOrders;
        var orderSet = _orderSet;
        var hit = _hit;
        orders.Clear();
        orderSet.Clear();
        hit.Clear();
        var groupList = session.Groups.Groups;
        for (int gi = 0; gi < groupList.Count; gi++)
        {
            var g = groupList[gi];
            if (g == null || g.Count == 0 || g.OrderId == 0) continue;
            if (!dirtyByNav.TryGetValue(g.NavId, out var tiles) || tiles.Count == 0) continue;
            Supersede(g);
            if (g.Planning) continue; // 在途规划由落帧裁决处理
            if (g.Goal < 0) continue;
            var nav = session.ResolveNavContext(g.NavId);
            if (nav.Passable[g.Goal] == 0)
            {
                AddOrder(session, orders, orderSet, g);
                continue;
            }

            g.GoalComp = nav.Comp[g.Goal]; // 连通域每次重烘都重标号
            if (!underway.Contains(g.Id) || g.Flow == null || !OnRemainingRoute(session, g, tiles, reach)) continue;
            hit.Add(g);
        }

        React(session, hit, orders, orderSet);
        return orders;
    }

    private void React(
        CrowdSimSession session, List<CrowdNavGroupSet.Group> hit, List<CrowdOrder> orders, HashSet<int> orderSet)
    {
        var lost = ReachLost(session, hit);
        foreach (var g in hit)
        {
            var nav = session.ResolveNavContext(g.NavId);
            if (lost.Contains(g.Id) || LeaderPathBlocked(g.Leader, nav, session)) AddOrder(session, orders, orderSet, g);
            else RefreshFlow(g);
        }
    }

    /// <summary>行进中成员(脚下格 / 链接落点格)的连通域不再可达目标 = 该组的路被切断。</summary>
    private HashSet<int> ReachLost(CrowdSimSession session, List<CrowdNavGroupSet.Group> hit)
    {
        var want = _reachWant;
        var lost = _reachLost;
        var memo = _reachPairMemo;
        want.Clear();
        lost.Clear();
        memo.Clear();
        foreach (var g in hit) want.Add(g.Id);
        var units = session.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            var st = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!want.Contains(st.GroupId) || lost.Contains(st.GroupId)) continue;
            if (st.State != (byte)CrowdUnitState.Moving && st.State != (byte)CrowdUnitState.Traversing) continue;
            if (!session.Groups.TryGet(st.GroupId, out var g)) continue;
            var nav = session.ResolveNavContext(g.NavId);
            int cell = CellOfUnit(session, i, st);
            int c = nav.CompAt(cell, st.Level);
            if (c < 0) continue;
            long key = ((long)st.GroupId << 32) ^ (uint)c;
            if (!memo.TryGetValue(key, out var ok)) memo[key] = ok = nav.CanReach(c, g.GoalComp);
            if (!ok) lost.Add(st.GroupId);
        }

        return lost;
    }

    /// <summary>领队剩余路线是否被挡:沿线四角探边的坏格带。上层托线由 upPass 覆盖;规划时已记下的基线墙格不算。</summary>
    internal static bool LeaderPathBlocked(CrowdLeader? leader, NavContext nav, CrowdSimSession session)
    {
        if (leader == null || leader.Done) return false;
        int navCellSizeCm = session.Config.NavCellSizeCm, navCellsPerSide = session.Config.NavCellsPerSide;
        Fix64 step = Fix64.FromInt(navCellSizeCm) / 2, e = Fix64.FromInt(navCellSizeCm) / 4;
        var pass = nav.Passable;
        var up = nav.UpPass;
        Fix64 ax = leader.XCm, ay = leader.YCm;
        for (int k = leader.Seg; k < leader.PathCount; k++)
        {
            var point = leader.PathAt(k);
            Fix64 bx = point.X, by = point.Y;
            int m = (int)Fix64.Ceiling(CrowdFix.Hypot(bx - ax, by - ay) / step).ToLong();
            for (int s = 1; s <= m; s++)
            {
                if (LeaderBlockedAt(leader, pass, up, ax + (bx - ax) * s / m, ay + (by - ay) * s / m, e, navCellSizeCm, navCellsPerSide)) return true;
            }

            ax = bx;
            ay = by;
        }

        return false;
    }

    private static bool LeaderBlockedAt(CrowdLeader leader, byte[] pass, byte[] up, Fix64 x, Fix64 y, Fix64 e, int navCellSizeCm, int navCellsPerSide)
    {
        return LeaderCellBad(leader, pass, up, CrowdSimulationSpace.CellAt(x - e, y - e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(leader, pass, up, CrowdSimulationSpace.CellAt(x + e, y - e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(leader, pass, up, CrowdSimulationSpace.CellAt(x - e, y + e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(leader, pass, up, CrowdSimulationSpace.CellAt(x + e, y + e, navCellSizeCm, navCellsPerSide));
    }

    private static bool LeaderCellBad(CrowdLeader leader, byte[] pass, byte[] up, int c) => pass[c] == 0 && up[c] == 0 && !leader.IsWall(c);

    /// <summary>变更是否落在组的剩余路线上:走廊掩码触到脏 tile,
    /// 且脏 tile 内存在某成员剩余路线长度可达的格——只用掩码会把身后的变更也算进来。</summary>
    private static bool OnRemainingRoute(
        CrowdSimSession session,
        CrowdNavGroupSet.Group g,
        List<int> tiles,
        Dictionary<int, Fix64> reach)
    {
        var f = g.Flow!;
        int navCellsPerSide = session.Config.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        var nav = session.ResolveNavContext(g.NavId);
        int navCellsPerTile = nav.Hpa!.NavCellsPerTile, navTilesPerSide = nav.Hpa.NavTilesPerSide;
        if (!reach.TryGetValue(g.Id, out var r)) return false;
        foreach (int t in tiles)
        {
            if (f.Mask![t] == 0) continue;
            int x0 = t % navTilesPerSide * navCellsPerTile, y0 = t / navTilesPerSide * navCellsPerTile;
            int x1 = Math.Min(navCellsPerSide, x0 + navCellsPerTile), y1 = Math.Min(navCellsPerSide, y0 + navCellsPerTile);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (f.Len[y * navCellsPerSide + x] <= r || f.Len[n2 + y * navCellsPerSide + x] <= r) return true;
                }
            }
        }

        return false;
    }

    /// <summary>已提交给路径服务的流场重建次数。clearCaches 入队后,由 ProcessRefreshes 在预算内提交。</summary>
    public int FlowRefreshSubmits { get; private set; }
    /// <summary>已提交的走廊扩展次数。</summary>
    public int ExtendSubmits { get; private set; }
    /// <summary>带着走失记录进入扩展轮转的组·tick。</summary>
    internal int StrayGroupTicks { get; private set; }
    /// <summary>轮到时刷新或扩展还在途,记录留到答复落帧后。</summary>
    internal int StraySkipInFlight { get; private set; }
    /// <summary>扩展带走的走失 tile 总数。</summary>
    internal int ExtendStrayTiles { get; private set; }
    /// <summary>一次扩展里最远的走失 tile 落在一圈垫层内。</summary>
    internal int ExtendWithinPad { get; private set; }
    /// <summary>一次扩展里最远的走失 tile 比一圈垫层更远。</summary>
    internal int ExtendBeyondPad { get; private set; }
    internal int[] ExtendByGroup => _extendByGroup;
    internal int[] ExtendDistance => _extendDist;
    internal int StalePlanApplies { get; private set; }
    /// <summary>落帧的刷新 / 扩展答复里,沿用请求时钉住的缓存场的次数。</summary>
    internal int RefreshCachedReplies { get; private set; }
    /// <summary>搭别组刷新的组数(每次刷新的第一组不算)。</summary>
    internal int RefreshRiders { get; private set; }
    /// <summary>落帧的组规划答复里,沿用规划时钉住的缓存场的组数。</summary>
    internal int PlanCachedReplies { get; private set; }
    /// <summary>落帧的组规划答复里,共用同指令另一组新建场的组数。</summary>
    internal int PlanSameReplies { get; private set; }
    /// <summary>认知变体规划时借用真相缓存场的次数。</summary>
    internal int BeliefFlowShares { get; private set; }
    public int FlowCacheHits => _cache.Hits;
    public int FlowCacheMisses => _cache.Misses;
    public int FlowCacheEvicts => _cache.Evicts;
    public int FlowCacheCount => _cache.Count;
    internal FlowFieldCache FlowCache => _cache;
    internal string? FlowCacheInvalidReason() => _cache.InvalidReason();
    internal bool FlowCacheContains(FlowField field) => _cache.Contains(field);

    /// <summary>有流场的组重新排队。先丢掉结果缓存,旧场留到答复替换。</summary>
    public int InvalidateFlows()
    {
        _cache.DropAll();
        int queued = 0;
        var groups = _session.Groups.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group?.Flow == null) continue;
            RefreshFlow(group);
            queued++;
        }

        return queued;
    }

    /// <summary>同走廊流场刷新排队(不在队列才入队;预算在 ProcessRefreshes 扣)。</summary>
    public void RefreshFlow(CrowdNavGroupSet.Group g)
    {
        if (!_refreshQueue.Contains(g)) _refreshQueue.Add(g);
    }

    /// <summary>每 tick 先消化流场刷新(生效帧 tick + refreshLatencyTicks),再消化走廊扩展(tick + latencyTicks)。
    /// 队里同导航同目标的组搭同一次刷新,走廊取并集。驻扎组是否沿用真相流场在 Retarget,由 SharesFlow 判定。</summary>
    public void ProcessRefreshes(int tick)
    {
        ReclaimRetired();
        int budget = _session.Config.Planning.MaxRefreshesPerTick;
        int served = 0;
        var queue = _refreshQueue;
        while (queue.Count > 0 && served < budget)
        {
            var g = queue[0];
            queue.RemoveAt(0);
            if (!FitsRefresh(g)) continue;
            var pr = RentRefresh();
            AddRider(pr, g);
            for (int k = queue.Count - 1; k >= 0; k--)
            {
                var h = queue[k];
                if (h.NavId != g.NavId || h.Goal != g.Goal || !FitsRefresh(h)) continue;
                AddRider(pr, h);
                queue.RemoveAt(k);
                RefreshRiders++;
            }

            var mask = RentMask(g.Flow!.Mask!);
            for (int r = 1; r < pr.Riders.Count; r++)
            {
                var other = pr.Riders[r].Group.Flow!.Mask!;
                for (int t = 0; t < mask.Length; t++) mask[t] |= other[t];
            }

            Submit(pr, g, mask, null, tick, false);
            FlowRefreshSubmits++;
            served++;
        }

        ProcessExtensions(tick);
    }

    /// <summary>陈旧答复的组:无场或目标被挡则重规划;否则再看成员连通域和领队剩余线,切断才重规划,否则刷新流场。</summary>
    private List<CrowdOrder> AfterStalePlan(List<CrowdNavGroupSet.Group> stale)
    {
        var orders = _reactOrders;
        var orderSet = _orderSet;
        var hit = _hit;
        orders.Clear();
        orderSet.Clear();
        hit.Clear();
        foreach (var g in stale)
        {
            if (!_session.Groups.TryGet(g.Id, out var live) || live != g || g.OrderId == 0 || g.Planning || g.Goal < 0) continue;
            if (g.Flow == null || _session.ResolveNavContext(g.NavId).Passable[g.Goal] == 0)
            {
                AddOrder(_session, orders, orderSet, g);
                continue;
            }

            g.GoalComp = _session.ResolveNavContext(g.NavId).Comp[g.Goal];
            hit.Add(g);
        }

        React(_session, hit, orders, orderSet);
        return orders;
    }

    private static void AddOrder(
        CrowdSimSession session, List<CrowdOrder> orders, HashSet<int> seen, CrowdNavGroupSet.Group g)
    {
        var orderList = session.Orders.List;
        for (int oi = 0; oi < orderList.Count; oi++)
        {
            var o = orderList[oi];
            if (o.Id != g.OrderId) continue;
            if (seen.Add(o.Id)) orders.Add(o);
            return;
        }
    }

    /// <summary>
    /// 按成员顺序收起点:每个 (tile, 层) 留第一个成员的格,穿越链接中的成员用落点格。不设上限,
    /// 一组最多 tile 数 × 2 个。返回起点对数。
    /// </summary>
    private int CollectOrigins(CrowdSimSession session, List<int> members, int[] origins)
    {
        var cfg = session.Config;
        int perTile = cfg.Hpa.NavCellsPerTile, side = cfg.NavCellsPerSide;
        int gen = ++_originGen;
        if (gen == int.MaxValue)
        {
            Array.Clear(_originStamp, 0, _originStamp.Length);
            _originGen = gen = 1;
        }

        int count = 0;
        foreach (int i in members)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i));
            if (st.Level > 1) throw new InvalidOperationException($"单位层号 {st.Level} 超出起点表的两层编码。");
            int cell = CellOfUnit(session, i, st);
            int key = NavGridSteps.TileOf(cell, side, perTile, _tilesPerSide) * 2 + st.Level;
            if (_originStamp[key] == gen) continue;
            _originStamp[key] = gen;
            origins[2 * count] = cell;
            origins[2 * count + 1] = st.Level;
            count++;
        }

        return count;
    }

    /// <summary>领队兜底起点:第一个底层起点;没有底层起点才用第一个起点。</summary>
    private static int RepresentativeStart(PendingGroup pg)
    {
        for (int k = 0; k < pg.OriginCount; k++)
        {
            if (pg.Origins[2 * k + 1] == 0) return pg.Origins[2 * k];
        }

        return pg.Origins[0];
    }

    private static int PendingGroupIndex(PendingOrder po, int groupId)
    {
        for (int i = 0; i < po.Groups.Count; i++)
        {
            if (po.Groups[i].GroupId == groupId) return i;
        }

        throw new InvalidOperationException($"指令 {po.Order.Id} 的待发组里没有组 {groupId}。");
    }

    private int[] RentOrigins()
    {
        if (_originBuffers.TryPop(out var buffer)) return buffer;
        return new int[_originStamp.Length * 2];
    }

    private void ReturnOrigins(PendingGroup pg)
    {
        if (pg.Origins.Length == 0) return;
        _originBuffers.Push(pg.Origins);
        pg.Origins = Array.Empty<int>();
        pg.OriginCount = 0;
    }

    private static int CompAtLevel(CrowdSimSession session, CrowdNavGroupSet.Group g, int cell, int level) =>
        session.ResolveNavContext(g.NavId).CompAt(cell, level);

    /// <summary>认知槽变更后的组重定向(retarget):moved 组——目标被挡 → 重规划;
    /// 行进中 → ReachLost / 领队线被挡裁决;驻扎组 → 流场不属于新导航才刷新(真相流场经
    /// sharesFlow 判定可复用——不是纯优化,刷新计数是对拍面)。stale = 原位揭示踩到真变
    /// tile 的组(只刷新,不重规划)。返回需重规划的指令。</summary>
    public List<CrowdOrder> Retarget(List<CrowdNavGroupSet.Group> moved, HashSet<CrowdNavGroupSet.Group>? stale = null)
    {
        var session = _session;
        var units = session.Units;
        var underway = _underway;
        underway.Clear();
        for (int i = 0; i < units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Traversing) underway.Add(st.GroupId);
        }

        var orders = _reactOrders;
        var orderSet = _orderSet;
        var hit = _hit;
        orders.Clear();
        orderSet.Clear();
        hit.Clear();
        for (int gi = 0; gi < moved.Count; gi++)
        {
            var g = moved[gi];
            if (g.Count == 0 || g.OrderId == 0) continue;
            Supersede(g);
            if (g.Planning) continue; // 在途规划由落帧裁决
            if (g.Goal < 0) continue;
            var nav = session.ResolveNavContext(g.NavId);
            if (nav.Passable[g.Goal] == 0)
            {
                AddOrder(session, orders, orderSet, g);
                continue;
            }

            g.GoalComp = nav.Comp[g.Goal]; // 连通域随认知重烘重标号
            if (g.Flow == null) continue;
            if (!underway.Contains(g.Id))
            {
                // 驻扎组:场必须还属于新导航(自有,或 sharesFlow 判定可复用的真相场),
                // 否则被唤醒/推挤的成员会踩旧认知——只刷新,不重规划
                var f = g.Flow;
                bool own = f.NavId == g.NavId;
                bool shared = !own && f.NavId == g.NavId % CrowdBeliefNavs.BeliefStride &&
                    _session.Beliefs != null && _session.Beliefs.SharesFlow(g.NavId, f.Mask!);
                if (stale?.Contains(g) == true || (!own && !shared)) RefreshFlow(g);
                continue;
            }

            hit.Add(g);
        }

        React(session, hit, orders, orderSet);
        return orders;
    }

    /// <summary>受触指令的领队重挂到同指令仍由它带队、净空最严的组的当前导航——
    /// 领队不踩已弃变体。</summary>
    public void RelinkLeaders(HashSet<CrowdOrder> touched)
    {
        // 顺序无关:各指令的组/领队互斥、同净空取更大下标已确定;按指令 id 升序显式化
        int touchCount = touched.Count;
        StableOrder.Ensure(ref _touchRank, touchCount);
        StableOrder.Ensure(ref _touchOrders, touchCount);
        int touchWrite = 0;
        foreach (var order in touched)
        {
            _touchOrders[touchWrite] = order;
            _touchRank[touchWrite] = new StableInt(order.Id, touchWrite);
            touchWrite++;
        }

        StableOrder.Sort(_touchRank, touchCount);
        for (int touchIndex = 0; touchIndex < touchCount; touchIndex++)
        {
            var o = _touchOrders[_touchRank[touchIndex].Seq];
            foreach (var leader in o.Leaders)
            {
                CrowdNavGroupSet.Group? m = null;
                foreach (var link in o.Groups)
                {
                    if (!_session.Groups.TryGet(link.GroupId, out var g) || g.Leader != leader) continue;
                    if (m == null || StricterNav(g, m)) m = g;
                }

                if (m != null) leader.Nav = _session.ResolveNavContext(m.NavId);
            }
        }
    }

    // 净空更大更严;同净空取更大下标(确定)
    private bool StricterNav(CrowdNavGroupSet.Group g, CrowdNavGroupSet.Group m)
    {
        int a = _session.ResolveNavContext(g.NavId).ClearanceNavCells;
        int b = _session.ResolveNavContext(m.NavId).ClearanceNavCells;
        return a > b || (a == b && g.RIdx > m.RIdx);
    }

    /// <summary>原位揭示:槽的已建变体只重烘被揭 tile。踩到的流场离开缓存。
    /// 在途刷新照常落帧,导航版本对不上的组重新排刷新。</summary>
    public Dictionary<int, List<int>> RevealBelief(int slot, List<int> tiles)
    {
        var dirty = _session.Beliefs!.Reveal(slot, tiles);
        foreach (var pair in dirty) _cache.EvictTouching(pair.Key, pair.Value);
        return dirty;
    }

    /// <summary>认知槽删除:该槽的流场离开缓存,算在它变体上的在途刷新作废。必须在槽注册表移除之前调用。</summary>
    public void OnBeliefSlotDropped(int slot)
    {
        _cache.EvictSlot(slot, CrowdBeliefNavs.BeliefStride);
        int stride = CrowdBeliefNavs.BeliefStride;
        for (int i = _pendingRefreshes.Count - 1; i >= 0; i--)
        {
            var pr = _pendingRefreshes[i];
            if (pr.NavId >= stride && pr.NavId / stride == slot) DropPendingAt(i);
        }
    }

    /// <summary>反应取代组的在途刷新和攒下的走失记录:答复落帧时这一组被跳过,
    /// 不清新请求的标记,也不再排一次刷新。</summary>
    private void Supersede(CrowdNavGroupSet.Group g)
    {
        if (g.Extending) g.StateSeq++;
        g.Extending = false;
        DropStrays(g);
    }

    private static int CellOfUnit(CrowdSimSession session, int dense, CrowdSimulationUnitState st)
    {
        var entity = session.Units.EntityAt(dense);
        int navCellsPerSide = session.Config.NavCellsPerSide, navCellSizeCm = session.Config.NavCellSizeCm;
        if (st.State == (byte)CrowdUnitState.Traversing)
        {
            var kin = session.World.Get<CrowdSimulationKinematics>(entity);
            return CrowdSimulationSpace.CellAtStrict(kin.LinkToCm.X, kin.LinkToCm.Y, navCellSizeCm, navCellsPerSide);
        }

        var p = session.World.Get<Components.WorldPositionCm>(entity).Value;
        return CrowdSimulationSpace.CellAtStrict(p.X, p.Y, navCellSizeCm, navCellsPerSide);
    }

    /// <summary>离 target 最近且从 fromComp 可达的可走格(环形外扩,平手取格号小者)。</summary>
    internal int NearestReachable(NavContext nav, int fromComp, int target, int navCellsPerSide)
    {
        if (fromComp < 0) return -1;
        int tx = target % navCellsPerSide, ty = target / navCellsPerSide;
        _nrPass = nav.Passable;
        _nearestOk.Clear();
        _nrBest = -1;
        _nrBd = long.MaxValue;
        _nrTx = tx;
        _nrTy = ty;
        _nrFrom = fromComp;
        _nrSide = navCellsPerSide;
        _nrNav = nav;
        int best = -1;
        long bd = long.MaxValue;
        for (int r = 0; r < navCellsPerSide && (long)r * r <= _nrBd; r++)
        {
            int x0 = tx - r, x1 = tx + r, y0 = ty - r, y1 = ty + r;
            for (int y = Math.Max(0, y0); y <= Math.Min(navCellsPerSide - 1, y1); y++)
            {
                if (y == y0 || y == y1)
                {
                    for (int x = Math.Max(0, x0); x <= Math.Min(navCellsPerSide - 1, x1); x++) TestNearest(x, y);
                }
                else
                {
                    if (x0 >= 0) TestNearest(x0, y);
                    if (x1 < navCellsPerSide && r != 0) TestNearest(x1, y);
                }
            }
        }

        return _nrBest;
    }

    private void TestNearest(int x, int y)
    {
        int c = y * _nrSide + x;
        if (_nrPass[c] == 0) return;
        long dx = x - _nrTx, dy = y - _nrTy, d = dx * dx + dy * dy;
        if (d > _nrBd || (d == _nrBd && c > _nrBest)) return;
        int k = _nrNav.Comp[c];
        if (!_nearestOk.TryGetValue(k, out bool reachable)) _nearestOk[k] = reachable = _nrNav.CanReach(_nrFrom, k);
        if (reachable)
        {
            _nrBest = c;
            _nrBd = d;
        }
    }

    private int TallySlot(int comp)
    {
        int slot = comp + 2;
        if ((uint)slot >= (uint)_compTally.Length)
        {
            throw new InvalidOperationException($"连通域编号 {comp} 超出规划计数表。");
        }

        return slot;
    }

    private List<int> RentMembers()
    {
        if (!_memberLists.TryPop(out var list)) list = new List<int>(8);
        list.Clear();
        _memberListsInUse.Add(list);
        return list;
    }

    private void ReturnMembers()
    {
        for (int i = 0; i < _memberListsInUse.Count; i++)
        {
            _memberListsInUse[i].Clear();
            _memberLists.Push(_memberListsInUse[i]);
        }

        _memberListsInUse.Clear();
        _membersByGroup.Clear();
    }

    private List<int> RentLayer()
    {
        if (!_layerBuckets.TryPop(out var list)) list = new List<int>(8);
        list.Clear();
        _layerBucketsInUse.Add(list);
        return list;
    }

    private void ReturnLayers()
    {
        for (int i = 0; i < _layerBucketsInUse.Count; i++)
        {
            _layerBucketsInUse[i].Clear();
            _layerBuckets.Push(_layerBucketsInUse[i]);
        }

        _layerBucketsInUse.Clear();
        _byLayer.Clear();
        _layersInOrder.Clear();
    }

    private PendingOrder RentOrder()
    {
        if (!_orderPool.TryPop(out var order)) order = new PendingOrder();
        order.Groups.Clear();
        order.Leaders.Clear();
        order.Job.LeaderCount = 0;
        return order;
    }

    private PendingGroup RentGroup()
    {
        if (!_groupPool.TryPop(out var group)) group = new PendingGroup { Corridor = new byte[_maskA.Length] };
        return group;
    }

    private PendingLeader RentLeaderReq()
    {
        if (!_leaderReqPool.TryPop(out var leader)) leader = new PendingLeader();
        leader.Prev = null;
        leader.BucketCount = 0;
        return leader;
    }

    private PendingPlan RentPlan()
    {
        if (!_planPool.TryPop(out var plan)) plan = new PendingPlan();
        plan.Orders.Clear();
        return plan;
    }

    private CrowdLeader RentLeader()
    {
        if (!_leaderPool.TryPop(out var leader)) leader = new CrowdLeader(PathCapacity);
        return leader;
    }

    private void RecycleOrder(PendingOrder order)
    {
        for (int i = 0; i < order.Groups.Count; i++)
        {
            var pg = order.Groups[i];
            ReturnOrigins(pg);
            UnpinPlanned(pg);
            if (pg.Flow != null) RecycleFlow(pg.Flow);
            pg.Flow = null;
            pg.Reply = PathPlanReply.None;
            _groupPool.Push(pg);
        }

        order.Groups.Clear();
        for (int i = 0; i < order.Leaders.Count; i++) _leaderReqPool.Push(order.Leaders[i]);
        order.Leaders.Clear();
        _orderPool.Push(order);
    }

    private void RecyclePlan(PendingPlan plan)
    {
        for (int i = 0; i < plan.Orders.Count; i++) RecycleOrder(plan.Orders[i]);
        plan.Orders.Clear();
        _planPool.Push(plan);
    }

    /// <summary>在途规划与流场刷新的身份，挂在 groups 诊断链末尾。</summary>
    internal uint MixInFlight(uint h)
    {
        h = CrowdSimChecksum.Mix(h, (uint)_pending.Count);
        for (int p = 0; p < _pending.Count; p++)
        {
            PendingPlan plan = _pending[p];
            h = CrowdSimChecksum.Mix(h, (uint)plan.DueTick);
            h = CrowdSimChecksum.Mix(h, (uint)plan.Orders.Count);
            for (int o = 0; o < plan.Orders.Count; o++)
            {
                PendingOrder order = plan.Orders[o];
                h = CrowdSimChecksum.Mix(h, (uint)order.Order.Id);
                h = CrowdSimChecksum.Mix(h, (uint)order.RequestId);
                h = CrowdSimChecksum.Mix(h, (uint)order.Groups.Count);
                for (int g = 0; g < order.Groups.Count; g++)
                {
                    PendingGroup pg = order.Groups[g];
                    h = CrowdSimChecksum.Mix(h, (uint)pg.GroupId);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.PlanSeq);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.NavId);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.Goal);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.Version);
                    h = CrowdSimChecksum.MixI64(h, pg.Pin != null ? _cache.KeyOf(pg.Pin.NavId, pg.Pin.Goal) : -1);
                }

                h = CrowdSimChecksum.Mix(h, (uint)order.Leaders.Count);
                for (int l = 0; l < order.Leaders.Count; l++)
                {
                    PendingLeader leader = order.Leaders[l];
                    h = CrowdSimChecksum.Mix(h, (uint)leader.StrictestGroupId);
                    h = CrowdSimChecksum.Mix(h, (uint)leader.StartCell);
                    h = CrowdSimChecksum.Mix(h, (uint)leader.RequestId);
                    h = CrowdSimChecksum.Mix(h, (uint)leader.BucketCount);
                    int n = leader.BucketCount;
                    if (n > leader.BucketGroupIds.Length) n = leader.BucketGroupIds.Length;
                    for (int b = 0; b < n; b++) h = CrowdSimChecksum.Mix(h, (uint)leader.BucketGroupIds[b]);
                }
            }
        }

        h = CrowdSimChecksum.Mix(h, (uint)_pendingRefreshes.Count);
        for (int i = 0; i < _pendingRefreshes.Count; i++)
        {
            PendingRefresh refresh = _pendingRefreshes[i];
            h = CrowdSimChecksum.Mix(h, (uint)refresh.RequestId);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.DueTick);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.NavId);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.Version);
            h = CrowdSimChecksum.Mix(h, refresh.Extend ? 1u : 0u);
            h = CrowdSimChecksum.MixI64(h, refresh.Cached != null ? _cache.KeyOf(refresh.Cached.NavId, refresh.Cached.Goal) : -1);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.Riders.Count);
            for (int r = 0; r < refresh.Riders.Count; r++)
            {
                h = CrowdSimChecksum.Mix(h, (uint)refresh.Riders[r].GroupId);
                h = CrowdSimChecksum.Mix(h, (uint)refresh.Riders[r].Seq);
            }
        }

        h = CrowdSimChecksum.Mix(h, (uint)_refreshQueue.Count);
        for (int i = 0; i < _refreshQueue.Count; i++)
            h = CrowdSimChecksum.Mix(h, (uint)_refreshQueue[i].Id);
        h = CrowdSimChecksum.Mix(h, (uint)_strayCursor);
        var groups = _session.Groups.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i] is { } g) h = CrowdSimChecksum.Mix(h, (uint)PendingStrays(g));
        }

        h = CrowdSimChecksum.Mix(h, (uint)_seq);
        int lru = _cache.CopyOldestFirst(_lruKeys);
        h = CrowdSimChecksum.Mix(h, (uint)lru);
        for (int i = 0; i < lru; i++) h = CrowdSimChecksum.MixI64(h, _lruKeys[i]);
        return h;
    }

    /// <summary>场外采样。每组每个 tile 记第一次碰到的格,跨 tick 累积到下一次扩展,处理在 <see cref="ProcessRefreshes"/>。</summary>
    public void NoteStray(int groupId, int cell)
    {
        if (!_session.Groups.TryGet(groupId, out var g)) return;
        var cfg = _session.Config;
        int tile = NavGridSteps.TileOf(cell, cfg.NavCellsPerSide, cfg.Hpa.NavCellsPerTile, _tilesPerSide);
        StraysOf(g, true)!.Add(tile, cell);
    }

    private StrayRecord? StraysOf(CrowdNavGroupSet.Group g, bool create)
    {
        var rec = _strays[g.Id];
        if (rec != null && rec.Owner != g)
        {
            ReturnStrays(rec);
            _strays[g.Id] = rec = null;
        }

        if (rec == null && create)
        {
            if (!_strayPool.TryPop(out rec)) rec = new StrayRecord(_maskA.Length);
            rec.Owner = g;
            _strays[g.Id] = rec;
        }

        return rec;
    }

    private void DropStrays(CrowdNavGroupSet.Group g)
    {
        var rec = _strays[g.Id];
        if (rec == null) return;
        _strays[g.Id] = null;
        ReturnStrays(rec);
    }

    private void ReturnStrays(StrayRecord rec)
    {
        rec.Clear();
        _strayPool.Push(rec);
    }

    private int PendingStrays(CrowdNavGroupSet.Group g)
    {
        var rec = _strays[g.Id];
        return rec != null && rec.Owner == g && rec.Count > 0 ? rec.Count : -1;
    }

    /// <param name="buildRevision">非 0 时写成该答复的路径请求号。0 表示沿用场上已有的号：缓存命中复用的就是当初挂上的那份。</param>
    private void SetFlow(CrowdNavGroupSet.Group g, FlowField? flow, bool admit, int buildRevision)
    {
        if (flow != null && buildRevision != 0) flow.BuildRevision = buildRevision;
        var old = g.Flow;
        if (ReferenceEquals(old, flow))
        {
            if (admit && flow != null) _cache.Admit(flow);
            return;
        }

        if (old != null && old.Refs > 0) old.Refs--;
        g.Flow = flow;
        if (flow != null) flow.Refs++;
        if (admit && flow != null) _cache.Admit(flow);
        if (old != null) _cache.Release(old);
    }

    private void ReleaseGroup(CrowdNavGroupSet.Group g)
    {
        SetFlow(g, null, false, 0);
        DropStrays(g);
    }

    /// <summary>从上一次服务过的下一组起轮转,有走失记录、有场、没有刷新或扩展在途的组提交扩展,每 tick 预算 maxRebuildsPerTick。
    /// 在途的组记录留着,答复落帧后再提。</summary>
    private void ProcessExtensions(int tick)
    {
        int budget = _session.Config.Flowfield.MaxRebuildsPerTick;
        var groups = _session.Groups.Groups;
        int count = groups.Count;
        for (int i = 0; i < count; i++)
        {
            var noted = groups[i];
            if (noted != null && PendingStrays(noted) > 0) StrayGroupTicks++;
        }

        int start = count > 0 ? _strayCursor % count : 0;
        for (int k = 0; k < count && budget > 0; k++)
        {
            int gi = (start + k) % count;
            var g = groups[gi];
            if (g == null) continue;
            var rec = StraysOf(g, false);
            if (rec == null || rec.Count == 0 || g.Flow?.Mask == null) continue;
            if (g.Extending)
            {
                StraySkipInFlight++;
                continue;
            }

            _strayCursor = gi + 1;
            SubmitExtension(g, rec, tick);
            budget--;
        }
    }

    private void SubmitExtension(CrowdNavGroupSet.Group g, StrayRecord rec, int tick)
    {
        var mask = g.Flow!.Mask!;
        int dist = 0;
        for (int i = 0; i < rec.Count; i++)
        {
            int d = TileDistance(mask, rec.Tiles[i]);
            if (d > dist) dist = d;
        }

        int pad = _session.Config.Flowfield.CorridorPaddingNavTiles;
        if (dist <= pad) ExtendWithinPad++;
        else ExtendBeyondPad++;
        if ((uint)dist < (uint)_extendDist.Length) _extendDist[dist]++;

        _strays[g.Id] = null;
        var pr = RentRefresh();
        AddRider(pr, g);
        pr.Strays = rec;
        Submit(pr, g, RentMask(mask), rec, tick, true);
        ExtendSubmits++;
        ExtendStrayTiles += rec.Count;
        if ((uint)g.Id < (uint)_extendByGroup.Length) _extendByGroup[g.Id]++;
    }

    private int TileDistance(byte[] mask, int tile)
    {
        int side = _tilesPerSide;
        if (side < 1 || (uint)tile >= (uint)mask.Length) return side;
        if (mask[tile] != 0) return 0;
        int tx = tile % side, ty = tile / side;
        int best = side;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            int d = Math.Max(Math.Abs(i % side - tx), Math.Abs(i / side - ty));
            if (d < best) best = d;
            if (best <= 1) break;
        }

        return best;
    }

    private bool HasPending(CrowdNavGroupSet.Group g)
    {
        for (int i = 0; i < _pendingRefreshes.Count; i++)
        {
            var riders = _pendingRefreshes[i].Riders;
            for (int r = 0; r < riders.Count; r++)
            {
                if (riders[r].Group == g && riders[r].Seq == g.StateSeq) return true;
            }
        }

        for (int i = 0; i < _maskHold.Count; i++)
        {
            var riders = _maskHold[i].Riders;
            for (int r = 0; r < riders.Count; r++)
            {
                if (riders[r].Group == g) return true;
            }
        }

        return false;
    }

    private bool FitsRefresh(CrowdNavGroupSet.Group g) =>
        _session.Groups.TryGet(g.Id, out var live) && live == g && g.Flow?.Mask != null && !g.Planning && !g.Extending;

    private PendingRefresh RentRefresh()
    {
        if (!_refreshPool.TryPop(out var pr)) pr = new PendingRefresh();
        return pr;
    }

    private static void AddRider(PendingRefresh pr, CrowdNavGroupSet.Group g) =>
        pr.Riders.Add(new Rider { Group = g, GroupId = g.Id, Seq = g.StateSeq });

    /// <summary>
    /// 钉住同 (导航, 目标) 的缓存场,把它的掩码交给工人。搭车的组都标成刷新中。
    /// 在途表按生效帧排序,同帧保持提交顺序。
    /// </summary>
    private void Submit(PendingRefresh pr, CrowdNavGroupSet.Group g, byte[] mask, StrayRecord? strays, int tick, bool extend)
    {
        var nav = _session.ResolveNavContext(g.NavId);
        var cached = PinCached(g.NavId, g.Goal);
        var req = new PathQuery(g.NavId, 0, g.Goal, 0)
        {
            CorridorMask = mask,
            CachedMask = cached?.Mask,
            StrayCells = strays?.Cells,
            StrayCount = strays?.Count ?? 0,
            Extension = extend,
        };
        pr.RequestId = _service.Request(req, tick);
        var planning = _session.Config.Planning;
        pr.DueTick = tick + (extend ? planning.LatencyTicks : planning.RefreshLatencyTicks);
        pr.NavId = g.NavId;
        pr.Version = nav.Version;
        pr.Extend = extend;
        pr.Cached = cached;
        pr.MaskCopy = mask;
        for (int r = 0; r < pr.Riders.Count; r++) pr.Riders[r].Group.Extending = true;
        int at = _pendingRefreshes.Count;
        while (at > 0 && _pendingRefreshes[at - 1].DueTick > pr.DueTick) at--;
        _pendingRefreshes.Insert(at, pr);
    }

    private byte[] RentMask(byte[] source)
    {
        if (!_maskCopies.TryPop(out var copy) || copy.Length != source.Length) copy = new byte[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private void ReleaseMask(PendingRefresh pr)
    {
        if (pr.Strays != null)
        {
            ReturnStrays(pr.Strays);
            pr.Strays = null;
        }

        if (pr.MaskCopy == null) return;
        _maskCopies.Push(pr.MaskCopy);
        pr.MaskCopy = null;
    }

    private void Unpin(PendingRefresh pr)
    {
        var pin = pr.Cached;
        pr.Cached = null;
        if (pin == null) return;
        if (pin.Refs > 0) pin.Refs--;
        _cache.Release(pin);
    }

    private void DropPendingAt(int index)
    {
        var pr = _pendingRefreshes[index];
        _pendingRefreshes.RemoveAt(index);
        Unpin(pr);
        bool idle = _service.Discard(pr.RequestId);
        var riders = pr.Riders;
        for (int r = 0; r < riders.Count; r++)
        {
            var g = riders[r].Group;
            if (g.StateSeq == riders[r].Seq) g.Extending = false;
        }

        if (idle)
        {
            ReleaseMask(pr);
            pr.Extend = false;
            riders.Clear();
            _refreshPool.Push(pr);
        }
        else _maskHold.Add(pr);
    }

    private void ReclaimRetired()
    {
        for (;;)
        {
            int n = _service.DrainRetired(_retiredIds);
            if (n == 0) return;
            for (int k = 0; k < n; k++)
            {
                int id = _retiredIds[k];
                for (int i = _maskHold.Count - 1; i >= 0; i--)
                {
                    if (_maskHold[i].RequestId != id) continue;
                    var pr = _maskHold[i];
                    _maskHold.RemoveAt(i);
                    ReleaseMask(pr);
                    pr.Extend = false;
                    pr.Riders.Clear();
                    _refreshPool.Push(pr);
                    break;
                }
            }
        }
    }
}
