using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// 窗两端用 <see cref="GC.GetTotalAllocatedBytes(bool)"/> 的精确读数，工人线程的分配算在同一扇窗里。
/// 读数放在独立进程：测试宿主的计时器与线程池回调会进同一进程计数，不能当成模拟的分配。
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
        string dll = typeof(CrowdZeroAllocTests).Assembly.Location;
        var start = new ProcessStartInfo
        {
            FileName = DotnetHost(),
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--zero-alloc");
        using var proc = Process.Start(start)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(180_000))
        {
            proc.Kill(entireProcessTree: true);
            Assert.Fail("零分配门进程超过 180 秒没有退出。");
        }

        TestContext.Out.WriteLine(stdout);
        if (stderr.Length > 0) TestContext.Out.WriteLine(stderr);
        Assert.That(proc.ExitCode, Is.EqualTo(0), stdout + stderr);
        var windows = Parse(stdout);
        Assert.That(windows.Steady, Is.EqualTo(0), $"稳态 {SteadyWindow} tick 分配 {windows.Steady} 字节");
        Assert.That(windows.Orders, Is.EqualTo(0), $"下令窗 {EventWindow} tick 分配 {windows.Orders} 字节");
        Assert.That(windows.Rebake, Is.EqualTo(0), $"重烘窗 {EventWindow} tick 分配 {windows.Rebake} 字节");
        Assert.That(windows.After, Is.EqualTo(0), $"事件后 {SteadyWindow} tick 分配 {windows.After} 字节");
        Assert.That(windows.FogRefreshes, Is.GreaterThan(0));
        Assert.That(windows.Rebakes, Is.GreaterThan(0));
    }

    public static int RunGate()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var host = new CrowdSimulationRuntime();
        Bind(host, session);
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            Cmd(0, """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":200,"unitType":0,"rIdx":0}"""),
            Cmd(0, """{"type":"spawnAt","player":2,"xCm":1120000,"yCm":1040000,"count":200,"unitType":0,"rIdx":0}"""),
            Cmd(2, """{"type":"order","player":1,"xCm":900000,"yCm":800000,"shape":"box"}"""),
            Cmd(2, """{"type":"order","player":2,"xCm":700000,"yCm":700000,"shape":"box"}"""),
            Cmd(8, """{"type":"placeStructure","template":"s7barrier","xCm":620000,"yCm":620000,"sizeCm":4000}"""),
        });
        for (int i = 0; i < WarmupTicks; i++)
        {
            host.Tick();
            if (session.TickCount != i + 1)
                throw new InvalidOperationException($"预热第 {i} 步停摆，tick={session.TickCount}。");
        }

        if (session.Units.Count != UnitsPerPlayer * 2)
            throw new InvalidOperationException($"预热后单位 {session.Units.Count}，应为 {UnitsPerPlayer * 2}。");
        if (session.Fog == null) throw new InvalidOperationException("零分配门缺少迷雾。");
        if (session.Telemetry.FogRefreshes <= 0) throw new InvalidOperationException("预热没有迷雾刷新。");
        if (session.Telemetry.Rebakes <= 0) throw new InvalidOperationException("预热没有重烘。");

        long steady = Measure(host, session, SteadyWindow);
        int at = session.TickCount;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            Cmd(at, """{"type":"order","player":1,"xCm":1000000,"yCm":900000,"shape":"box"}"""),
            Cmd(at, """{"type":"order","player":2,"xCm":600000,"yCm":800000,"shape":"box"}"""),
        });
        long orders = Measure(host, session, EventWindow);
        at = session.TickCount;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            Cmd(at, """{"type":"placeStructure","template":"building","xCm":700000,"yCm":640000,"sizeCm":8000}"""),
        });
        long rebake = Measure(host, session, EventWindow);
        long after = Measure(host, session, SteadyWindow);
        Console.WriteLine(
            $"steady={steady} orders={orders} rebake={rebake} after={after} fog={session.Telemetry.FogRefreshes} rebakes={session.Telemetry.Rebakes} plans={session.Telemetry.Plans}");
        return steady == 0 && orders == 0 && rebake == 0 && after == 0 ? 0 : 1;
    }

    private static long Measure(CrowdSimulationRuntime host, CrowdSimSession session, int ticks)
    {
        _ = GC.GetTotalAllocatedBytes(precise: true);
        int at = session.TickCount;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < ticks; i++)
        {
            host.Tick();
            if (session.TickCount != at + i + 1)
                throw new InvalidOperationException($"测量第 {i} 步停摆，tick={session.TickCount}。");
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    private readonly record struct Windows(long Steady, long Orders, long Rebake, long After, int FogRefreshes, int Rebakes);

    private static Windows Parse(string stdout)
    {
        string? line = null;
        foreach (var raw in stdout.Split('\n'))
        {
            if (raw.StartsWith("steady=", StringComparison.Ordinal)) line = raw.Trim();
        }

        if (line == null) throw new InvalidOperationException("零分配门没有打印窗口读数。\n" + stdout);
        long steady = 0, orders = 0, rebake = 0, after = 0;
        int fog = 0, rebakes = 0;
        foreach (var part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq];
            string value = part[(eq + 1)..];
            switch (key)
            {
                case "steady": steady = long.Parse(value); break;
                case "orders": orders = long.Parse(value); break;
                case "rebake": rebake = long.Parse(value); break;
                case "after": after = long.Parse(value); break;
                case "fog": fog = int.Parse(value); break;
                case "rebakes": rebakes = int.Parse(value); break;
            }
        }

        return new Windows(steady, orders, rebake, after, fog, rebakes);
    }

    private static string DotnetHost()
    {
        string? path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path)
            && !Path.GetFileName(path).Contains("testhost", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root)) return Path.Combine(root, "dotnet");
        return "dotnet";
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
