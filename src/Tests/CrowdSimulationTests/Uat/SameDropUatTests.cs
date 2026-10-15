using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>同一导航上的两个组共用一条指令，第二组的答复是 same（指向第一组新建的场）。答复在途时第一个组被清空、导航版本被结构放置顶起，那张场回池。答复落地后每个存活组都有流场或仍在规划，300 tick 后没有单位原地永久站立。</summary>
[TestFixture]
public sealed class SameDropUatTests
{
    private const int Ticks = 300, PerBucket = 40;

    [Test]
    public void DroppedGroupInFlight_LeavesSurvivorWithAFieldAndMoving()
    {
        using var scene = CrowdUatKit.Scene();
        var sim = scene.Session;
        int cs = scene.CellCm, n = scene.CellsPerSide;
        var nav = scene.Nav;
        var nav1 = sim.NavFor(scene.Runtime.UnitTypes[0].AgentTypeIndex, 1);
        Assert.That(nav1.Id, Is.EqualTo(nav.Id), $"场景无效：半径级 0 / 1 未共享导航上下文（{nav.Id} vs {nav1.Id}）");

        Fix64 half = scene.WorldCm / 2;
        var (sx, sy) = CrowdUatKit.Spot(sim, nav, half, half);
        int c0 = CrowdSimulationSpace.CellAt(sx, sy, cs, n);
        Fix64 d = scene.Cells(10);
        CrowdUatKit.SpawnAt(sim, sx, sy, PerBucket, rIdx: 0);
        CrowdUatKit.SpawnAt(sim, sx + d, sy, PerBucket, rIdx: 1);
        HashSet<int> GroupsOf()
        {
            var set = new HashSet<int>();
            for (int i = 0; i < sim.Units.Count; i++) set.Add(CrowdUatKit.StateAt(sim, i).GroupId);
            return set;
        }

        int gA = CrowdUatKit.StateAt(sim, 0).GroupId;
        var aPos = new List<Fix64Vec2>();
        for (int i = 0; i < sim.Units.Count; i++)
        {
            if (CrowdUatKit.StateAt(sim, i).GroupId == gA) aPos.Add(CrowdUatKit.PositionAt(sim, i));
        }

        Fix64 cx = Fix64.Zero, cy = Fix64.Zero;
        foreach (var p in aPos) { cx += p.X; cy += p.Y; }
        cx /= aPos.Count;
        cy /= aPos.Count;
        Fix64 rA = Fix64.Zero;
        foreach (var p in aPos) rA = Fix64.Max(rA, CrowdFix.Hypot(p.X - cx, p.Y - cy));
        rA += Fix64.FromInt(100);
        Assert.That(GroupsOf().Count, Is.EqualTo(2), "场景无效：应有两个组");

        (Fix64 X, Fix64 Y)? goal = null;
        Fix64 far = scene.WorldCm * Fix64.FromDouble(0.08);
        for (int a = 0; a < 16 && goal == null; a++)
        {
            Fix64 gx = sx + Fix64.FromDouble(Math.Cos(a * Math.PI / 8)) * far, gy = sy + Fix64.FromDouble(Math.Sin(a * Math.PI / 8)) * far;
            if (gx <= Fix64.Zero || gy <= Fix64.Zero || gx >= scene.WorldCm || gy >= scene.WorldCm) continue;
            int c = CrowdSimulationSpace.CellAt(gx, gy, cs, n);
            if (nav.Passable[c] != 0 && nav.Comp[c] == nav.Comp[c0]) goal = (gx, gy);
        }

        Assert.That(goal, Is.Not.Null, "场景无效：找不到可达目标");
        CrowdUatKit.Step(sim, 2);
        var start = new Dictionary<uint, Fix64Vec2>();
        for (int i = 0; i < sim.Units.Count; i++) start[sim.Units.HandleAt(i)] = CrowdUatKit.PositionAt(sim, i);

        var errors = new List<string>();
        CrowdUatKit.Order(sim, goal!.Value.X, goal.Value.Y);
        int planning = GroupsOf().Count(g => sim.Groups.Groups[g]!.Planning);
        Assert.That(planning, Is.EqualTo(2), $"场景无效：指令后应有 2 个规划中的组，实际 {planning}");

        int v0 = nav.Version, before = sim.Units.Count;
        sim.Commands.Submit(sim, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = (int)cx.ToLong(),
            YCm = (int)cy.ToLong(),
            RadiusCm = (int)Fix64.Ceiling(rA).ToLong(),
            // 冲量为 0 时外圈不起作用；入队校验要求外圈倍数 > 1。
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        }, CrowdSimCommands.Exec);
        int killed = before - sim.Units.Count;
        Assert.That(killed, Is.EqualTo(aPos.Count), $"场景无效：应清空首个组 {aPos.Count} 个单位，实际 {killed}");
        sim.Commands.Submit(sim, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "building",
            XCm = (int)(sx + d / 2).ToLong(),
            YCm = (int)(sy + scene.Cells(12)).ToLong(),
            SizeCm = 6 * cs,
        }, CrowdSimCommands.Exec);
        Assert.That(sim.LastPlacementReject, Is.EqualTo(CrowdPlacementReject.None), "场景无效：结构放置被拒");
        Assert.That(nav.Version, Is.Not.EqualTo(v0), "场景无效：放置结构未使导航版本递增");
        Assert.That(GroupsOf().Count, Is.EqualTo(1), "场景无效：清空后应剩 1 个组");

        var planner = sim.Planner!;
        int sameBefore = planner.PlanSameReplies;
        CrowdUatKit.Step(sim, sim.Config.Planning.LatencyTicks + 1);
        Assert.That(planner.PlanSameReplies - sameBefore, Is.EqualTo(1), "场景无效：存活组没有收到 same 答复");
        int bare = GroupsOf().Count(g => sim.Groups.Groups[g]!.Flow == null && !sim.Groups.Groups[g]!.Planning);
        if (bare > 0) errors.Add($"回复应用后有 {bare} 个存活组既无流场也不在规划中");

        CrowdUatKit.Step(sim, Ticks);
        int still = 0, live = 0;
        for (int i = 0; i < sim.Units.Count; i++)
        {
            if (!start.TryGetValue(sim.Units.HandleAt(i), out var s)) continue;
            live++;
            var p = CrowdUatKit.PositionAt(sim, i);
            bool moved = CrowdFix.Hypot(p.X - s.X, p.Y - s.Y) > scene.Cells(2);
            byte st = CrowdUatKit.StateAt(sim, i).State;
            if (!moved && st != (byte)CrowdUnitState.Arrived && st != (byte)CrowdUnitState.Unreachable) still++;
        }

        TestContext.Out.WriteLine($"killed={killed} bareAfterReply={bare} live={live} still={still}");
        Assert.That(live, Is.GreaterThan(0), "场景无效：存活单位为 0");
        if (still > 0) errors.Add($"{Ticks} tick 后仍有 {still}/{live} 个单位原地未动且未到达（永久站立）");
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSim(sim));
        Assert.That(errors, Is.Empty);
    }
}
