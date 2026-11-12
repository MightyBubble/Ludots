using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>半径级净空各不相同时，每只领队走的是它所带各组里净空最大的那份导航。</summary>
[TestFixture]
public sealed class LeaderNavUatTests
{
    private static readonly int[] RadiusCm = { 100, 3000, 6000, 9000 };

    [Test]
    public void Leader_PacesOnLargestClearanceOfItsBucket()
    {
        using var scene = CrowdUatKit.Scene(CrowdUatKit.DemoBodiesWithRadii(RadiusCm));
        var sim = scene.Session;
        var clearances = new HashSet<int>();
        foreach (var p in scene.Runtime.Profiles) clearances.Add(p.ClearanceNavCells);
        Assert.That(clearances.Count, Is.GreaterThanOrEqualTo(2), "场景无效：半径级净空没有拉开");

        sim.Commands.Submit(sim, new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = 600 }, CrowdSimCommands.Exec);
        Ludots.Core.Mathematics.FixedPoint.Fix64 half = scene.WorldCm / 2;
        CrowdUatKit.Order(sim, half, half);
        CrowdUatKit.Step(sim, 4);

        int leaders = 0, mixed = 0;
        var errors = new List<string>();
        foreach (var o in sim.Orders.List)
        {
            foreach (var leader in o.Leaders)
            {
                leaders++;
                int want = -1, least = int.MaxValue;
                foreach (var link in o.Groups)
                {
                    if (!sim.Groups.TryGet(link.GroupId, out var g) || g.Leader != leader) continue;
                    int c = CrowdDeployment.ProfileOf(scene.Runtime, g.LayerIdx, g.RIdx).ClearanceNavCells;
                    want = System.Math.Max(want, c);
                    least = System.Math.Min(least, c);
                }

                if (least < want) mixed++;

                if (leader.Nav.ClearanceNavCells != want) errors.Add($"领队导航 clearance {leader.Nav.ClearanceNavCells}，应为桶内最大 {want}");
            }
        }

        TestContext.Out.WriteLine($"leaders={leaders} mixedBuckets={mixed} clearances=[{string.Join(",", clearances)}]");
        Assert.That(leaders, Is.GreaterThan(0), "没有生成领队");
        Assert.That(mixed, Is.GreaterThan(0), "场景无效：没有一只领队带着不同净空的组");
        Assert.That(errors, Is.Empty);
    }
}
