using System;
using System.Collections.Generic;
using System.Linq;
using CrowdSimulationTests.Uat;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;
using NUnit.Framework;
using static CrowdSimulationTests.Uat.CrowdFogUatTools;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 迷雾探索、看见改道、共享视野三个演示 Mod 走真实进图，脚本由会话宿主自己跑，在脚本指令的 tick 上核对隐藏 UAT、迷雾改道 UAT 和 UAT-FOG-01/02 的同一组判定。
/// </summary>
[TestFixture]
public sealed class FogShowcaseTests
{
    private const int Me = CrowdUatKit.Player;
    private const int Ally = 3;
    private const int Squad = 30, NearCount = 5, NearOffsetCm = 20000;
    private const int RerouteCount = 60, RerouteSizeCells = 11;
    private const int ExploreMax = 4000, TravelMax = 1200, SeenPoll = 10, MaxTicks = 6000;

    [Test]
    public void Explore_HidesWhatTheViewerHasNotSeen_ThenShowsItOnceSeen()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogExploreShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        int w = WorldCm(s);
        int foe = EnemyOf(s, Me);
        int vision = (int)s.Config.Fog.VisionCm.ToLong();
        (int X, int Y) own = ((int)(0.2 * w), (int)(0.5 * w)), foeAt = ((int)(0.8 * w), (int)(0.5 * w));
        (int X, int Y) goal = ((int)(0.8 * w), (int)(0.25 * w)), bld = ((int)(0.5 * w), (int)(0.85 * w));
        int wait = ShowWaitTicks(s);
        int nearTick = host.TicksOf("spawnAt", foe).Last();
        int exploreTick = host.TicksOf("order", Me).Single();
        Assert.Multiple(() =>
        {
            Assert.That(host.CommandAt("fogSight", 0)["on"]!.GetValue<bool>(), Is.False, "隐藏场景关视线遮挡");
            AssertSpawn(host.CommandAt("spawnAt", 0), Me, own, Squad);
            AssertSpawn(host.CommandAt("spawnAt", 1), foe, foeAt, Squad);
            AssertSpawn(host.CommandAt("spawnAt", 2), foe, (own.X + NearOffsetCm, own.Y), NearCount);
            AssertOrder(host.CommandAt("order", 0), foe, goal);
            var place = host.CommandAt("placeStructure", 0);
            Assert.That((place["xCm"]!.GetValue<int>(), place["yCm"]!.GetValue<int>(), place["sizeCm"]!.GetValue<int>()),
                Is.EqualTo((bld.X, bld.Y, vision / 2)), "新建筑的位置和边长");
            Assert.That(nearTick, Is.GreaterThanOrEqualTo(60 + wait), "进入视野的敌人出现在隐藏核对之前");
            Assert.That(exploreTick, Is.GreaterThanOrEqualTo(nearTick + 2 + wait), "探索令早于进入视野核对");
        });

        host.AdvanceTo(1);
        int bid = s.Structures!.EntityIds.Max();
        int viewer = Group(s, Me);
        int explored0 = ExploredCount(fog, viewer);
        host.AdvanceTo(60 + wait);
        var (ownShown, ownN) = Shown(s, viewer, Me);
        var (foeShown, foeN) = Shown(s, viewer, foe);
        Assert.That(ownN > 0 && ownShown == ownN, Is.True, $"本方单位应全部显示：{ownShown}/{ownN}");
        Assert.That(foeN, Is.EqualTo(Squad), "场景无效：敌方单位未生成");
        Assert.That(foeShown, Is.Zero, $"视野外敌方单位被显示：{foeShown}/{foeN}");
        Assert.That(s.Orders.List.Any(o => o.Player == foe && o.Groups.Count > 0), Is.True, "场景无效：敌方指令未产生");
        var drawn = CrowdPlayerViewHost.LatestOrder(s, Me);
        Assert.That(drawn == null || drawn.Player == Me, Is.True, "敌方目标被显示");
        Assert.That(CrowdPlayerVisibility.StructureShown(s, viewer, bid, hideFog: true), Is.False, "视野外的新建筑被显示");
        Assert.That(fog.Belief[viewer].ContainsKey(bid), Is.False, "视野外的新建筑进了认知");

        host.AdvanceTo(nearTick + 2 + wait);
        var (nearShown, _) = Shown(s, viewer, foe);
        Assert.That(nearShown, Is.GreaterThanOrEqualTo(1), "进入视野的敌方单位未显示（对照失败）");
        CrowdSimulationValidator.ValidateSim(s);

        host.AdvanceTo(exploreTick);
        Assert.That(fog.Belief[viewer].ContainsKey(bid), Is.False, "探索前新建筑已进认知");
        int exploredAtOrder = ExploredCount(fog, viewer), last = exploredAtOrder, shrank = -1, shownEarly = -1;
        bool seen = host.AdvanceUntil(() => fog.Belief[viewer].ContainsKey(bid), ExploreMax, () =>
        {
            int now = ExploredCount(fog, viewer);
            if (now < last && shrank < 0) shrank = s.TickCount;
            last = now;
            if (shownEarly < 0 && !fog.Belief[viewer].ContainsKey(bid) && CrowdPlayerVisibility.StructureShown(s, viewer, bid, hideFog: true))
                shownEarly = s.TickCount;
        });
        int seenAt = s.TickCount;
        TestContext.Out.WriteLine(
            $"explore: hidden own {ownShown}/{ownN} enemy {foeShown}/{foeN}, enemy in view {nearShown}, exploreAt {exploreTick} seenAt {seenAt}, explored {explored0}->{exploredAtOrder}->{last} cells");
        Assert.That(seen, Is.True, $"{ExploreMax} tick 内没看见新建筑");
        Assert.That(shownEarly, Is.EqualTo(-1), $"tick {shownEarly} 新建筑还没进认知就画出来了");
        Assert.That(shrank, Is.EqualTo(-1), $"tick {shrank} 已探索格数减少");
        Assert.That(last, Is.GreaterThan(exploredAtOrder), "探索途中已探索格数没有增加");
        Assert.That(CrowdPlayerVisibility.StructureShown(s, viewer, bid, hideFog: true), Is.True, "进入视野的新建筑没有显示");
        CrowdSimulationValidator.ValidateSim(s);
    }

    [Test]
    public void Reroute_UnseenBuildingIsPlannedThroughUntilSeenThenAvoided()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogRerouteShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        int w = WorldCm(s);
        int size = RerouteSizeCells * s.Config.NavCellSizeCm;
        int[] places = host.TicksOf("placeStructure");
        Assert.Multiple(() =>
        {
            AssertSpawn(host.CommandAt("spawnAt", 0), Me, ((int)(0.19 * w), (int)(0.5 * w)), RerouteCount);
            var order = host.CommandAt("order", 0);
            AssertOrder(order, Me, ((int)(0.81 * w), (int)(0.5 * w)));
            Assert.That(order["fogTerrain"]!.GetValue<bool>(), Is.False, "改道 UAT 关乐观迷雾");
            Assert.That(places, Has.Length.EqualTo(2));
            Assert.That(host.CommandAt("placeStructure", 0)["sizeCm"]!.GetValue<int>(), Is.EqualTo(size));
            Assert.That(host.CommandAt("placeStructure", 1)["sizeCm"]!.GetValue<int>(), Is.EqualTo(size));
        });

        int v = Group(s, Me);
        host.AdvanceTo(places[0]);
        var g = ReadyGroup(s, Me);
        Assert.That(g, Is.Not.Null, $"tick {places[0]} 放建筑时指令还没有领队");
        var leader = g!.Leader!;
        host.Advance();
        int id = s.Structures!.EntityIds.Max();
        var at = s.Structures.FootprintOf(id);
        Assert.That(at.Covers(Fix64.FromInt(host.CommandAt("placeStructure", 0)["xCm"]!.GetValue<int>()), Fix64.FromInt(host.CommandAt("placeStructure", 0)["yCm"]!.GetValue<int>())), Is.True, "第一栋建筑没放下");
        int seenCells = FootprintCells(s, id).Count(c => Visible(fog, v, c));
        Assert.That(seenCells, Is.Zero, $"场景无效：放置时足迹有 {seenCells} 个迷雾格对视野组可见");
        bool planningAfterPlace = g.Planning;
        Assert.That(places[1], Is.EqualTo(places[0] + 2), "第二栋建筑在第一栋之后 2 tick");
        host.AdvanceTo(places[1]);
        g = ReadyGroup(s, Me);
        Assert.That(g, Is.Not.Null, $"放置后指令未完成规划 planning={planningAfterPlace}");
        var errors = new List<string>();
        if (planningAfterPlace) errors.Add("迷雾中放置建筑触发了重新规划（信息泄漏）");
        if (fog.Belief[v].ContainsKey(id)) errors.Add("迷雾中的新建筑被提前知晓");
        bool truthBlocked = LeaderBlocked(s, g!, g!.BodyNavId);
        bool beliefBlocked = LeaderBlocked(s, g, g.NavId);
        if (!truthBlocked) errors.Add("新建筑未阻挡领队路线（场景无效）");
        if (beliefBlocked) errors.Add("视野组按未知建筑改道（应按认知规划）");
        CheckBeliefs(s, "认知变体", errors);

        host.AdvanceTo(places[1] + 2);
        Assert.That(s.Structures.EntityIds.Max(), Is.GreaterThan(id), "第二栋建筑没放下");
        CheckBeliefs(s, "再次放置后认知变体", errors);

        var path0 = PathOf(g.Leader!);
        int seenAt = -1;
        for (int t = 0; t < MaxTicks && seenAt < 0; t += SeenPoll)
        {
            host.AdvanceTo(s.TickCount + SeenPoll);
            if (fog.Belief[v].ContainsKey(id)) seenAt = s.TickCount;
        }

        bool replanned = false, blockedAfter = false;
        if (seenAt < 0) errors.Add($"{MaxTicks} tick 内未看见建筑");
        else
        {
            host.AdvanceTo(s.TickCount + s.Config.Fog.RevealTicks + s.Config.Planning.LatencyTicks + 30);
            var after = ReadyGroup(s, Me);
            if (after == null) errors.Add("看见建筑后指令丢失");
            else
            {
                replanned = !PathOf(after.Leader!).SequenceEqual(path0);
                blockedAfter = LeaderBlocked(s, after, after.BodyNavId);
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
            $"reroute: placed {places[0]}/{places[1]} leaderAtPlace seg {leader.Seg}/{leader.PathCount}, planningAfterPlace {planningAfterPlace}, truthBlocked {truthBlocked}, beliefBlocked {beliefBlocked}, seenAt {seenAt}, replanned {replanned}, blockedAfter {blockedAfter}, ticks {s.TickCount}");
        Assert.That(errors, Is.Empty, string.Join("；", errors));
    }

    [Test]
    public void SharedVision_AllySightReachesViewer_GhostStaysUntilAllyLooksAgain()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogAllyVisionShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        int enemy = EnemyOf(s, Me);
        int viewer = Group(s, Me), allyGroup = Group(s, Ally), enemyGroup = Group(s, enemy);
        Assert.That(allyGroup, Is.EqualTo(viewer), "场景无效：盟友和观察方不在同一视野组");
        Assert.That(enemyGroup, Is.Not.EqualTo(viewer), "场景无效：敌方和观察方在同一视野组");
        Assert.That(host.TicksOf("order", Me), Is.Empty, "观察方自己不下令，看见全靠盟友");
        int[] allyOrders = host.TicksOf("order", Ally);
        Assert.That(allyOrders, Has.Length.EqualTo(3), "盟友：去看建筑、离开、回来");
        int removeTick = host.TicksOf("removeStructureAt").Single();
        var place = host.CommandAt("placeStructure", 0);
        int bx = place["xCm"]!.GetValue<int>(), by = place["yCm"]!.GetValue<int>();
        var remove = host.CommandAt("removeStructureAt", 0);
        Assert.That((remove["xCm"]!.GetValue<int>(), remove["yCm"]!.GetValue<int>()), Is.EqualTo((bx, by)), "拆的就是这栋建筑");
        AssertOrder(host.CommandAt("order", 0), Ally, (bx, by));
        AssertOrder(host.CommandAt("order", 2), Ally, (bx, by));
        double vision = s.Config.Fog.VisionCm.ToDouble();
        int memoryCell = FogCell(fog, bx, by);

        host.AdvanceTo(1, Validate);
        int id = s.Structures!.EntityIds.Max();
        int enemyDense = DenseOf(s, enemy);
        host.AdvanceTo(allyOrders[0], Validate);
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.False, "场景无效：盟友出发前建筑已在认知里");
        Assert.That(CrowdPlayerVisibility.UnitShown(s, viewer, enemyDense, hideFog: true), Is.False, "场景无效：敌人一开始就可见");
        Assert.That(CrowdPlayerVisibility.UnitShown(s, viewer, enemyDense, hideFog: false), Is.True, "关闭隐藏后视野外的敌方单位仍不显示");

        int seenAt = -1;
        double viewerNearestAtSeen = 0;
        host.AdvanceTo(allyOrders[1], () =>
        {
            Validate();
            if (seenAt >= 0 || !fog.Belief[viewer].ContainsKey(id)) return;
            seenAt = s.TickCount;
            viewerNearestAtSeen = NearestOf(s, Me, bx, by);
        });
        Assert.That(seenAt, Is.GreaterThanOrEqualTo(0), $"盟友离开（tick {allyOrders[1]}）前视野组还没看见建筑");
        Assert.That(viewerNearestAtSeen, Is.GreaterThan(vision), $"看见建筑时观察方自己的单位离它 {viewerNearestAtSeen:F0} 厘米，在视野内（场景无效）");
        Assert.That(CrowdPlayerVisibility.StructureShown(s, viewer, id, hideFog: true), Is.True, "盟友看见的建筑没有给观察方显示");
        Assert.That(fog.Belief[enemyGroup].ContainsKey(id), Is.False, "敌方视野组得到了友方的认知");

        int enemyShownAt = -1;
        double viewerNearestEnemy = 0;
        var enemyPos = CrowdUatKit.PositionAt(s, enemyDense);
        host.AdvanceTo(removeTick, () =>
        {
            Validate();
            if (enemyShownAt >= 0 || !CrowdPlayerVisibility.UnitShown(s, viewer, enemyDense, hideFog: true)) return;
            enemyShownAt = s.TickCount;
            viewerNearestEnemy = NearestOf(s, Me, enemyPos.X.ToInt(), enemyPos.Y.ToInt());
        });
        Assert.That(enemyShownAt, Is.GreaterThanOrEqualTo(0), "盟友路过敌人时观察方没有看到它");
        Assert.That(viewerNearestEnemy, Is.GreaterThan(vision), $"看到敌人时观察方自己的单位离它 {viewerNearestEnemy:F0} 厘米，在视野内（场景无效）");
        Assert.That(fog.CellVisibilityOf(viewer, memoryCell), Is.Not.EqualTo(CellVisibility.Visible), "拆除时建筑格仍可见（场景无效）");

        uint goneBefore = fog.Gone[viewer];
        uint enemyGone = fog.Gone[enemyGroup];
        int enemyGhosts = fog.GhostCount[enemyGroup];
        host.Advance();
        Validate();
        Assert.That(s.Structures.EntityIds.Contains(id), Is.False, "场景无效：建筑没被拆掉");
        Assert.That(fog.Gone[viewer], Is.EqualTo(goneBefore), "视野外拆除时已消失计数增加了");
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.True, "视野外拆除后没有留下残影");
        Assert.That(fog.GhostCount[enemyGroup], Is.EqualTo(enemyGhosts), "别的视野组收到了这次拆除");
        Assert.That(fog.Gone[enemyGroup], Is.EqualTo(enemyGone), "别的视野组收到了这次拆除");

        host.AdvanceTo(allyOrders[2], Validate);
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.True, "盟友回去之前残影就没了");
        bool cleared = host.AdvanceUntil(() => !fog.Belief[viewer].ContainsKey(id), TravelMax, Validate);
        int clearedAt = s.TickCount;
        host.AdvanceTo(clearedAt + s.Config.Planning.RefreshLatencyTicks + 1, Validate);
        TestContext.Out.WriteLine(
            $"shared: allyGo {allyOrders[0]} seenAt {seenAt} (viewer nearest {viewerNearestAtSeen:F0} cm, vision {vision:F0}), allyLeave {allyOrders[1]}, enemyShownAt {enemyShownAt} (viewer nearest {viewerNearestEnemy:F0} cm), remove {removeTick}, allyReturn {allyOrders[2]}, ghostClearedAt {clearedAt}, gone {goneBefore}->{fog.Gone[viewer]}");
        Assert.That(cleared, Is.True, $"盟友回去 {TravelMax} tick 内残影还在");
        Assert.That(fog.Gone[viewer], Is.EqualTo(goneBefore + 1), "重新看见后已消失计数没有加一");
        Assert.That(fog.Belief[enemyGroup].ContainsKey(id), Is.False, "敌方视野组得到了友方的认知");

        void Validate() => CrowdSimulationValidator.ValidateSim(s);
    }

    private static int ExploredCount(CrowdFog fog, int group)
    {
        int n = 0, cells = CellCount(fog);
        for (int c = 0; c < cells; c++)
        {
            if (Explored(fog, group, c)) n++;
        }

        return n;
    }

    private static int FogCell(CrowdFog fog, int xCm, int yCm)
        => yCm / fog.FogCellSizeCm * fog.FogCellsPerSide + xCm / fog.FogCellSizeCm;

    private static int DenseOf(CrowdSimSession s, int player)
    {
        for (int i = 0; i < s.Units.Count; i++)
        {
            if (s.World.Get<PlayerOwner>(s.Units.EntityAt(i)).PlayerId == player) return i;
        }

        throw new InvalidOperationException($"场景无效：玩家 {player} 没有单位。");
    }

    private static double NearestOf(CrowdSimSession s, int player, int xCm, int yCm)
    {
        double best = double.MaxValue;
        for (int i = 0; i < s.Units.Count; i++)
        {
            if (s.World.Get<PlayerOwner>(s.Units.EntityAt(i)).PlayerId != player) continue;
            var p = CrowdUatKit.PositionAt(s, i);
            double dx = p.X.ToDouble() - xCm, dy = p.Y.ToDouble() - yCm;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }

        return best;
    }

    private static void AssertSpawn(System.Text.Json.Nodes.JsonObject cmd, int player, (int X, int Y) at, int count)
    {
        Assert.That(cmd["player"]!.GetValue<int>(), Is.EqualTo(player), "生成的玩家");
        Assert.That((cmd["xCm"]!.GetValue<int>(), cmd["yCm"]!.GetValue<int>()), Is.EqualTo(at), $"玩家 {player} 生成位置");
        Assert.That(cmd["count"]!.GetValue<int>(), Is.EqualTo(count), $"玩家 {player} 生成数量");
    }

    private static void AssertOrder(System.Text.Json.Nodes.JsonObject cmd, int player, (int X, int Y) at)
    {
        Assert.That(cmd["player"]!.GetValue<int>(), Is.EqualTo(player), "下令的玩家");
        Assert.That((cmd["xCm"]!.GetValue<int>(), cmd["yCm"]!.GetValue<int>()), Is.EqualTo(at), $"玩家 {player} 指令目标");
    }
}
