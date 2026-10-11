using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 局部避让(avoidance.js computeSeparation 移植,Fix64 厘米域):非对称分离——
/// 重叠对的让出份额按关系矩阵的推挤模式分摊;priority 模式有效优先级 = 兵种原型优先级
/// +(移动中 + movingBonus),低者让(1 - dominantShare)、相等对半、高者近不动;
/// rigid 模式优先级失效,休息单位对移动单位不可动、同状态对半;同导航组且都在移动:
/// 对称对半,不查关系表。跳跃(JUMP)单位腾空:半径记 0,不收推也不推人。
/// 两个预算保证最坏 O(n) 而不偏袒谁被看见:maxNeighbors 只数重叠对,maxScan 数所有
/// 读到的候选(含跳过的);查询范围 = 单位自己的哈希环。
/// 静/动分界(calm):非移动、零速、上次解出的推力低于 restDeadband 即睡;整片可达邻域
/// 全睡的占格是静态岛——不收推不耗算,任何醒着的单位在触及范围内即唤醒整格。
/// 降频:stride &gt; 1 时本调用只解 cy % stride == phase 的行,其余保留上次向量。
/// </summary>
public static class CrowdAvoidance
{

    /// <summary>完全堆叠对的判定域:参考端 1e-6 m² = (1 mm)²,厘米域即 0.01 cm²(FromDouble 精确落格,非零)。</summary>
    private static readonly Fix64 StackedD2Cm = Fix64.FromDouble(0.01);
    /// <summary>堆叠守卫的防零垫:参考端 1e-3 m² = 10 cm²(垫出 ≈3.2 cm 的解算距离)。</summary>
    private static readonly Fix64 StackedD2GuardCm = Fix64.FromInt(10);
    /// <summary>堆叠轴的伪随机中心:((lo·16807) % 7) − 2.5,2.5 二进精确。</summary>
    private static readonly Fix64 StackedAxisCenter = Fix64.FromInt(5) / Fix64.FromInt(2);
    /// <summary>堆叠轴的量纲:参考端轴值是米,厘米域 ×100(与 d2 的 cm² 口径一致)。</summary>
    private static readonly Fix64 StackedAxisScaleCm = Fix64.FromInt(100);

    public static void Solve(CrowdMovementKernel k)
    {
        k.EnsureAvoidanceCapacity(k.Session.Units.Capacity);
        int active = k.Hash.ActiveCount;
        // 聚集写完唤醒标记之后才能解分离:邻格是否醒着要看全部占格。哈希插入不在这里。
        k.RunRanges(active, CrowdMovementKernel.RangePhaseAvoidGather, default);
        k.RunRanges(active, CrowdMovementKernel.RangePhaseAvoidSolve, default);
    }

    internal static void GatherRange(CrowdMovementKernel k, int start, int end)
    {
        var hash2 = k.Hash;
        var gx = k.GatherX; var gy = k.GatherY; var gr = k.GatherRadius; var ge = k.GatherPriority;
        var gp = k.GatherPlayer; var gm = k.GatherMoving; var gl = k.GatherLevel; var gg = k.GatherGroup;
        var calm = k.Calm; var awake = k.Awake;
        for (int a = start; a < end; a++)
        {
            int c = hash2.Active[a];
            int kStart = hash2.StartOf(c), kEnd = kStart + hash2.CountOf(c);
            int aw = 0;
            for (int kk = kStart; kk < kEnd; kk++)
            {
                int j = hash2.Items[kk];
                var state = k.States[j];
                bool moving = state.State == (byte)CrowdUnitState.Moving;
                bool jump = state.State == (byte)CrowdUnitState.Jump;
                var pos = k.Positions[j];
                var kin = k.Kins[j];
                gx[kk] = pos.X;
                gy[kk] = pos.Y;
                gr[kk] = jump ? Fix64.Zero : k.Radii[j];
                gp[kk] = (byte)k.PlayerIdx[j]; // 玩家表序下标(参考端 gp 同口径)
                gl[kk] = state.Level;
                gg[kk] = state.GroupId;
                gm[kk] = moving ? (byte)1 : (byte)0;
                ge[kk] = k.PushPriorities[j] + (moving ? k.MovingBonus : Fix64.Zero);
                if (moving || jump || calm[j] == 0 || kin.Velocity.X != Fix64.Zero || kin.Velocity.Y != Fix64.Zero) aw = 1;
            }

            awake[a] = (byte)aw;
        }
    }

    internal static void SolveRange(CrowdMovementKernel k, int start, int end)
    {
        var session = k.Session;
        var relations = session.Config.Relations;
        int P = relations.PlayerCount;
        var pushMode = relations.PushModeByPair;
        var hash2 = k.Hash;
        int dim = hash2.Dim, last = dim - 1;
        int rr = hash2.Rings + 1;
        int width = hash2.Width;
        Fix64 cellCm = hash2.CellSize;
        Fix64 reach = hash2.Reach;
        Fix64 keep = Fix64.OneValue - k.Smoothing;
        var gx = k.GatherX; var gy = k.GatherY; var gr = k.GatherRadius; var ge = k.GatherPriority;
        var gp = k.GatherPlayer; var gm = k.GatherMoving; var gl = k.GatherLevel; var gg = k.GatherGroup;
        var calm = k.Calm; var awake = k.Awake;
        for (int a = start; a < end; a++)
        {
            int c = hash2.Active[a];
            int cy = c / dim;
            if (k.Stride > 1 && cy % k.Stride != k.Phase) continue;
            int kStart = hash2.StartOf(c), kEnd = kStart + hash2.CountOf(c);
            int solo = a * width, rc = a * rr;
            bool hot = awake[a] != 0;
            for (int o = 0, e = hash2.RingCount[rc + hash2.CellRingOf(c)]; o < e && !hot; o++)
            {
                hot = awake[hash2.NeighborSlot[solo + o]] != 0;
            }

            if (!hot)
            {
                for (int kk = kStart; kk < kEnd; kk++)
                {
                    k.Separation[hash2.Items[kk]] = Fix64Vec2.Zero;
                }

                continue;
            }

            for (int kk = kStart; kk < kEnd; kk++)
            {
                Fix64 xi = gx[kk], yi = gy[kk], ri = gr[kk], eiV = ge[kk];
                int mi = gm[kk], li = gl[kk], grp = gg[kk], pi = gp[kk];
                if (ri == Fix64.Zero)
                {
                    int i0 = hash2.Items[kk];
                    k.Separation[i0] = Fix64Vec2.Zero;
                    calm[i0] = 0;
                    continue; // 腾空:双向都不收推
                }

                int nearby = hash2.RingCount[rc + hash2.ItemRing[kk]];
                Fix64 sx = Fix64.Zero, sy = Fix64.Zero;
                int scanned = 0, contacts = 0;
                for (int o = 0; o < nearby && scanned < k.MaxScan && contacts < k.MaxNeighbors; o++)
                {
                    int pos = solo + o, neighborEnd = hash2.NeighborEnd[pos];
                    // 格剔除:矩形超出 ri + reach 的邻格不可能相碰(边格收着钳位单位,永不剔除)
                    int nc = hash2.Active[hash2.NeighborSlot[pos]], ny = nc / dim, nx = nc - ny * dim;
                    if (nx < last && ny < last)
                    {
                        Fix64 x0 = nx * cellCm, y0 = ny * cellCm;
                        Fix64 ex = xi < x0 ? x0 - xi : xi > x0 + cellCm ? xi - x0 - cellCm : Fix64.Zero;
                        Fix64 ey = yi < y0 ? y0 - yi : yi > y0 + cellCm ? yi - y0 - cellCm : Fix64.Zero;
                        Fix64 lim = ri + reach;
                        if (ex * ex + ey * ey >= lim * lim) continue;
                    }

                    for (int q = hash2.NeighborStart[pos]; q < neighborEnd && scanned < k.MaxScan && contacts < k.MaxNeighbors; q++)
                    {
                        Fix64 rq = gr[q];
                        scanned++; // 读到的候选都计入 maxScan,含跳过的
                        if (q == kk || rq == Fix64.Zero || gl[q] != li) continue; // 只同层相碰
                        Fix64 dx = xi - gx[q], dy = yi - gy[q];
                        Fix64 rs = ri + rq;
                        Fix64 d2 = dx * dx + dy * dy;
                        if (d2 >= rs * rs) continue;
                        contacts++;
                        int mj = gm[q];
                        Fix64 share;
                        // 同导航组且都在移动:对称对半,不查关系表
                        if (mi != 0 && mj != 0 && gg[q] == grp)
                        {
                            share = Fix64.HalfValue;
                        }
                        else if (pushMode[pi * P + gp[q]] == CrowdSimulationPushMode.Rigid)
                        {
                            share = mi == mj ? Fix64.HalfValue : mi != 0 ? Fix64.OneValue : Fix64.Zero;
                        }
                        else
                        {
                            Fix64 ejV = ge[q];
                            share = eiV == ejV ? Fix64.HalfValue : eiV < ejV ? Fix64.OneValue - k.DominantShare : k.DominantShare;
                        }

                        if (share == Fix64.Zero) continue;
                        if (d2 < StackedD2Cm)
                        {
                            // 完全堆叠对:由(无序)下标对派生确定性伪随机轴,反对称——只会推开,绝不同向
                            int i = hash2.Items[kk], j = hash2.Items[q];
                            long lo = i < j ? i : j, hi = i < j ? j : i;
                            Fix64 sg = i < j ? Fix64.OneValue : -Fix64.OneValue;
                            dx = (Fix64.FromInt((int)((lo * 16807L) % 7)) - StackedAxisCenter) * StackedAxisScaleCm * sg;
                            dy = (Fix64.FromInt((int)((hi * 48271L) % 7)) - StackedAxisCenter) * StackedAxisScaleCm * sg;
                            d2 = dx * dx + dy * dy + StackedD2GuardCm;
                        }

                        Fix64 d = Fix64Math.Sqrt(d2);
                        // 接触边界连续;share 0.5 = 对称;两轴共用一次除法
                        Fix64 s = (rs - d) * share * 2 / (rs * d);
                        sx += dx * s;
                        sy += dy * s;
                    }
                }

                // 截断 + 时间平滑,消密集人群的逐帧推力抖动
                int i2 = hash2.Items[kk];
                k.Contacts[i2] += (ushort)contacts;
                Fix64 m = CrowdFix.Hypot(sx, sy);
                calm[i2] = m <= k.RestDeadband ? (byte)1 : (byte)0;
                if (m > k.MaxPush)
                {
                    Fix64 f = k.MaxPush / m;
                    sx *= f;
                    sy *= f;
                }

                var previous = k.Separation[i2];
                k.Separation[i2] = new Fix64Vec2(previous.X * k.Smoothing + sx * keep, previous.Y * k.Smoothing + sy * keep);
            }
        }
    }
}
