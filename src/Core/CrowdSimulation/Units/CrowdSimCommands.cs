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
    /// <summary>指令玩家号门禁:关系矩阵按玩家号直查(1..P),表外 id 不允许进求解器。
    /// 主门在入队时(Validate);Exec 内的调用是防御断言,兜住绕过队列直呼 Exec 的路径。</summary>
    private static void RequirePlayer(CrowdSimSession sim, int player)
    {
        int count = sim.Config.Relations.PlayerCount;
        if (player < 1 || player > count)
        {
            throw new System.InvalidOperationException(
                $"指令 player = {player},有效范围 1..{count}(关系矩阵按玩家号直查,表外 id 不允许)。");
        }
    }

    /// <summary>入队校验(L23):坏指令在进日志/队列之前拒绝——拖到 Exec 时刻才抛,坏指令已入
    /// 日志再停摆,live 与回放不对称(D29 同型)。覆盖带玩家号的指令面;缺字段/未知 type 的
    /// 结构性坏指令仍由 Exec 防御层拦(不入队路径的合同不变)。</summary>
    public static void Validate(CrowdSimSession sim, JsonNode cmd)
    {
        string? type = cmd["type"]?.GetValue<string>();
        if (type is not ("spawnAt" or "select" or "selectAll" or "order" or "reveal" or "obscure" or "forget" or "fogShare")) return;
        var player = cmd["player"] ?? throw new System.InvalidOperationException($"指令 {type} 缺少 player 字段。");
        RequirePlayer(sim, player.GetValue<int>());
        if (type == "fogShare")
        {
            var with = cmd["with"] ?? throw new System.InvalidOperationException("fogShare 指令缺少 with 字段。");
            RequirePlayer(sim, with.GetValue<int>());
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
                // F02 乐观迷雾开关随下令提交(参考端 issueOrder 的 opts.fogTerrain;随日志回放,
                // 确定性)。切换即同步认知(与参考端同点:指令内联)。
                if (cmd["fogTerrain"] is { } ftNode)
                {
                    var fog = sim.Fog ?? throw new System.InvalidOperationException("order: 迷雾未启用(会话没有运动内核)。");
                    bool ft = ftNode.GetValue<bool>();
                    if (fog.Terrain != ft)
                    {
                        fog.Terrain = ft;
                        Fog.CrowdBeliefSync.Sync(sim);
                    }
                }

                return Movement.CrowdIssueOrder.Issue(
                    sim,
                    Fix64.FromInt(cmd["xCm"]!.GetValue<int>()),
                    Fix64.FromInt(cmd["yCm"]!.GetValue<int>()),
                    cmd["player"]!.GetValue<int>(),
                    cmd["shape"]?.GetValue<string>() ?? "box",
                    cmd["auto"]?.GetValue<bool>(),
                    cmd["face"] is { } f ? new Fix64Vec2(Fix64.FromDouble(f[0]!.GetValue<double>()), Fix64.FromDouble(f[1]!.GetValue<double>())) : (Fix64Vec2?)null,
                    cmd["widthCm"] is { } w ? Fix64.FromInt(w.GetValue<int>()) : Fix64.Zero);
            case "fogSight":
                (sim.Fog ?? throw new System.InvalidOperationException("fogSight: 迷雾未启用(会话没有运动内核)。")).Los =
                    cmd["on"]?.GetValue<bool>() ?? true;
                return null;
            case "reveal":
            case "obscure":
            case "forget":
            case "fogShare":
                return FogCmd(sim, type, cmd);
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
    /// <summary>D50 迷雾/知识面命令(sim/playerCommands.js fogCmd 移植):主体 = 玩家(实体域
    /// 语法糖)→ 其视野组,组内玩家同享。area = rect/circle/poly 恰一(米→厘米在脚本边界换算)。
    /// 命令只改迷雾数据;认知同步在下一 tick 管线拾起(fogShare 的 force 除外——并组当场标记)。</summary>
    private static object? FogCmd(CrowdSimSession sim, string type, JsonNode cmd)
    {
        var fog = sim.Fog ?? throw new System.InvalidOperationException($"{type}: 迷雾未启用(会话没有运动内核)。");
        var relations = sim.Config.Relations;
        int player = cmd["player"] is { } p ? p.GetValue<int>() : throw new System.InvalidOperationException($"{type}: 缺少 player 字段。");
        if (player < 1 || player > relations.PlayerCount)
        {
            throw new System.InvalidOperationException($"{type}: 无效玩家 {player}。");
        }

        int g = fog.GroupOf[relations.IndexByPlayerId[player]];
        if (type == "fogShare")
        {
            int with = cmd["with"] is { } w ? w.GetValue<int>() : throw new System.InvalidOperationException("fogShare: 缺少 with 字段。");
            if (with < 1 || with > relations.PlayerCount)
            {
                throw new System.InvalidOperationException($"fogShare: 无效玩家 {with}。");
            }

            if (cmd["on"]?.GetValue<bool>() == false)
            {
                throw new System.InvalidOperationException("fogShare: 拆分视野组尚未支持(需显式重组命令)。");
            }

            int to = fog.MergeGroups(g, fog.GroupOf[relations.IndexByPlayerId[with]]);
            if (to >= 0)
            {
                var force = sim.Belief!.Force ??= new HashSet<int>();
                force.Add(to);
            }

            return to;
        }

        var shape = Fog.CrowdFogShape.Parse(type, cmd);
        var (cells, box) = Fog.CrowdFogArea.AreaCells(fog.F, fog.FcsCm, shape);
        if (type == "reveal") fog.RevealArea(g, cells);
        else if (type == "forget") fog.ForgetArea(g, cells, box);
        else
        {
            int ticks = cmd["ticks"] is { } t ? t.GetValue<int>() : throw new System.InvalidOperationException("obscure: 缺少 ticks(需为 ≥1 的整数)。");
            if (ticks < 1)
            {
                throw new System.InvalidOperationException("obscure: ticks 需为 ≥1 的整数。");
            }

            fog.ObscureArea(g, cells, sim.TickCount + ticks);
        }

        return g;
    }

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
