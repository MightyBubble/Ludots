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
/// 本层含重烘反应、认知重定向、原位揭示和流场刷新。走廊扩展没有。
/// 答复未回则本 tick 停摆；领队路径答复不可达时，非跳跃成员标不可达。
/// </summary>
public sealed class CrowdSimPlanner
{
    private const int GroupScratch = CrowdSimulationLimits.HotPathSpanReserve;
    private const int LeaderPoolCount = 32;
    private const int PathCapacity = CrowdSimulationLimits.HotPathSpanReserve;

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
        public CrowdOrder Order = null!;
        public readonly List<PendingGroup> Groups = new(8);
        public readonly List<PendingLeader> Leaders = new(4);
    }

    private sealed class PendingGroup
    {
        public int GroupId;
        public int PlanSeq;
        public int NavId;
        public int Goal;
        public int RequestId;
        /// <summary>答复落帧时导航版本或导航号变了，组进入 AfterStalePlan。</summary>
        public int Version;
    }

    /// <summary>流场刷新的到点答复:同走廊在新导航上重建流场,领队与槽位不动。</summary>
    private sealed class PendingRefresh
    {
        public int RequestId;
        public int DueTick;
        public int GroupId;
        public CrowdNavGroupSet.Group Group = null!;
        public int Seq;
        public int NavId;
        public int Version;
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
        for (int i = 0; i < 16; i++)
        {
            _refreshPool.Push(new PendingRefresh());
            _dirtyTileLists.Add(new List<int>(8));
        }
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

    /// <summary>为一批指令起规划:组目标/可达替代/成员标记/出发代表格,再发请求。</summary>
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
                var g = session.Groups.Groups[link.GroupId]!;
                g.Goal = CrowdDeployment.NearestPassable(session.ResolveNavContext(g.NavId), link.GoalCell);
                g.GoalComp = g.Goal >= 0 ? session.ResolveNavContext(g.NavId).Comp[g.Goal] : -2;
                if (g.Flow != null) RecycleFlow(g);
                g.Leader = null;
                g.Planning = true;
                g.StateSeq++;
            }
        }

        // 目标不可达 → 换最近的可达格:按组员多数所在连通域判定
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            foreach (var link in o.Groups)
            {
                var g = session.Groups.Groups[link.GroupId]!;
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

        // 逐单位:成员/可达性/状态;跳跃中的单位从落点出发规划
        var membersByGroup = _membersByGroup;
        ReturnMembers();
        for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
        {
            var o = orders[orderIndex];
            foreach (var link in o.Groups)
            {
                var g = session.Groups.Groups[link.GroupId]!;
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
            bool jumping = st.State == (byte)CrowdUnitState.Jump;
            if (!jumping)
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
                if (!jumping)
                {
                    st.State = (byte)CrowdUnitState.Unreachable;
                    st.Order = 0;
                    session.World.Set(entity, st);
                }

                continue;
            }

            if (!jumping && st.State != (byte)CrowdUnitState.Arrived)
            {
                st.State = (byte)CrowdUnitState.Moving;
            }

            st.Order = (uint)g.OrderId; // 组的指令字段是唯一真相(扫指令簿会被回收组号的陈旧链接命中)
            session.World.Set(entity, st);
            mem.Add(i);
        }

        // 发请求:组流场(代表格→目标)+ 每(指令×移动类型)一条领队路径(最严净空导航,联合质心起点)
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
                var g = session.Groups.Groups[link.GroupId]!;
                var mem = membersByGroup[g.Id];
                if (mem.Count == 0)
                {
                    g.Planning = false;
                    continue;
                }

                int repCell = RepresentativeCell(session, mem);
                int reqId = _service.Request(new PathQuery(g.NavId, repCell, g.Goal, 0), tick);
                var pg = RentGroup();
                pg.GroupId = g.Id;
                pg.PlanSeq = ++_seq;
                pg.NavId = g.NavId;
                pg.Goal = g.Goal;
                pg.RequestId = reqId;
                pg.Version = session.ResolveNavContext(g.NavId).Version;
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
                if (lc < 0 || !nav.CanReach(nav.Comp[lc], strictest.GoalComp)) lc = RepresentativeCell(session, membersByGroup[strictestId]);
                CrowdLeader? prev = null;
                foreach (int gid in bucket)
                {
                    if (prev == null && session.Groups.Groups[gid]!.Leader is { Done: false } l) prev = l;
                }

                foreach (int gid in bucket) session.Groups.Groups[gid]!.PrevLeader = null;
                int leaderReq = _service.Request(new PathQuery(strictest.NavId, lc, strictest.Goal, 0), tick);
                var pl = RentLeaderReq();
                pl.StrictestGroupId = strictestId;
                if (bucket.Count > pl.BucketGroupIds.Length) pl.BucketGroupIds = new int[Math.Max(bucket.Count, pl.BucketGroupIds.Length * 2)];
                for (int bi = 0; bi < bucket.Count; bi++) pl.BucketGroupIds[bi] = bucket[bi];
                pl.BucketCount = bucket.Count;
                pl.StartCell = lc;
                pl.RequestId = leaderReq;
                pl.Prev = prev;
                po.Leaders.Add(pl);
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
    /// block=true:同步等答复(AwaitDue,超时即故障)。
    /// 进入时已经故障则抛。非阻塞路径上,Ready 超时只把服务标成故障并返回 false,这次调用停摆,下一次调用才抛。</summary>
    public bool ApplyDue(int tick, bool block)
    {
        if ((_pending.Count > 0 || _pendingRefreshes.Count > 0) && _service.Faulted)
        {
            throw new PathServiceFaultException(_service.FaultMessage ?? "路径服务故障(原因未记录)");
        }

        int k = 0, rk = 0;
        if (!block)
        {
            while (k < _pending.Count && _pending[k].DueTick <= tick)
            {
                var plan = _pending[k];
                foreach (var po in plan.Orders)
                {
                    foreach (var pg in po.Groups)
                    {
                        if (!_service.Ready(pg.RequestId)) return false;
                    }

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
            foreach (var pg in po.Groups)
            {
                var result = _service.AwaitDue(pg.RequestId);
                if (!_session.Groups.TryGet(pg.GroupId, out var g) || g.OrderId != o.Id)
                {
                    Recycle(result);
                    continue;
                }

                if (!result.Reachable || result.Flow == null)
                {
                    Recycle(result);
                    continue;
                }

                // 陈旧答复(请求后导航重烘过):场先挂上(刷新落地前组用它),组进裁决清单
                var nav = _session.ResolveNavContext(g.NavId);
                if (nav.Version != pg.Version || g.NavId != pg.NavId) stale.Add(g);
                g.Flow = result.Flow;
                result.Flow = null;
                _service.ReleaseReply(result);
                g.Planning = false;
                live[pg.GroupId] = new LiveGroup { Pg = pg };
            }

            FinishOrder(po, live);
        }
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
            var strictest = session.Groups.Groups[pl.StrictestGroupId]!;
            if (!leaderResult.Reachable || leaderResult.Points == null)
            {
                // 非跳跃成员标不可达并清掉指令号;跳跃中的单位保持跳跃。组目标清成 -1。
                foreach (int gid in bucket)
                {
                    var g = session.Groups.Groups[gid]!;
                    foreach (int i in membersByGroup[gid])
                    {
                        var e = units.EntityAt(i);
                        var st = session.World.Get<CrowdSimulationUnitState>(e);
                        if (st.State != (byte)CrowdUnitState.Jump)
                        {
                            st.State = (byte)CrowdUnitState.Unreachable;
                            st.Order = 0;
                            session.World.Set(e, st);
                        }
                    }

                    RecycleFlow(g);
                    g.Goal = -1;
                    g.GoalComp = -2;
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
                    Fix64 speed = session.World.Get<CrowdSimulationAgent>(units.EntityAt(i)).SpeedCmPerSecond;
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

            o.Leaders.Add(leader);
            var mem = RentMembers();
            foreach (int gid in bucket)
            {
                var g = session.Groups.Groups[gid]!;
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

    private void RecycleFlow(CrowdNavGroupSet.Group g)
    {
        if (g.Flow is { } f)
        {
            RecycleFlow(f);
            g.Flow = null;
        }
    }

    private static void RecycleFlow(FlowField f)
    {
        f.OriginPool?.Give(f);
    }

    private void ApplyRefresh(PendingRefresh pr)
    {
        var g = pr.Group;
        if (!_session.Groups.TryGet(pr.GroupId, out var live) || live != g || g.StateSeq != pr.Seq)
        {
            Recycle(_service.AwaitDue(pr.RequestId));
            return;
        }

        var result = _service.AwaitDue(pr.RequestId);
        var nav = _session.ResolveNavContext(g.NavId);
        if (g.NavId != pr.NavId || nav.Version != pr.Version)
        {
            // 答复基于旧导航:丢弃并重新排队刷新
            Recycle(result);
            RefreshFlow(g);
            return;
        }

        if (result.Flow == null)
        {
            Recycle(result);
            return;
        }

        if (g.Flow is { } old) RecycleFlow(old);
        g.Flow = result.Flow;
        result.Flow = null;
        _service.ReleaseReply(result);
    }

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
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Jump) underway.Add(g0.Id);
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

    /// <summary>行进中成员(脚下格 / 跳跃落点格)的连通域不再可达目标 = 该组的路被切断。</summary>
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
            if (st.State != (byte)CrowdUnitState.Moving && st.State != (byte)CrowdUnitState.Jump) continue;
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

    /// <summary>领队剩余路线是否被挡:沿线四角探边的坏格带。桥面托线由 upPass 覆盖。</summary>
    private static bool LeaderPathBlocked(CrowdLeader? leader, NavContext nav, CrowdSimSession session)
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
                if (LeaderBlockedAt(pass, up, ax + (bx - ax) * s / m, ay + (by - ay) * s / m, e, navCellSizeCm, navCellsPerSide)) return true;
            }

            ax = bx;
            ay = by;
        }

        return false;
    }

    private static bool LeaderBlockedAt(byte[] pass, byte[] up, Fix64 x, Fix64 y, Fix64 e, int navCellSizeCm, int navCellsPerSide)
    {
        return LeaderCellBad(pass, up, CrowdSimulationSpace.CellAt(x - e, y - e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(pass, up, CrowdSimulationSpace.CellAt(x + e, y - e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(pass, up, CrowdSimulationSpace.CellAt(x - e, y + e, navCellSizeCm, navCellsPerSide))
            && LeaderCellBad(pass, up, CrowdSimulationSpace.CellAt(x + e, y + e, navCellSizeCm, navCellsPerSide));
    }

    private static bool LeaderCellBad(byte[] pass, byte[] up, int c) => pass[c] == 0 && up[c] == 0;

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

    /// <summary>有流场的组重新排队。旧场留到答复替换,不另建一套跳过等待的缓存。</summary>
    public int InvalidateFlows()
    {
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

    /// <summary>每 tick 预算内的流场刷新提交(生效帧 = tick + refreshLatencyTicks)。
    /// 不做走廊扩展,也不做同目标共乘。驻扎组是否沿用真相流场在 Retarget,由 SharesFlow 判定。</summary>
    public void ProcessRefreshes(int tick)
    {
        int n = 0;
        while (_refreshQueue.Count > 0 && n < _session.Config.Planning.MaxRefreshesPerTick)
        {
            var g = _refreshQueue[0];
            _refreshQueue.RemoveAt(0);
            if (!_session.Groups.TryGet(g.Id, out var live) || live != g || g.Flow == null || g.Planning) continue;
            SubmitRefresh(g, tick);
            n++;
        }
    }

    private void SubmitRefresh(CrowdNavGroupSet.Group g, int tick)
    {
        var nav = _session.ResolveNavContext(g.NavId);
        // 同走廊:掩码用组现流场的走廊(未填 padding),服务侧按 padMask ∪ 原掩码建场
        var req = new PathQuery(g.NavId, 0, g.Goal, 0) { CorridorMask = g.Flow!.Mask };
        int id = _service.Request(req, tick);
        g.StateSeq++;
        FlowRefreshSubmits++;
        if (!_refreshPool.TryPop(out var pr)) pr = new PendingRefresh();
        pr.RequestId = id;
        pr.DueTick = tick + _session.Config.Planning.RefreshLatencyTicks;
        pr.GroupId = g.Id;
        pr.Group = g;
        pr.Seq = g.StateSeq;
        pr.NavId = g.NavId;
        pr.Version = nav.Version;
        _pendingRefreshes.Add(pr);
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

    /// <summary>按成员列表顺序,返回第一个地面层单位的格;没有地面成员才用第一个成员的格。</summary>
    private int RepresentativeCell(CrowdSimSession session, List<int> members)
    {
        int fallback = -1;
        foreach (int i in members)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i));
            int cell = CellOfUnit(session, i, st);
            if (fallback < 0) fallback = cell;
            if (st.Level == 0) return cell;
        }

        return fallback;
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
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Jump) underway.Add(st.GroupId);
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

    /// <summary>原位揭示:槽的已建变体只重烘被揭 tile。流场挂在组上,踩脏的组由调用方的 stale 集合刷新。</summary>
    public Dictionary<int, List<int>> RevealBelief(int slot, List<int> tiles) =>
        _session.Beliefs!.Reveal(slot, tiles);

    private static int CellOfUnit(CrowdSimSession session, int dense, CrowdSimulationUnitState st)
    {
        var entity = session.Units.EntityAt(dense);
        int navCellsPerSide = session.Config.NavCellsPerSide, navCellSizeCm = session.Config.NavCellSizeCm;
        if (st.State == (byte)CrowdUnitState.Jump)
        {
            var kin = session.World.Get<CrowdSimulationKinematics>(entity);
            return CrowdSimulationSpace.CellAtStrict(kin.JumpToCm.X, kin.JumpToCm.Y, navCellSizeCm, navCellsPerSide);
        }

        var p = session.World.Get<Components.WorldPositionCm>(entity).Value;
        return CrowdSimulationSpace.CellAtStrict(p.X, p.Y, navCellSizeCm, navCellsPerSide);
    }

    /// <summary>离 target 最近且从 fromComp 可达的可走格(环形外扩,平手取格号小者)。</summary>
    private int NearestReachable(NavContext nav, int fromComp, int target, int navCellsPerSide)
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
        return order;
    }

    private PendingGroup RentGroup()
    {
        if (!_groupPool.TryPop(out var group)) group = new PendingGroup();
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
        for (int i = 0; i < order.Groups.Count; i++) _groupPool.Push(order.Groups[i]);
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
                h = CrowdSimChecksum.Mix(h, (uint)order.Groups.Count);
                for (int g = 0; g < order.Groups.Count; g++)
                {
                    PendingGroup pg = order.Groups[g];
                    h = CrowdSimChecksum.Mix(h, (uint)pg.GroupId);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.PlanSeq);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.NavId);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.Goal);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.RequestId);
                    h = CrowdSimChecksum.Mix(h, (uint)pg.Version);
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
            h = CrowdSimChecksum.Mix(h, (uint)refresh.GroupId);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.Seq);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.NavId);
            h = CrowdSimChecksum.Mix(h, (uint)refresh.Version);
        }

        h = CrowdSimChecksum.Mix(h, (uint)_refreshQueue.Count);
        for (int i = 0; i < _refreshQueue.Count; i++)
            h = CrowdSimChecksum.Mix(h, (uint)_refreshQueue[i].Id);
        return h;
    }
}
