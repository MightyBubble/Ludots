using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 指令规划器(planner.js 的 S5 子集移植):指令 → 逐组(目标格/可达替代/成员/出发簇)→
/// 路径服务请求(固定生效帧)→ 答复落帧时建领队 + 排阵型槽位 + 挂流场。
/// 重烘焙 / 迷雾认知 / 走廊扩展属于 S7/S5-b,不在此层。领队路径无答复(D33)= 全组不可达。
/// </summary>
public sealed class CrowdSimPlanner
{
    private readonly CrowdSimSession _session;
    private readonly PathQueryService _service;
    private readonly List<PendingPlan> _pending = new();
    private readonly List<PendingRefresh> _pendingRefreshes = new();
    /// <summary>待刷新流场的组队列(S7 队伍反应;每 tick 预算 maxRefreshesPerTick)。</summary>
    private readonly List<CrowdNavGroupSet.Group> _refreshQueue = new();
    private int _seq;
    private bool _loggedFirstPlan;
    private bool _loggedFirstApply;

    /// <summary>路径服务(RT-05:结构变更与在途路径计算共用同一份导航态,经它的互斥锁串行)。</summary>
    public PathQueryService Service => _service;
    /// <summary>当前待刷新流场的队列长度(重烘报告字段)。</summary>
    public int RefreshQueueCount => _refreshQueue.Count;

    private sealed class PendingPlan
    {
        public required int DueTick { get; init; }
        public required List<PendingOrder> Orders { get; init; }
    }

    private sealed class PendingOrder
    {
        public required CrowdOrder Order { get; init; }
        public required List<PendingGroup> Groups { get; init; }
        public required List<PendingLeader> Leaders { get; init; }
    }

    private sealed class PendingGroup
    {
        public required int GroupId { get; init; }
        public required int PlanSeq { get; init; }
        public required int NavId { get; init; }
        public required int Goal { get; init; }
        public required int RequestId { get; init; }
        /// <summary>请求时的导航版本:答复落帧时版本变了 = 陈旧答复,按 F-5 裁决。</summary>
        public required int Version { get; init; }
    }

    /// <summary>流场刷新的到点答复(S7 队伍反应:同走廊在新导航上重建流场,领队与槽位不动)。</summary>
    private sealed class PendingRefresh
    {
        public required int RequestId { get; init; }
        public required int DueTick { get; init; }
        public required int GroupId { get; init; }
        public required CrowdNavGroupSet.Group Group { get; init; }
        public required int Seq { get; init; }
        public required int NavId { get; init; }
        public required int Version { get; init; }
    }

    private sealed class PendingLeader
    {
        public required int StrictestGroupId { get; init; }
        public required int[] BucketGroupIds { get; init; }
        public required int StartCell { get; init; }
        public required int RequestId { get; init; }
        public required CrowdLeader? Prev { get; init; }
    }

    public CrowdSimPlanner(CrowdSimSession session, PathQueryService service)
    {
        _session = session;
        _service = service;
    }

    public int PendingCount => _pending.Count;

    /// <summary>为一批指令起规划(plan 移植):组目标/可达替代/成员标记/出发代表格,再发请求。</summary>
    public void Plan(IReadOnlyList<CrowdOrder> orders, int tick)
    {
        var session = _session;
        var cfg = session.Config;
        int n = cfg.NavCellCount;
        foreach (var o in orders)
        {
            o.Leaders.Clear();
            foreach (var link in o.Groups)
            {
                var g = session.Groups.Groups[link.GroupId]!;
                g.Goal = CrowdDeployment.NearestPassable(session.ResolveNavContext(g.NavId), link.GoalCell);
                g.GoalComp = g.Goal >= 0 ? session.ResolveNavContext(g.NavId).Comp[g.Goal] : -2;
                g.Flow = null;
                g.Leader = null;
                g.Planning = true;
                g.StateSeq++;
            }
        }

        // 目标不可达 → 换最近的可达格(F-09):按组员多数所在连通域判定
        foreach (var o in orders)
        {
            foreach (var link in o.Groups)
            {
                var g = session.Groups.Groups[link.GroupId]!;
                var tally = new Dictionary<int, int>();
                var units = session.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
                    if (st.GroupId != g.Id) continue;
                    int cell = CellOfUnit(session, i, st);
                    int c = CompAtLevel(session, g, cell, st.Level);
                    tally[c] = tally.TryGetValue(c, out var cnt) ? cnt + 1 : 1;
                }

                int best = -1, bestN = 0;
                foreach (var (c, cnt) in tally)
                {
                    if (c >= 0 && cnt > bestN) { best = c; bestN = cnt; }
                }

                if (best < 0 || (g.Goal >= 0 && session.ResolveNavContext(g.NavId).CanReach(best, g.GoalComp))) continue;
                g.Goal = NearestReachable(session.ResolveNavContext(g.NavId), best, link.GoalCell, n);
                g.GoalComp = g.Goal >= 0 ? session.ResolveNavContext(g.NavId).Comp[g.Goal] : -2;
                link.GoalCell = g.Goal;
            }
        }

        // 逐单位:成员/可达性/状态;跳跃中的单位从落点出发规划
        var membersByGroup = new Dictionary<int, List<int>>();
        foreach (var o in orders)
        {
            foreach (var link in o.Groups)
            {
                var g = session.Groups.Groups[link.GroupId]!;
                membersByGroup[g.Id] = new List<int>();
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
        var pendingOrders = new List<PendingOrder>();
        foreach (var o in orders)
        {
            var po = new PendingOrder { Order = o, Groups = new(), Leaders = new() };
            var byLayer = new Dictionary<int, List<int>>();
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
                po.Groups.Add(new PendingGroup { GroupId = g.Id, PlanSeq = ++_seq, NavId = g.NavId, Goal = g.Goal, RequestId = reqId, Version = session.ResolveNavContext(g.NavId).Version });
                if (!byLayer.TryGetValue(g.LayerIdx, out var bucket)) byLayer[g.LayerIdx] = bucket = new List<int>();
                bucket.Add(g.Id);
            }

            foreach (var (layerIdx, bucket) in byLayer)
            {
                int strictestId = bucket[0];
                foreach (int gid in bucket)
                {
                    if (session.ResolveNavContext(session.Groups.Groups[gid]!.NavId).ClearanceCells >
                        session.ResolveNavContext(session.Groups.Groups[strictestId]!.NavId).ClearanceCells)
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
                    CrowdDeployment.CellAt(sx / cnt, sy / cnt, n, cfg.NavCellSizeCm));
                if (lc < 0 || !nav.CanReach(nav.Comp[lc], strictest.GoalComp)) lc = RepresentativeCell(session, membersByGroup[strictestId]);
                CrowdLeader? prev = null;
                foreach (int gid in bucket)
                {
                    if (prev == null && session.Groups.Groups[gid]!.Leader is { Done: false } l) prev = l;
                }

                foreach (int gid in bucket) session.Groups.Groups[gid]!.PrevLeader = null;
                int leaderReq = _service.Request(new PathQuery(strictest.NavId, lc, strictest.Goal, 0), tick);
                po.Leaders.Add(new PendingLeader
                {
                    StrictestGroupId = strictestId,
                    BucketGroupIds = bucket.ToArray(),
                    StartCell = lc,
                    RequestId = leaderReq,
                    Prev = prev,
                });
            }

            if (po.Groups.Count > 0) pendingOrders.Add(po);
        }

        if (pendingOrders.Count > 0)
        {
            _pending.Add(new PendingPlan { DueTick = tick + _session.Config.Planning.LatencyTicks, Orders = pendingOrders });
            if (!_loggedFirstPlan)
            {
                _loggedFirstPlan = true;
                int groups = 0, leaders = 0;
                foreach (var po in pendingOrders) { groups += po.Groups.Count; leaders += po.Leaders.Count; }
                Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"CrowdSimulation first plan at tick {tick}: orders={pendingOrders.Count} groups={groups} leaders={leaders}.");
            }
        }
    }

    /// <summary>答复落帧(applyDue 移植):到点的按请求序应用;有一个没回来,仿真停摆(返回 false)。
    /// block=true(无头对拍/回放):同步等答复(AwaitDue,超时即故障)——与引擎停摆的语义相同,
    /// 答复内容与生效帧不变,只是调用线程代替"下一帧重试"等在那里。
    /// 服务故障(答复永不回)直接抛——不静默停摆。</summary>
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

        var stale = new List<CrowdNavGroupSet.Group>();
        for (int i = 0; i < k; i++)
        {
            ApplyPlan(_pending[i], stale);
        }

        for (int i = 0; i < rk; i++)
        {
            ApplyRefresh(_pendingRefreshes[i]);
        }

        if (k > 0) _pending.RemoveRange(0, k);
        if (rk > 0) _pendingRefreshes.RemoveRange(0, rk);
        if (stale.Count > 0)
        {
            // F-5:在途期间导航变了(重烘)的规划已按答复应用,现在按现行导航裁决:
            // 目标被挡或无场 → 重规划;否则刷新流场(它来自旧导航)
            var orders = AfterStalePlan(stale);
            if (orders.Count > 0) Plan(orders, tick);
        }

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
            var live = new Dictionary<int, (PendingGroup Pg, PathResult Result)>();
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
                g.Planning = false;
                live[pg.GroupId] = (pg, result);
            }

            FinishOrder(po, live);
        }
    }

    /// <summary>领队 + 阵型槽位 + 到达足印(finishOrder 移植;质心与朝向继承按落帧时刻的成员位置)。</summary>
    private void FinishOrder(PendingOrder po, Dictionary<int, (PendingGroup Pg, PathResult Result)> live)
    {
        var o = po.Order;
        var session = _session;
        var cfg = session.Config;
        var fc = cfg.Formation;
        int cellSizeCm = cfg.NavCellSizeCm;
        Fix64 cs = Fix64.FromInt(cellSizeCm);

        // 成员名单按落帧时刻重收(请求后单位可能移动)
        var membersByGroup = new Dictionary<int, List<int>>();
        var units = session.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
            if (st.Order == o.Id && st.GroupId >= 0 && live.ContainsKey(st.GroupId))
            {
                (membersByGroup.TryGetValue(st.GroupId, out var m) ? m : membersByGroup[st.GroupId] = new List<int>()).Add(i);
            }
        }

        Fix64 area = Fix64.Zero;
        int total = 0;
        foreach (var (gid, _) in live)
        {
            if (!membersByGroup.TryGetValue(gid, out var mem)) continue;
            foreach (var i in mem)
            {
                Fix64 d = units.PersonalRadiusCmAt(i) * 2 * fc.SpacingScale;
                area += d * d;
            }

            total += mem.Count;
        }

        if (total == 0) return;
        var shape = CrowdFormations.ShapeOf(cfg.Formations, o.ShapeId);
        var blocks = new List<(Fix64 Width, IReadOnlyList<int> Members)>();
        Fix64 ext = Fix64.Zero;
        foreach (var pl in po.Leaders)
        {
            var bucket = new List<int>();
            foreach (int gid in pl.BucketGroupIds)
            {
                if (live.ContainsKey(gid)) bucket.Add(gid);
            }

            if (bucket.Count == 0) continue;
            var leaderResult = _service.AwaitDue(pl.RequestId);
            var strictest = session.Groups.Groups[pl.StrictestGroupId]!;
            if (!leaderResult.Reachable || leaderResult.Points == null)
            {
                // D33:路径服务无路 → 桶内成员全部不可达,组放弃(清场清目标)
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

            var leader = new CrowdLeader(
                ToVec2Path(leaderResult.Points, cellSizeCm), strictest.LayerIdx, session.ResolveNavContext(strictest.NavId),
                o.Mode == CrowdOrderMode.Preserve, o.Face, fc.LeaderLookAhead * cs);

            // D62:质心取落帧时刻;起点格是请求时刻的,接受其 8 邻域
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
                int c = CrowdDeployment.CellAt(cx, cy, cfg.NavCellCount, cellSizeCm);
                if (c >= 0 && strictestPassable(session.ResolveNavContext(strictest.NavId), c) &&
                    Math.Abs(c % cfg.NavCellCount - pl.StartCell % cfg.NavCellCount) <= 1 &&
                    Math.Abs(c / cfg.NavCellCount - pl.StartCell / cfg.NavCellCount) <= 1)
                {
                    leader.SetStart(cx, cy);
                }
            }

            // D61:反转继承 mirror;D62 同向继承朝向(近似同向才继,真转弯立刻按新朝向排槽)
            if (pl.Prev is { } prev)
            {
                leader.InheritFrom(prev, fc.MirrorFlipDot, fc.HeadingInheritDot);
            }

            o.Leaders.Add(leader);
            var mem = new List<int>();
            foreach (int gid in bucket)
            {
                var g = session.Groups.Groups[gid]!;
                g.Leader = leader;
                g.LosAll = true; // D58:首个意图趟全员重查槽位视线
                foreach (int i in membersByGroup[gid])
                {
                    mem.Add(i);
                    var e = units.EntityAt(i);
                    var st = session.World.Get<CrowdSimulationUnitState>(e);
                    if (st.State == (byte)CrowdUnitState.Arrived)
                    {
                        st.State = (byte)CrowdUnitState.Moving; // D56:路径落地才离站
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
                Fix64 widthShare = o.FaceWidthM > Fix64.Zero
                    ? Fix64.FromInt((int)(o.FaceWidthM.ToLong() * 100)) * mem.Count / total
                    : Fix64.Zero;
                blocks.Add((CrowdFormations.AssignSlots(session, mem, leader.Hx, leader.Hy, fc.SpacingScale, shape, widthShare, leader.Mirror), mem));
            }
        }

        if (blocks.Count > 1) CrowdFormations.LayoutSideBySide(session, blocks);
        // reachCells:聚拢按触碰判到达只认这个半径;阵型单到达视野上限也用它
        if (o.Mode == CrowdOrderMode.Converge)
        {
            o.ReachCells = (int)Fix64.Max(fc.MinClumpCells, (fc.ClumpSlack * Fix64Math.Sqrt(area / Fix64.Pi)) / cs).ToLong();
        }
        else if (o.Mode == CrowdOrderMode.Preserve)
        {
            o.ReachCells = (int)(ext / cs + fc.SettleRadiusCells).ToLong();
        }
        else
        {
            Fix64 w2 = Fix64.Zero;
            foreach (var b in blocks) w2 += b.Width;
            o.ReachCells = (int)((w2 / 2 + Fix64Math.Sqrt(area)) / cs + fc.SettleRadiusCells).ToLong();
        }
    }

    private static bool strictestPassable(NavContext nav, int cell) => nav.Passable[cell] != 0;

    /// <summary>PathResult.Points 是格系坐标(格心 +0.5);领队走世界厘米,出服务边界处 ×cellSize 换算。</summary>
    private static List<Fix64Vec2> ToVec2Path(Fix64[] points, int cellSizeCm)
    {
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        var path = new List<Fix64Vec2>(points.Length / 2);
        for (int i = 0; i + 1 < points.Length; i += 2) path.Add(new Fix64Vec2(points[i] * cs, points[i + 1] * cs));
        return path;
    }

    private void Recycle(PathResult result)
    {
        if (result.Flow is { } f) RecycleFlow(f);
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
            return; // 被更新请求取代:静默丢弃(D20)
        }

        var result = _service.AwaitDue(pr.RequestId);
        var nav = _session.ResolveNavContext(g.NavId);
        if (g.NavId != pr.NavId || nav.Version != pr.Version)
        {
            // 答复基于旧导航:丢弃并重新排队刷新(参考 applyExtend 的重排队语义)
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
    }

    /// <summary>队伍反应(reactRebake 移植):对看到本次变更的组逐个判定——目标被挡 → 重规划指令;
    /// 行进中且变更落在剩余路线上 → 领队线/成员可达被切断则重规划,否则同走廊刷新流场;
    /// 驻扎组不动槽位。返回需要重规划的指令清单。</summary>
    public List<CrowdOrder> ReactRebake(IReadOnlyList<CrowdNavRebakeContextResult> dirty)
    {
        var session = _session;
        var dirtyByNav = new Dictionary<int, List<int>>();
        foreach (var r in dirty)
        {
            if (r.DirtyTiles.Count > 0) dirtyByNav[r.NavId] = r.DirtyTiles;
        }

        int n = session.Config.NavCellCount;
        var units = session.Units;
        var underway = new HashSet<int>();
        var reach = new Dictionary<int, Fix64>();
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            var st = session.World.Get<CrowdSimulationUnitState>(entity);
            if (!session.Groups.TryGet(st.GroupId, out var g0)) continue;
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Jump) underway.Add(g0.Id);
            if (g0.Flow == null) continue;
            int cell = CellOfUnit(session, i, st);
            var len = g0.Flow.Len[(st.Level != 0 ? n * n : 0) + cell];
            if (!reach.TryGetValue(g0.Id, out var cur) || len > cur) reach[g0.Id] = len;
        }

        var orders = new List<CrowdOrder>();
        var orderSet = new HashSet<int>();
        var hit = new List<CrowdNavGroupSet.Group>();
        foreach (var g in session.Groups.Groups)
        {
            if (g == null || g.Count == 0 || g.OrderId == 0) continue;
            if (!dirtyByNav.TryGetValue(g.NavId, out var tiles) || tiles.Count == 0) continue;
            if (g.Planning) continue; // F-5:在途规划由落帧裁决处理
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

    /// <summary>F-6:行进中成员(脚下格 / 跳跃落点格)的连通域不再可达目标 = 该组的路被切断。</summary>
    private HashSet<int> ReachLost(CrowdSimSession session, List<CrowdNavGroupSet.Group> hit)
    {
        var want = new HashSet<int>();
        foreach (var g in hit) want.Add(g.Id);
        var lost = new HashSet<int>();
        var memo = new Dictionary<(int GroupId, int Comp), bool>();
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
            var key = (st.GroupId, c);
            if (!memo.TryGetValue(key, out var ok)) memo[key] = ok = nav.CanReach(c, g.GoalComp);
            if (!ok) lost.Add(st.GroupId);
        }

        return lost;
    }

    /// <summary>领队剩余路线是否被挡(leaderPathBlocked 移植):沿线四角探边的坏格带;
    /// 桥面托线由 upPass 覆盖(参考端的 wall 集在 C# 领队无对应物,省略)。</summary>
    private static bool LeaderPathBlocked(CrowdLeader? leader, NavContext nav, CrowdSimSession session)
    {
        if (leader == null || leader.Done) return false;
        int cs = session.Config.NavCellSizeCm, n = session.Config.NavCellCount;
        Fix64 step = Fix64.FromInt(cs) / 2, e = Fix64.FromInt(cs) / 4;
        var pass = nav.Passable;
        var up = nav.UpPass;
        bool bad(int c) => pass[c] == 0 && up[c] == 0;
        bool blockedAt(Fix64 x, Fix64 y) =>
            bad(CrowdSimCell.At(x - e, y - e, cs, n)) && bad(CrowdSimCell.At(x + e, y - e, cs, n)) &&
            bad(CrowdSimCell.At(x - e, y + e, cs, n)) && bad(CrowdSimCell.At(x + e, y + e, cs, n));
        var path = leader.Path;
        Fix64 ax = leader.X, ay = leader.Y;
        for (int k = leader.Seg; k < path.Count; k++)
        {
            Fix64 bx = path[k].X, by = path[k].Y;
            int m = (int)Fix64.Ceiling(CrowdFix.Hypot(bx - ax, by - ay) / step).ToLong();
            for (int s = 1; s <= m; s++)
            {
                if (blockedAt(ax + (bx - ax) * s / m, ay + (by - ay) * s / m)) return true;
            }

            ax = bx;
            ay = by;
        }

        return false;
    }

    /// <summary>变更是否落在组的剩余路线上(onRemainingRoute 移植):走廊掩码触到脏 tile,
    /// 且脏 tile 内存在某成员剩余路线长度可达的格——只用掩码会把身后的变更也算进来。</summary>
    private static bool OnRemainingRoute(
        CrowdSimSession session,
        CrowdNavGroupSet.Group g,
        List<int> tiles,
        Dictionary<int, Fix64> reach)
    {
        var f = g.Flow!;
        int n = session.Config.NavCellCount, n2 = n * n;
        var nav = session.ResolveNavContext(g.NavId);
        int s = nav.Hpa!.ClusterSize, c = nav.Hpa.ClustersPerSide;
        if (!reach.TryGetValue(g.Id, out var r)) return false;
        foreach (int t in tiles)
        {
            if (f.Mask![t] == 0) continue;
            int x0 = t % c * s, y0 = t / c * s;
            int x1 = Math.Min(n, x0 + s), y1 = Math.Min(n, y0 + s);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (f.Len[y * n + x] <= r || f.Len[n2 + y * n + x] <= r) return true;
                }
            }
        }

        return false;
    }

    /// <summary>同走廊流场刷新排队(不在队列才入队;预算在 ProcessRefreshes 扣)。</summary>
    public void RefreshFlow(CrowdNavGroupSet.Group g)
    {
        if (!_refreshQueue.Contains(g)) _refreshQueue.Add(g);
    }

    /// <summary>每 tick 预算内的流场刷新提交(processRefreshes 移植;生效帧 = tick + refreshLatencyTicks)。
    /// 参考端还有走廊外单位的 stray 扩展与同目标组共乘——C# 无走廊扩展,共乘属缓存优化,均不在本层。</summary>
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
        _pendingRefreshes.Add(new PendingRefresh
        {
            RequestId = id,
            DueTick = tick + _session.Config.Planning.RefreshLatencyTicks,
            GroupId = g.Id,
            Group = g,
            Seq = g.StateSeq,
            NavId = g.NavId,
            Version = nav.Version,
        });
    }

    /// <summary>F-5 落帧裁决(afterStalePlan 移植):陈旧答复的组——无场或目标被挡 → 重规划;
    /// 否则按现行导航刷新流场。返回需要重规划的指令。</summary>
    private List<CrowdOrder> AfterStalePlan(List<CrowdNavGroupSet.Group> stale)
    {
        var orders = new List<CrowdOrder>();
        var orderSet = new HashSet<int>();
        var hit = new List<CrowdNavGroupSet.Group>();
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
        foreach (var o in session.Orders.List)
        {
            if (o.Id != g.OrderId) continue;
            if (seen.Add(o.Id)) orders.Add(o);
            return;
        }
    }

    /// <summary>组代表出发格:取一个地面成员格(近质心),无地面成员时取任意成员的格。</summary>
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

    /// <summary>认知槽变更后的组重定向(retarget 移植,F02):moved 组——目标被挡 → 重规划;
    /// 行进中 → ReachLost / 领队线被挡裁决;驻扎组 → 流场不属于新导航才刷新(真相流场经
    /// sharesFlow 判定可复用——不是纯优化,刷新计数是对拍面)。stale = 原位揭示踩到真变
    /// tile 的组(只刷新,不重规划)。返回需重规划的指令。</summary>
    public List<CrowdOrder> Retarget(IReadOnlyList<CrowdNavGroupSet.Group> moved, HashSet<CrowdNavGroupSet.Group>? stale = null)
    {
        var session = _session;
        var units = session.Units;
        var underway = new HashSet<int>();
        for (int i = 0; i < units.Count; i++)
        {
            var st = session.World.Get<CrowdSimulationUnitState>(units.EntityAt(i));
            if (st.State == (byte)CrowdUnitState.Moving || st.State == (byte)CrowdUnitState.Jump) underway.Add(st.GroupId);
        }

        var orders = new List<CrowdOrder>();
        var orderSet = new HashSet<int>();
        var hit = new List<CrowdNavGroupSet.Group>();
        foreach (var g in moved)
        {
            if (g.Count == 0 || g.OrderId == 0) continue;
            if (g.Planning) continue; // F-5:在途规划由落帧裁决
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

    /// <summary>F-3:受触指令的领队重挂到同指令仍由它带队、净空最严的组的当前导航——
    /// 领队不踩已弃变体。</summary>
    public void RelinkLeaders(HashSet<CrowdOrder> touched)
    {
        foreach (var o in touched)
        {
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

    // D13:净空更大更严;同净空取更大下标(确定)
    private bool StricterNav(CrowdNavGroupSet.Group g, CrowdNavGroupSet.Group m)
    {
        int a = _session.ResolveNavContext(g.NavId).ClearanceCells;
        int b = _session.ResolveNavContext(m.NavId).ClearanceCells;
        return a > b || (a == b && g.RIdx > m.RIdx);
    }

    /// <summary>原位揭示(revealBelief 移植):槽的已建变体只重烘被揭 tile。C# 流场挂在组上
    /// (无全局流场缓存),踩脏的组由调用方的 stale 集合刷新——参考端的 flowCache 清理无对应动作。</summary>
    public Dictionary<int, List<int>> RevealBelief(int slot, List<int> tiles) =>
        _session.Beliefs!.Reveal(slot, tiles);

    private static int CellOfUnit(CrowdSimSession session, int dense, CrowdSimulationUnitState st)
    {
        var entity = session.Units.EntityAt(dense);
        int n = session.Config.NavCellCount, cs = session.Config.NavCellSizeCm;
        if (st.State == (byte)CrowdUnitState.Jump)
        {
            var kin = session.World.Get<CrowdSimulationKinematics>(entity);
            return CrowdDeployment.CellAt(kin.JumpToCm.X, kin.JumpToCm.Y, n, cs);
        }

        var p = session.World.Get<Components.WorldPositionCm>(entity).Value;
        return CrowdDeployment.CellAt(p.X, p.Y, n, cs);
    }

    /// <summary>离 target 最近且从 fromComp 可达的可走格(D34 环形外扩,平手取格号小者)。</summary>
    private static int NearestReachable(NavContext nav, int fromComp, int target, int n)
    {
        if (fromComp < 0) return -1;
        int tx = target % n, ty = target / n;
        var pass = nav.Passable;
        var ok = new Dictionary<int, bool>();
        int best = -1;
        long bd = long.MaxValue;
        for (int r = 0; r < n && (long)r * r <= bd; r++)
        {
            int x0 = tx - r, x1 = tx + r, y0 = ty - r, y1 = ty + r;
            for (int y = Math.Max(0, y0); y <= Math.Min(n - 1, y1); y++)
            {
                if (y == y0 || y == y1)
                {
                    for (int x = Math.Max(0, x0); x <= Math.Min(n - 1, x1); x++) Test(x, y);
                }
                else
                {
                    if (x0 >= 0) Test(x0, y);
                    if (x1 < n && r != 0) Test(x1, y);
                }
            }
        }

        return best;

        void Test(int x, int y)
        {
            int c = y * n + x;
            if (pass[c] == 0) return;
            long dx = x - tx, dy = y - ty, d = dx * dx + dy * dy;
            if (d > bd || (d == bd && c > best)) return;
            int k = nav.Comp[c];
            if (!ok.TryGetValue(k, out bool r)) ok[k] = r = nav.CanReach(fromComp, k);
            if (r) { best = c; bd = d; }
        }
    }
}
