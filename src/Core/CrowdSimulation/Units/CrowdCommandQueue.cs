using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一条带 tick 的指令(数据;指令流 + 初始态 = 完整输入,回放即复现)。</summary>
public readonly record struct CrowdCommand(int Tick, JsonNode Payload);

/// <summary>
/// TICK 戳指令队列(core/commands.js CommandQueue 移植):
/// submit(现场指令,当前 tick 执行并记日志)、schedule(按 tick 升序的脚本,与存量归并)、
/// flush(执行本 tick 及以前的全部到点指令;迟到的指令按实际执行 tick 记日志并标 lateFrom)。
/// 日志是会话的完整输入:sources + log 重放即复现状态。
/// </summary>
public sealed class CrowdCommandQueue
{
    private readonly List<(int Tick, JsonNode Cmd)> _log = new();
    private List<(int Tick, JsonNode Cmd)> _queue = new();
    private int _head;

    public IReadOnlyList<(int Tick, JsonNode Cmd)> Log => _log;
    public int PendingCount => _queue.Count - _head;

    public T Submit<T>(CrowdSimSession sim, JsonNode cmd, Func<CrowdSimSession, JsonNode, T> exec)
    {
        var copy = (JsonNode)cmd.DeepClone();
        _log.Add((sim.TickCount, copy));
        return exec(sim, copy);
    }

    /// <summary>脚本入队(须按 tick 升序;与未执行的存量按 tick 归并,同 tick 先到先执行)。</summary>
    public void Schedule(IReadOnlyList<CrowdCommand> entries)
    {
        for (int k = 1; k < entries.Count; k++)
        {
            if (entries[k].Tick < entries[k - 1].Tick)
            {
                throw new InvalidOperationException("指令脚本需按 tick 升序。");
            }
        }

        var rest = _queue.GetRange(_head, PendingCount);
        var add = new List<(int Tick, JsonNode Cmd)>(entries.Count);
        foreach (var e in entries) add.Add((e.Tick, (JsonNode)e.Payload.DeepClone()));
        var merged = new List<(int Tick, JsonNode Cmd)>(rest.Count + add.Count);
        int a = 0, b = 0;
        while (a < rest.Count || b < add.Count)
        {
            if (b >= add.Count || (a < rest.Count && rest[a].Tick <= add[b].Tick)) merged.Add(rest[a++]);
            else merged.Add(add[b++]);
        }

        _queue = merged;
        _head = 0;
    }

    /// <summary>执行全部到点指令(D29:已过期的立即执行,日志记实际执行 tick 与 lateFrom)。</summary>
    public void Flush(CrowdSimSession sim, Func<CrowdSimSession, JsonNode, object?> exec)
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
        _queue = new List<(int, JsonNode)>();
        _head = 0;
    }
}
