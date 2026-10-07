using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
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
    private int _seq;
    private bool _loggedFirstPlan;
    private bool _loggedFirstApply;

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
                g.Goal = CrowdDeployment.NearestPassable(session.Navs[g.NavId], link.GoalCell);
                g.GoalComp = g.Goal >= 0 ? session.Navs[g.NavId].Comp[g.Goal] : -2;
                g.Flow = null;
                g.Leader = null;
                g.Planning = true;
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

                if (best < 0 || (g.Goal >= 0 && session.Navs[g.NavId].CanReach(best, g.GoalComp))) continue;
                g.Goal = NearestReachable(session.Navs[g.NavId], best, link.GoalCell, n);
                g.GoalComp = g.Goal >= 0 ? session.Navs[g.NavId].Comp[g.Goal] : -2;
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
            if (g.Goal < 0 || !session.Navs[g.NavId].CanReach(CompAtLevel(session, g, cell, st.Level), g.GoalComp))
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

            st.Order = (uint)FindOrderIdOf(session, g.Id);
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
                po.Groups.Add(new PendingGroup { GroupId = g.Id, PlanSeq = ++_seq, NavId = g.NavId, Goal = g.Goal, RequestId = reqId });
                if (!byLayer.TryGetValue(g.LayerIdx, out var bucket)) byLayer[g.LayerIdx] = bucket = new List<int>();
                bucket.Add(g.Id);
            }

            foreach (var (layerIdx, bucket) in byLayer)
            {
                int strictestId = bucket[0];
                foreach (int gid in bucket)
                {
                    if (session.Navs[session.Groups.Groups[gid]!.NavId].ClearanceCells >
                        session.Navs[session.Groups.Groups[strictestId]!.NavId].ClearanceCells)
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

                var nav = session.Navs[strictest.NavId];
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
        if (_pending.Count > 0 && _service.Faulted)
        {
            throw new PathServiceFaultException(_service.FaultMessage ?? "路径服务故障(原因未记录)");
        }

        int k = 0;
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
        }
        else
        {
            while (k < _pending.Count && _pending[k].DueTick <= tick) k++;
        }

        for (int i = 0; i < k; i++)
        {
            ApplyPlan(_pending[i]);
        }

        if (k > 0) _pending.RemoveRange(0, k);
        if (k > 0 && !_loggedFirstApply)
        {
            _loggedFirstApply = true;
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation first plan applied at tick {tick}.");
        }

        return true;
    }

    private void ApplyPlan(PendingPlan plan)
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
                Fix64 d = session.ProfileRadiusCm(units.ProfileIdAt(i)) * 2 * fc.SpacingScale;
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
                ToVec2Path(leaderResult.Points, cellSizeCm), strictest.LayerIdx, session.Navs[strictest.NavId],
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
                if (c >= 0 && strictestPassable(session.Navs[strictest.NavId], c) &&
                    Math.Abs(c % cfg.NavCellCount - pl.StartCell % cfg.NavCellCount) <= 1 &&
                    Math.Abs(c / cfg.NavCellCount - pl.StartCell / cfg.NavCellCount) <= 1)
                {
                    leader.SetStart(cx, cy);
                }
            }

            // D61:反转继承 mirror;D62 同向继承朝向(近似同向才继,真转弯立刻按新朝向排槽)
            if (pl.Prev is { } prev)
            {
                leader.InheritFrom(prev);
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

    private static int FindOrderIdOf(CrowdSimSession session, int groupId)
    {
        var list = session.Orders.List;
        for (int i = 0; i < list.Count; i++)
        {
            for (int k = 0; k < list[i].Groups.Count; k++)
            {
                if (list[i].Groups[k].GroupId == groupId) return list[i].Id;
            }
        }

        return 0;
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
        session.Navs[g.NavId].CompAt(cell, level);

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
