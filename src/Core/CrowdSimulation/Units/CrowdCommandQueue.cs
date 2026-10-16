using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一条带 tick 的指令。载荷在边界一次解析为 struct，JsonNode 不进队列与日志。</summary>
public readonly record struct CrowdCommand(int Tick, CrowdSimCommand Cmd);

/// <summary>
/// 带 tick 的指令队列。
/// submit（现场指令，当前 tick 执行并记日志）、schedule（按 tick 升序的脚本，与存量归并）、
/// flush（执行本 tick 及以前的全部到点指令）。到点或已过期的指令立即执行。日志 tick 与当前 tick 不同时，只把日志 tick 改成当前 tick，不保留原 tick。
/// 两个入队口都先过校验：载荷结构在 CrowdSimCommand.Parse 边界一次成形，会话域（玩家号/
/// 迷雾启用）在 Validate——坏指令不进日志、不进队列。
/// 回放输入是指令日志。有结构仓时另用烘焙输入重建导航。
/// </summary>
public sealed class CrowdCommandQueue
{
    private readonly List<(int Tick, CrowdSimCommand Cmd)> _log = new(CrowdSimulationLimits.HotPathSlotReserve);
    private List<(int Tick, CrowdSimCommand Cmd)> _queue = new();
    private int _head;

    public IReadOnlyList<(int Tick, CrowdSimCommand Cmd)> Log => _log;
    public int PendingCount => _queue.Count - _head;

    /// <summary>未执行队列的第 <paramref name="index"/> 条（0 是下一条）。诊断摘要读队列里的那条。</summary>
    internal (int Tick, CrowdSimCommand Cmd) PendingAt(int index) => _queue[_head + index];

    /// <summary>现场指令(JSON 边界):解析、校验、记日志、当 tick 执行一气呵成。</summary>
    public T Submit<T>(CrowdSimSession sim, JsonNode cmd, Func<CrowdSimSession, CrowdSimCommand, T> exec)
        => Submit(sim, CrowdSimCommand.Parse(cmd), exec);

    /// <summary>现场指令(已成形载荷):现场生成方直接构造 struct,不过 JSON 边界。</summary>
    public T Submit<T>(CrowdSimSession sim, CrowdSimCommand cmd, Func<CrowdSimSession, CrowdSimCommand, T> exec)
    {
        cmd = CrowdSimCommands.Validate(sim, cmd);
        // 现场提交时单位已经在，句柄当场核对。脚本入队的句柄和全体己方选择都留到执行 tick 再解析。
        if (cmd.Kind == CrowdSimCommandKind.Order)
        {
            if (cmd.AllOwnMembers) throw new InvalidOperationException("order: 现场下令要给成员句柄，全体己方选择只用于脚本。");
            sim.BindMemberSet(cmd.Members!, cmd.Player);
        }
        _log.Add((sim.TickCount, cmd));
        T result = exec(sim, cmd);
        sim.Units.PlaybackStructuralChanges();
        sim.SyncKernelMirror();
        return result;
    }

    /// <summary>脚本入队(须按 tick 升序;与未执行的存量按 tick 归并,同 tick 先到先执行)。
    /// 条目按 <see cref="CrowdSimCommand.Detached"/> 入队，与调用方的数组脱钩。</summary>
    public void Schedule(CrowdSimSession sim, IReadOnlyList<CrowdCommand> entries)
    {
        for (int k = 1; k < entries.Count; k++)
        {
            if (entries[k].Tick < entries[k - 1].Tick)
            {
                throw new InvalidOperationException("指令脚本需按 tick 升序。");
            }
        }

        var resolved = new CrowdCommand[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            resolved[i] = new CrowdCommand(entries[i].Tick, CrowdSimCommands.Validate(sim, entries[i].Cmd));
        }

        var rest = _queue.GetRange(_head, PendingCount);
        var merged = new List<(int Tick, CrowdSimCommand Cmd)>(rest.Count + resolved.Length);
        int a = 0, b = 0;
        while (a < rest.Count || b < resolved.Length)
        {
            if (b >= resolved.Length || (a < rest.Count && rest[a].Tick <= resolved[b].Tick)) merged.Add(rest[a++]);
            else merged.Add((resolved[b].Tick, resolved[b++].Cmd.Detached()));
        }

        _queue = merged;
        _head = 0;
    }

    /// <summary>执行全部到点指令。已过期的立即执行，日志 tick 改成当前 tick。</summary>
    public void Flush(CrowdSimSession sim, Func<CrowdSimSession, CrowdSimCommand, object?> exec)
    {
        while (_head < _queue.Count && _queue[_head].Tick <= sim.TickCount)
        {
            var e = _queue[_head++];
            _log.Add(e.Tick == sim.TickCount ? e : (sim.TickCount, e.Cmd));
            exec(sim, e.Cmd);
        }
    }

    public void Reset()
    {
        _log.Clear();
        _queue = new List<(int, CrowdSimCommand)>();
        _head = 0;
    }
}
