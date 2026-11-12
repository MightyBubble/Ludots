using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>公用场景工具本身可用：方块生成在可走格上，下令后领队路径在限内落地，质心只数跟随者。</summary>
[TestFixture]
public sealed class CrowdUatKitTests
{
    [Test]
    public void Block_Order_UntilLanded_GivesLeaderAndCentroid()
    {
        using var scene = CrowdUatKit.Block(120);
        var session = scene.Session;
        Assert.That(session.Units.Count, Is.EqualTo(120));

        var (ex, ey) = CrowdUatKit.Spot(session, scene.Nav, scene.Sx + scene.Cells(60), scene.Sy);
        CrowdUatKit.Order(session, ex, ey);
        var landed = CrowdUatKit.UntilLanded(session, 60);

        Assert.That(landed, Is.Not.Null, "首令路径 60 tick 内未落地");
        Assert.That(landed!.Order, Is.SameAs(CrowdUatKit.Newest(session)));
        Assert.That(landed.Leader.Hx, Is.GreaterThan(Fix64.Zero), "领队朝向应指向东侧目标");
        var c = CrowdUatKit.Centroid(session, landed.Order);
        Assert.That(c, Is.Not.Null);
        Fix64 d = Fix64.Abs(c!.Value.X - scene.Sx) + Fix64.Abs(c.Value.Y - scene.Sy);
        Assert.That(d.ToDouble(), Is.LessThan(scene.Cells(3).ToDouble()), "刚落地时质心应还在出生格附近");
        TestContext.Out.WriteLine($"landed at tick {session.TickCount}, centroid offset {d.ToDouble() / scene.CellCm:F2} cells");
    }
}
