using System;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>行进中再下令时，领队从路径落地那一刻方块所在处出发；延迟长到方块走出请求格 8 邻域时，领队改从请求格中心出发，不追质心。</summary>
[TestFixture]
public sealed class CentroidUatTests
{
    private const int Count = 120, Mid = 40, SlowLatency = 60;
    private const double MaxStartDistCells = 1.5;

    private readonly record struct Reorder(double DistCells, Fix64 Px, Fix64 Py, int CellCm);

    [Test]
    public void DefaultLatency_LeaderStartsAtLandingCentroid()
    {
        var a = ReorderMidMarch(null);
        TestContext.Out.WriteLine($"dist={a.DistCells:F3} cells");
        Assert.That(a.DistCells, Is.LessThanOrEqualTo(MaxStartDistCells), $"中途再下令：领队起点距落地质心 {a.DistCells:F2} 格 > {MaxStartDistCells}");
    }

    [Test]
    public void LongLatency_LeaderFallsBackToRequestCellCentre()
    {
        var b = ReorderMidMarch(cfg => cfg["planning"]!["latencyTicks"] = SlowLatency);
        TestContext.Out.WriteLine($"fallbackDist={b.DistCells:F3} cells start=({b.Px.ToDouble():F1},{b.Py.ToDouble():F1})cm");
        Assert.That(b.DistCells, Is.GreaterThan(MaxStartDistCells), $"场景无效：长延迟下质心仍在起点邻域（{b.DistCells:F2} 格）");
        Fix64 half = Fix64.FromInt(b.CellCm) / 2;
        Fix64 cell = Fix64.FromInt(b.CellCm);
        bool Centre(Fix64 v) => (v - half) % cell == Fix64.Zero;
        Assert.That(Centre(b.Px) && Centre(b.Py), Is.True, "有界回退：质心离开 8 邻域时领队应从起点格中心出发");
    }

    private static Reorder ReorderMidMarch(Action<System.Text.Json.Nodes.JsonObject>? tune)
    {
        using var scene = CrowdUatKit.Block(Count, tune);
        var sim = scene.Session;
        var (ex, ey) = CrowdUatKit.Spot(sim, scene.Nav, scene.Sx + scene.Cells(60), scene.Sy);
        CrowdUatKit.Order(sim, ex, ey);
        Assert.That(CrowdUatKit.UntilLanded(sim, 120), Is.Not.Null, "场景无效：首令路径未落地");
        CrowdUatKit.Step(sim, Mid);
        var (tx, ty) = CrowdUatKit.Spot(sim, scene.Nav, scene.Sx + scene.Cells(60), scene.Sy + scene.Cells(30));
        CrowdUatKit.Order(sim, tx, ty);
        var r = CrowdUatKit.UntilLanded(sim, 120, () => CrowdUatKit.Centroid(sim, CrowdUatKit.Newest(sim)!));
        Assert.That(r?.Pre, Is.Not.Null, "场景无效：再下令路径未落地");
        var start = r!.Leader.PathAt(0);
        var (mx, my) = r.Pre!.Value;
        double dist = CrowdFixHypot(start.X - mx, start.Y - my) / scene.CellCm;
        return new Reorder(dist, start.X, start.Y, scene.CellCm);
    }

    private static double CrowdFixHypot(Fix64 dx, Fix64 dy)
        => Ludots.Core.CrowdSimulation.Movement.CrowdFix.Hypot(dx, dy).ToDouble();
}
