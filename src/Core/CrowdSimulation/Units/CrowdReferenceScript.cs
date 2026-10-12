using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 参考脚本到指令日志的行为级映射。真值文件仍是 select / selectAll / clearSelection
/// 和无成员 order；选中不进校验码。这里在入日志前把它们译成带成员句柄的 order：
/// 当时选中非空就用那份选中，否则用本方花名册（隐藏模板键除外）。
/// 译完的日志不再含选中记录，回放只重放这份日志。
/// </summary>
public sealed class CrowdReferenceScript
{
    private readonly (int Tick, JsonObject Cmd)[] _entries;
    private readonly byte[] _on;
    private readonly ushort[] _gen;
    private int _index;
    private int _selectedCount;

    private CrowdReferenceScript((int Tick, JsonObject Cmd)[] entries, int unitCapacity)
    {
        _entries = entries;
        _on = new byte[unitCapacity];
        _gen = new ushort[unitCapacity];
    }

    public int Count => _entries.Length;

    public static CrowdReferenceScript Load(JsonArray script, int unitCapacity)
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

        return new CrowdReferenceScript(entries, unitCapacity);
    }

    public void Rewind()
    {
        _index = 0;
        InvalidateShadow();
    }

    public void InvalidateShadow()
    {
        Array.Clear(_on);
        Array.Clear(_gen);
        _selectedCount = 0;
    }

    /// <summary>结构校验（玩家号、阵型、迷雾、字段类型）。无成员的 order 留到执行 tick 再补成员。</summary>
    public void ValidateStructure(CrowdSimSession sim)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            var cmd = _entries[i].Cmd;
            string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
            switch (type)
            {
                case "select":
                    RequirePlayer(sim, RequireInt(cmd, type, "player"));
                    RequireInt(cmd, type, "x0Cm");
                    RequireInt(cmd, type, "y0Cm");
                    RequireInt(cmd, type, "x1Cm");
                    RequireInt(cmd, type, "y1Cm");
                    if (cmd["additive"] is { }) OptionalBool(cmd, type);
                    break;
                case "selectAll":
                    RequirePlayer(sim, RequireInt(cmd, type, "player"));
                    break;
                case "clearSelection":
                    break;
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

    private void Apply(CrowdSimSession sim, JsonObject cmd)
    {
        string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
        switch (type)
        {
            case "select":
                SelectRect(sim, RequireInt(cmd, "select", "player"),
                    Fix64.FromInt(RequireInt(cmd, "select", "x0Cm")),
                    Fix64.FromInt(RequireInt(cmd, "select", "y0Cm")),
                    Fix64.FromInt(RequireInt(cmd, "select", "x1Cm")),
                    Fix64.FromInt(RequireInt(cmd, "select", "y1Cm")),
                    cmd["additive"] is { } && OptionalBool(cmd, "select"));
                return;
            case "selectAll":
                SelectAll(sim, RequireInt(cmd, "selectAll", "player"));
                return;
            case "clearSelection":
                InvalidateShadow();
                return;
            case "order":
                var order = CrowdSimCommand.Parse(cmd, requireOrderMembers: false);
                if (order.Members == null || order.Members.Length == 0)
                {
                    order = order with { Members = Capture(sim, order.Player) };
                }

                sim.Commands.Submit<object?>(sim, order, CrowdSimCommands.Exec);
                return;
            default:
                sim.Commands.Submit<object?>(sim, CrowdSimCommand.Parse(cmd), CrowdSimCommands.Exec);
                return;
        }
    }

    private uint[] Capture(CrowdSimSession sim, int player)
    {
        var buffer = sim.MemberCapture;
        int count;
        if (_selectedCount > 0)
        {
            count = 0;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                int slot = sim.Units.SlotAt(i);
                if (_on[slot] == 0 || _gen[slot] != sim.Units.GenOf(slot)) continue;
                if (sim.World.Get<PlayerOwner>(sim.Units.EntityAt(i)).PlayerId != player) continue;
                buffer[count++] = sim.Units.HandleAt(i);
            }
        }
        else
        {
            count = sim.Units.CopyOwnHandles(player, buffer);
        }

        if (count == 0) return Array.Empty<uint>();
        var members = new uint[count];
        buffer.AsSpan(0, count).CopyTo(members);
        return members;
    }

    private void SelectRect(CrowdSimSession sim, int player, Fix64 x0, Fix64 y0, Fix64 x1, Fix64 y1, bool additive)
    {
        var (ax, bx) = x0 < x1 ? (x0, x1) : (x1, x0);
        var (ay, by) = y0 < y1 ? (y0, y1) : (y1, y0);
        int count = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            int slot = sim.Units.SlotAt(i);
            var entity = sim.Units.EntityAt(i);
            int owner = sim.World.Get<PlayerOwner>(entity).PlayerId;
            if (owner != player)
            {
                _on[slot] = 0;
                continue;
            }

            var pos = sim.World.Get<WorldPositionCm>(entity).Value;
            bool inside = pos.X >= ax && pos.X <= bx && pos.Y >= ay && pos.Y <= by;
            bool keep = inside || (additive && _on[slot] != 0 && _gen[slot] == sim.Units.GenOf(slot));
            _on[slot] = keep ? (byte)1 : (byte)0;
            if (keep)
            {
                _gen[slot] = sim.Units.GenOf(slot);
                count++;
            }
        }

        _selectedCount = count;
    }

    private void SelectAll(CrowdSimSession sim, int player)
    {
        int count = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            int slot = sim.Units.SlotAt(i);
            var entity = sim.Units.EntityAt(i);
            bool own = sim.World.Get<PlayerOwner>(entity).PlayerId == player;
            _on[slot] = own ? (byte)1 : (byte)0;
            if (own)
            {
                _gen[slot] = sim.Units.GenOf(slot);
                count++;
            }
        }

        _selectedCount = count;
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

    private static int RequireInt(JsonObject cmd, string type, string field)
    {
        return cmd[field] is JsonValue value && value.TryGetValue<int>(out int parsed) && value.GetValueKind() == JsonValueKind.Number
            ? parsed
            : throw new InvalidOperationException($"指令 {type} 的 {field} 字段需为整数。");
    }

    private static bool OptionalBool(JsonObject cmd, string type)
    {
        var node = cmd["additive"];
        return node is JsonValue value && value.TryGetValue<bool>(out bool parsed)
            ? parsed
            : throw new InvalidOperationException($"指令 {type} 的 additive 字段需为布尔。");
    }
}
