using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 移动意图层(intent.js computeIntents 移植):纯转向决策——读路径产物(指令/领队/槽位/流场),
/// 写期望速度(厘米/秒)与移动状态机(MOVING→ARRIVED/UNREACHABLE,被推离休息锚点则回到 MOVING),
/// 永不直接挪位置。到达按单位自己的目的地判:槽位可见时用槽位直线距,否则用流场绷紧路线长;
/// 地形/链接代价不进任何到达阈值。槽位↔流场连续交叉淡化(blend),视线判定带滞回,槽位跟踪
/// = 领队速度前馈 + 比例修正——防抖动三件套,一个不少。
/// </summary>
public static class CrowdIntents
{
    /// <summary>允许的路线/直线绕行比上限(D59:去槽位的路线不能比直线长太多,否则走流场)。</summary>
    private static readonly Fix64 RouteRatio = Fix64.FromFloat(1.5f);

    public static void Compute(CrowdMovementKernel k, Fix64 dt)
    {
        var session = k.Session;
        var world = session.World;
        var units = session.Units;
        var groups = session.Groups;
        int n = units.Count;
        int navN = session.Config.NavCellCount;
        Fix64 cs = Fix64.FromInt(session.Config.NavCellSizeCm);
        Fix64 worldSizeCm = cs * navN;
        int n2 = navN * navN;
        Fix64 bStep = Fix64.Min(Fix64.OneValue, k.BlendRate * dt);
        Fix64 commit = k.BlendCommit;
        int every = k.SlotCheckInterval;
        Fix64 wake2 = Square(cs * k.WakeDistanceCells);
        Fix64 arriveSlot2 = Square(cs * k.SlotArriveCells);
        Fix64 settle2 = Square(cs * k.SettleRadiusCells);
        Fix64 stallV2 = k.StallSpeedRatio * k.StallSpeedRatio;

        for (int i = 0; i < n; i++)
        {
            k.Intent[i] = Fix64Vec2.Zero;
            var entity = units.EntityAt(i);
            var state = world.Get<CrowdSimulationUnitState>(entity);
            var kin = world.Get<CrowdSimulationKinematics>(entity);

            // ARRIVED 且无速度:只在被推离休息锚点足够远时醒来(S5 无休眠位,恒醒)
            if (state.State == (byte)CrowdUnitState.Arrived)
            {
                if (kin.Velocity.X == Fix64.Zero && kin.Velocity.Y == Fix64.Zero)
                {
                    var pos0 = world.Get<WorldPositionCm>(entity).Value;
                    Fix64 ox = kin.RestCm.X - pos0.X, oy = kin.RestCm.Y - pos0.Y;
                    if (ox * ox + oy * oy > wake2)
                    {
                        state.State = (byte)CrowdUnitState.Moving;
                        world.Set(entity, state);
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }

            if (!groups.TryGet(state.GroupId, out var g)) continue;

            // 重发指令等路径答复:保持当前速度,不刹车
            if (g.Flow == null && g.Planning && state.State == (byte)CrowdUnitState.Moving)
            {
                k.Intent[i] = kin.Velocity;
                continue;
            }

            if (g.Flow == null || (state.State != (byte)CrowdUnitState.Moving && state.State != (byte)CrowdUnitState.Arrived))
            {
                continue;
            }

            var flow = g.Flow;
            var leader = g.Leader!;
            var nav = session.Navs[g.NavId];
            var order = OrderOf(session, g.OrderId);
            bool converge = order?.Mode == CrowdOrderMode.Converge;

            var pos = world.Get<WorldPositionCm>(entity).Value;
            Fix64 px = pos.X, py = pos.Y;
            int cx = CellCoord(px, cs), cy = CellCoord(py, cs);
            int cell = cy * navN + cx;

            // LY-5 层切换:双层可走的桥头格上,取到终点路线更短的那层;等长(跨层边 0 代价)
            // 时按路点打破平局——场会把本层格指向另一层同格。
            if (nav.Portal[cell] != 0 && nav.Passable[cell] != 0 && nav.UpPass[cell] != 0)
            {
                Fix64 a = flow.Integ[cell], b = flow.Integ[n2 + cell];
                int cur = state.Level;
                int other = cur != 0 ? cell : n2 + cell;
                if (a < b) state.Level = 0;
                else if (b < a) state.Level = 1;
                else if (flow.Wp[cur != 0 ? n2 + cell : cell] == other) state.Level = (byte)(cur ^ 1);
            }

            int lv = state.Level;
            byte[] pass = lv != 0 ? nav.UpPass : nav.Passable;

            // D32:到达优先于跳跃——已在目标可达范围内的单位不起跳
            if (lv == 0 && flow.Lk != null && flow.Lk[cell] >= 0 &&
                !(CrowdFlowSample.RouteLength(flow, navN, session.Config.NavCellSizeCm, px, py, cell, lv) <= k.GoalArriveCells))
            {
                if (TryStartJump(world, entity, ref state, ref kin, flow.Links!, flow.Lk[cell], nav, navN, session.Config.NavCellSizeCm))
                {
                    world.Set(entity, state);
                    world.Set(entity, kin);
                    continue;
                }
            }

            Fix64 spd = ProfileSpeedOf(session, world.Get<CrowdSimulationAgent>(entity).ProfileId);
            Fix64 costHere = (lv != 0 ? nav.UpCost : nav.Cost)[cell];
            spd = spd / (costHere == Fix64.Zero ? Fix64.OneValue : costHere);

            // 槽位世界坐标 = 领队位置 + 槽位偏移旋进领队系
            Fix64 lat = kin.SlotOffsetCm.X, back = kin.SlotOffsetCm.Y;
            Fix64 tx = leader.X - leader.Hy * lat - leader.Hx * back;
            Fix64 ty = leader.Y + leader.Hx * lat - leader.Hy * back;
            int sc = tx >= Fix64.Zero && ty >= Fix64.Zero && tx < worldSizeCm && ty < worldSizeCm
                ? CellCoord(ty, cs) * navN + CellCoord(tx, cs)
                : -1;
            // 槽位在地面(目标层)才算数;桥面上只跟流场
            bool slotOk = !converge && lv == 0 && sc >= 0 && pass[sc] == 1 && nav.Comp[sc] == g.GoalComp;

            if (!slotOk)
            {
                state.Mode = 0;
            }
            else if (g.LosAll || (int)(units.HandleAt(i) + (uint)k.Tick) % every == 0)
            {
                Fix64 sightIn = Fix64.Max(k.SlotSightCells, Fix64.FromInt(order?.ReachCells ?? 1));
                Fix64 lim = state.Mode != 0 ? sightIn * k.SightHysteresis : sightIn;
                int scx = sc % navN, scy = sc / navN;
                bool vis = Fix64.Abs(Fix64.FromInt(scx - cx)) <= lim && Fix64.Abs(Fix64.FromInt(scy - cy)) <= lim
                    && NavGridSteps.LineOfSight(pass, navN, cell, sc);
                if (vis)
                {
                    // D59 第二判据:去槽位的流场路线不能比直线长太多(场外 = ∞ 时只看 LOS,D60 转向接管)
                    Fix64 ra = CrowdFlowSample.RouteLength(flow, navN, session.Config.NavCellSizeCm, px, py, cell, 0);
                    Fix64 rb = CrowdFlowSample.RouteLength(flow, navN, session.Config.NavCellSizeCm, tx, ty, sc, 0);
                    if (ra < CrowdFlowSample.Unreachable / 4 && rb < CrowdFlowSample.Unreachable / 4 &&
                        ra - rb > CrowdFix.Hypot(tx - px, ty - py) / cs * RouteRatio + Fix64.OneValue)
                    {
                        vis = false;
                    }
                }

                state.Mode = vis ? (byte)1 : (byte)0;
            }

            Fix64 w = kin.Blend;
            w = state.Mode != 0 ? Fix64.Min(Fix64.OneValue, w + bStep) : Fix64.Max(Fix64.Zero, w - bStep);
            kin.Blend = w;
            Fix64 dvx = Fix64.Zero, dvy = Fix64.Zero;
            bool arrived = false;
            Fix64 route = Fix64.FromInt(-1);
            Fix64 ox2 = tx - px, oy2 = ty - py;
            Fix64 slotD2 = CrowdFix.DistSq(ox2, oy2);
            if (w > Fix64.Zero)
            {
                if (leader.Done && w > commit && slotD2 < arriveSlot2)
                {
                    arrived = true;
                }
                else
                {
                    // 槽位跟踪:领队速度前馈 + 比例修正(无 bang-bang)
                    Fix64 sx = leader.Vx + ox2 * k.SlotGain, sy = leader.Vy + oy2 * k.SlotGain;
                    Fix64 cap = spd * k.CatchUp, m2 = sx * sx + sy * sy;
                    if (m2 > cap * cap)
                    {
                        Fix64 s = cap / Fix64Math.Sqrt(m2);
                        sx *= s;
                        sy *= s;
                    }

                    dvx = sx * w;
                    dvy = sy * w;
                }
            }

            if (!arrived && w < Fix64.OneValue)
            {
                Fix64 fx, fy;
                route = CrowdFlowSample.SampleFlow(flow, pass, navN, session.Config.NavCellSizeCm, px, py, cell, out fx, out fy, lv);
                if (route >= CrowdFlowSample.Unreachable / 4)
                {
                    if (!nav.CanReach(CompAt(nav, cell, lv), g.GoalComp))
                    {
                        state.State = (byte)CrowdUnitState.Unreachable;
                        world.Set(entity, state);
                        world.Set(entity, kin);
                        continue;
                    }

                    k.OnStray?.Invoke(g.Id, cell);
                    // D60:走廊外保持朝领队速度,等走廊扩展落地
                    Fix64 lx = leader.X - px, ly = leader.Y - py;
                    Fix64 ld = CrowdFix.Hypot(lx, ly);
                    if (ld > Fix64.FromFloat(1e-6f))
                    {
                        dvx += (lx / ld) * spd * (Fix64.OneValue - w);
                        dvy += (ly / ld) * spd * (Fix64.OneValue - w);
                    }
                }
                else if (!slotOk && route <= k.GoalArriveCells && w < commit)
                {
                    arrived = true; // 聚拢指令:目标格就是目的地
                }
                else
                {
                    // 车道展开:沿流场垂直方向朝自己槽位车道漂移,队伍以阵型宽度行进,不收成一条线
                    Fix64 cap = spd * k.LaneSpread;
                    Fix64 latDrift = converge ? Fix64.Zero : (fx * oy2 - fy * ox2) * k.SlotGain;
                    latDrift = Fix64.Min(cap, Fix64.Max(-cap, latDrift));
                    dvx += (fx * spd - fy * latDrift) * (Fix64.OneValue - w);
                    dvy += (fy * spd + fx * latDrift) * (Fix64.OneValue - w);
                }
            }

            // 群体到达:撞上同指令的静止单位,或近目标后长期无进展——只在靠近自己目的地时才算
            bool near = slotOk
                ? slotD2 <= settle2
                : (route >= Fix64.Zero ? route : CrowdFlowSample.RouteLength(flow, navN, session.Config.NavCellSizeCm, px, py, cell, lv)) <= Fix64.FromInt(order?.ReachCells ?? 1);
            if (!arrived && leader.Done && near)
            {
                Fix64 v2 = kin.Velocity.X * kin.Velocity.X + kin.Velocity.Y * kin.Velocity.Y;
                if (v2 < spd * spd * stallV2)
                {
                    kin.StallSeconds += dt;
                }
                else if (kin.StallSeconds > Fix64.Zero)
                {
                    kin.StallSeconds = Fix64.Max(Fix64.Zero, kin.StallSeconds - dt * k.StallDecay);
                }

                if (kin.StallSeconds > k.SettleTimeSeconds) arrived = true;
            }
            else if (!arrived)
            {
                kin.StallSeconds = Fix64.Zero;
            }

            if (!arrived && near && (leader.Done || converge) &&
                CrowdContact.TouchesRestingPeer(k.Hash, session, i, k.MaxScan))
            {
                arrived = true;
            }

            if (arrived)
            {
                state.State = (byte)CrowdUnitState.Arrived;
                kin.StallSeconds = Fix64.Zero;
                kin.RestCm = new Fix64Vec2(px, py);
            }

            world.Set(entity, state);
            world.Set(entity, kin);
            k.Intent[i] = new Fix64Vec2(dvx, dvy);
        }

        for (int gi = 0; gi < groups.Groups.Count; gi++)
        {
            if (groups.Groups[gi] is { } gg) gg.LosAll = false;
        }
    }

    private static CrowdOrder? OrderOf(CrowdSimSession session, int orderId)
    {
        if (orderId <= 0) return null;
        var list = session.Orders.List;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == orderId) return list[i];
        }

        return null;
    }

    private static Fix64 ProfileSpeedOf(CrowdSimSession session, string profileId) => session.ProfileSpeedCmPerSecond(profileId);

    private static int CompAt(NavContext nav, int cell, int lv) => nav.CompAt(cell, lv);

    private static int CellCoord(Fix64 vCm, Fix64 cellSizeCm) => (int)(vCm / cellSizeCm).ToLong();

    private static Fix64 Square(Fix64 v) => v * v;

    /// <summary>D44:落点保底是链接末端格心,且必须可走;保不住就不跳。</summary>
    private static bool TryStartJump(
        Arch.Core.World world, Arch.Core.Entity entity,
        ref CrowdSimulationUnitState state, ref CrowdSimulationKinematics kin,
        NavLinkSet links, int linkIndex, NavContext nav, int n, int cellSizeCm)
    {
        Fix64 px = kin.RestCm.X, py = kin.RestCm.Y;
        var pos = world.Get<WorldPositionCm>(entity).Value;
        px = pos.X;
        py = pos.Y;
        int a = links.From[linkIndex], b = links.To[linkIndex];
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        Fix64 tx = (Fix64.FromInt(b % n) + Fix64.HalfValue) * cs + px - (Fix64.FromInt(a % n) + Fix64.HalfValue) * cs;
        Fix64 ty = (Fix64.FromInt(b / n) + Fix64.HalfValue) * cs + py - (Fix64.FromInt(a / n) + Fix64.HalfValue) * cs;
        int c = (int)(ty / cs).ToLong() * n + (int)(tx / cs).ToLong();
        if (!(tx > Fix64.Zero && ty > Fix64.Zero && tx < cs * n && ty < cs * n && nav.Passable[c] != 0))
        {
            if (nav.Passable[b] == 0) return false;
            tx = (Fix64.FromInt(b % n) + Fix64.HalfValue) * cs;
            ty = (Fix64.FromInt(b / n) + Fix64.HalfValue) * cs;
        }

        state.State = (byte)CrowdUnitState.Jump;
        kin.RestCm = new Fix64Vec2(px, py);
        kin.JumpToCm = new Fix64Vec2(tx, ty);
        kin.JumpT = Fix64.Zero;
        kin.JumpLengthCm = Fix64.Max(Fix64.FromFloat(1e-3f), Fix64Math.Sqrt(Square(tx - px) + Square(ty - py)));
        return true;
    }
}
