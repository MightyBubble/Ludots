using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static CrowdSimulationTests.Uat.CrowdFogUatTools;

namespace CrowdSimulationTests.Uat;

/// <summary>迷雾里放到领队路线上的建筑：视野组看见之前不知道它、按认知导航照旧穿过它且不因放置重新规划；看见之后改道绕开；认知变体始终等于对它自己认知场的全量烘焙。</summary>
public sealed class FogUatTests
{
    private const int P0 = CrowdUatKit.Player;
    private const int Count = 60;
    private const int SizeCells = 11;
    private const double SecondOffset = 0.125;
    private const int MaxTicks = 6000;
    private const int SettleTicks = 120;
    private const int SecondSearchRings = 40;

    [Test]
    public void UnseenBuildingOnRoute_IsPlannedThroughUntilSeenThenAvoided()
    {
        using var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        var fog = s.Fog!;
        int w = WorldCm(s);
        int size = SizeCells * s.Config.NavCellSizeCm;
        int fromX = (int)(0.19 * w), fromY = (int)(0.5 * w), toX = (int)(0.81 * w), toY = (int)(0.5 * w);
        int v = Group(s, P0);
        int p0 = s.Config.PairRules.IndexByPlayerId[P0];

        CrowdNavGroupSet.Group? Ready() => s.Groups.Groups.FirstOrDefault(g =>
            g != null && g.Count > 0 && g.Player == p0 && g.OrderId != 0 && g.Leader != null && !g.Planning);
        CrowdNavGroupSet.Group? Settle()
        {
            for (int t = 0; t < SettleTicks && Ready() == null; t++) CrowdUatKit.Step(s);
            return Ready();
        }

        bool Blocked(CrowdNavGroupSet.Group g, int navId) => CrowdSimPlanner.LeaderPathBlocked(g.Leader, s.ResolveNavContext(navId), s);

        CrowdUatKit.SpawnAt(s, Fix64.FromInt(fromX), Fix64.FromInt(fromY), Count, rIdx: 0);
        Order(s, P0, toX, toY, fogTerrain: false);
        CrowdUatKit.Step(s, 10);
        var g = Settle();
        Assert.That(g, Is.Not.Null, "指令未产生领队");

        var leader = g!.Leader!;
        int k = Math.Max(1, (int)Math.Floor(leader.PathCount * 0.65)), pathCount0 = leader.PathCount;
        var spot = LegalSpotOnRoute(s, leader, k, size);
        Assert.That(spot, Is.Not.Null, $"场景无效：整条路线上没有能合法放下 {size} 厘米建筑的点");
        var (bx, by, offsetCells, spotArc, leaderArc, total) = spot!.Value;
        Assert.That(spotArc, Is.GreaterThan(leaderArc), $"场景无效：放置点（路线 {spotArc / total:P1}）不在领队（{leaderArc / total:P1}）前方");
        int id = Place(s, "building", bx, by, size);
        int seenCells = FootprintCells(s, id).Count(c => Visible(fog, v, c));
        Assert.That(seenCells, Is.Zero, $"场景无效：放置时足迹有 {seenCells} 个迷雾格对视野组可见");
        bool planningAfterPlace = g.Planning;
        CrowdUatKit.Step(s, 2);
        g = Settle();
        Assert.That(g, Is.Not.Null, $"放置后指令未完成规划 planning={planningAfterPlace}");
        var errors = new List<string>();
        if (planningAfterPlace) errors.Add("迷雾中放置建筑触发了重新规划（信息泄漏）");
        if (fog.Belief[v].ContainsKey(id)) errors.Add("迷雾中的新建筑被提前知晓");
        bool truthBlocked = Blocked(g!, g!.BodyNavId);
        bool beliefBlocked = Blocked(g, g.NavId);
        if (!truthBlocked) errors.Add("新建筑未阻挡领队路线（场景无效）");
        if (beliefBlocked) errors.Add("视野组按未知建筑改道（应按认知规划）");
        CheckBeliefs(s, "认知变体", errors);

        int x2 = Math.Min(w - size, bx + (int)(SecondOffset * w)), y2 = Math.Min(w - size, by + (int)(SecondOffset * w));
        var second = LegalSpotNear(s, x2, y2, size);
        Assert.That(second, Is.Not.Null, $"场景无效：({x2}, {y2}) 周围 {SecondSearchRings} 圈内放不下第二栋建筑");
        int ring2;
        (x2, y2, ring2) = second!.Value;
        Place(s, "building", x2, y2, size);
        CrowdUatKit.Step(s, 2);
        CheckBeliefs(s, "再次放置后认知变体", errors);

        var path0 = PathOf(g.Leader!);
        int seenAt = -1;
        for (int t = 0; t < MaxTicks && seenAt < 0; t += 10)
        {
            CrowdUatKit.Step(s, 10);
            if (fog.Belief[v].ContainsKey(id)) seenAt = s.TickCount;
        }

        bool replanned = false, blockedAfter = false;
        if (seenAt < 0) errors.Add($"{MaxTicks} tick 内未看见建筑");
        else
        {
            CrowdUatKit.Step(s, s.Config.Fog.RevealTicks + s.Config.Planning.LatencyTicks + 30);
            var after = Ready();
            if (after == null) errors.Add("看见建筑后指令丢失");
            else
            {
                replanned = !PathOf(after.Leader!).SequenceEqual(path0);
                blockedAfter = Blocked(after, after.BodyNavId);
                if (blockedAfter) errors.Add("看见建筑后未改道（领队路线仍被真实导航阻挡）");
            }
        }

        try
        {
            CrowdSimulationValidator.ValidateSim(s);
        }
        catch (CrowdSimulationInvariantException ex)
        {
            errors.Add(ex.Message);
        }

        TestContext.Out.WriteLine(
            $"fog: building ({bx}, {by}) size {size}, waypoint {k}/{pathCount0} at {(spotArc - offsetCells * (double)s.Config.NavCellSizeCm) / total:P1}, offset {offsetCells:+0;-0;0} cells, route {spotArc / total:P1}, leader {leaderArc / total:P1}, second ({x2}, {y2}) ring {ring2}, planningAfterPlace {planningAfterPlace}, " +
            $"truthBlocked {truthBlocked}, beliefBlocked {beliefBlocked}, seenAt {seenAt}, replanned {replanned}, blockedAfter {blockedAfter}, ticks {s.TickCount}");
        Assert.That(errors, Is.Empty, string.Join("；", errors));
    }

    /// <summary>
    /// 放置指令拒收与已有结构重叠的足迹（沙盒的 place 不判合法性）。从第 k 个路径点起沿路线逐格前移到终点、再从它逐格后移到起点，
    /// 取第一个放置裁定通过且足迹盖住该路线点的位置，建筑边长不变。返回偏移格数与该点、领队各自的路线弧长。
    /// </summary>
    private static (int X, int Y, int OffsetCells, double SpotArc, double LeaderArc, double Total)? LegalSpotOnRoute(
        CrowdSimSession s, CrowdLeader leader, int k, int size)
    {
        var store = s.Structures!;
        var template = s.Config.Structures.Templates[store.TemplateIndexOf("building")];
        int cs = s.Config.NavCellSizeCm, n = leader.PathCount;
        double Dist(Fix64Vec2 a, Fix64 bx, Fix64 by) =>
            Math.Sqrt(Math.Pow((bx - a.X).ToDouble(), 2) + Math.Pow((by - a.Y).ToDouble(), 2));
        var arc = new double[n];
        for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + Dist(leader.PathAt(i - 1), leader.PathAt(i).X, leader.PathAt(i).Y);
        double total = arc[n - 1], start = arc[k];
        double leaderArc = arc[leader.Seg - 1] + Dist(leader.PathAt(leader.Seg - 1), leader.XCm, leader.YCm);

        (int, int, int, double, double, double)? Try(int step)
        {
            double at = start + (double)step * cs;
            int seg = 1;
            while (seg < n - 1 && arc[seg] < at) seg++;
            var a = leader.PathAt(seg - 1);
            var b = leader.PathAt(seg);
            double len = arc[seg] - arc[seg - 1], t = len > 0 ? (at - arc[seg - 1]) / len : 0;
            var x = Fix64.FromInt((int)Math.Round(a.X.ToDouble() + (b.X - a.X).ToDouble() * t));
            var y = Fix64.FromInt((int)Math.Round(a.Y.ToDouble() + (b.Y - a.Y).ToDouble() * t));
            var fp = CrowdStructureFootprint.Make(template, x, y, Fix64.FromInt(size), Fix64.Zero, Fix64.Zero);
            if (store.JudgePlacement(template, fp) != CrowdPlacementReject.None || !fp.Covers(x, y)) return null;
            return (x.ToInt(), y.ToInt(), step, at, leaderArc, total);
        }

        for (int step = 0; start + (double)step * cs <= total; step++)
        {
            if (Try(step) is { } hit) return hit;
        }

        for (int step = -1; start + (double)step * cs >= 0; step--)
        {
            if (Try(step) is { } hit) return hit;
        }

        return null;
    }

    /// <summary>第二栋建筑只用来再造一次真相变化；原位被放置裁定拒收时按格环外移，边长不变。</summary>
    private static (int X, int Y, int Ring)? LegalSpotNear(CrowdSimSession s, int x, int y, int size)
    {
        var store = s.Structures!;
        var template = s.Config.Structures.Templates[store.TemplateIndexOf("building")];
        int cs = s.Config.NavCellSizeCm;
        for (int ring = 0; ring <= SecondSearchRings; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    int px = x + dx * cs, py = y + dy * cs;
                    var fp = CrowdStructureFootprint.Make(template, Fix64.FromInt(px), Fix64.FromInt(py), Fix64.FromInt(size), Fix64.Zero, Fix64.Zero);
                    if (store.JudgePlacement(template, fp) == CrowdPlacementReject.None) return (px, py, ring);
                }
            }
        }

        return null;
    }

    private static Fix64Vec2[] PathOf(CrowdLeader leader) =>
        Enumerable.Range(0, leader.PathCount).Select(leader.PathAt).ToArray();

    private static void CheckBeliefs(CrowdSimSession s, string when, List<string> errors)
    {
        try
        {
            CrowdSimulationValidator.ValidateNav(s);
        }
        catch (CrowdSimulationInvariantException ex)
        {
            errors.Add($"{when}：{ex.Message}");
        }
    }
}
