using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>
/// 重复下令走判重早退。预热一次后，同参数第二次不得再拼字符串、再建字典。
/// </summary>
public sealed class CrowdOrderDedupAllocTests
{
    [Test]
    public void RepeatedIssue_SecondCallAllocatesNothing()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 32, 1, 0, 0), Is.EqualTo(32));

        string shape = runtime.Formations[0].Id;
        var mode = CrowdIssueOrder.Issue(session, Fix64.FromInt(900000), Fix64.FromInt(800000), 1, shape, null, null, Fix64.Zero);
        Assert.That(mode, Is.Not.Null);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var again = CrowdIssueOrder.Issue(session, Fix64.FromInt(900000), Fix64.FromInt(800000), 1, shape, null, null, Fix64.Zero);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"repeated issue allocated {delta} bytes");
        Assert.That(again, Is.EqualTo(mode));
        Assert.That(delta, Is.EqualTo(0), $"重复下令分配了 {delta} 字节");

        var first = CrowdDeployment.DistinctRadiusClasses(runtime);
        long radiusBefore = GC.GetAllocatedBytesForCurrentThread();
        var second = CrowdDeployment.DistinctRadiusClasses(runtime);
        long radiusDelta = GC.GetAllocatedBytesForCurrentThread() - radiusBefore;
        TestContext.Out.WriteLine($"repeated radius-class read allocated {radiusDelta} bytes");
        Assert.That(radiusDelta, Is.EqualTo(0), $"半径级表重复读取分配了 {radiusDelta} 字节");
        var radiusReturn = typeof(CrowdDeployment).GetMethod(nameof(CrowdDeployment.DistinctRadiusClasses))!.ReturnType;
        Assert.That(radiusReturn, Is.EqualTo(typeof(IReadOnlyList<int>)));
        Assert.That(radiusReturn.GetMethod("Add"), Is.Null);
        Assert.That(first.GetType().GetMethod("Add"), Is.Null);
        Assert.That(second.GetType().GetMethod("Add"), Is.Null);
        Assert.That(first, Is.Not.InstanceOf<List<int>>());
        Assert.That(second, Is.Not.InstanceOf<List<int>>());
        Assert.That(first, Is.Not.InstanceOf<int[]>());
    }
}
