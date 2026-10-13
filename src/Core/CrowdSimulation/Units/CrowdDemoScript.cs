using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 演示调试脚本。能成形的行在校验码之前排进指令队列，没有成员的 order 等该玩家有了单位再补句柄。
/// 句柄数组和入队归并只发生在该行第一次成形的 tick。select / selectAll / clearSelection 不属于演示脚本。
/// </summary>
public sealed class CrowdDemoScript
{
    private readonly (int Tick, JsonObject Cmd)[] _entries;
    private readonly bool[] _staged;
    private readonly CrowdCommand[] _row = new CrowdCommand[1];

    private CrowdDemoScript((int Tick, JsonObject Cmd)[] entries)
    {
        _entries = entries;
        _staged = new bool[entries.Length];
    }

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

    /// <summary>
    /// 把已经能成形的行排进队列。更早的 order 还没有单位时，后面的行照样入队。
    /// 返回 true 表示这次新排进了至少一行。
    /// </summary>
    public bool StageFormable(CrowdSimSession sim)
    {
        bool any = false;
        for (int i = 0; i < _entries.Length; i++)
        {
            if (_staged[i]) continue;
            if (!TryStage(sim, i)) continue;
            _staged[i] = true;
            any = true;
        }

        return any;
    }

    private bool TryStage(CrowdSimSession sim, int index)
    {
        var (tick, cmd) = _entries[index];
        string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
        CrowdSimCommand parsed;
        switch (type)
        {
            case "select":
            case "selectAll":
            case "clearSelection":
                throw new InvalidOperationException($"演示脚本不接受 {type}。");
            case "order":
                parsed = CrowdSimCommand.Parse(cmd, requireOrderMembers: false);
                if (parsed.Members == null || parsed.Members.Length == 0)
                {
                    int count = sim.Units.CopyOwnHandles(parsed.Player, sim.MemberCapture);
                    if (count == 0) return false;
                    var members = new uint[count];
                    sim.MemberCapture.AsSpan(0, count).CopyTo(members);
                    parsed = parsed with { Members = members };
                }

                break;
            default:
                parsed = CrowdSimCommand.Parse(cmd);
                break;
        }

        _row[0] = new CrowdCommand(tick, parsed);
        sim.Commands.Schedule(sim, _row);
        return true;
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
