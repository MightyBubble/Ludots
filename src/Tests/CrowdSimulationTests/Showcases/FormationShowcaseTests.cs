using System;
using System.Collections.Generic;
using System.Linq;
using CrowdSimulationTests.Uat;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 行军、折返、连点三个演示 Mod 走真实进图，脚本由会话宿主自己跑，在脚本指令的 tick 上核对行进 UAT 与折返 UAT 的同一组不变量和阈值。
/// 场景几何按 UAT 的格数在本会话的导航上重算，和脚本坐标逐一比对。
/// </summary>
[TestFixture]
public sealed class FormationShowcaseTests
{
    private const int P = CrowdUatKit.Player;
    private const int SettleMax = 900;
    private const int LandMax = 60;
    private const int RepeatWindow = 80, RepeatEvery = 10, TurnWindow = 20, TurnDelay = 30;

    [Test]
    public void Reversal_KeepsPlaceInBlock_AndMirrorIsInherited()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationReversalShowcaseMod");
        var sim = host.Session;
        var g = Geometry.Of(sim);
        int[] orders = host.TicksOf("order", P);
        Assert.That(orders, Has.Length.EqualTo(3), "折返脚本应有首令、折返令、同向再令三条");
        AssertSpawn(host, g.Sx, g.Sy, ReversalUatTests.Count);
        AssertTarget(host, 0, g.Spot(60, 0));
        AssertTarget(host, 1, g.Spot(-60, 0));
        AssertTarget(host, 2, g.Spot(-70, 0));

        var first = UntilLanded(host, orders[0], null);
        Assert.That(first, Is.Not.Null, "场景无效：首令路径未落地");
        int landed = sim.TickCount;
        Fix64 hx = first!.Leaders[0].Hx, hy = first.Leaders[0].Hy;
        Assert.That(orders[1], Is.GreaterThanOrEqualTo(landed), $"折返令（tick {orders[1]}）早于首令落地（tick {landed}）");
        Assert.That(orders[1] - landed, Is.LessThanOrEqualTo(ReversalUatTests.MarchMax), "折返前行进超过 UAT 上限");
        host.AdvanceTo(orders[1]);
        var s0 = CrowdMotionInvariants.HeadingProjection(sim, hx, hy);
        double depth = CrowdMotionInvariants.DepthCells(s0, g.CellCm);
        Assert.That(first.Leaders[0].Done, Is.False, "场景无效：折返前领队已到达（无上一任领队）");
        Assert.That(depth, Is.GreaterThanOrEqualTo(ReversalUatTests.MinDepth), $"场景无效：折返时队伍纵深 {depth:F2} 格");

        var rev = UntilLanded(host, orders[1], first);
        Assert.That(rev, Is.Not.Null, "场景无效：折返令路径未落地");
        int revLanded = sim.TickCount;
        double dot = (rev!.Leaders[0].Hx * hx + rev.Leaders[0].Hy * hy).ToDouble();
        Assert.That(dot, Is.LessThanOrEqualTo(-0.5), $"场景无效：折返令航向点积 {dot:F2} > −0.5");
        var errors = new List<string>();
        if (!rev.Leaders[0].Mirror) errors.Add("折返后领队 mirror 应为 true");

        Assert.That(orders[2], Is.GreaterThanOrEqualTo(revLanded + ReversalUatTests.Watch),
            $"同向再令（tick {orders[2]}）截断了折返后的 {ReversalUatTests.Watch} tick 观察窗（落地 tick {revLanded}）");
        double worst = 0;
        host.AdvanceTo(revLanded + ReversalUatTests.Watch,
            () => worst = Math.Max(worst, CrowdMotionInvariants.WorstShiftCells(s0, CrowdMotionInvariants.HeadingProjection(sim, hx, hy), g.CellCm)));
        double lim = depth / 2 + ReversalUatTests.TolCells;
        if (worst > lim) errors.Add($"折返穿队：单位相对位移 {worst:F2} 格 > 纵深/2+{ReversalUatTests.TolCells}={lim:F2} 格");

        var again = UntilLanded(host, orders[2], rev);
        Assert.That(again, Is.Not.Null, "场景无效：同向再令路径未落地");
        if (!again!.Leaders[0].Mirror) errors.Add("同向再下令后 mirror 应仍为 true（继承不回翻）");
        TestContext.Out.WriteLine(
            $"reversal: firstLanded={landed} reverseAt={orders[1]} depth={depth:F2} cells reverseLanded={revLanded} dot={dot:F3} worstShift={worst:F2} limit={lim:F2} againAt={orders[2]} mirror={rev.Leaders[0].Mirror}/{again.Leaders[0].Mirror}");
        Assert.That(errors, Is.Empty, string.Join("；", errors));
    }

    [Test]
    public void RepeatClicks_DoNotBackstepOrFlip()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationRepeatClickShowcaseMod");
        var sim = host.Session;
        var g = Geometry.Of(sim);
        int[] orders = host.TicksOf("order", P);
        AssertSpawn(host, g.Sx, g.Sy, ProgressUatTests.Count);
        var (ax, ay) = g.Spot(20, 0);
        for (int i = 0; i < orders.Length; i++) AssertTarget(host, i, (ax, ay));
        int[] clicks = orders.Skip(1).ToArray();
        int[] expected = Enumerable.Range(0, RepeatWindow / RepeatEvery).Select(k => clicks[0] + k * RepeatEvery).ToArray();
        Assert.That(clicks, Is.EqualTo(expected), $"连点应从第一次连点起每 {RepeatEvery} tick 一次，覆盖 {RepeatWindow} tick 观察窗");

        int settledAt = AdvanceUntilSettled(host);
        Assert.That(settledAt, Is.GreaterThanOrEqualTo(0), "场景无效：部队未集结");
        Assert.That(settledAt, Is.LessThanOrEqualTo(clicks[0]), $"连点开始（tick {clicks[0]}）时部队还没集结");
        host.AdvanceTo(clicks[0]);
        Assert.That(CrowdMotionInvariants.Settled(sim), Is.True, "连点开始时部队已不在集结状态");
        var tally = new CrowdProgressTally(sim, ProgressUatTests.EpsCells * g.CellCm, ax, ay);
        host.AdvanceTo(clicks[0] + RepeatWindow, tally.Observe);
        double flipsPerUnit = (double)tally.Flips / ProgressUatTests.Count;
        TestContext.Out.WriteLine($"repeat: settledAt={settledAt} clicks={clicks[0]}..{clicks[^1]} repeatBack={tally.Back:P2} repeatFlips/unit={flipsPerUnit:F2}");
        Assert.Multiple(() =>
        {
            Assert.That(tally.Back, Is.LessThanOrEqualTo(ProgressUatTests.RepeatBackLimit), $"同目标连点：逆向单位-tick {tally.Back:P1}");
            Assert.That(flipsPerUnit, Is.LessThanOrEqualTo(ProgressUatTests.RepeatFlipsLimit), $"同目标连点：人均状态翻转 {flipsPerUnit:F1} 次");
        });
    }

    [Test]
    public void March_TurnMidMarchStaysForward()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationMarchShowcaseMod");
        var sim = host.Session;
        var g = Geometry.Of(sim);
        int[] orders = host.TicksOf("order", P);
        Assert.That(orders, Has.Length.EqualTo(3), "行军脚本应有集结、行军、转向三条");
        AssertSpawn(host, g.Sx, g.Sy, ProgressUatTests.Count);
        var a = g.Spot(20, 0);
        AssertTarget(host, 0, a);
        AssertTarget(host, 1, g.Spot(a, 40, 0));
        var (tx, ty) = g.Spot(a, 20, 30);
        AssertTarget(host, 2, (tx, ty));
        Assert.That(orders[2] - orders[1], Is.EqualTo(TurnDelay), $"转向令应在行军令之后 {TurnDelay} tick");

        int settledAt = AdvanceUntilSettled(host);
        Assert.That(settledAt, Is.GreaterThanOrEqualTo(0), "场景无效：部队未集结");
        Assert.That(settledAt, Is.LessThanOrEqualTo(orders[1]), $"行军令（tick {orders[1]}）时部队还没集结");
        host.AdvanceTo(orders[2]);
        var tally = new CrowdProgressTally(sim, ProgressUatTests.EpsCells * g.CellCm, tx, ty);
        host.AdvanceTo(orders[2] + TurnWindow, tally.Observe);
        TestContext.Out.WriteLine($"march: settledAt={settledAt} marchAt={orders[1]} turnAt={orders[2]} turnBack={tally.Back:P2} turnMoved={tally.Moved}");
        Assert.That(tally.Moved, Is.GreaterThan(0), "场景无效：转向后无单位移动");
        Assert.That(tally.Back, Is.LessThanOrEqualTo(ProgressUatTests.TurnBackLimit), $"行进中 ~60° 转向：逆向单位-tick {tally.Back:P1}");
    }

    /// <summary>和 <see cref="CrowdUatKit.UntilLanded(CrowdSimSession, int)"/> 同一节拍：执行指令的那个 tick 之后再逐 tick 查领队是否落地。</summary>
    private static CrowdOrder? UntilLanded(CrowdShowcaseHost host, int orderTick, CrowdOrder? previous)
    {
        host.AdvanceTo(orderTick + 1);
        var order = CrowdUatKit.Newest(host.Session);
        Assert.That(order, Is.Not.Null.And.Not.SameAs(previous), $"tick {orderTick} 的指令没有产生新指令");
        for (int t = 1; t < LandMax; t++)
        {
            host.Advance();
            if (order!.Leaders.Count > 0) return order;
        }

        return null;
    }

    private static int AdvanceUntilSettled(CrowdShowcaseHost host)
    {
        int settledAt = -1;
        host.AdvanceUntil(() =>
        {
            if (host.Session.Units.Count > 0 && CrowdMotionInvariants.Settled(host.Session)) settledAt = host.Session.TickCount;
            return settledAt >= 0;
        }, SettleMax);
        return settledAt;
    }

    private static void AssertSpawn(CrowdShowcaseHost host, Fix64 x, Fix64 y, int count)
    {
        var spawn = host.CommandAt("spawnAt", 0);
        Assert.Multiple(() =>
        {
            Assert.That(host.TicksOf("spawnAt"), Is.EqualTo(new[] { 0 }), "方块只在 tick 0 生成一次");
            Assert.That(spawn["player"]!.GetValue<int>(), Is.EqualTo(P));
            Assert.That(spawn["xCm"]!.GetValue<int>(), Is.EqualTo(x.ToInt()), "方块中心 x 不是地图中心最近的可走格");
            Assert.That(spawn["yCm"]!.GetValue<int>(), Is.EqualTo(y.ToInt()), "方块中心 y 不是地图中心最近的可走格");
            Assert.That(spawn["count"]!.GetValue<int>(), Is.EqualTo(count));
            Assert.That(spawn["unitType"]!.GetValue<int>(), Is.Zero);
            Assert.That(spawn["rIdx"]!.GetValue<int>(), Is.Zero);
        });
    }

    private static void AssertTarget(CrowdShowcaseHost host, int index, (Fix64 X, Fix64 Y) at)
    {
        var order = host.CommandAt("order", index);
        Assert.Multiple(() =>
        {
            Assert.That(order["xCm"]!.GetValue<int>(), Is.EqualTo(at.X.ToInt()), $"第 {index} 条指令目标 x");
            Assert.That(order["yCm"]!.GetValue<int>(), Is.EqualTo(at.Y.ToInt()), $"第 {index} 条指令目标 y");
            Assert.That(order["shape"]!.GetValue<string>(), Is.EqualTo("box"));
            Assert.That(order["auto"]!.GetValue<bool>(), Is.False, "UAT 下令关 magic box");
        });
    }

    /// <summary>编队 UAT 的场景几何：地图中心最近的可走格，以及相对它按格数取的最近可走格。</summary>
    private sealed record Geometry(CrowdSimSession Session, NavContext Nav, Fix64 Sx, Fix64 Sy, int CellCm)
    {
        public static Geometry Of(CrowdSimSession sim)
        {
            var nav = sim.NavFor(sim.Config.UnitTypes[0].AgentTypeIndex, 0);
            Fix64 half = Fix64.FromInt(sim.Config.NavCellsPerSide) * Fix64.FromInt(sim.Config.NavCellSizeCm) / 2;
            var (sx, sy) = CrowdUatKit.Spot(sim, nav, half, half);
            return new Geometry(sim, nav, sx, sy, sim.Config.NavCellSizeCm);
        }

        public (Fix64 X, Fix64 Y) Spot(int dxCells, int dyCells) => Spot((Sx, Sy), dxCells, dyCells);

        public (Fix64 X, Fix64 Y) Spot((Fix64 X, Fix64 Y) from, int dxCells, int dyCells)
            => CrowdUatKit.Spot(Session, Nav, from.X + Fix64.FromInt(dxCells * CellCm), from.Y + Fix64.FromInt(dyCells * CellCm));
    }
}
