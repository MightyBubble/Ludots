using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Tests;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 多玩家稳态零分配门。预热里完成生成、下令、重烘和若干次迷雾刷新；
/// 测量窗再下一轮指令和一次放置，窗内含迷雾刷新与 <see cref="CrowdSimulationRuntime.Tick"/>。
/// 当前线程分配增量必须为 0。
/// </summary>
public sealed class CrowdZeroAllocTests
{
    private const int UnitsPerPlayer = 200;
    private const int WarmupTicks = 120;
    private const int SteadyWindow = 80;
    private const int EventWindow = 40;

    [Test]
    public void CrowdRuntime_MultiPlayerEvents_ZeroAllocation()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var host = new CrowdSimulationRuntime();
        Bind(host, session);

        var setup = new List<CrowdCommand>
        {
            Cmd(0, """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":200,"unitType":0,"rIdx":0}"""),
            Cmd(0, """{"type":"spawnAt","player":2,"xCm":1120000,"yCm":1040000,"count":200,"unitType":0,"rIdx":0}"""),
            Cmd(2, """{"type":"order","player":1,"xCm":900000,"yCm":800000,"shape":"box"}"""),
            Cmd(2, """{"type":"order","player":2,"xCm":700000,"yCm":700000,"shape":"box"}"""),
            Cmd(8, """{"type":"placeStructure","template":"s7barrier","xCm":620000,"yCm":620000,"sizeCm":4000}"""),
        };
        session.Commands.Schedule(session, setup);
        for (int i = 0; i < WarmupTicks; i++)
        {
            host.Tick();
            if (session.TickCount != i + 1)
            {
                throw new InvalidOperationException($"预热第 {i} 步停摆，tick={session.TickCount}。");
            }
        }

        Assert.That(session.Units.Count, Is.EqualTo(UnitsPerPlayer * 2));
        Assert.That(session.Fog, Is.Not.Null);
        Assert.That(session.Telemetry.FogRefreshes, Is.GreaterThan(0));
        Assert.That(session.Telemetry.Rebakes, Is.GreaterThan(0));

        long steady = Measure(host, session, SteadyWindow, "steady");
        int at = session.TickCount;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            Cmd(at, """{"type":"order","player":1,"xCm":1000000,"yCm":900000,"shape":"box"}"""),
            Cmd(at, """{"type":"order","player":2,"xCm":600000,"yCm":800000,"shape":"box"}"""),
        });
        long orders = Measure(host, session, EventWindow, "orders");
        at = session.TickCount;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            Cmd(at, """{"type":"placeStructure","template":"building","xCm":700000,"yCm":640000,"sizeCm":8000}"""),
        });
        long rebake = Measure(host, session, EventWindow, "rebake");
        long after = Measure(host, session, SteadyWindow, "after");
        TestContext.Out.WriteLine(
            $"steady{SteadyWindow}={steady} orders{EventWindow}={orders} rebake{EventWindow}={rebake} after{SteadyWindow}={after} fog={session.Telemetry.FogRefreshes} rebakes={session.Telemetry.Rebakes} plans={session.Telemetry.Plans}");
        Assert.That(steady, Is.EqualTo(0), $"稳态 {SteadyWindow} tick 分配 {steady} 字节");
        Assert.That(orders, Is.EqualTo(0), $"下令窗 {EventWindow} tick 分配 {orders} 字节");
        Assert.That(rebake, Is.EqualTo(0), $"重烘窗 {EventWindow} tick 分配 {rebake} 字节");
        Assert.That(after, Is.EqualTo(0), $"事件后 {SteadyWindow} tick 分配 {after} 字节");
    }

    private static long Measure(CrowdSimulationRuntime host, CrowdSimSession session, int ticks, string label)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int at = session.TickCount;
        var hitTick = new int[16];
        var hitDelta = new long[16];
        int hits = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < ticks; i++)
        {
            long tickBefore = GC.GetAllocatedBytesForCurrentThread();
            host.Tick();
            long tickDelta = GC.GetAllocatedBytesForCurrentThread() - tickBefore;
            if (tickDelta != 0 && hits < hitTick.Length)
            {
                hitTick[hits] = session.TickCount;
                hitDelta[hits] = tickDelta;
                hits++;
            }

            if (session.TickCount != at + i + 1)
            {
                throw new InvalidOperationException($"测量第 {i} 步停摆，tick={session.TickCount}。");
            }
        }

        long total = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int i = 0; i < hits; i++) TestContext.Out.WriteLine($"{label} tick {hitTick[i]} +{hitDelta[i]}");
        return total;
    }

    private static CrowdCommand Cmd(int tick, string json)
        => new(tick, CrowdSimCommand.Parse(JsonNode.Parse(json)!));

    private static void Bind(CrowdSimulationRuntime host, CrowdSimSession session)
    {
        var relations = TeamRelationTestHarness.Create(session.World);
        var rules = CrowdPairRules.Matrices(1, new CrowdPushResponse[1], new bool[1]);
        var guard = new CrowdPairRulesSessionGuard(
            relations.Query,
            session.World,
            Array.Empty<Arch.Core.Entity>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            relations.HostileTypeId,
            relations.FriendlyTypeId,
            rules,
            relations.Query.Revision);
        Set(host, "_session", session);
        Set(host, "_pairRulesGuard", guard);
    }

    private static void Set(CrowdSimulationRuntime host, string field, object value)
    {
        typeof(CrowdSimulationRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
    }
}
