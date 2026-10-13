using System;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 移动意图层:纯转向决策——读路径产物(指令/领队/槽位/流场),
/// 写期望速度(厘米/秒)与移动状态机(MOVING→ARRIVED/UNREACHABLE,被推离休息锚点则回到 MOVING),
/// 永不直接挪位置。到达按单位自己的目的地判:槽位可见时用槽位直线距,否则用流场绷紧路线长;
/// 地形/链接代价不进任何到达阈值。槽位↔流场连续交叉淡化(blend),视线判定带滞回,槽位跟踪
/// = 领队速度前馈 + 比例修正——防抖动三件套,一个不少。
/// </summary>
public static class CrowdIntents
{
    /// <summary>允许的路线/直线绕行比上限(去槽位的路线不能比直线长太多,否则走流场)。</summary>
    private static readonly Fix64 RouteRatio = Fix64.FromFloat(1.5f);

    public static void Compute(CrowdMovementKernel k, Fix64 dt)
    {
        var session = k.Session;
        var groups = session.Groups;
        int n = session.Units.Count;
        int navCellsPerSide = session.Config.NavCellsPerSide;
        int navCellSizeCm = session.Config.NavCellSizeCm;
        Fix64 navCellCm = Fix64.FromInt(navCellSizeCm);
        k.IntentScalars = new CrowdIntentScalars
        {
            Dt = dt,
            Cs = navCellCm,
            NavCellSizeCm = navCellSizeCm,
            WorldSizeCm = navCellCm * navCellsPerSide,
            BStep = Fix64.Min(Fix64.OneValue, k.BlendRate * dt),
            Wake2 = Square(navCellCm * k.WakeDistanceNavCells),
            ArriveSlot = navCellCm * k.SlotArriveNavCells,
            Settle = navCellCm * k.SettleRadiusNavCells,
            StallV2 = k.StallSpeedRatio * k.StallSpeedRatio,
            NavN = navCellsPerSide,
            N2 = navCellsPerSide * navCellsPerSide,
            Every = k.SlotCheckInterval,
        };
        if (k.Proposals.Length < n) k.EnsureCapacity(Math.Max(n, session.Units.Capacity));

        // 撞上已到达同伴读的是本轮已提交的更小稠密下标。提案不读邻居到达状态，可以按区间并行；
        // 提交必须按原下标串行。场外记录写在提案上，回调留在提交，避免为了扩展把整段意图改成串行。
        if (k.RequireParallelWorkerCount() <= 1)
        {
            for (int i = 0; i < n; i++)
            {
                WriteProposal(k, i);
                CommitProposal(k, i);
            }
        }
        else
        {
            k.RunRanges(n, CrowdMovementKernel.RangePhaseIntent, dt);
            for (int i = 0; i < n; i++) CommitProposal(k, i);
        }

        for (int gi = 0; gi < groups.Groups.Count; gi++)
        {
            if (groups.Groups[gi] is { } gg) gg.LosAll = false;
        }
    }

    internal static void WriteRange(CrowdMovementKernel k, int start, int end, int step)
    {
        for (int i = start; i < end; i += step) WriteProposal(k, i);
    }

    private static void WriteProposal(CrowdMovementKernel k, int i)
    {
        var s = k.IntentScalars;
        var session = k.Session;
        var groups = session.Groups;
        ref var p = ref k.Proposals[i];
        p = default;
        var state = k.States[i];
        var kin = k.Kins[i];
        Fix64 ix = Fix64.Zero, iy = Fix64.Zero;

        // 到达且速度非零:保持到达,意图写零,不做位移唤醒。只有速度已是零且不 calm,才按离休息锚点的距离唤醒。
        if (state.State == (byte)CrowdUnitState.Arrived)
        {
            if (kin.Velocity.X == Fix64.Zero && kin.Velocity.Y == Fix64.Zero)
            {
                if (k.Calm[i] != 0)
                {
                    Store(ref p, state, kin, ix, iy);
                    return;
                }

                var pos0 = k.Positions[i];
                Fix64 ox = kin.RestCm.X - pos0.X, oy = kin.RestCm.Y - pos0.Y;
                if (ox * ox + oy * oy > s.Wake2)
                {
                    state.State = (byte)CrowdUnitState.Moving;
                }
                else
                {
                    Store(ref p, state, kin, ix, iy);
                    return;
                }
            }
            else
            {
                Store(ref p, state, kin, ix, iy);
                return;
            }
        }

        if (!groups.TryGet(state.GroupId, out var g))
        {
            Store(ref p, state, kin, ix, iy);
            return;
        }

        // 重发指令等路径答复:保持当前速度,不刹车
        if (g.Flow == null && g.Planning && state.State == (byte)CrowdUnitState.Moving)
        {
            Store(ref p, state, kin, kin.Velocity.X, kin.Velocity.Y);
            return;
        }

        if (g.Flow == null || (state.State != (byte)CrowdUnitState.Moving && state.State != (byte)CrowdUnitState.Arrived))
        {
            Store(ref p, state, kin, ix, iy);
            return;
        }

        var flow = g.Flow;
        var leader = g.Leader!;
        var nav = k.MemoNav(state.GroupId, g);
        var order = k.MemoOrder(state.GroupId, g);
        bool converge = order?.Mode == CrowdOrderMode.Converge;

        var pos = k.Positions[i];
        Fix64 px = pos.X, py = pos.Y;
        int cx = CellCoord(px, s.NavCellSizeCm), cy = CellCoord(py, s.NavCellSizeCm);
        int cell = cy * s.NavN + cx;

        // 桥头双层可走时比较 flow.Integ。等积分时用路点跨层打破平局。
        if (nav.Portal[cell] != 0 && nav.Passable[cell] != 0 && nav.UpPass[cell] != 0)
        {
            Fix64 a = flow.Integ[cell], b = flow.Integ[s.N2 + cell];
            int cur = state.Level;
            int other = cur != 0 ? cell : s.N2 + cell;
            if (a < b) state.Level = 0;
            else if (b < a) state.Level = 1;
            else if (flow.Wp[cur != 0 ? s.N2 + cell : cell] == other) state.Level = (byte)(cur ^ 1);
        }

        int lv = state.Level;
        byte[] pass = lv != 0 ? nav.UpPass : nav.Passable;

        // 到达优先于跳跃——已在目标可达范围内的单位不起跳
        if (lv == 0 && flow.Lk != null && flow.Lk[cell] >= 0 &&
            !(CrowdFlowSample.RouteLength(flow, s.NavN, session.Config.NavCellSizeCm, px, py, cell, lv) <= k.GoalArriveNavCells))
        {
            if (TryStartJump(ref state, ref kin, flow.Links!, flow.Lk[cell], nav, s.NavN, session.Config.NavCellSizeCm, pos))
            {
                Store(ref p, state, kin, ix, iy);
                return;
            }
        }

        Fix64 spd = k.Speeds[i];
        Fix64 divHere = (lv != 0 ? nav.UpSpeedDivisor : nav.SpeedDivisor)[cell];
        if (divHere != Fix64.OneValue) spd = spd / (divHere == Fix64.Zero ? Fix64.OneValue : divHere);

        // 槽位世界坐标 = 领队位置 + 槽位偏移旋进领队系
        Fix64 lat = kin.SlotOffsetCm.X, back = kin.SlotOffsetCm.Y;
        Fix64 tx = leader.XCm - leader.Hy * lat - leader.Hx * back;
        Fix64 ty = leader.YCm + leader.Hx * lat - leader.Hy * back;
        int sc = tx >= Fix64.Zero && ty >= Fix64.Zero && tx < s.WorldSizeCm && ty < s.WorldSizeCm
            ? CellCoord(ty, s.NavCellSizeCm) * s.NavN + CellCoord(tx, s.NavCellSizeCm)
            : -1;
        // 槽位在地面(目标层)才算数;桥面上只跟流场
        bool slotOk = !converge && lv == 0 && sc >= 0 && pass[sc] == 1 && nav.Comp[sc] == g.GoalComp;

        if (!slotOk)
        {
            state.Mode = 0;
        }
        else if (g.LosAll || (int)(session.Units.HandleAt(i) + (uint)k.Tick) % s.Every == 0)
        {
            Fix64 sightIn = Fix64.Max(k.SlotSightNavCells, Fix64.FromInt(order?.ReachCells ?? 1));
            Fix64 lim = state.Mode != 0 ? sightIn * k.SightHysteresis : sightIn;
            int scx = sc % s.NavN, scy = sc / s.NavN;
            bool vis = Fix64.Abs(Fix64.FromInt(scx - cx)) <= lim && Fix64.Abs(Fix64.FromInt(scy - cy)) <= lim
                && NavGridSteps.LineOfSight(pass, s.NavN, cell, sc);
            if (vis)
            {
                // 第二判据:去槽位的流场路线不能比直线长太多(场外 = ∞ 时只看 LOS,转向接管)
                Fix64 ra = CrowdFlowSample.RouteLength(flow, s.NavN, session.Config.NavCellSizeCm, px, py, cell, 0);
                Fix64 rb = CrowdFlowSample.RouteLength(flow, s.NavN, session.Config.NavCellSizeCm, tx, ty, sc, 0);
                if (ra < CrowdFlowSample.Unreachable / 4 && rb < CrowdFlowSample.Unreachable / 4 &&
                    ra - rb > Fix64.DivPositiveInt(CrowdFix.Hypot(tx - px, ty - py), s.NavCellSizeCm) * RouteRatio + Fix64.OneValue)
                {
                    vis = false;
                }
            }

            state.Mode = vis ? (byte)1 : (byte)0;
        }

        Fix64 w = kin.Blend;
        w = state.Mode != 0 ? Fix64.Min(Fix64.OneValue, w + s.BStep) : Fix64.Max(Fix64.Zero, w - s.BStep);
        kin.Blend = w;
        Fix64 dvx = Fix64.Zero, dvy = Fix64.Zero;
        bool arrived = false;
        Fix64 route = Fix64.FromInt(-1);
        Fix64 ox2 = tx - px, oy2 = ty - py;
        Fix64 slotDist = CrowdFix.Hypot(ox2, oy2);
        if (w > Fix64.Zero)
        {
            if (leader.Done && w > k.BlendCommit && slotDist < s.ArriveSlot)
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
                    Fix64 scale = cap / Fix64Math.Sqrt(m2);
                    sx *= scale;
                    sy *= scale;
                }

                dvx = sx * w;
                dvy = sy * w;
            }
        }

        if (!arrived && w < Fix64.OneValue)
        {
            Fix64 fx, fy;
            route = CrowdFlowSample.SampleFlow(flow, pass, s.NavN, session.Config.NavCellSizeCm, px, py, cell, out fx, out fy, lv);
            if (route >= CrowdFlowSample.Unreachable / 4)
            {
                if (!nav.CanReach(CompAt(nav, cell, lv), g.GoalComp))
                {
                    state.State = (byte)CrowdUnitState.Unreachable;
                    Store(ref p, state, kin, ix, iy);
                    return;
                }

                p.Stray = 1;
                p.StrayCell = cell;
                // 场外时朝领队当前位置的方向,速度用本单位巡航速度。扩展答复落地前一直这样走。
                Fix64 lx = leader.XCm - px, ly = leader.YCm - py;
                Fix64 ld = CrowdFix.Hypot(lx, ly);
                if (ld > Fix64.FromFloat(1e-6f))
                {
                    dvx += (lx / ld) * spd * (Fix64.OneValue - w);
                    dvy += (ly / ld) * spd * (Fix64.OneValue - w);
                }
            }
            else if (!slotOk && route <= k.GoalArriveNavCells && w < k.BlendCommit)
            {
                arrived = true; // 槽位无效且路线短、混合权重低时,按目标格到达
            }
            else
            {
                // 车道展开:沿流场垂直方向朝自己槽位车道漂移,队伍以阵型宽度行进,不收成一条线
                Fix64 cap = spd * k.LaneSpreadSpeedRatio;
                Fix64 latDrift = converge ? Fix64.Zero : (fx * oy2 - fy * ox2) * k.SlotGain;
                latDrift = Fix64.Min(cap, Fix64.Max(-cap, latDrift));
                dvx += (fx * spd - fy * latDrift) * (Fix64.OneValue - w);
                dvy += (fy * spd + fx * latDrift) * (Fix64.OneValue - w);
            }
        }

        // 群体到达:撞上同指令的静止单位,或近目标后长期无进展——只在靠近自己目的地时才算
        bool near = slotOk
            ? slotDist <= s.Settle
            : (route >= Fix64.Zero ? route : CrowdFlowSample.RouteLength(flow, s.NavN, session.Config.NavCellSizeCm, px, py, cell, lv)) <= Fix64.FromInt(order?.ReachCells ?? 1);
        if (!arrived && leader.Done && near)
        {
            Fix64 v2 = kin.Velocity.X * kin.Velocity.X + kin.Velocity.Y * kin.Velocity.Y;
            if (v2 < spd * spd * s.StallV2)
            {
                kin.StallSeconds += s.Dt;
            }
            else if (kin.StallSeconds > Fix64.Zero)
            {
                kin.StallSeconds = Fix64.Max(Fix64.Zero, kin.StallSeconds - s.Dt * k.StallDecay);
            }

            if (kin.StallSeconds > k.SettleTimeSeconds) arrived = true;
        }
        else if (!arrived)
        {
            kin.StallSeconds = Fix64.Zero;
        }

        if (!arrived && near && (leader.Done || converge)) p.Flags |= IntentProposal.NeedsContact;

        if (arrived)
        {
            state.State = (byte)CrowdUnitState.Arrived;
            kin.StallSeconds = Fix64.Zero;
            kin.RestCm = new Fix64Vec2(px, py);
        }

        Store(ref p, state, kin, dvx, dvy);
    }

    private static void CommitProposal(CrowdMovementKernel k, int i)
    {
        ref var p = ref k.Proposals[i];
        if (p.Stray != 0) k.OnStray?.Invoke(p.State.GroupId, p.StrayCell);
        if ((p.Flags & IntentProposal.NeedsContact) != 0 &&
            CrowdContact.TouchesRestingPeer(k.Hash, k, i, k.MaxScan))
        {
            var pos = k.Positions[i];
            p.State.State = (byte)CrowdUnitState.Arrived;
            p.Kin.StallSeconds = Fix64.Zero;
            p.Kin.RestCm = new Fix64Vec2(pos.X, pos.Y);
        }

        k.States[i] = p.State;
        k.Kins[i] = p.Kin;
        k.Intent[i] = new Fix64Vec2(p.IntentX, p.IntentY);
    }

    private static void Store(ref IntentProposal p, CrowdSimulationUnitState state, CrowdSimulationKinematics kin, Fix64 ix, Fix64 iy)
    {
        p.State = state;
        p.Kin = kin;
        p.IntentX = ix;
        p.IntentY = iy;
    }

    private static int CompAt(NavContext nav, int cell, int lv) => nav.CompAt(cell, lv);

    private static int CellCoord(Fix64 vCm, int navCellSizeCm) => CrowdSimulationSpace.NavCellCoord(vCm, navCellSizeCm);

    private static Fix64 Square(Fix64 v) => v * v;

    /// <summary>落点保底是链接末端格心,且必须可走;保不住就不跳。</summary>
    private static bool TryStartJump(
        ref CrowdSimulationUnitState state, ref CrowdSimulationKinematics kin,
        NavLinkSet links, int linkIndex, NavContext nav, int navCellsPerSide, int navCellSizeCm, Fix64Vec2 pos)
    {
        Fix64 px = pos.X, py = pos.Y;
        int a = links.From[linkIndex], b = links.To[linkIndex];
        Fix64 navCellCm = Fix64.FromInt(navCellSizeCm);
        Fix64 tx = (Fix64.FromInt(b % navCellsPerSide) + Fix64.HalfValue) * navCellCm + px - (Fix64.FromInt(a % navCellsPerSide) + Fix64.HalfValue) * navCellCm;
        Fix64 ty = (Fix64.FromInt(b / navCellsPerSide) + Fix64.HalfValue) * navCellCm + py - (Fix64.FromInt(a / navCellsPerSide) + Fix64.HalfValue) * navCellCm;
        int c = CrowdSimulationSpace.NavCellCoord(ty, navCellSizeCm) * navCellsPerSide + CrowdSimulationSpace.NavCellCoord(tx, navCellSizeCm);
        if (!(tx > Fix64.Zero && ty > Fix64.Zero && tx < navCellCm * navCellsPerSide && ty < navCellCm * navCellsPerSide && nav.Passable[c] != 0))
        {
            if (nav.Passable[b] == 0) return false;
            tx = (Fix64.FromInt(b % navCellsPerSide) + Fix64.HalfValue) * navCellCm;
            ty = (Fix64.FromInt(b / navCellsPerSide) + Fix64.HalfValue) * navCellCm;
        }

        state.State = (byte)CrowdUnitState.Jump;
        kin.RestCm = new Fix64Vec2(px, py);
        kin.JumpToCm = new Fix64Vec2(tx, ty);
        kin.JumpT = Fix64.Zero;
        kin.JumpLengthCm = Fix64.Max(Fix64.FromFloat(1e-3f), Fix64Math.Sqrt(Square(tx - px) + Square(ty - py)));
        return true;
    }
}

internal struct IntentProposal
{
    public const byte NeedsContact = 1;
    public CrowdSimulationUnitState State;
    public CrowdSimulationKinematics Kin;
    public Fix64 IntentX;
    public Fix64 IntentY;
    public byte Flags;
    public byte Stray;
    public int StrayCell;
}
