using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 移动下令(issueOrder.js 移植,Fix64 厘米域):选中单位(无选中 = 全体本方)按
/// (移动类型 × 半径级) 重组进新组、挂同一条新指令;magic box 定落点模式;
/// PRESERVE 时各层块按(层质心 − 指令质心)平移目标。
/// D57:同玩家同成员同目标同参数的重复指令 = 空操作(仍进日志,回放逐位一致)。
/// </summary>
public static class CrowdIssueOrder
{
    public static CrowdOrderMode? Issue(
        CrowdSimSession sim, Fix64 wxCm, Fix64 wyCm, int player,
        string shapeId, bool? auto, Fix64Vec2? face, Fix64 widthCm)
    {
        var world = sim.World;
        var units = sim.Units;
        bool useSel = sim.SelectedCount > 0;
        int clickCell = CrowdDeployment.CellAt(wxCm, wyCm, sim.Config.NavCellCount, sim.Config.NavCellSizeCm);
        string key = $"{shapeId}|{auto}|{(face.HasValue ? $"{face.Value.X.RawValue},{face.Value.Y.RawValue}" : "-")}|{widthCm.RawValue}";

        // D57:同玩家、同成员集、同目标格与同参数的存活指令 → 直接复用
        CrowdOrder? same = null;
        int m = 0;
        bool broken = false;
        for (int i = 0; i < units.Count; i++)
        {
            var st = world.Get<CrowdSimulationUnitState>(units.EntityAt(i));
            if (world.Get<Gameplay.Components.PlayerOwner>(units.EntityAt(i)).PlayerId != player) continue;
            if (useSel && st.Selected == 0) continue;
            var g = sim.Groups.Groups[st.GroupId];
            int oid = g?.OrderId ?? 0;
            CrowdOrder? o = oid > 0 ? FindOrder(sim, oid) : null;
            if (o == null || (same != null && o != same)) { same = null; broken = true; break; }
            same = o;
            m++;
        }

        if (!broken && same != null && same.ClickCell == clickCell && same.DedupKey == key)
        {
            int memberSum = 0;
            foreach (var l in same.Groups) memberSum += sim.Groups.Groups[l.GroupId]?.Count ?? 0;
            bool allGoalOrPlanning = true;
            foreach (var l in same.Groups)
            {
                var g = sim.Groups.Groups[l.GroupId]!;
                if (g.Goal < 0 && !g.Planning) { allGoalOrPlanning = false; break; }
            }

            if (memberSum == m && allGoalOrPlanning) return same.Mode;
        }

        // 重组:每个 (层 × 半径级) 一个新组,挂同一条新指令;顺带算全体质心与包围盒
        var fresh = new Dictionary<int, CrowdNavGroupSet.Group>();
        var lsum = new Dictionary<int, (Fix64 X, Fix64 Y, int N)>();
        Fix64 x0 = Fix64.MaxValue, y0 = Fix64.MaxValue, x1 = Fix64.MinValue, y1 = Fix64.MinValue;
        Fix64 sx = Fix64.Zero, sy = Fix64.Zero;
        int n = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            var st = world.Get<CrowdSimulationUnitState>(entity);
            if (world.Get<Gameplay.Components.PlayerOwner>(entity).PlayerId != player) continue;
            if (useSel && st.Selected == 0) continue;
            var old = sim.Groups.Groups[st.GroupId]!;
            int gk = old.LayerIdx * 256 + old.RIdx;
            if (!fresh.TryGetValue(gk, out var g))
            {
                // F02 双句柄:新组以真相句柄落底(老组可能持变体号),规划句柄随即挂当前槽。
                // Group.Player 是玩家表序下标(SpawnAt 传 player-1 同口径)——迷雾视野组按它索引。
                g = sim.Groups.Alloc(sim.Config.Relations.IndexByPlayerId[player], old.LayerIdx, old.RIdx, old.BodyNavId);
                sim.AttachGroupNav(g);
                fresh[gk] = g;
            }

            if (g.PrevLeader == null && old.Leader is { Done: false }) g.PrevLeader = old.Leader;
            old.Count--;
            st.GroupId = g.Id;
            world.Set(entity, st);
            g.Count++;
            var p = world.Get<WorldPositionCm>(entity).Value;
            if (!lsum.TryGetValue(g.LayerIdx, out var acc)) acc = (Fix64.Zero, Fix64.Zero, 0);
            lsum[g.LayerIdx] = (acc.X + p.X, acc.Y + p.Y, acc.N + 1);
            sx += p.X;
            sy += p.Y;
            n++;
            if (p.X < x0) x0 = p.X;
            if (p.X > x1) x1 = p.X;
            if (p.Y < y0) y0 = p.Y;
            if (p.Y > y1) y1 = p.Y;
        }

        sim.Groups.ReleaseEmpty();
        if (n == 0) return null;

        var fc = sim.Config.Formation;
        var mode = CrowdOrderModeChooser.Choose(
            x0, y0, x1, y1, wxCm, wyCm,
            fc.MagicBoxPadCells * Fix64.FromInt(sim.Config.NavCellSizeCm),
            fc.MagicBoxMaxSpreadCm,
            face.HasValue, auto == false);
        var order = sim.Orders.Create(player, mode, shapeId, face, widthCm);
        order.ClickCell = clickCell;
        order.DedupKey = key;
        Fix64 ccx = sx / n, ccy = sy / n;
        foreach (var g in fresh.Values)
        {
            // PRESERVE:各层块按(层质心 − 指令质心)平移目标
            Fix64 tx = wxCm, ty = wyCm;
            if (mode == CrowdOrderMode.Preserve && lsum.TryGetValue(g.LayerIdx, out var acc) && acc.N > 0)
            {
                tx = wxCm + acc.X / acc.N - ccx;
                ty = wyCm + acc.Y / acc.N - ccy;
            }

            int goalCell = CrowdDeployment.CellAt(tx, ty, sim.Config.NavCellCount, sim.Config.NavCellSizeCm);
            order.Groups.Add(new CrowdOrderGroupLink { GroupId = g.Id, GoalCell = goalCell });
            g.OrderId = order.Id;
        }

        sim.Planner?.Plan(new[] { order }, sim.TickCount);
        return mode;
    }

    private static CrowdOrder? FindOrder(CrowdSimSession sim, int orderId)
    {
        var list = sim.Orders.List;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == orderId) return list[i];
        }

        return null;
    }
}
