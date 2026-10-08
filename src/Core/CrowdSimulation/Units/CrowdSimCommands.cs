using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// S4+S5 指令集:spawn(批量部署)、spawnAt(点名生成)、select(框选)、selectAll(全选)、
/// clearSelection(清空选择)、order(移动下令:S5,右键点=移动,拖线=摆阵)。
/// 指令是数据,只在 tick 边界执行——仿真频率就是 FixedHz,不存在第二个时钟。
/// </summary>
public static class CrowdSimCommands
{
    /// <summary>指令玩家号门禁:关系矩阵按玩家号直查(1..P),表外 id 不允许进求解器。</summary>
    private static void RequirePlayer(CrowdSimSession sim, int player)
    {
        int count = sim.Config.Relations.PlayerCount;
        if (player < 1 || player > count)
        {
            throw new System.InvalidOperationException(
                $"指令 player = {player},有效范围 1..{count}(关系矩阵按玩家号直查,表外 id 不允许)。");
        }
    }

    public static object? Exec(CrowdSimSession sim, JsonNode cmd)
    {
        string type = cmd["type"]?.GetValue<string>()
            ?? throw new System.InvalidOperationException("指令缺少 type 字段。");
        switch (type)
        {
            case "spawn":
                return CrowdDeployment.Spawn(sim, cmd["count"]!.GetValue<int>());
            case "spawnAt":
                RequirePlayer(sim, cmd["player"]!.GetValue<int>());
                return CrowdDeployment.SpawnAt(
                    sim,
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()),
                    cmd["count"]!.GetValue<int>(),
                    cmd["player"]!.GetValue<int>(),
                    cmd["unitType"]!.GetValue<int>(),
                    cmd["rIdx"]!.GetValue<int>());
            case "select":
                RequirePlayer(sim, cmd["player"]!.GetValue<int>());
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
                RequirePlayer(sim, cmd["player"]!.GetValue<int>());
                SelectAll(sim, cmd["player"]!.GetValue<int>());
                return null;
            case "clearSelection":
                ClearSelection(sim);
                return null;
            case "order":
                RequirePlayer(sim, cmd["player"]!.GetValue<int>());
                return Movement.CrowdIssueOrder.Issue(
                    sim,
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()),
                    cmd["player"]!.GetValue<int>(),
                    cmd["shape"]?.GetValue<string>() ?? "box",
                    cmd["auto"]?.GetValue<bool>(),
                    cmd["face"] is { } f ? new Fix64Vec2(Fix64.FromDouble(f[0]!.GetValue<double>()), Fix64.FromDouble(f[1]!.GetValue<double>())) : (Fix64Vec2?)null,
                    cmd["widthCm"] is { } w ? Fix64.FromInt(w.GetValue<int>()) : Fix64.Zero);
            case "placeStructure":
                return Structures.CrowdStructureOps.PlaceStructure(
                    sim,
                    cmd["template"]!.GetValue<string>(),
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["sizeCm"]!.GetValue<int>()),
                    cmd["toXCm"] is { } tx ? Fix64.FromInt(tx.GetValue<int>()) : Fix64.Zero,
                    cmd["toYCm"] is { } ty ? Fix64.FromInt(ty.GetValue<int>()) : Fix64.Zero);
            case "removeStructureAt":
                return Structures.CrowdStructureOps.RemoveStructureAt(
                    sim,
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()));
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
