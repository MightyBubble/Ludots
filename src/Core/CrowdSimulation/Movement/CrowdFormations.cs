using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 阵型槽位(formation.js 移植,Fix64 厘米域;阵型形状是数据formations[],未知 id 回退第一项)。
/// 槽位在领队局部系(横向,后向):单位按当前前向/侧向投影排序保相对位置(不交叉穿插);
/// 混体型:每个单位占自己 footprint(半径×2×spacingScale×shape.spacing),按小到大排序,
/// 每行同构、行按自身深度堆叠。拖线 width 覆盖行宽。mirror = 反转后原地掉头(大行先排)。
/// </summary>
public static class CrowdFormations
{
    public static RuntimeFormationShape ShapeOf(IReadOnlyList<RuntimeFormationShape> shapes, string id)
    {
        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i].Id == id) return shapes[i];
        }

        return shapes[0];
    }

    /// <summary>在领队系(fx,fy 前向)给成员排槽位;返回最宽行宽(厘米)。</summary>
    public static Fix64 AssignSlots(
        CrowdSimSession session, IReadOnlyList<int> members,
        Fix64 fx, Fix64 fy, Fix64 spacingScale, RuntimeFormationShape shape, Fix64 widthCm,
        bool mirror = false)
    {
        int n = members.Count;
        if (n == 0) return Fix64.Zero;
        var world = session.World;
        var units = session.Units;
        Fix64 ks = spacingScale * shape.Spacing;
        StableOrder.Ensure(ref session.FormationDiameter, n);
        StableOrder.Ensure(ref session.FormationForward, n);
        StableOrder.Ensure(ref session.FormationLateral, n);
        StableOrder.Ensure(ref session.FormationRanks, n);
        StableOrder.Ensure(ref session.FormationOrder, n);
        StableOrder.Ensure(ref session.FormationRows, n);
        var d = session.FormationDiameter;
        Fix64 area = Fix64.Zero;
        for (int k = 0; k < n; k++)
        {
            Fix64 radiusCm = units.PersonalRadiusCmAt(members[k]);
            d[k] = radiusCm * 2 * ks;
            area += d[k] * d[k];
        }

        Fix64 W = widthCm > Fix64.Zero ? widthCm : Fix64Math.Sqrt(area * shape.Aspect);
        Fix64 cx = Fix64.Zero, cy = Fix64.Zero;
        for (int k = 0; k < n; k++)
        {
            var p = world.Get<Components.WorldPositionCm>(units.EntityAt(members[k])).Value;
            cx += p.X;
            cy += p.Y;
        }

        cx /= n;
        cy /= n;
        Fix64 rx = -fy, ry = fx;
        var f = session.FormationForward;
        var s = session.FormationLateral;
        for (int k = 0; k < n; k++)
        {
            var p = world.Get<Components.WorldPositionCm>(units.EntityAt(members[k])).Value;
            Fix64 dx = p.X - cx, dy = p.Y - cy;
            f[k] = dx * fx + dy * fy;
            s[k] = dx * rx + dy * ry;
        }

        // 行分配:主键直径(mirror 时大者在前),决胜前向投影降序,再平局保成员下标。
        var order = session.FormationOrder;
        RankMembers(mirror, d, f, n, session.FormationRanks, order);
        int k0 = 0;
        Fix64 maxW = Fix64.Zero, back = Fix64.Zero, prevDepth = Fix64.Zero;
        for (int r = 0; k0 < n; r++)
        {
            int m = 0;
            Fix64 rw = Fix64.Zero, depth = Fix64.Zero;
            while (k0 + m < n)
            {
                Fix64 dd = d[order[k0 + m]];
                if (m != 0 && (shape.Wedge ? m >= 2 * r + 1 : rw + dd > W + Fix64.FromFloat(1e-3f))) break;
                rw += dd;
                depth = Fix64.Max(depth, dd);
                m++;
            }

            if (r != 0) back += (prevDepth + depth) / 2;
            // 行内横向:单键侧向投影;平局保行分配产出序,不是成员下标。
            var row = session.FormationRows;
            RankRow(s, order, k0, m, row);
            Fix64 acc = -rw / 2;
            for (int ri = 0; ri < m; ri++)
            {
                int k = row[ri].Member;
                var kin = world.Get<CrowdSimulationKinematics>(units.EntityAt(members[k]));
                kin.SlotOffsetCm = new Fix64Vec2(acc + d[k] / 2, back);
                world.Set(units.EntityAt(members[k]), kin);
                acc += d[k];
            }

            prevDepth = depth;
            maxW = Fix64.Max(maxW, rw);
            k0 += m;
        }

        // 领队从成员质心起步:把编队深度对中到质心,否则质心前方的单位槽位落在自己身后
        Fix64 mb = Fix64.Zero;
        for (int k = 0; k < n; k++)
        {
            mb += world.Get<CrowdSimulationKinematics>(units.EntityAt(members[k])).SlotOffsetCm.Y;
        }

        mb /= n;
        for (int k = 0; k < n; k++)
        {
            var e = units.EntityAt(members[k]);
            var kin = world.Get<CrowdSimulationKinematics>(e);
            kin.SlotOffsetCm = new Fix64Vec2(kin.SlotOffsetCm.X, kin.SlotOffsetCm.Y - mb);
            world.Set(e, kin);
        }

        return maxW;
    }

    internal readonly struct FormationRank : IComparable<FormationRank>
    {
        public readonly long Diameter;
        public readonly long Forward;
        public readonly int Member;
        public readonly bool Mirror;

        public FormationRank(long diameter, long forward, int member, bool mirror)
        {
            Diameter = diameter;
            Forward = forward;
            Member = member;
            Mirror = mirror;
        }

        public int CompareTo(FormationRank other)
        {
            int c = Mirror ? other.Diameter.CompareTo(Diameter) : Diameter.CompareTo(other.Diameter);
            if (c != 0) return c;
            c = other.Forward.CompareTo(Forward);
            if (c != 0) return c;
            return Member.CompareTo(other.Member);
        }
    }

    internal readonly struct RowRank : IComparable<RowRank>
    {
        public readonly long Lateral;
        public readonly int Prior;
        public readonly int Member;

        public RowRank(long lateral, int prior, int member)
        {
            Lateral = lateral;
            Prior = prior;
            Member = member;
        }

        public int CompareTo(RowRank other)
        {
            int c = Lateral.CompareTo(other.Lateral);
            return c != 0 ? c : Prior.CompareTo(other.Prior);
        }
    }

    internal static void RankMembers(bool mirror, Fix64[] diameter, Fix64[] forward, int count, FormationRank[] ranks, int[] order)
    {
        for (int k = 0; k < count; k++) ranks[k] = new FormationRank(diameter[k].RawValue, forward[k].RawValue, k, mirror);
        Array.Sort(ranks, 0, count);
        for (int k = 0; k < count; k++) order[k] = ranks[k].Member;
    }

    internal static void RankRow(Fix64[] lateral, int[] incoming, int offset, int count, RowRank[] ranks)
    {
        for (int i = 0; i < count; i++)
        {
            int member = incoming[offset + i];
            ranks[i] = new RowRank(lateral[member].RawValue, i, member);
        }

        Array.Sort(ranks, 0, count);
    }

    /// <summary>magic-box 保持相对位置:槽位 = 当前相对成员质心的偏移(领队系);返回展开半径(厘米)。</summary>
    public static Fix64 RelativeSlots(CrowdSimSession session, IReadOnlyList<int> members, Fix64 fx, Fix64 fy)
    {
        int n = members.Count;
        if (n == 0) return Fix64.Zero;
        var world = session.World;
        var units = session.Units;
        Fix64 cx = Fix64.Zero, cy = Fix64.Zero, ext = Fix64.Zero;
        foreach (int k in members)
        {
            var p = world.Get<Components.WorldPositionCm>(units.EntityAt(k)).Value;
            cx += p.X;
            cy += p.Y;
        }

        cx /= n;
        cy /= n;
        foreach (int k in members)
        {
            var p = world.Get<Components.WorldPositionCm>(units.EntityAt(k)).Value;
            Fix64 dx = p.X - cx, dy = p.Y - cy;
            var e = units.EntityAt(k);
            var kin = world.Get<CrowdSimulationKinematics>(e);
            kin.SlotOffsetCm = new Fix64Vec2(-dx * fy + dy * fx, -(dx * fx + dy * fy));
            world.Set(e, kin);
            Fix64 dist = CrowdFix.Hypot(dx, dy);
            ext = Fix64.Max(ext, dist);
        }

        return ext;
    }

    /// <summary>同指令的多个移动类型块并排站。</summary>
    public static void LayoutSideBySide(CrowdSimSession session, IReadOnlyList<(Fix64 Width, IReadOnlyList<int> Members)> blocks)
    {
        Fix64 total = Fix64.Zero;
        foreach (var b in blocks) total += b.Width;
        Fix64 acc = -total / 2;
        var world = session.World;
        var units = session.Units;
        foreach (var (width, members) in blocks)
        {
            Fix64 off = acc + width / 2;
            foreach (int k in members)
            {
                var e = units.EntityAt(k);
                var kin = world.Get<CrowdSimulationKinematics>(e);
                kin.SlotOffsetCm = new Fix64Vec2(kin.SlotOffsetCm.X + off, kin.SlotOffsetCm.Y);
                world.Set(e, kin);
            }

            acc += width;
        }
    }
}
