using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>
/// 一次活会话的回放输入。命令只取已经执行的日志：未到点的队列还没改当前 tick 的状态。
/// <see cref="Handles"/> 是句柄断面，不在合计校验码里。
/// </summary>
public sealed record CrowdSimReplayCapture(
    CrowdCommand[] Commands,
    int Tick,
    ulong Checksum,
    CrowdSimStateDigest Digest,
    ulong Handles)
{
    public CrowdSimReplayCapture WithCommands(CrowdCommand[] commands) => this with { Commands = commands };
}

public sealed class CrowdSimReplayReport
{
    public required bool Passed { get; init; }
    public required ulong Expected { get; init; }
    public required ulong Checksum { get; init; }
    public required int Tick { get; init; }
    public required int Commands { get; init; }
    public required IReadOnlyList<string> Differing { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 把捕获重放到调用方给的新会话上，对同一 tick 的合计和分区。
/// 新会话要自己带同一套导航、运动策略和冻结开关，这里不重建世界。
/// 运行时环比较仍走 <c>CrowdSimulationRuntime.RunReplay</c>。
/// </summary>
public static class CrowdSimReplay
{
    public static CrowdSimReplayCapture Capture(CrowdSimSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var log = session.Commands.Log;
        var commands = new CrowdCommand[log.Count];
        for (int i = 0; i < log.Count; i++)
        {
            var entry = log[i];
            commands[i] = new CrowdCommand(entry.Tick, entry.Cmd);
        }

        bool diagnostic = session.DiagnosticDigest;
        session.DiagnosticDigest = true;
        CrowdSimStateDigest digest;
        try
        {
            digest = CrowdSimChecksum.Compute(session);
        }
        finally
        {
            session.DiagnosticDigest = diagnostic;
        }

        return new CrowdSimReplayCapture(
            commands,
            session.TickCount,
            session.LastDigest.Total,
            digest,
            CrowdSimChecksum.MixHandles(session));
    }

    public static CrowdSimReplayReport Replay(CrowdSimSession session, CrowdSimReplayCapture capture)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(capture);
        if (session.TickCount != 0)
        {
            return Report(capture, session, Array.Empty<string>(), "回放会话要从 tick 0 开始。");
        }

        session.DiagnosticDigest = true;
        try
        {
            session.Commands.Schedule(session, capture.Commands);
            session.Advance(capture.Tick);
        }
        catch (Exception ex)
        {
            return Report(capture, session, Array.Empty<string>(), ex.Message);
        }

        if (session.TickCount != capture.Tick)
        {
            return Report(capture, session, Array.Empty<string>(), $"回放到 tick {session.TickCount}，记录是 {capture.Tick}。");
        }

        var names = new List<string>();
        CrowdSimChecksum.CollectDifferences(
            capture.Digest, capture.Handles, session.LastDigest, CrowdSimChecksum.MixHandles(session), names);
        if (session.LastDigest.Total != capture.Checksum && !names.Contains(CrowdSimChecksum.AvoidanceSection))
        {
            names.Add(CrowdSimChecksum.AvoidanceSection);
        }

        string? error = names.Count == 0 ? null : "分区不一致: " + string.Join(",", names);
        return Report(capture, session, names, error);
    }

    private static CrowdSimReplayReport Report(
        CrowdSimReplayCapture capture, CrowdSimSession session, IReadOnlyList<string> differing, string? error)
        => new()
        {
            Passed = error == null,
            Expected = capture.Checksum,
            Checksum = session.TickCount == 0 ? 0 : session.LastDigest.Total,
            Tick = session.TickCount,
            Commands = capture.Commands.Length,
            Differing = differing,
            Error = error,
        };
}
