using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 马达层(motor.js integrateMotor 移植,Fix64 厘米域):把期望速度变成受物理约束的运动——
/// 一阶速度响应、最大转向率、速度上限、墙面滑动。唯一写 x/y/vx/vy 的层。
/// 参考端 Float32 存储取整(fround)是它的存储层口径;甲方存储是 Fix64 原始值,不二次量化。
/// 阻挡盒只站在地面层;桥面层(lv=1)不吃地面阻挡。
/// </summary>
public static class CrowdMotor
{
    public static void Integrate(CrowdMovementKernel k, Fix64 dt)
    {
        var session = k.Session;
        int navN = session.Config.NavCellCount;
        int cellSizeCm = session.Config.NavCellSizeCm;
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        Fix64 maxTurn = k.UnitTurnRate * dt;
        k.MotorFrame = new CrowdMotorFrame
        {
            NavN = navN,
            CellSizeCm = cellSizeCm,
            N2 = navN * navN,
            Dt = dt,
            Cs = cs,
            Lim = cs * navN - Fix64.FromFloat(0.01f),
            Response = Fix64.OneValue - Fix64Math.Exp(-k.Accel * dt),
            ResponseRest = Fix64.OneValue - Fix64Math.Exp(-k.Accel * k.RestAccelScale * dt),
            CosT = Fix64Math.Cos(maxTurn),
            SinT = Fix64Math.Sin(maxTurn),
            Dead = k.RestDeadband,
            Stop2 = k.StopSpeedRatio * k.StopSpeedRatio,
            CapR = k.SpeedCapRatio,
            HasObs = session.Blockers is { Count: > 0 },
        };
        FillOpenCells(k);
        k.RunRanges(session.Units.Count, CrowdMovementKernel.RangePhaseMotor, dt);
    }

    internal static void IntegrateRange(CrowdMovementKernel k, int start, int end, Fix64 dt)
    {
        var session = k.Session;
        var frame = k.MotorFrame;
        int navN = frame.NavN;
        int cellSizeCm = frame.CellSizeCm;
        int n2 = frame.N2;
        Fix64 cs = frame.Cs;
        Fix64 lim = frame.Lim;
        Fix64 a = frame.Response;
        Fix64 aRest = frame.ResponseRest;
        Fix64 cosT = frame.CosT, sinT = frame.SinT;
        Fix64 dead = frame.Dead;
        Fix64 stop2 = frame.Stop2;
        Fix64 capR = frame.CapR;
        bool hasObs = frame.HasObs;
        var colliders = session.Blockers;
        Nav.NavContext? openNav = null;
        byte[]? openGrid = null;

        for (int i = start; i < end; i++)
        {
            var state = k.States[i];
            var kin = k.Kins[i];
            var pos = k.Positions[i];

            if (state.State == (byte)CrowdUnitState.Jump)
            {
                // 离网链接 travers:起点→落点直线,不吃墙面/避让
                Fix64 sp = k.Speeds[i] * k.JumpSpeedRatio;
                Fix64 L = kin.JumpLengthCm;
                kin.JumpT += sp * dt;
                Fix64 t = Fix64.Min(Fix64.OneValue, kin.JumpT / L);
                Fix64 ex = (kin.JumpToCm.X - kin.RestCm.X) / L, ey = (kin.JumpToCm.Y - kin.RestCm.Y) / L;
                pos = new Fix64Vec2(kin.RestCm.X + ex * L * t, kin.RestCm.Y + ey * L * t);
                kin.Velocity = new Fix64Vec2(ex * sp, ey * sp);
                if (t >= Fix64.OneValue)
                {
                    pos = kin.JumpToCm;
                    state.State = (byte)CrowdUnitState.Moving;
                    state.Mode = 0;
                    kin.Blend = Fix64.Zero;
                }

                k.Positions[i] = pos;
                k.States[i] = state;
                k.Kins[i] = kin;
                continue;
            }

            bool rest = state.State != (byte)CrowdUnitState.Moving;
            // 静态岛休眠(rest && calm && 全零则整步跳过,参考 motor 同款条件):推力/意图/速度
            // 全零的睡单位不耗马达;新生单位 calm=0 必醒——生成落点压住被挡格时墙面滑出仍是真实位置来源。
            if (rest && k.Calm[i] != 0
                && kin.Velocity.X == Fix64.Zero && kin.Velocity.Y == Fix64.Zero
                && k.Separation[i].X == Fix64.Zero && k.Separation[i].Y == Fix64.Zero
                && k.Intent[i].X == Fix64.Zero && k.Intent[i].Y == Fix64.Zero)
            {
                continue;
            }

            var nav = session.Navs[session.Groups.Groups[state.GroupId]!.BodyNavId]; // F02 双句柄:马达物理走真相
            int lv = state.Level;
            byte[] pass = lv != 0 ? nav.UpPass : nav.Passable;
            byte[] walk = lv != 0 ? nav.UpPass : nav.Walk; // 甲板层没有独立亚格 walk 网格:甲板面无亚格障碍,以 UpPass 为口径(与 S2 数据面对齐)
            Fix64 px = pos.X, py = pos.Y;
            // 格归属保留 Fix64 除法(单次有理下取整,对真值格号偏差 ≤1 ulp)。
            // 参考端是 x*inv(预存 f64 倒数);曾试改 Fix64 乘法同形,inv 本身下取整 + 乘积再
            // 下取整的双重取整反而更偏离参考——S5 实测 p50 0.07cm → 17.3cm(2026-10-09),已回退。
            // 格边界上与参考的 1 ulp 差属双数值系残余,不可在此框架内消除。
            int cx = (int)(px / cs).ToLong(), cy = (int)(py / cs).ToLong();
            Fix64 spd = k.Speeds[i];
            Fix64 costHere = (lv != 0 ? nav.UpCost : nav.Cost)[cy * navN + cx];
            spd = spd / (costHere == Fix64.Zero ? Fix64.OneValue : costHere);
            Fix64 slow2 = spd * spd * stop2;

            // 静止单位(闲置/到达/不可达)对轻接触(deadband)不让步,软让步并停稳
            Fix64 sxi = k.Separation[i].X, syi = k.Separation[i].Y;
            Fix64 sk = k.SeparationWeight;
            if (rest)
            {
                Fix64 sm = Fix64Math.Sqrt(sxi * sxi + syi * syi);
                Fix64 f = sm > dead ? (sm - dead) / sm : Fix64.Zero;
                sxi *= f;
                syi *= f;
                sk *= k.RestSeparationScale;
            }

            Fix64 dvx = k.Intent[i].X + sxi * spd * sk, dvy = k.Intent[i].Y + syi * spd * sk;
            // 一阶响应逼近期望速度
            Fix64 ovx = kin.Velocity.X, ovy = kin.Velocity.Y;
            Fix64 r = rest ? aRest : a;
            Fix64 nvx = ovx + (dvx - ovx) * r, nvy = ovy + (dvy - ovy) * r;
            Fix64 o2 = ovx * ovx + ovy * ovy;
            Fix64 n2v = nvx * nvx + nvy * nvy;
            // 转向限速:旧朝向朝新朝向最多转 maxTurn
            if (state.State == (byte)CrowdUnitState.Moving && o2 > slow2 && n2v > slow2)
            {
                // o2·n2v 是速度平方的乘积,厘米域轻易超 Q31.32 界——分开开方,中间量不进同一乘法
                if (ovx * nvx + ovy * nvy < cosT * Fix64Math.Sqrt(o2) * Fix64Math.Sqrt(n2v))
                {
                    Fix64 s = ovx * nvy - ovy * nvx >= Fix64.Zero ? sinT : -sinT;
                    Fix64 f = Fix64Math.Sqrt(n2v / o2);
                    nvx = (ovx * cosT - ovy * s) * f;
                    nvy = (ovx * s + ovy * cosT) * f;
                }
            }

            Fix64 cap = spd * capR;
            if (n2v > cap * cap)
            {
                Fix64 f = cap / Fix64Math.Sqrt(n2v);
                nvx *= f;
                nvy *= f;
            }
            else if (rest && n2v < slow2)
            {
                nvx = Fix64.Zero;
                nvy = Fix64.Zero;
            }

            // 积分 + 墙面滑动(按实际存储值取格:格边取整不能把贴墙单位带进被挡格)
            Fix64 nx2 = px + nvx * dt, ny2 = py + nvy * dt;
            if (nx2 < Fix64.Zero) nx2 = Fix64.Zero; else if (nx2 > lim) nx2 = lim;
            if (ny2 < Fix64.Zero) ny2 = Fix64.Zero; else if (ny2 > lim) ny2 = lim;
            // 同一对坐标的商只除一次。贴墙把 nx2/ny2 退回 px/py 时,商就是本步开头的 cx/cy。
            // 墙面解算改写坐标后商不再相同,必须重除。
            int ncx = (int)(nx2 / cs).ToLong(), ncy = (int)(ny2 / cs).ToLong();
            if (pass[ncy * navN + ncx] == 0)
            {
                if (pass[cy * navN + ncx] != 0) { ny2 = py; nvy = Fix64.Zero; ncy = cy; }
                else if (pass[ncy * navN + cx] != 0) { nx2 = px; nvx = Fix64.Zero; ncx = cx; }
                else { nx2 = px; ny2 = py; nvx = Fix64.Zero; nvy = Fix64.Zero; ncx = cx; ncy = cy; }
            }

            // 精确盘面接触:先净空取整剩下的亚格被挡格,再阻挡盒(仅地面层)
            Fix64 radiusCm = k.Radii[i];
            if (nav != openNav)
            {
                openNav = nav;
                openGrid = k.OpenCache.GridFor(nav);
            }

            bool wallFree = radiusCm <= cs &&
                CrowdWalls.IsOpen(openGrid!, ncy * navN + ncx, lv * n2);
            if (!wallFree && CrowdWalls.ResolveWalls(walk, navN, cellSizeCm, nx2, ny2, radiusCm,
                    out var wx, out var wy, out var wnx, out var wny) &&
                AcceptContact(pass, navN, cellSizeCm, lim, ref wx, ref wy))
            {
                nx2 = wx;
                ny2 = wy;
                ncx = (int)(nx2 / cs).ToLong();
                ncy = (int)(ny2 / cs).ToLong();
                Fix64 vn = nvx * wnx + nvy * wny;
                if (vn < Fix64.Zero)
                {
                    nvx -= vn * wnx;
                    nvy -= vn * wny;
                }
            }

            if (hasObs && lv == 0)
            {
                int cell = ncy * navN + ncx;
                if (colliders!.Resolve(cell, nx2, ny2, radiusCm, out var bx, out var by, out var bnx, out var bny) &&
                    AcceptContact(pass, navN, cellSizeCm, lim, ref bx, ref by))
                {
                    nx2 = bx;
                    ny2 = by;
                    Fix64 vn = nvx * bnx + nvy * bny;
                    if (vn < Fix64.Zero)
                    {
                        nvx -= vn * bnx;
                        nvy -= vn * bny;
                    }
                }
            }

            pos = new Fix64Vec2(nx2, ny2);
            kin.Velocity = new Fix64Vec2(nvx, nvy);
            if (state.State == (byte)CrowdUnitState.Arrived && nvx == Fix64.Zero && nvy == Fix64.Zero)
            {
                kin.RestCm = pos;
            }

            k.Positions[i] = pos;
            k.States[i] = state;
            k.Kins[i] = kin;
        }
    }

    private static void FillOpenCells(CrowdMovementKernel k)
    {
        var session = k.Session;
        int n = session.Units.Count;
        var groups = session.Groups.Groups;
        Span<int> seen = stackalloc int[64];
        int filled = 0;
        for (int i = 0; i < n; i++)
        {
            int navId = groups[k.States[i].GroupId]!.BodyNavId;
            bool known = false;
            for (int s = 0; s < filled; s++)
            {
                if (seen[s] == navId) { known = true; break; }
            }

            if (known) continue;
            if (filled == seen.Length)
            {
                throw new InvalidOperationException($"开放格预填的导航上下文超过 {seen.Length} 个。");
            }

            seen[filled++] = navId;
            k.OpenCache.EnsureFilled(session.Navs[navId]);
        }
    }

    /// <summary>落在被挡格的修正不接收(参考实现的 acceptContact:钳到界内 + 落格可走才存)。</summary>
    private static bool AcceptContact(byte[] pass, int n, int cellSizeCm, Fix64 lim, ref Fix64 x, ref Fix64 y)
    {
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        Fix64 qx = Fix64.Min(lim, Fix64.Max(Fix64.Zero, x));
        Fix64 qy = Fix64.Min(lim, Fix64.Max(Fix64.Zero, y));
        if (pass[(int)(qy / cs).ToLong() * n + (int)(qx / cs).ToLong()] == 0) return false;
        x = qx;
        y = qy;
        return true;
    }

}
