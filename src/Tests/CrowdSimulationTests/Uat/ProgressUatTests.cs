using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>逐单位的行进手感：已集结方块被同目标连点时不后退、不在到达与行进之间反复翻转；行进中约 60° 转向时背离新目标的单位-tick 占比受限。</summary>
[TestFixture]
public sealed class ProgressUatTests
{
    private const int Count = 120;
    private const double EpsCells = 0.02;
    private const double RepeatBackLimit = 0.03, RepeatFlipsLimit = 2, TurnBackLimit = 0.02;

    private readonly struct Measured
    {
        public Measured(double back, int moved, int flips) { Back = back; Moved = moved; Flips = flips; }
        public double Back { get; }
        public int Moved { get; }
        public int Flips { get; }
    }

    [Test]
    public void RepeatClicks_DoNotBackstepOrFlip_AndTurnStaysForward()
    {
        using var scene = CrowdUatKit.Block(Count);
        var sim = scene.Session;

        var (ax, ay) = CrowdUatKit.Spot(sim, scene.Nav, scene.Sx + scene.Cells(20), scene.Sy);
        CrowdUatKit.Order(sim, ax, ay);
        Assert.That(Settle(sim, 900), Is.True, "场景无效：部队未集结");
        var r = Measure(scene, 80, ax, ay, t => { if (t % 10 == 0) CrowdUatKit.Order(sim, ax, ay); });
        double flipsPerUnit = (double)r.Flips / Count;

        var (bx, by) = CrowdUatKit.Spot(sim, scene.Nav, ax + scene.Cells(40), ay);
        CrowdUatKit.Order(sim, bx, by);
        CrowdUatKit.Step(sim, 30);
        var (tx, ty) = CrowdUatKit.Spot(sim, scene.Nav, ax + scene.Cells(20), ay + scene.Cells(30));
        CrowdUatKit.Order(sim, tx, ty);
        var q = Measure(scene, 20, tx, ty, null);

        TestContext.Out.WriteLine($"repeatBack={r.Back:P2} repeatFlips/unit={flipsPerUnit:F2} turnBack={q.Back:P2} turnMoved={q.Moved}");
        Assert.Multiple(() =>
        {
            Assert.That(r.Back, Is.LessThanOrEqualTo(RepeatBackLimit), $"同目标连点：逆向单位-tick {r.Back:P1} > {RepeatBackLimit:P0}");
            Assert.That(flipsPerUnit, Is.LessThanOrEqualTo(RepeatFlipsLimit), $"同目标连点：人均状态翻转 {flipsPerUnit:F1} 次 > {RepeatFlipsLimit}");
            Assert.That(q.Moved, Is.GreaterThan(0), "场景无效：转向后无单位移动");
            Assert.That(q.Back, Is.LessThanOrEqualTo(TurnBackLimit), $"行进中 ~60° 转向：逆向单位-tick {q.Back:P1} > {TurnBackLimit:P0}");
        });
    }

    /// <summary>推进 n tick，数背离 (tx, ty) 的单位-tick（沿指向目标方向的位移分量 &lt; −eps，不设距离门）与到达↔非到达翻转。</summary>
    private static Measured Measure(CrowdUatScene scene, int n, Fix64 tx, Fix64 ty, Action<int>? each)
    {
        var sim = scene.Session;
        double eps = EpsCells * scene.CellCm, txd = tx.ToDouble(), tyd = ty.ToDouble();
        var prev = new Dictionary<uint, (double X, double Y, bool Arrived)>();
        void Snap()
        {
            prev.Clear();
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var p = CrowdUatKit.PositionAt(sim, i);
                prev[sim.Units.HandleAt(i)] = (p.X.ToDouble(), p.Y.ToDouble(), CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived);
            }
        }

        int back = 0, moved = 0, flips = 0;
        Snap();
        for (int t = 0; t < n; t++)
        {
            each?.Invoke(t);
            CrowdUatKit.Step(sim);
            for (int i = 0; i < sim.Units.Count; i++)
            {
                if (!prev.TryGetValue(sim.Units.HandleAt(i), out var p)) continue;
                var c = CrowdUatKit.PositionAt(sim, i);
                double dx = c.X.ToDouble() - p.X, dy = c.Y.ToDouble() - p.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > eps)
                {
                    moved++;
                    double gx = txd - p.X, gy = tyd - p.Y, gl = Math.Sqrt(gx * gx + gy * gy);
                    double along = (dx * gx + dy * gy) / (gl > 0 ? gl : 1);
                    if (along < -eps) back++;
                }

                bool arrived = CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived;
                if (arrived != p.Arrived) flips++;
            }

            Snap();
        }

        return new Measured((double)back / Math.Max(1, sim.Units.Count * n), moved, flips);
    }

    private static bool Settle(CrowdSimSession sim, int max)
    {
        for (int t = 0; t < max; t++)
        {
            CrowdUatKit.Step(sim);
            int a = 0;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                if (CrowdUatKit.StateAt(sim, i).State == (byte)CrowdUnitState.Arrived) a++;
            }

            if (a * 100 >= sim.Units.Count * 95) return true;
        }

        return false;
    }
}
