using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 演示调试脚本。到点的无成员 order 用本方单位句柄补上，不保留选中影子。
/// select / selectAll / clearSelection 不属于演示脚本。
/// </summary>
public sealed class CrowdDemoScript
{
    private readonly (int Tick, JsonObject Cmd)[] _entries;
    private int _index;

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

    public void ApplyDue(CrowdSimSession sim)
    {
        while (_index < _entries.Length && _entries[_index].Tick <= sim.TickCount)
        {
            Apply(sim, _entries[_index++].Cmd);
        }
    }

    private static void Apply(CrowdSimSession sim, JsonObject cmd)
    {
        string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
        switch (type)
        {
            case "select":
            case "selectAll":
            case "clearSelection":
                throw new InvalidOperationException($"演示脚本不接受 {type}。");
            case "order":
                var order = CrowdSimCommand.Parse(cmd, requireOrderMembers: false);
                if (order.Members == null || order.Members.Length == 0)
                {
                    order = order with { Members = CaptureOwn(sim, order.Player) };
                }

                sim.Commands.Submit<object?>(sim, order, CrowdSimCommands.Exec);
                return;
            default:
                sim.Commands.Submit<object?>(sim, CrowdSimCommand.Parse(cmd), CrowdSimCommands.Exec);
                return;
        }
    }

    private static uint[] CaptureOwn(CrowdSimSession sim, int player)
    {
        var buffer = sim.MemberCapture;
        int count = sim.Units.CopyOwnHandles(player, buffer);
        if (count == 0)
        {
            throw new InvalidOperationException("order: 成员集为空。");
        }

        var members = new uint[count];
        buffer.AsSpan(0, count).CopyTo(members);
        return members;
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
