using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
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
        int k = Math.Max(1, (int)Math.Floor(leader.PathCount * 0.65));
        var at = leader.PathAt(k);
        int bx = (int)at.X.ToLong(), by = (int)at.Y.ToLong();
        int id = Place(s, "building", bx, by, size);
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
        Place(s, "building", x2, y2, size);
        CrowdUatKit.Step(s, 2);
        CheckBeliefs(s, "再次放置后认知变体", errors);

        var leader0 = g.Leader;
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
                replanned = !ReferenceEquals(after.Leader, leader0) || after.Leader!.PathCount != leader0!.PathCount;
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
            $"fog: building ({bx}, {by}) size {size}, path point {k}/{leader.PathCount}, planningAfterPlace {planningAfterPlace}, " +
            $"truthBlocked {truthBlocked}, beliefBlocked {beliefBlocked}, seenAt {seenAt}, replanned {replanned}, blockedAfter {blockedAfter}, ticks {s.TickCount}");
        Assert.That(errors, Is.Empty, string.Join("；", errors));
    }

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
