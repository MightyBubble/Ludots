using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// S4 指令集(sim 指令面子的子集):spawn(批量部署)、spawnAt(点名生成)、
/// select(框选)、selectAll(全选)、clearSelection(清空选择)。
/// 指令是数据,只在 tick 边界执行——仿真频率就是 FixedHz,不存在第二个时钟。
/// </summary>
public static class CrowdSimCommands
{
    public static object? Exec(CrowdSimSession sim, JsonNode cmd)
    {
        string type = cmd["type"]?.GetValue<string>()
            ?? throw new System.InvalidOperationException("指令缺少 type 字段。");
        switch (type)
        {
            case "spawn":
                return CrowdDeployment.Spawn(sim, cmd["count"]!.GetValue<int>());
            case "spawnAt":
                return CrowdDeployment.SpawnAt(
                    sim,
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()),
                    cmd["count"]!.GetValue<int>(),
                    cmd["player"]!.GetValue<int>(),
                    cmd["unitType"]!.GetValue<int>(),
                    cmd["rIdx"]!.GetValue<int>());
            case "select":
                SelectRect(
                    sim,
                    cmd["player"]!.GetValue<int>(),
                    Fix64.FromInt(cmd["x0Cm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["y0Cm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["x1Cm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["y1Cm"]!.GetValue<int>()),
                    cmd["additive"]?.GetValue<bool>() ?? false);
                return null;
            case "selectAll":
                SelectAll(sim, cmd["player"]!.GetValue<int>());
                return null;
            case "clearSelection":
                ClearSelection(sim);
                return null;
            default:
                throw new System.InvalidOperationException($"未知指令 {type}。");
        }
    }

    /// <summary>框选(selectRect 移植,位置单位为厘米):框内本方单位入选;
    /// additive = false 时清空框外与其余玩家的选择。</summary>
    public static void SelectRect(CrowdSimSession sim, int player, Fix64 x0, Fix64 y0, Fix64 x1, Fix64 y1, bool additive)
    {
        var (ax, bx) = x0 < x1 ? (x0, x1) : (x1, x0);
        var (ay, by) = y0 < y1 ? (y0, y1) : (y1, y0);
        int count = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var entity = sim.Units.EntityAt(i);
            var owner = sim.World.Get<Gameplay.Components.PlayerOwner>(entity).PlayerId;
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            if (owner != player)
            {
                if (state.Selected != 0) { state.Selected = 0; sim.World.Set(entity, state); }
                continue;
            }

            var pos = sim.World.Get<Components.WorldPositionCm>(entity).Value;
            bool inside = pos.X >= ax && pos.X <= bx && pos.Y >= ay && pos.Y <= by;
            byte next = inside ? (byte)1 : additive ? state.Selected : (byte)0;
            if (next != state.Selected) { state.Selected = next; sim.World.Set(entity, state); }
            if (next != 0) count++;
        }

        sim.SetSelectedCount(count);
        SelectionMirror.Sync(sim);
    }

    /// <summary>全选(selectAll):本方全部单位入选。</summary>
    public static void SelectAll(CrowdSimSession sim, int player)
    {
        int count = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var entity = sim.Units.EntityAt(i);
            var owner = sim.World.Get<Gameplay.Components.PlayerOwner>(entity).PlayerId;
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            byte next = owner == player ? (byte)1 : (byte)0;
            if (next != state.Selected) { state.Selected = next; sim.World.Set(entity, state); }
            if (next != 0) count++;
        }

        sim.SetSelectedCount(count);
        SelectionMirror.Sync(sim);
    }

    public static void ClearSelection(CrowdSimSession sim)
    {
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var entity = sim.Units.EntityAt(i);
            var state = sim.World.Get<CrowdSimulationUnitState>(entity);
            if (state.Selected != 0) { state.Selected = 0; sim.World.Set(entity, state); }
        }

        sim.SetSelectedCount(0);
        SelectionMirror.Sync(sim);
    }
}
