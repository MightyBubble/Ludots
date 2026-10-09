using System;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 指令入队校验门(玩家号/迷雾面/恰一形状/字段类型):坏指令在 Submit/Schedule 即拒——
/// 不进日志、不进队列,live 与回放对称(此前拖到 Exec 才抛,坏指令已入日志再停摆)。
/// 好指令照常入队执行,拒收不污染会话。
/// </summary>
public sealed class CrowdCommandQueueGateTests
{
    /// <summary>迷雾启用会话(S7Fog 同装配:BuildSession + EnableMovement,路径服务随用随释)。</summary>
    private static void WithFogSession(Action<CrowdSimSession> body)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        body(session);
    }

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

    /// <summary>迷雾面指令(fogSight/reveal/obscure/forget/fogShare 与 order.fogTerrain)
    /// 在无迷雾会话(BuildSession 不 EnableMovement,Fog == null)入队即拒。</summary>
    [Test]
    public void FogCommands_OnSessionWithoutFog_RejectedAtEnqueue()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var bad = new JsonNode[]
        {
            new JsonObject { ["type"] = "fogSight", ["on"] = true },
            new JsonObject { ["type"] = "reveal", ["player"] = 1, ["rect"] = new JsonArray(0, 0, 100, 100) },
            new JsonObject { ["type"] = "obscure", ["player"] = 1, ["circle"] = new JsonArray(0, 0, 50), ["ticks"] = 10 },
            new JsonObject
            {
                ["type"] = "forget", ["player"] = 1,
                ["poly"] = new JsonArray(new JsonArray(0, 0), new JsonArray(10, 0), new JsonArray(0, 10)),
            },
            new JsonObject { ["type"] = "fogShare", ["player"] = 1, ["with"] = 2 },
            new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = 0, ["yCm"] = 0, ["fogTerrain"] = true },
        };

        foreach (var cmd in bad)
        {
            string label = cmd["type"]!.GetValue<string>();
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec),
                $"{label} 无迷雾会话现场指令应在入队即拒");
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Schedule(session, new[] { new CrowdCommand(0, cmd) }),
                $"{label} 无迷雾会话脚本指令应在入队即拒");
        }

        Assert.That(session.Commands.Log, Is.Empty, "坏指令不得进日志");
        Assert.That(session.Commands.PendingCount, Is.EqualTo(0), "坏指令不得进队列");
        Assert.That(session.Step(), Is.Not.Null, "拒收后会话照常推进");
    }

    /// <summary>迷雾区域指令的 area 需且仅需一种形状——零形状/双形状入队即拒,
    /// 恰一形状照常入队执行。</summary>
    [Test]
    public void FogAreaCommands_WithoutExactlyOneShape_RejectedAtEnqueue()
    {
        WithFogSession(session =>
        {
            var bad = new JsonNode[]
            {
                new JsonObject { ["type"] = "reveal", ["player"] = 1 },
                new JsonObject { ["type"] = "obscure", ["player"] = 1, ["ticks"] = 10 },
                new JsonObject
                {
                    ["type"] = "forget", ["player"] = 1,
                    ["rect"] = new JsonArray(0, 0, 1, 1),
                    ["poly"] = new JsonArray(new JsonArray(0, 0), new JsonArray(1, 0), new JsonArray(0, 1)),
                },
                new JsonObject
                {
                    ["type"] = "reveal", ["player"] = 1,
                    ["rect"] = new JsonArray(0, 0, 1, 1),
                    ["circle"] = new JsonArray(0, 0, 1),
                },
            };

            foreach (var cmd in bad)
            {
                string label = cmd["type"]!.GetValue<string>();
                Assert.Throws<InvalidOperationException>(
                    () => session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec),
                    $"{label} 零/双形状应在入队即拒");
                Assert.Throws<InvalidOperationException>(
                    () => session.Commands.Schedule(session, new[] { new CrowdCommand(0, cmd) }),
                    $"{label} 零/双形状脚本应在入队即拒");
            }

            Assert.That(session.Commands.Log, Is.Empty, "坏指令不得进日志");
            Assert.That(session.Commands.PendingCount, Is.EqualTo(0), "坏指令不得进队列");

            session.Commands.Submit<object?>(
                session,
                new JsonObject { ["type"] = "reveal", ["player"] = 1, ["rect"] = new JsonArray(0.0, 0.0, 100.0, 100.0) },
                CrowdSimCommands.Exec);
            Assert.That(session.Commands.Log, Has.Count.EqualTo(1), "恰一形状的好指令照常记日志");
        });
    }

    /// <summary>player/with 字段类型错(字符串/小数/数组/布尔,含脚本解析路径)入队即拒,
    /// 合同异常;好指令照常。</summary>
    [Test]
    public void NonIntegerPlayerOrWithField_RejectedAtEnqueue()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var bad = new JsonNode[]
        {
            new JsonObject { ["type"] = "selectAll", ["player"] = "1" },
            new JsonObject { ["type"] = "selectAll", ["player"] = 1.5 },
            new JsonObject { ["type"] = "selectAll", ["player"] = new JsonArray(1) },
            new JsonObject { ["type"] = "selectAll", ["player"] = true },
            JsonNode.Parse("""{"type":"selectAll","player":"1"}""")!,
            JsonNode.Parse("""{"type":"select","player":1.5,"x0Cm":0,"y0Cm":0,"x1Cm":1,"y1Cm":1}""")!,
        };

        foreach (var cmd in bad)
        {
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec),
                "字段类型错的现场指令应在入队即拒(合同异常)");
            Assert.Throws<InvalidOperationException>(
                () => session.Commands.Schedule(session, new[] { new CrowdCommand(0, cmd) }),
                "字段类型错的脚本指令应在入队即拒(合同异常)");
        }

        Assert.That(session.Commands.Log, Is.Empty, "坏指令不得进日志");
        Assert.That(session.Commands.PendingCount, Is.EqualTo(0), "坏指令不得进队列");
        Assert.That(session.Step(), Is.Not.Null, "拒收后会话照常推进");

        WithFogSession(fogSession =>
        {
            Assert.Throws<InvalidOperationException>(
                () => fogSession.Commands.Submit<object?>(
                    fogSession,
                    new JsonObject { ["type"] = "fogShare", ["player"] = 1, ["with"] = "2" },
                    CrowdSimCommands.Exec),
                "with 字段类型错应在入队即拒");
            Assert.That(fogSession.Commands.Log, Is.Empty, "坏指令不得进日志");

            fogSession.Commands.Submit<object?>(
                fogSession,
                new JsonObject { ["type"] = "fogShare", ["player"] = 1, ["with"] = 2 },
                CrowdSimCommands.Exec);
            Assert.That(fogSession.Commands.Log, Has.Count.EqualTo(1), "整数字段的好指令照常记日志");
        });
    }
}
