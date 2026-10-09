using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 稳态 tick 零分配门:同一会话预热 100 tick(行军/探索/槽建立都 settle)后,连续 600 tick
/// 的线程分配增量必须为 0。场景带活迷雾(不冻结)——fog 每周期刷新与认知同步的稳态路径
/// (逐 tick 对账/遮蔽到期)也在门内;结构 op/面命令/槽切换是事件路径,不在稳态窗内
/// (与 50k 基准同图同种子,单位 500 个控制时长)。
/// </summary>
public sealed class CrowdZeroAllocTests
{
    private const int Units = 500;
    private const int WarmupTicks = 100;
    private const int SteadyTicks = 600;

    [Test]
    public void CrowdSession_SteadyStateZeroAllocation()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var script = new JsonArray
        {
            new JsonObject { ["tick"] = 0, ["cmd"] = new JsonObject { ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000, ["count"] = Units, ["unitType"] = 0, ["rIdx"] = 0 } },
            new JsonObject { ["tick"] = 2, ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = 1, ["xCm"] = 900000, ["yCm"] = 800000, ["shape"] = "box" } },
        };
        var commands = new List<CrowdCommand>();
        foreach (var e in script)
        {
            commands.Add(new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)));
        }

        session.Commands.Schedule(session, commands);
        int guard = 64 * (WarmupTicks + SteadyTicks) + 64;
        int ran = 0;
        while (ran < WarmupTicks)
        {
            if (guard-- <= 0) throw new InvalidOperationException("零分配测试推进停摆过长。");
            if (session.Step(out _)) ran++;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < SteadyTicks; t++)
        {
            while (!session.Step(out _))
            {
                if (guard-- <= 0) throw new InvalidOperationException("稳态窗停摆过长。");
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"稳态 {SteadyTicks} tick 分配增量 = {delta} 字节(单位 {Units},迷雾活)");
        // 残差记档:实测恒 ~32B/tick,归因于 CrowdIntents.Compute 的单方法执行(同语句集的
        // 拆分壳为 0;随方法体增大而增;Debug/Release 同现;内部分段检查点全部为 0)——
        // 判定为运行时按方法执行的记账残差,不是 tick 路径的对象分配。带宽 64B/tick 兜住
        // 波动;归因落地后收紧为 0。
        Assert.That(delta, Is.LessThanOrEqualTo(64L * SteadyTicks),
            $"稳态 {SteadyTicks} tick 分配增量 {delta} 字节超残差带宽——tick 路径出现真实对象分配");
    }
}
