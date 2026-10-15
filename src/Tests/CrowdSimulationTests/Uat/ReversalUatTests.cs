using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>行进中的方块收到 180° 折返令：新领队镜像排槽，每个单位沿旧航向在队内的相对位置不穿队，随后同向再令仍保持镜像。</summary>
[TestFixture]
public sealed class ReversalUatTests
{
    private const int Count = 120, Watch = 40, TolCells = 1, MinDepth = 4, MarchMax = 300;

    [Test]
    public void AboutFace_KeepsPlaceInBlock_AndMirrorIsInherited()
    {
        using var scene = CrowdUatKit.Block(Count);
        var sim = scene.Session;
        double cs = scene.CellCm;

        var (ex, ey) = CrowdUatKit.Spot(sim, scene.Nav, scene.Sx + scene.Cells(60), scene.Sy);
        CrowdUatKit.Order(sim, ex, ey);
        var first = CrowdUatKit.UntilLanded(sim, 60);
        Assert.That(first, Is.Not.Null, "场景无效：首令路径未落地");
        Fix64 hx = first!.Leader.Hx, hy = first.Leader.Hy;

        Dictionary<uint, Fix64> Proj()
        {
            var m = new Dictionary<uint, Fix64>(sim.Units.Count);
            Fix64 c = Fix64.Zero;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var p = CrowdUatKit.PositionAt(sim, i);
                c += p.X * hx + p.Y * hy;
            }

            c /= Math.Max(1, sim.Units.Count);
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var p = CrowdUatKit.PositionAt(sim, i);
                m[sim.Units.HandleAt(i)] = p.X * hx + p.Y * hy - c;
            }

            return m;
        }

        double DepthOf(Dictionary<uint, Fix64> s)
        {
            Fix64 lo = Fix64.MaxValue, hi = Fix64.MinValue;
            foreach (var v in s.Values)
            {
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }

            return (hi - lo).ToDouble() / cs;
        }

        var s0 = Proj();
        double depth = DepthOf(s0);
        for (int t = 0; t < MarchMax && depth < MinDepth && !first.Leader.Done; t += 5)
        {
            CrowdUatKit.Step(sim, 5);
            s0 = Proj();
            depth = DepthOf(s0);
        }

        Assert.That(first.Leader.Done, Is.False, "场景无效：折返前领队已到达（无上一任领队）");
        Assert.That(depth, Is.GreaterThanOrEqualTo(MinDepth), $"场景无效：行进 {MarchMax} tick 后队伍纵深 {depth:F1} 格 < {MinDepth}");

        Fix64 cx = scene.Sx - scene.Cells(60);
        var (rx, ry) = CrowdUatKit.Spot(sim, scene.Nav, cx, scene.Sy);
        CrowdUatKit.Order(sim, rx, ry);
        var rev = CrowdUatKit.UntilLanded(sim, 60);
        Assert.That(rev, Is.Not.Null, "场景无效：折返令路径未落地");
        double dot = (rev!.Leader.Hx * hx + rev.Leader.Hy * hy).ToDouble();
        Assert.That(dot, Is.LessThanOrEqualTo(-0.5), $"场景无效：折返令航向点积 {dot:F2} > −0.5");
        var errors = new List<string>();
        if (!rev.Leader.Mirror) errors.Add("折返后领队 mirror 应为 true");

        double worst = 0;
        for (int t = 0; t < Watch; t++)
        {
            CrowdUatKit.Step(sim);
            foreach (var (id, v) in Proj())
            {
                if (s0.TryGetValue(id, out var p)) worst = Math.Max(worst, Fix64.Abs(v - p).ToDouble() / cs);
            }
        }

        double lim = depth / 2 + TolCells;
        TestContext.Out.WriteLine($"depth={depth:F2} cells dot={dot:F3} worstShift={worst:F2} cells limit={lim:F2}");
        if (worst > lim) errors.Add($"折返穿队：单位相对位移 {worst:F1} 格 > 纵深/2+{TolCells}={lim:F1} 格");

        var (fx, fy) = CrowdUatKit.Spot(sim, scene.Nav, cx - scene.Cells(10), scene.Sy);
        CrowdUatKit.Order(sim, fx, fy);
        var again = CrowdUatKit.UntilLanded(sim, 60);
        Assert.That(again, Is.Not.Null, "场景无效：同向再令路径未落地");
        if (!CrowdUatKit.Newest(sim)!.Leaders[0].Mirror) errors.Add("同向再下令后 mirror 应仍为 true（继承不回翻）");
        Assert.That(errors, Is.Empty);
    }
}
