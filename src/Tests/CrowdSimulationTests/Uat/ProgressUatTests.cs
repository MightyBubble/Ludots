using System;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>逐单位的行进手感：已集结方块被同目标连点时不后退、不在到达与行进之间反复翻转；行进中约 60° 转向时背离新目标的单位-tick 占比受限。</summary>
[TestFixture]
public sealed class ProgressUatTests
{
    internal const int Count = 120;
    internal const double EpsCells = 0.02;
    internal const double RepeatBackLimit = 0.03, RepeatFlipsLimit = 2, TurnBackLimit = 0.02;

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

    /// <summary>推进 n tick，每 tick 前先调 <paramref name="each"/>。</summary>
    private static CrowdProgressTally Measure(CrowdUatScene scene, int n, Fix64 tx, Fix64 ty, Action<int>? each)
    {
        var tally = new CrowdProgressTally(scene.Session, EpsCells * scene.CellCm, tx, ty);
        for (int t = 0; t < n; t++)
        {
            each?.Invoke(t);
            CrowdUatKit.Step(scene.Session);
            tally.Observe();
        }

        return tally;
    }

    private static bool Settle(CrowdSimSession sim, int max)
    {
        for (int t = 0; t < max; t++)
        {
            CrowdUatKit.Step(sim);
            if (CrowdMotionInvariants.Settled(sim)) return true;
        }

        return false;
    }
}
