using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一条带 tick 的指令(数据;指令流 + 初始态 = 完整输入,回放即复现)。
/// 载荷在边界一次解析为 struct,JsonNode 不进队列与日志。</summary>
public readonly record struct CrowdCommand(int Tick, CrowdSimCommand Cmd);

/// <summary>
/// TICK 戳指令队列(core/commands.js CommandQueue 移植):
/// submit(现场指令,当前 tick 执行并记日志)、schedule(按 tick 升序的脚本,与存量归并)、
/// flush(执行本 tick 及以前的全部到点指令;迟到的指令按实际执行 tick 记日志并标 lateFrom)。
/// 两个入队口都先过校验:载荷结构在 CrowdSimCommand.Parse 边界一次成形,会话域(玩家号/
/// 迷雾启用)在 Validate——坏指令不进日志/队列,live 与回放对称拒收。
/// 日志是会话的完整输入:sources + log 重放即复现状态。
/// </summary>
public sealed class CrowdCommandQueue
{
    private readonly List<(int Tick, CrowdSimCommand Cmd)> _log = new();
    private List<(int Tick, CrowdSimCommand Cmd)> _queue = new();
    private int _head;

    public IReadOnlyList<(int Tick, CrowdSimCommand Cmd)> Log => _log;
    public int PendingCount => _queue.Count - _head;

    /// <summary>现场指令(JSON 边界):解析、校验、记日志、当 tick 执行一气呵成。</summary>
    public T Submit<T>(CrowdSimSession sim, JsonNode cmd, Func<CrowdSimSession, CrowdSimCommand, T> exec)
        => Submit(sim, CrowdSimCommand.Parse(cmd), exec);

    /// <summary>现场指令(已成形载荷):现场生成方直接构造 struct,不过 JSON 边界。</summary>
    public T Submit<T>(CrowdSimSession sim, CrowdSimCommand cmd, Func<CrowdSimSession, CrowdSimCommand, T> exec)
    {
        CrowdSimCommands.Validate(sim, cmd);
        _log.Add((sim.TickCount, cmd));
        return exec(sim, cmd);
    }

    /// <summary>脚本入队(须按 tick 升序;与未执行的存量按 tick 归并,同 tick 先到先执行)。</summary>
    public void Schedule(CrowdSimSession sim, IReadOnlyList<CrowdCommand> entries)
    {
        for (int k = 1; k < entries.Count; k++)
        {
            if (entries[k].Tick < entries[k - 1].Tick)
            {
                throw new InvalidOperationException("指令脚本需按 tick 升序。");
            }
        }

        foreach (var e in entries) CrowdSimCommands.Validate(sim, e.Cmd);

        var rest = _queue.GetRange(_head, PendingCount);
        var merged = new List<(int Tick, CrowdSimCommand Cmd)>(rest.Count + entries.Count);
        int a = 0, b = 0;
        while (a < rest.Count || b < entries.Count)
        {
            if (b >= entries.Count || (a < rest.Count && rest[a].Tick <= entries[b].Tick)) merged.Add(rest[a++]);
            else merged.Add((entries[b].Tick, entries[b++].Cmd));
        }

        _queue = merged;
        _head = 0;
    }

    /// <summary>执行全部到点指令(已过期的立即执行,日志记实际执行 tick 与 lateFrom)。</summary>
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
