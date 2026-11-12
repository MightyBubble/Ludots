using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using static CrowdSimulationTests.Uat.CrowdFogUatTools;

namespace CrowdSimulationTests.Uat;

/// <summary>玩家视图按本方视野组隐藏：视野外的敌方单位、敌方目标和没见过的新建筑都不画，本方单位和走进视野的敌方照常画；视线遮挡打开时墙后的敌方被藏住，关掉后重新可见。</summary>
public sealed class HideUatTests
{
    private const int Me = CrowdUatKit.Player;
    private const int Squad = 30;
    private const int NearCount = 5;
    private const int NearOffsetCm = 20000;

    [Test]
    public void HiddenScene_ShowsOnlyWhatTheViewerGroupSees()
    {
        using var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        var fog = s.Fog!;
        int w = WorldCm(s);
        int foe = EnemyOf(s, Me);
        int vision = (int)s.Config.Fog.VisionCm.ToLong();
        (int X, int Y) own = ((int)(0.2 * w), (int)(0.5 * w)), foeAt = ((int)(0.8 * w), (int)(0.5 * w));
        (int X, int Y) goal = ((int)(0.8 * w), (int)(0.25 * w)), bld = ((int)(0.5 * w), (int)(0.85 * w));
        FogSight(s, false);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(own.X), Fix64.FromInt(own.Y), Squad, rIdx: 0, player: Me);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(foeAt.X), Fix64.FromInt(foeAt.Y), Squad, rIdx: 0, player: foe);
        CrowdUatKit.Order(s, Fix64.FromInt(goal.X), Fix64.FromInt(goal.Y), foe);
        int bid = Place(s, "building", bld.X, bld.Y, vision / 2);
        CrowdUatKit.Step(s, 60);
        Wait(s);

        int viewer = Group(s, Me);
        var (ownShown, ownN) = Shown(s, viewer, Me);
        var (foeShown, foeN) = Shown(s, viewer, foe);
        Assert.That(ownN > 0 && ownShown == ownN, Is.True, $"本方单位应全部显示：{ownShown}/{ownN}");
        Assert.That(foeN, Is.GreaterThan(0), "场景无效：敌方单位未生成");
        Assert.That(foeShown, Is.Zero, $"视野外敌方单位被显示：{foeShown}/{foeN}");
        Assert.That(s.Orders.List.Any(o => o.Player == foe && o.Groups.Count > 0), Is.True, "场景无效：敌方指令未产生");
        var drawn = CrowdPlayerViewHost.LatestOrder(s, Me);
        Assert.That(drawn == null || drawn.Player == Me, Is.True, "敌方目标被显示");
        Assert.That(CrowdPlayerVisibility.StructureShown(s, viewer, bid, hideFog: true), Is.False, "视野外的新建筑被显示");

        CrowdUatKit.SpawnAt(s, Fix64.FromInt(own.X + NearOffsetCm), Fix64.FromInt(own.Y), NearCount, rIdx: 0, player: foe);
        CrowdUatKit.Step(s, 2);
        Wait(s);
        var (nearShown, _) = Shown(s, viewer, foe);
        Assert.That(nearShown, Is.GreaterThanOrEqualTo(1), "进入视野的敌方单位未显示（对照失败）");
        CrowdSimulationValidator.ValidateSim(s);
        TestContext.Out.WriteLine($"hidden: own {ownShown}/{ownN}, enemy {foeShown}/{foeN}, enemy in view {nearShown}");
    }

    [Test]
    public void LineOfSightScene_WallHidesEnemyOnlyWhileSightIsOn()
    {
        using var scene = CrowdUatKit.Scene();
        var s = scene.Session;
        int w = WorldCm(s);
        int foe = EnemyOf(s, Me);
        int vision = (int)s.Config.Fog.VisionCm.ToLong();
        (int X, int Y) own = ((int)(0.3 * w), (int)(0.5 * w));
        int wallX = own.X + (int)(vision * 0.45);
        (int X, int Y) foeAt = (own.X + (int)(vision * 0.8), own.Y);
        FogSight(s, true);
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(own.X), Fix64.FromInt(own.Y), Squad, rIdx: 0, player: Me);
        foreach (int dy in new[] { -1, 0, 1 }) Place(s, "building", wallX, own.Y + (int)(dy * vision * 0.3), (int)(vision * 0.3));
        CrowdUatKit.SpawnAt(s, Fix64.FromInt(foeAt.X), Fix64.FromInt(foeAt.Y), Squad, rIdx: 0, player: foe);
        CrowdUatKit.Step(s, 2);
        Wait(s);

        int viewer = Group(s, Me);
        var (on, n1) = Shown(s, viewer, foe);
        FogSight(s, false);
        Wait(s);
        var (off, n2) = Shown(s, viewer, foe);
        TestContext.Out.WriteLine($"los: on {on}/{n1}, off {off}/{n2}");
        Assert.That(n1, Is.GreaterThan(0), "场景无效：墙后敌方未生成");
        Assert.That(on, Is.Zero, $"视线遮挡开启时墙后敌方仍显示：{on}/{n1}");
        Assert.That(off, Is.GreaterThan(0), "视线遮挡关闭时墙后敌方应可见（对照失败）");
    }

    private static void FogSight(CrowdSimSession s, bool on)
        => s.Commands.Submit(s, new JsonObject { ["type"] = "fogSight", ["on"] = on }, CrowdSimCommands.Exec);

    private static void Wait(CrowdSimSession s) => CrowdUatKit.Step(s, ShowWaitTicks(s));
}
