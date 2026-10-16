using System.Reflection;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Diagnostics;
using CrowdSimulationTests.Parity;
using Ludots.Tests;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdRuntimeTickAllocTests
{
    [Test]
    public void RuntimeTick_WithDebugLogDisabled_AllocatesZero()
    {
        Log.SetGlobalLevel(LogLevel.Warning);
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        var host = new CrowdSimulationRuntime();
        Bind(host, session);
        for (int i = 0; i < 4; i++)
        {
            host.Tick();
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            host.Tick();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"关闭 Debug 日志后 tick 分配了 {allocated} 字节");
    }

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
