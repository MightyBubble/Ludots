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
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Vision;
using Ludots.Core.Mathematics;
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
    private const int ExploreMax = 4000, TravelMax = 1200;
    // 种子 1337：陆地 (60,57) 向北到岸 (60,105)。水域 (60,103) 落在开局视野够不到的导航 tile 上，整块 tile 仍按未探索处理。
    private const int OptimisticCellCm = 6250;
    private const int OptimisticSquad = 8;
    private const int OptimisticStartX = 378125, OptimisticStartY = 359375;
    private const int OptimisticGoalX = 378125, OptimisticGoalY = 659375;
    private const int OptimisticBuildX = 378125, OptimisticBuildY = 640625;
    private const int OptimisticBuildSizeCm = 6250;
    private const int OptimisticWaterCellX = 60, OptimisticWaterCellY = 103;
    private const int OptimisticPlaceTick = 8;
    private const int OptimisticArriveMax = 8000;

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
        var playerView = CrowdPlayerViewConfig.Load(CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationPlayerView.json"), "CrowdSimulationPlayerView.json");
        Assert.That(playerView.FogHide, Is.True, "场景无效：玩家视图没开迷雾");
        var decal = host.GroundDecal();
        Assert.That(decal?.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Fog), "迷雾开时地形贴花应是本机座位的迷雾");

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
        AssertDecalCoversMapAndUnseenCornersReadUnseen(host, fog, viewer);
        CrowdSimulationValidator.ValidateSim(s);
    }

    private static void AssertDecalCoversMapAndUnseenCornersReadUnseen(CrowdShowcaseHost host, CrowdFog fog, int viewer)
    {
        int side = fog.FogCellsPerSide;
        var decal = host.GroundDecal();
        Assert.That(decal?.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Fog), "迷雾开时地形贴花应是本机座位的迷雾");
        Assert.That(decal!.Descriptor.BoundsCells, Is.EqualTo(new IntRect(0, 0, side, side)), "迷雾贴花应盖住整张图");
        var field = fog.VisionField(viewer);
        int unseenCorners = 0;
        foreach (var (x, y) in new[] { (0, 0), (side - 1, 0), (0, side - 1), (side - 1, side - 1) })
        {
            var truth = field.GetVisibility(new FogCell(x, y));
            Assert.That(decal.ByteAt(x, y), Is.EqualTo((byte)truth), $"角格 ({x},{y}) 贴花值与迷雾场不一致");
            if (truth == CellVisibility.Unseen) unseenCorners++;
        }

        Assert.That(unseenCorners, Is.GreaterThan(0), "场景无效：四个角都被看过，核对不到从没见过的格");
    }

    [Test]
    public void OptimisticTerrain_StraightThroughUnexploredWaterThenReroutesAndArrives()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogRerouteShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        Assert.Multiple(() =>
        {
            Assert.That(s.Config.Fog.OptimisticTerrain, Is.True, "演示开局应把未探索地形当成能走");
            Assert.That(fog.Terrain, Is.True, "进图时 CrowdFog.Terrain 应抄配置");
            AssertSpawn(host.CommandAt("spawnAt", 0), Me, (OptimisticStartX, OptimisticStartY), OptimisticSquad);
            var order = host.CommandAt("order", 0);
            AssertOrder(order, Me, (OptimisticGoalX, OptimisticGoalY));
            Assert.That(order["fogTerrain"]!.GetValue<bool>(), Is.True, "演示脚本打开乐观迷雾");
            var place = host.CommandAt("placeStructure", 0);
            Assert.That(host.TicksOf("placeStructure"), Is.EqualTo(new[] { OptimisticPlaceTick }));
            Assert.That((place["xCm"]!.GetValue<int>(), place["yCm"]!.GetValue<int>(), place["sizeCm"]!.GetValue<int>()),
                Is.EqualTo((OptimisticBuildX, OptimisticBuildY, OptimisticBuildSizeCm)));
        });

        int viewer = Group(s, Me);
        bool planned = host.AdvanceUntil(() => ReadyGroup(s, Me) != null, 60);
        var before = ReadyGroup(s, Me);
        Assert.That(planned && before != null, Is.True, "下令后没有规划完的领队");
        Assert.That(before!.NavId, Is.Not.EqualTo(before.BodyNavId), "乐观开局应走认知导航，而不是真相导航");
        var path0 = PathOf(before.Leader!);
        Assert.That(PathHitsCell(path0, OptimisticWaterCellX, OptimisticWaterCellY, OptimisticCellCm), Is.True,
            "乐观路径应直穿未探索的水域 " + Describe(path0));

        host.AdvanceTo(OptimisticPlaceTick + 1);
        int id = s.Structures!.EntityIds.Max();
        Assert.That(s.Structures.FootprintOf(id).Covers(Fix64.FromInt(OptimisticBuildX), Fix64.FromInt(OptimisticBuildY)), Is.True, "岸上的建筑没放下");
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.False, "没看见的建筑进了认知");
        Assert.That(FootprintCells(s, id).Count(c => Visible(fog, viewer, c)), Is.Zero, "放置时建筑足迹已经可见");
        var held = ReadyGroup(s, Me);
        Assert.That(held, Is.Not.Null);
        Assert.That(PathOf(held!.Leader!), Is.EqualTo(path0), "放置未见建筑不应改领队路径");

        bool leftWater = false;
        bool seen = host.AdvanceUntil(() =>
        {
            if (!fog.Belief[viewer].ContainsKey(id)) return false;
            var now = ReadyGroup(s, Me);
            if (now == null) return false;
            var path = PathOf(now.Leader!);
            leftWater = !PathHitsCell(path, OptimisticWaterCellX, OptimisticWaterCellY, OptimisticCellCm)
                && !path.SequenceEqual(path0);
            return leftWater;
        }, OptimisticArriveMax);
        Assert.That(seen, Is.True, "看见建筑后路径仍穿过水域，或没有改道");
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.True, "改道时建筑还不在认知里");

        bool arrived = host.AdvanceUntil(() => AllArrived(s, OptimisticSquad), OptimisticArriveMax);
        TestContext.Out.WriteLine($"optimistic: plannedNav {before.NavId} body {before.BodyNavId} leftWater {leftWater} arrived {arrived} tick {s.TickCount}");
        Assert.That(arrived, Is.True, $"{OptimisticArriveMax} tick 内没有全部到达");
        CrowdSimulationValidator.ValidateSim(s);
    }

    [Test]
    public void KnownTerrain_PathsAroundWaterFromTheFirstPlanAndArrives()
    {
        using var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        Assert.That(s.Config.Fog.OptimisticTerrain, Is.False, "对拍沙盒开局应关闭乐观迷雾");
        Assert.That(s.Fog!.Terrain, Is.False);
        int spawned = CrowdUatKit.SpawnAt(s, Fix64.FromInt(OptimisticStartX), Fix64.FromInt(OptimisticStartY), OptimisticSquad, rIdx: 0);
        Assert.That(spawned, Is.EqualTo(OptimisticSquad));
        Order(s, Me, OptimisticGoalX, OptimisticGoalY, fogTerrain: false);
        Assert.That(s.Fog.Terrain, Is.False);

        var group = ReadyGroup(s, Me);
        for (int t = 0; group == null && t < 60; t++)
        {
            CrowdUatKit.Step(s);
            group = ReadyGroup(s, Me);
        }

        Assert.That(group, Is.Not.Null, "关闭乐观后没有规划完的领队");
        Assert.That(group!.NavId, Is.EqualTo(group.BodyNavId), "没有未知实体时，关闭乐观应走真相导航");
        var path0 = PathOf(group.Leader!);
        Assert.That(PathHitsCell(path0, OptimisticWaterCellX, OptimisticWaterCellY, OptimisticCellCm), Is.False,
            "已知地形的第一条路径不应穿过水域");
        int id = Place(s, "building", OptimisticBuildX, OptimisticBuildY, OptimisticBuildSizeCm);
        Assert.That(s.Fog.Belief[Group(s, Me)].ContainsKey(id), Is.False, "出发时岸上的建筑不应已在认知里");
        CrowdUatKit.Step(s, 4);
        var afterPlace = ReadyGroup(s, Me);
        Assert.That(afterPlace, Is.Not.Null);
        Assert.That(PathHitsCell(PathOf(afterPlace!.Leader!), OptimisticWaterCellX, OptimisticWaterCellY, OptimisticCellCm), Is.False,
            "放下一栋还没看见的建筑后，已知路线不应改去穿过水域");

        bool crossed = false;
        bool arrived = false;
        for (int t = 0; t < OptimisticArriveMax; t++)
        {
            var now = ReadyGroup(s, Me);
            if (now?.Leader != null && PathHitsCell(PathOf(now.Leader), OptimisticWaterCellX, OptimisticWaterCellY, OptimisticCellCm))
                crossed = true;
            if (AllArrived(s, OptimisticSquad))
            {
                arrived = true;
                break;
            }

            CrowdUatKit.Step(s);
        }

        TestContext.Out.WriteLine($"known: nav {group.NavId} crossed {crossed} arrived {arrived} tick {s.TickCount}");
        Assert.That(crossed, Is.False, "已知路线中途改去穿过水域");
        Assert.That(arrived, Is.True, $"{OptimisticArriveMax} tick 内没有全部到达");
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

    private static string Describe(Fix64Vec2[] path)
    {
        static string At(Fix64Vec2 p)
        {
            int x = (int)(p.X.ToDouble() / OptimisticCellCm);
            int y = (int)(p.Y.ToDouble() / OptimisticCellCm);
            return $"({x},{y})";
        }

        if (path.Length == 0) return "空路径";
        int last = path.Length - 1;
        return $"共 {path.Length} 点，起 {At(path[0])} 终 {At(path[last])}";
    }

    private static bool AllArrived(CrowdSimSession session, int count)
    {
        if (session.Units.Count != count) return false;
        for (int i = 0; i < count; i++)
        {
            if (CrowdUatKit.StateAt(session, i).State != (byte)CrowdUnitState.Arrived) return false;
        }

        return true;
    }

    /// <summary>折线是否进入导航格内部。边界内缩 1 cm，贴着格边走不算穿过。</summary>
    private static bool PathHitsCell(Fix64Vec2[] path, int cellX, int cellY, int cellCm)
    {
        double left = cellX * (double)cellCm + 1;
        double bottom = cellY * (double)cellCm + 1;
        double right = (cellX + 1) * (double)cellCm - 1;
        double top = (cellY + 1) * (double)cellCm - 1;
        for (int i = 0; i < path.Length; i++)
        {
            double x = path[i].X.ToDouble();
            double y = path[i].Y.ToDouble();
            if (i == 0)
            {
                if (Inside(x, y, left, bottom, right, top)) return true;
                continue;
            }

            double px = path[i - 1].X.ToDouble();
            double py = path[i - 1].Y.ToDouble();
            if (SegmentHits(px, py, x, y, left, bottom, right, top)) return true;
        }

        return false;
    }

    private static bool Inside(double x, double y, double left, double bottom, double right, double top)
        => x >= left && x <= right && y >= bottom && y <= top;

    private static bool SegmentHits(double x0, double y0, double x1, double y1, double left, double bottom, double right, double top)
    {
        const int LeftBit = 1, RightBit = 2, BottomBit = 4, TopBit = 8;
        static int Code(double x, double y, double l, double b, double r, double t)
        {
            int c = 0;
            if (x < l) c |= LeftBit;
            else if (x > r) c |= RightBit;
            if (y < b) c |= BottomBit;
            else if (y > t) c |= TopBit;
            return c;
        }

        int c0 = Code(x0, y0, left, bottom, right, top);
        int c1 = Code(x1, y1, left, bottom, right, top);
        for (int i = 0; i < 8; i++)
        {
            if ((c0 | c1) == 0) return true;
            if ((c0 & c1) != 0) return false;
            int outCode = c0 != 0 ? c0 : c1;
            double x, y;
            if ((outCode & TopBit) != 0)
            {
                x = x0 + (x1 - x0) * (top - y0) / (y1 - y0);
                y = top;
            }
            else if ((outCode & BottomBit) != 0)
            {
                x = x0 + (x1 - x0) * (bottom - y0) / (y1 - y0);
                y = bottom;
            }
            else if ((outCode & RightBit) != 0)
            {
                y = y0 + (y1 - y0) * (right - x0) / (x1 - x0);
                x = right;
            }
            else
            {
                y = y0 + (y1 - y0) * (left - x0) / (x1 - x0);
                x = left;
            }

            if (outCode == c0)
            {
                x0 = x;
                y0 = y;
                c0 = Code(x0, y0, left, bottom, right, top);
            }
            else
            {
                x1 = x;
                y1 = y;
                c1 = Code(x1, y1, left, bottom, right, top);
            }
        }

        return false;
    }
}
