using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using CrowdSimulationTests.Uat;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;
using Ludots.UI.Skia;
using NUnit.Framework;
using SkiaSharp;
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
    // 种子 1337：陆地 (213,50) 向北到陆地 (213,84)。y=80..83 是一排水域，整块导航 tile 在开局视野外；岸上 y=79 放建筑。
    private const int OptimisticCellCm = 6250;
    private const int OptimisticSquad = 2;
    private const int OptimisticStartX = 1334375, OptimisticStartY = 315625;
    private const int OptimisticGoalX = 1334375, OptimisticGoalY = 528125;
    private const int OptimisticBuildX = 1334375, OptimisticBuildY = 496875;
    private const int OptimisticBuildSizeCm = 6250;
    private const int OptimisticPlaceTick = 60;
    private const int OptimisticWallX = 213, OptimisticWallY0 = 80, OptimisticWallY1 = 83;
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
        var bodyNav = s.ResolveNavContext(before.BodyNavId);
        var beliefNav = s.ResolveNavContext(before.NavId);
        for (int y = OptimisticWallY0; y <= OptimisticWallY1; y++)
        {
            int i = y * bodyNav.NavCellsPerSide + OptimisticWallX;
            Assert.That(bodyNav.Passable[i], Is.EqualTo(0), $"水域 ({OptimisticWallX},{y}) 在真相导航上应走不了");
            Assert.That(beliefNav.Passable[i], Is.Not.EqualTo(0), $"水域 ({OptimisticWallX},{y}) 在认知导航上应被当成能走");
        }

        bool crossed = host.AdvanceUntil(() =>
        {
            var now = ReadyGroup(s, Me);
            return now?.Leader != null && VisitsWall(PathOf(now.Leader));
        }, 40);
        int crossedAt = s.TickCount;
        Assert.That(crossed, Is.True, "乐观路径应直穿正前方那排水域");
        SaveHudSnapshot(host, "fog-script-optimistic-crossing.png");

        host.AdvanceTo(OptimisticPlaceTick);
        before = ReadyGroup(s, Me);
        Assert.That(before, Is.Not.Null, "放建筑前领队丢了");
        var path0 = PathOf(before!.Leader!);
        Assert.That(VisitsWall(path0), Is.True, "放到建筑之前，乐观路径应仍直穿正前方的水域 " + Describe(path0));

        host.Advance();
        int id = s.Structures!.EntityIds.Max();
        Assert.That(s.Structures.FootprintOf(id).Covers(Fix64.FromInt(OptimisticBuildX), Fix64.FromInt(OptimisticBuildY)), Is.True, "岸上的建筑没放下");
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.False, "没看见的建筑进了认知");
        Assert.That(FootprintCells(s, id).Count(c => Visible(fog, viewer, c)), Is.Zero, "放置时建筑足迹已经可见");
        bool heldReady = host.AdvanceUntil(() => ReadyGroup(s, Me) != null, 40);
        var held = ReadyGroup(s, Me);
        Assert.That(heldReady && held != null, Is.True, "放下未见建筑后领队没有回到可核对的路径");
        path0 = PathOf(held!.Leader!);
        Assert.That(VisitsWall(path0), Is.True, "放下还没看见的建筑后，乐观路径不应改去绕开正前方的水域 " + Describe(path0));

        bool leftWater = false;
        bool seen = host.AdvanceUntil(() =>
        {
            if (!fog.Belief[viewer].ContainsKey(id)) return false;
            var now = ReadyGroup(s, Me);
            if (now == null) return false;
            var path = PathOf(now.Leader!);
            leftWater = !VisitsWall(path) && !SameRoute(path, path0);
            return leftWater;
        }, OptimisticArriveMax);
        Assert.That(seen, Is.True,
            $"看见建筑后路径仍穿过正前方的水域，或没有改道。建筑进认知={fog.Belief[viewer].ContainsKey(id)} tick={s.TickCount}");
        Assert.That(fog.Belief[viewer].ContainsKey(id), Is.True, "改道时建筑还不在认知里");
        int reroutedAt = s.TickCount;

        bool arrived = host.AdvanceUntil(() => AllArrived(s, OptimisticSquad), OptimisticArriveMax);
        TestContext.Out.WriteLine(
            $"optimistic: plannedNav {before.NavId} body {before.BodyNavId}, crossed at {crossedAt}, building placed at {OptimisticPlaceTick}, rerouted at {reroutedAt}, leftWater {leftWater}, arrived {arrived} at {s.TickCount}");
        Assert.That(arrived, Is.True, $"{OptimisticArriveMax} tick 内没有全部到达 {Lagging(s)}");
        SaveHudSnapshot(host, "fog-script-optimistic-arrived.png");
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
        Assert.That(VisitsWall(path0), Is.False, "已知地形的第一条路径不应穿过正前方那排水域 " + Describe(path0));
        int id = Place(s, "building", OptimisticBuildX, OptimisticBuildY, OptimisticBuildSizeCm);
        Assert.That(s.Fog.Belief[Group(s, Me)].ContainsKey(id), Is.False, "出发时岸上的建筑不应已在认知里");
        CrowdUatKit.Step(s, 4);
        var afterPlace = ReadyGroup(s, Me);
        Assert.That(afterPlace, Is.Not.Null);
        Assert.That(VisitsWall(PathOf(afterPlace!.Leader!)), Is.False,
            "放下一栋还没看见的建筑后，已知路线不应改去穿过正前方的水域");

        bool crossed = false;
        bool arrived = false;
        for (int t = 0; t < OptimisticArriveMax; t++)
        {
            var now = ReadyGroup(s, Me);
            if (now?.Leader != null && VisitsWall(PathOf(now.Leader)))
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
        Assert.That(arrived, Is.True, $"{OptimisticArriveMax} tick 内没有全部到达 {Lagging(s)}");
    }

    [Test]
    public void OptimisticHotkeyAndHudSwitch_ChangeOnlyThePreference()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogRerouteShowcaseMod");
        var s = host.Session;
        var runtime = host.Engine.CrowdRuntime;
        string toggle = PlayerViewAction(host, "toggleOptimisticTerrain");
        Assert.Multiple(() =>
        {
            Assert.That(toggle, Is.EqualTo("CrowdSimulation.Player.ToggleOptimisticTerrain"), "玩家视图的乐观迷雾动作名");
            Assert.That(BindingPath(toggle), Is.EqualTo("<Keyboard>/rightBracket"), "乐观迷雾快捷键应是 ]");
            Assert.That(runtime.OrderPrefs!.OptimisticTerrain, Is.True, "进图时偏好应抄配置的开局值");
        });

        host.AdvanceTo(1);
        Assert.That(s.Fog!.Terrain, Is.True, "脚本第一笔令后迷雾开关应是开");
        Assert.That(HudSwitchOn(host), Is.True, "开局 HUD 开关应显示开");

        PressAction(host, toggle);
        host.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.OrderPrefs!.OptimisticTerrain, Is.False, "按 ] 后偏好应翻成关");
            Assert.That(s.Fog.Terrain, Is.True, "快捷键只改偏好，迷雾开关要等下一笔令");
        });

        Assert.That(AdvanceUntilHudReflects(host, () => !HudSwitchOn(host)), Is.True, "HUD 开关应采样成关");
        SaveHudSnapshot(host, "fog-hotkey-preference-off.png");

        PressAction(host, toggle);
        host.Advance();
        Assert.That(runtime.OrderPrefs!.OptimisticTerrain, Is.True, "再按 ] 偏好应翻回开");
        Assert.That(s.Fog.Terrain, Is.True, "偏好来回切不影响迷雾开关");
        Assert.That(AdvanceUntilHudReflects(host, () => HudSwitchOn(host)), Is.True, "HUD 开关应采样回开");
    }

    [Test]
    public void DragLineOrders_CarryCurrentOptimisticState_BothTerrainStatesPlanDifferently()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogRerouteShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        var runtime = host.Engine.CrowdRuntime;
        string secondary = PlayerViewAction(host, "secondaryHold");
        string commit = PlayerViewAction(host, "formationCommit");
        string toggle = PlayerViewAction(host, "toggleOptimisticTerrain");
        // 拖线的中点就是令的目标：线段两端放在目标南北各 12500cm，中点落在 OptimisticGoal 上。
        (int X, int Y) dragFrom = (OptimisticGoalX, OptimisticGoalY - 12500), dragTo = (OptimisticGoalX, OptimisticGoalY + 12500);
        var ray = new PickingRay();
        host.Engine.SetService(CoreServiceKeys.ScreenRayProvider, ray);
        host.AdvanceTo(1);
        int viewer = Group(s, Me);
        int logAtStart = s.Commands.Log.Count;
        Assert.That(s.Units.Count, Is.EqualTo(OptimisticSquad), "场景无效：演示脚本应生成两人的小队");
        Assert.That(fog.Terrain, Is.True, "脚本开局令后迷雾开关应是开");

        // 开（偏好还是进图抄入的开）：拖线下令，这笔令应带开的迷雾状态，按未探索地形直穿水域。
        DragLine(host, ray, secondary, commit, dragFrom, dragTo);
        var opened = LastOrder(s, logAtStart);
        Assert.Multiple(() =>
        {
            Assert.That(opened.Cmd.Kind, Is.EqualTo(CrowdSimCommandKind.Order), "拖线应提交 order 指令");
            Assert.That(opened.Cmd.Player, Is.EqualTo(Me), "拖线令的玩家");
            Assert.That((opened.Cmd.XCm, opened.Cmd.YCm), Is.EqualTo((OptimisticGoalX, OptimisticGoalY)), "拖线令的目标应是线段中点");
            Assert.That(opened.Cmd.HasFace, Is.True, "拖线令应带朝向");
            Assert.That(opened.Cmd.WidthCm, Is.EqualTo(25000), "拖线令应带线段长度的阵型宽");
            Assert.That(opened.Cmd.Members, Has.Length.EqualTo(OptimisticSquad), "无选择时拖线令应带上全体己方单位");
            Assert.That(opened.Cmd.HasFogTerrain, Is.True, "拖线令应携带迷雾开关");
            Assert.That(opened.Cmd.FogTerrain, Is.True, "第一笔拖线令应携带进图抄入的偏好（开）");
        });

        Assert.That(fog.Terrain, Is.True, "开的那笔令执行后迷雾开关保持开");
        bool optimisticallyCrossed = host.AdvanceUntil(() =>
        {
            var now = ReadyGroup(s, Me);
            return now?.Leader != null && VisitsWall(PathOf(now.Leader));
        }, 200);
        var optimisticGroup = ReadyGroup(s, Me);
        Assert.That(optimisticGroup, Is.Not.Null, "乐观拖线令后没有规划完的领队");
        Assert.That(optimisticGroup!.NavId, Is.Not.EqualTo(optimisticGroup.BodyNavId), "打开乐观后应走认知导航规划");
        Assert.That(optimisticallyCrossed, Is.True, "乐观路径应直穿正前方那排水域");
        SaveHudSnapshot(host, "fog-dragline-optimistic-terrain.png");
        TestContext.Out.WriteLine($"optimistic(drag): order tick {opened.Tick}, crossed at {s.TickCount}");

        // 关：热键翻偏好后再拖同一条线，这笔令应带关的迷雾状态，按已知真值绕开水域。
        PressAction(host, toggle);
        host.Advance();
        Assert.That(runtime.OrderPrefs!.OptimisticTerrain, Is.False, "第二笔拖线前偏好应已是关");
        logAtStart = s.Commands.Log.Count;
        DragLine(host, ray, secondary, commit, dragFrom, dragTo);
        var closed = LastOrder(s, logAtStart);
        Assert.Multiple(() =>
        {
            Assert.That(closed.Cmd.HasFogTerrain, Is.True, "第二笔拖线令也应携带迷雾开关");
            Assert.That(closed.Cmd.FogTerrain, Is.False, "切换后的令应携带新偏好（关）");
        });

        Assert.That(fog.Terrain, Is.False, "关的那笔令执行后内核迷雾开关应关上");
        bool known = host.AdvanceUntil(() => ReadyGroup(s, Me) != null, 60);
        var knownGroup = ReadyGroup(s, Me);
        Assert.That(known && knownGroup != null, Is.True, "关掉乐观后没有规划完的领队");
        Assert.That(knownGroup!.NavId, Is.EqualTo(knownGroup.BodyNavId), "关闭乐观后应按已知真值导航规划");
        Assert.That(VisitsWall(PathOf(knownGroup.Leader!)), Is.False,
            "已知地形的路径不应穿过正前方那排水域 " + Describe(PathOf(knownGroup.Leader!)));

        // 脚本在 tick 60 放下岸上那栋谁都没见过的建筑；关着的态应仍按真值导航，不为未知改道穿水。
        host.AdvanceTo(OptimisticPlaceTick + 1);
        int placed = s.Structures!.EntityIds.Max();
        Assert.That(s.Structures.FootprintOf(placed).Covers(Fix64.FromInt(OptimisticBuildX), Fix64.FromInt(OptimisticBuildY)), Is.True, "脚本应已放下岸上的建筑");
        Assert.That(fog.Belief[viewer].ContainsKey(placed), Is.False, "放下的建筑还没被看见就进了认知");
        bool knownCrossed = false, knownArrived = false;
        int knownArriveTick = -1;
        for (int t = 0; t < OptimisticArriveMax; t++)
        {
            var now = ReadyGroup(s, Me);
            if (now?.Leader != null && VisitsWall(PathOf(now.Leader))) knownCrossed = true;
            if (AllArrived(s, OptimisticSquad))
            {
                knownArrived = true;
                knownArriveTick = s.TickCount;
                break;
            }

            host.Advance();
        }

        SaveHudSnapshot(host, "fog-dragline-known-terrain.png");
        TestContext.Out.WriteLine(
            $"known(drag): order tick {closed.Tick}, building placed at {OptimisticPlaceTick}, seen={fog.Belief[viewer].ContainsKey(placed)}, crossed {knownCrossed}, arrived {knownArrived} at {knownArriveTick}");
        Assert.That(knownCrossed, Is.False, "已知路线中途改去穿过水域");
        Assert.That(knownArrived, Is.True, $"{OptimisticArriveMax} tick 内没有全部到达 {Lagging(s)}");
        CrowdSimulationValidator.ValidateSim(s);
    }

    [Test]
    public void MouseOrders_ThroughTheOrderBuffer_CarryTheSharedPreference()
    {
        using var host = CrowdShowcaseHost.Start("CrowdSimulationFogRerouteShowcaseMod");
        var s = host.Session;
        var fog = s.Fog!;
        var runtime = host.Engine.CrowdRuntime;
        var registry = host.Engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("引擎没有 OrderTypeRegistry。");
        var queue = host.Engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("引擎没有 OrderQueue。");
        Assert.That(registry.TryGetId(CrowdSimulationOrderAdapter.OrderTypeKey, out int typeId) && typeId > 0, Is.True,
            "crowdSimulationMove 应已登记");
        string toggle = PlayerViewAction(host, "toggleOptimisticTerrain");
        host.AdvanceTo(1);
        int logAtStart = s.Commands.Log.Count;

        // 无选择：右键的 GAS 订单跟着被选演员走，一个演员都没有时不会产生批次，适配器收到空批次直接返回。
        CrowdSimulationOrderAdapter.Accept(s, ReadOnlySpan<Order>.Empty, runtime.OrderPrefs);
        host.Advance();
        Assert.That(s.Commands.Log.Count, Is.EqualTo(logAtStart), "无选择的右键不应产生群体指令");
        Assert.That(fog.Terrain, Is.True, "无选择的右键不应改迷雾开关");

        // 有单位的右键令经引擎订单缓冲排空进适配器，携带共享偏好里的开。
        var (first, orders) = MouseBatch(s, typeId, 1000000, 300000);
        Assert.That(queue.TryEnqueueSharedBatch(orders), Is.EqualTo(OrderSubmitResult.Queued), "右键订单没进引擎订单队列");
        Assert.That(host.AdvanceUntil(() => s.Commands.Log.Count > logAtStart, 60), Is.True, "订单没有经缓冲排空进会话");
        var firstOrder = LastOrder(s, logAtStart);
        Assert.Multiple(() =>
        {
            Assert.That(firstOrder.Cmd.Kind, Is.EqualTo(CrowdSimCommandKind.Order), "右键令应是 order 指令");
            Assert.That((firstOrder.Cmd.XCm, firstOrder.Cmd.YCm), Is.EqualTo(first), "右键令的目标点");
            Assert.That(firstOrder.Cmd.ShapeId, Is.EqualTo(CrowdSimulationOrderAdapter.PlainClickShapeId), "普通右键的阵型");
            Assert.That(firstOrder.Cmd.HasAuto && firstOrder.Cmd.Auto == runtime.OrderPrefs!.MagicBox, Is.True,
                "普通右键走偏好路径，auto 位应抄绑定的 magic box 偏好");
            Assert.That(firstOrder.Cmd.HasFogTerrain, Is.True, "右键令应携带迷雾开关");
            Assert.That(firstOrder.Cmd.FogTerrain, Is.True, "第一笔右键令应携带进图抄入的偏好（开）");
        });

        // 快捷键翻偏好后的下一笔右键令携带关，并把它落到内核。
        PressAction(host, toggle);
        host.Advance();
        Assert.That(runtime.OrderPrefs!.OptimisticTerrain, Is.False, "第二笔右键前偏好应已是关");
        logAtStart = s.Commands.Log.Count;
        var (second, more) = MouseBatch(s, typeId, 1000000, 1200000);
        Assert.That(queue.TryEnqueueSharedBatch(more), Is.EqualTo(OrderSubmitResult.Queued), "第二笔订单没进队列");
        Assert.That(host.AdvanceUntil(() => s.Commands.Log.Count > logAtStart, 60), Is.True, "第二笔订单没有排空");
        var secondOrder = LastOrder(s, logAtStart);
        Assert.Multiple(() =>
        {
            Assert.That(secondOrder.Cmd.FogTerrain, Is.False, "切换后的右键令应携带新偏好（关）");
            Assert.That((secondOrder.Cmd.XCm, secondOrder.Cmd.YCm), Is.EqualTo(second));
        });

        Assert.That(host.AdvanceUntil(() => !fog.Terrain, 60), Is.True, "第二笔右键令执行后内核迷雾开关应关上");
    }

    [Test]
    public void OrderFogTerrainOnSessionWithoutFog_IsExplicitlyRejectedOrHarmless()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        Assert.That(session.Fog, Is.Null, "场景无效：这应该是没有迷雾的会话");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        uint member = session.Units.HandleAt(0);

        var withFog = JsonNode.Parse($$"""
            {"type":"order","player":1,"xCm":480000,"yCm":460000,"shape":"box","fogTerrain":true,"members":[{{member}}]}
            """)!;
        InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(
            () => session.Commands.Submit<object?>(session, withFog, CrowdSimCommands.Exec))!;
        Assert.That(rejected.Message, Does.Contain("迷雾未启用"), "无迷雾会话上带 fogTerrain 的令应显式报错");
        Assert.That(session.Commands.Log, Is.Empty, "被拒的令不应进日志");

        var withoutFog = JsonNode.Parse($$"""
            {"type":"order","player":1,"xCm":480000,"yCm":460000,"shape":"box","members":[{{member}}]}
            """)!;
        Assert.DoesNotThrow(() => session.Commands.Submit<object?>(session, withoutFog, CrowdSimCommands.Exec),
            "无迷雾会话上不带 fogTerrain 的令应无害执行");
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1), "不带开关的令应正常进日志");
    }

    [Test]
    public void FogTerrainOrders_ReplyToTheSameChecksum()
    {
        var (_, live) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(live.Navs, live.Config, 1, TimeSpan.FromSeconds(30), live.ResolveNavContext);
        live.EnableMovement(CrowdMovementKernel.Create(live), new CrowdSimPlanner(live, service));
        TestDefaults.EnableFog(live);
        live.BlockOnDueReplies = true;
        Assert.That(live.Fog!.Terrain, Is.False, "对拍沙盒开局应关闭乐观迷雾");
        live.Commands.Submit<object?>(live, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = Me,
            XCm = OptimisticStartX,
            YCm = OptimisticStartY,
            Count = OptimisticSquad,
            UnitType = 0,
            RIdx = 0,
        }, CrowdSimCommands.Exec);
        Order(live, Me, OptimisticGoalX, OptimisticGoalY, fogTerrain: true);
        Assert.That(live.Fog.Terrain, Is.True, "开的令执行后迷雾开关应是开");
        CrowdUatKit.Step(live, 40);
        Order(live, Me, OptimisticGoalX, OptimisticStartY, fogTerrain: false);
        Assert.That(live.Fog.Terrain, Is.False, "关的令执行后迷雾开关应是关");
        CrowdUatKit.Step(live, 40);

        var capture = CrowdSimReplay.Capture(live);
        var replay = new CrowdSimSession(live.Config, Arch.Core.World.Create(), live.Navs, live.NavByLayerRadius, live.Presentation)
        {
            Structures = live.Structures,
            RebakeSources = live.RebakeSources,
            NavTileCache = live.NavTileCache,
        };
        using var replayService = new PathQueryService(replay.Navs, replay.Config, 1, TimeSpan.FromSeconds(30), replay.ResolveNavContext);
        replay.EnableMovement(CrowdMovementKernel.Create(replay), new CrowdSimPlanner(replay, replayService));
        TestDefaults.EnableFog(replay);
        replay.BlockOnDueReplies = true;
        var report = CrowdSimReplay.Replay(replay, capture);
        Assert.That(report.Passed, Is.True, report.Error);
        Assert.That(report.Checksum, Is.EqualTo(capture.Checksum), "开与关的令回放后校验码不一致");
        Assert.That(report.Commands, Is.EqualTo(3), "回放应执行生成加两笔令");
        Assert.That(replay.Fog!.Terrain, Is.False, "回放终态的迷雾开关应与活会话一致");
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

    private static string PlayerViewAction(CrowdShowcaseHost host, string key)
        => CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationPlayerView.json")["actions"]![key]!.GetValue<string>();

    private static string BindingPath(string actionId)
    {
        string file = Path.Combine(CrowdShowcaseHost.RepoRoot(), "mods", "showcases", "crowd_simulation",
            "CrowdSimulationS4DeployMod", "assets", "Input", "default_input.json");
        return FindBinding(JsonNode.Parse(File.ReadAllText(file))!, actionId)
            ?? throw new InvalidOperationException($"default_input.json 里没有 {actionId} 的绑定。");
    }

    private static string? FindBinding(JsonNode node, string actionId)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("actionId", out var id) && id != null && id.GetValue<string>() == actionId
                && obj.TryGetPropertyValue("path", out var path) && path != null)
            {
                return path.GetValue<string>();
            }

            foreach (var (_, child) in obj)
            {
                if (child != null && FindBinding(child, actionId) is { } found) return found;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
            {
                if (child != null && FindBinding(child, actionId) is { } found) return found;
            }
        }

        return null;
    }

    private static AuthoritativeInputAccumulator Accumulator(CrowdShowcaseHost host)
        => host.Engine.GetService(CoreServiceKeys.AuthoritativeInputAccumulator)
            ?? throw new InvalidOperationException("引擎没有权威输入累加器。");

    private static void PressAction(CrowdShowcaseHost host, string actionId)
        => Accumulator(host).CaptureAction(actionId, Vector3.One, true, true, false);

    private static bool HudSwitchOn(CrowdShowcaseHost host)
        => host.HudNode("crowd-optimistic-switch").HasClass("nf-switch-on");

    /// <summary>HUD 面板按墙钟限速采样（hud.json 的 sampleMs.player），等 HUD 反映新状态要按墙钟等，tick 数不作数。</summary>
    private static bool AdvanceUntilHudReflects(CrowdShowcaseHost host, Func<bool> done)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)((HudPlayerSampleMs() * 2 + 200) / 1000d * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            host.Advance();
            if (done()) return true;
        }

        return false;
    }

    private static int HudPlayerSampleMs()
    {
        string file = Path.Combine(CrowdShowcaseHost.RepoRoot(), "mods", "capabilities", "navigation",
            "CrowdSimulationUiMod", "assets", "CrowdSimulationUi", "hud.json");
        return JsonNode.Parse(File.ReadAllText(file))!["sampleMs"]!["player"]!.GetValue<int>();
    }

    /// <summary>把这一帧渲染出的 HUD 存进测试产物目录，佐证同次运行的状态。</summary>
    private static void SaveHudSnapshot(CrowdShowcaseHost host, string file)
    {
        if (host.Ui.IsDirty) host.Ui.Render();
        using var surface = SKSurface.Create(new SKImageInfo(1280, 720));
        surface.Canvas.Clear(new SKColor(96, 104, 88));
        new SkiaUiRenderer().RenderToCanvas(host.UiHost.Scene!, surface.Canvas, 1280, 720);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, file);
        using var output = File.Create(path);
        png.SaveTo(output);
        TestContext.Out.WriteLine($"snapshot: {path}");
    }

    private static (int Tick, CrowdSimCommand Cmd) LastOrder(CrowdSimSession s, int afterIndex)
    {
        var log = s.Commands.Log;
        return log.Count <= afterIndex
            ? throw new InvalidOperationException("没有新的群体指令。")
            : log[log.Count - 1];
    }

    private static ((int X, int Y), Order[]) MouseBatch(CrowdSimSession s, int typeId, int xCm, int yCm)
    {
        var batch = new Order[s.Units.Count];
        for (int i = 0; i < batch.Length; i++)
        {
            batch[i] = new Order
            {
                OrderTypeId = typeId,
                PlayerId = Me,
                Actor = s.Units.EntityAt(i),
                Args = OrderArgs.CreateSingleWorldCm(new Vector3(xCm, 0f, yCm)),
            };
        }

        return ((xCm, yCm), batch);
    }

    /// <summary>按住辅键从 from 拖到 to 松开提交：真实输入动作走 ApplyInput 的拖线摆阵路径。</summary>
    private static void DragLine(CrowdShowcaseHost host, PickingRay ray, string secondary, string commit, (int X, int Y) from, (int X, int Y) to)
    {
        var input = Accumulator(host);
        ray.At = from;
        input.CaptureAction(secondary, Vector3.One, true, false, false);
        host.Advance();
        ray.At = to;
        input.CaptureAction(secondary, Vector3.One, true, false, false);
        input.CaptureAction(commit, Vector3.One, true, true, false);
        host.Advance();
        input.CaptureAction(secondary, Vector3.One, false, false, true);
    }

    /// <summary>不论屏幕坐标，射线都从目标点正上方垂直向下，落点就是给定的厘米坐标。</summary>
    private sealed class PickingRay : IScreenRayProvider
    {
        public (int X, int Y) At;

        public ScreenRay GetRay(Vector2 screenPosition)
            => new(new Vector3(At.X / 100f, 10000f, At.Y / 100f), new Vector3(0f, -1f, 0f));
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

    private static string Lagging(CrowdSimSession session)
    {
        int arrivedCount = 0;
        var lag = new List<string>();
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (CrowdUatKit.StateAt(session, i).State == (byte)CrowdUnitState.Arrived) arrivedCount++;
            else
            {
                var p = CrowdUatKit.PositionAt(session, i);
                lag.Add($"#{i}@({(int)(p.X.ToDouble() / OptimisticCellCm)},{(int)(p.Y.ToDouble() / OptimisticCellCm)})");
            }
        }

        return $"到达 {arrivedCount}/{session.Units.Count} 落后 {string.Join(" ", lag)}";
    }

    /// <summary>领队会把路径起点改成自己的当前位置。后面的路点不动，才算同一条路线。</summary>
    private static bool SameRoute(Fix64Vec2[] path, Fix64Vec2[] prior)
    {
        if (path.Length != prior.Length || path.Length < 2) return false;
        for (int i = 1; i < path.Length; i++)
        {
            if (path[i].X != prior[i].X || path[i].Y != prior[i].Y) return false;
        }

        return true;
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
        return $"共 {path.Length} 点 " + string.Join(" ", path.Select(At));
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

    private static bool VisitsWall(Fix64Vec2[] path)
    {
        for (int y = OptimisticWallY0; y <= OptimisticWallY1; y++)
        {
            if (Visits(path, OptimisticWallX, y)) return true;
        }

        return false;
    }

    /// <summary>折线是否走进这一格。点落在哪一格按厘米整除；线段用格步进补上中间格。</summary>
    private static bool Visits(Fix64Vec2[] path, int cellX, int cellY)
    {
        bool hit = false;
        void Consider(int x, int y)
        {
            if (x == cellX && y == cellY) hit = true;
        }

        for (int i = 0; i < path.Length; i++)
        {
            int x = (int)(path[i].X.ToDouble() / OptimisticCellCm);
            int y = (int)(path[i].Y.ToDouble() / OptimisticCellCm);
            if (i > 0)
            {
                int px = (int)(path[i - 1].X.ToDouble() / OptimisticCellCm);
                int py = (int)(path[i - 1].Y.ToDouble() / OptimisticCellCm);
                int dx = Math.Abs(x - px), dy = Math.Abs(y - py);
                int sx = px < x ? 1 : -1, sy = py < y ? 1 : -1;
                int err = dx - dy;
                while (px != x || py != y)
                {
                    Consider(px, py);
                    int e2 = err * 2;
                    if (e2 > -dy)
                    {
                        err -= dy;
                        px += sx;
                    }

                    if (e2 < dx)
                    {
                        err += dx;
                        py += sy;
                    }
                }
            }

            Consider(x, y);
        }

        return hit;
    }
}
