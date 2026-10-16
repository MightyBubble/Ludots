using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 演示调试脚本。会话第一 tick 之前整段排进指令队列，诊断断面和回放看到同一份未执行队列。
/// 没写 members 的 order 是全体己方选择，到执行 tick 才取句柄。select / selectAll / clearSelection 不属于演示脚本。
/// </summary>
public sealed class CrowdDemoScript
{
    private readonly (int Tick, JsonObject Cmd)[] _entries;

    private CrowdDemoScript((int Tick, JsonObject Cmd)[] entries) => _entries = entries;

    public int Count => _entries.Length;

    public static CrowdDemoScript Load(JsonArray script)
    {
        var entries = new (int, JsonObject)[script.Count];
        int previous = int.MinValue;
        for (int i = 0; i < script.Count; i++)
        {
            var row = script[i]?.AsObject() ?? throw new InvalidOperationException("指令脚本条目需为对象。");
            int tick = row["tick"] is JsonValue tickValue && tickValue.TryGetValue<int>(out int parsed)
                ? parsed
                : throw new InvalidOperationException("指令脚本条目缺少整数 tick。");
            if (tick < previous) throw new InvalidOperationException("指令脚本需按 tick 升序。");
            previous = tick;
            entries[i] = (tick, row["cmd"]?.AsObject() ?? throw new InvalidOperationException("指令脚本条目缺少 cmd。"));
        }

        return new CrowdDemoScript(entries);
    }

    public void ValidateStructure(CrowdSimSession sim)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            var cmd = _entries[i].Cmd;
            string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
            switch (type)
            {
                case "select":
                case "selectAll":
                case "clearSelection":
                    throw new InvalidOperationException($"演示脚本不接受 {type}。");
                case "order":
                    var order = CrowdSimCommand.Parse(cmd, requireOrderMembers: false);
                    RequirePlayer(sim, order.Player);
                    _ = Movement.CrowdFormations.ShapeOf(sim.Config.Formations, order.ShapeId!);
                    if (order.HasFogTerrain && sim.Fog == null)
                    {
                        throw new InvalidOperationException("order: 迷雾未启用(会话没有运动内核)。");
                    }

                    break;
                default:
                    CrowdSimCommands.Validate(sim, CrowdSimCommand.Parse(cmd));
                    break;
            }
        }
    }

    public void Schedule(CrowdSimSession sim)
    {
        if (sim.TickCount != 0)
        {
            throw new InvalidOperationException($"演示脚本要在第一 tick 之前入队，会话已在 tick {sim.TickCount}。");
        }

        var rows = new CrowdCommand[_entries.Length];
        for (int i = 0; i < _entries.Length; i++)
        {
            var (tick, cmd) = _entries[i];
            string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
            rows[i] = new CrowdCommand(tick, type switch
            {
                "select" or "selectAll" or "clearSelection" => throw new InvalidOperationException($"演示脚本不接受 {type}。"),
                "order" => CrowdSimCommand.Parse(cmd, requireOrderMembers: false),
                _ => CrowdSimCommand.Parse(cmd),
            });
        }

        sim.Commands.Schedule(sim, rows);
    }

    private static void RequirePlayer(CrowdSimSession sim, int player)
    {
        int count = sim.Config.PairRules.PlayerCount;
        if (player < 1 || player > count)
        {
            throw new InvalidOperationException(
                $"指令 player = {player},有效范围 1..{count}(功能位矩阵按玩家号直查,表外 id 不允许)。");
        }
    }
}
