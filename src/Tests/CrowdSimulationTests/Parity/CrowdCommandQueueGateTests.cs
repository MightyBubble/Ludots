using System;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 指令入队校验门(L23):玩家号表外的坏指令在 Submit/Schedule 即拒——不进日志、不进队列,
/// live 与回放对称(此前拖到 Exec 才抛,坏指令已入日志再停摆)。好指令照常入队执行,
/// 拒收不污染会话。
/// </summary>
public sealed class CrowdCommandQueueGateTests
{
    [Test]
    public void BadPlayerCommand_RejectedAtEnqueue_NeverEntersLogOrQueue()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");

        foreach (int badPlayer in new[] { 0, int.MaxValue })
        {
            var bad = new JsonObject { ["type"] = "selectAll", ["player"] = badPlayer };
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Submit<object?>(session, bad, CrowdSimCommands.Exec),
                $"player={badPlayer} 现场指令应在入队即拒");
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Schedule(session, new[] { new CrowdCommand(0, bad) }),
                $"player={badPlayer} 脚本指令应在入队即拒");
        }

        Assert.That(session.Commands.Log, Is.Empty, "坏指令不得进日志");
        Assert.That(session.Commands.PendingCount, Is.EqualTo(0), "坏指令不得进队列");
        Assert.That(session.Step(), Is.Not.Null, "拒收后会话照常推进");
    }

    [Test]
    public void ValidCommand_StillEnqueuesAndExecutes()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");

        session.Commands.Submit<object?>(
            session,
            new JsonObject { ["type"] = "selectAll", ["player"] = 1 },
            CrowdSimCommands.Exec);

        Assert.That(session.Commands.Log, Has.Count.EqualTo(1), "好指令照常记日志");
    }
}
