using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>
/// 走廊查询失败且起点在走廊掩码外时，可达的路径请求仍描出路线、流场盖到起点 tile；
/// 每组每 tick 都有场外采样、扩展预算每 tick 一次时，所有带流场的组在 2 × 组数 tick 内都轮到扩展。
/// 刷新不收缩缓存条目由 <c>CrowdFlowCacheAndStrayTests.RefreshHit_DoesNotSpendThePerTickBudget</c> 断言。
/// </summary>
[TestFixture]
public sealed class D52UatTests
{
    private const int StrayUnits = 3000;
    private const int SettleTicks = 10;
    private const int MinGroups = 4;

    [Test]
    public void LeaderStartOutsideCorridor_CorridorFails_StillTracesWithStartTileJoined()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        var nav = session.Navs.OrderBy(kv => kv.Key).Select(kv => kv.Value).First(n => n.Links == null && PassableCells(n).Any());
        int side = nav.NavCellsPerSide, perTile = nav.Hpa!.NavCellsPerTile, tilesPerSide = nav.Hpa.NavTilesPerSide;
        int pad = session.Config.Flowfield.CorridorPaddingNavTiles;
        int Tile(int c) => NavGridSteps.TileOf(c, side, perTile, tilesPerSide);
        int Tx(int c) => Tile(c) % tilesPerSide;
        int Ty(int c) => Tile(c) / tilesPerSide;

        var cells = PassableCells(nav).ToArray();
        int goal = cells[cells.Length >> 1], comp = nav.Comp[goal];
        int near = cells.Where(c => c != goal && Tile(c) == Tile(goal) && nav.Comp[c] == comp).DefaultIfEmpty(-1).First();
        int far = cells.Where(c => nav.Comp[c] == comp &&
            Math.Max(Math.Abs(Tx(c) - Tx(goal)), Math.Abs(Ty(c) - Ty(goal))) == pad + 1).DefaultIfEmpty(-1).First();
        if (near < 0 || far < 0) Assert.Fail("场景无效：找不到测试格");

        var service = scene.Service;
        PathResult reply;
        service.TestCorridorFails = true;
        try
        {
            int id = service.Request(new PathQuery(nav.Id, far, goal, 0), session.TickCount);
            reply = service.AwaitDue(id);
        }
        finally
        {
            service.TestCorridorFails = false;
        }

        try
        {
            TestContext.Out.WriteLine(
                $"[uat-d52-leader] nav={nav.Id} goal={goal} far={far} pad={pad} reachable={reply.Reachable} points={reply.PointCount} " +
                $"startTileInField={(reply.Flow?.Mask?[Tile(far)] ?? 0) != 0}");
            Assert.That(reply.Reachable && reply.Points != null, Is.True, "领队起点在走廊外：流场兜底失败，可达组被判不可达");
            Assert.That(reply.Flow, Is.Not.Null);
            Assert.That(reply.Flow!.Mask![Tile(far)], Is.Not.EqualTo(0), "重试后的流场未覆盖领队起点 tile");
        }
        finally
        {
            if (reply.Flow != null) service.Recycle(reply.Flow);
        }
    }

    [Test]
    public void EveryGroupWithAField_GetsAnExtensionWithinTwiceTheGroupCount()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        Assert.That(session.Config.Flowfield.MaxRebuildsPerTick, Is.EqualTo(1), "场景无效：扩展预算不是每 tick 一次");
        session.Commands.Submit<object?>(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = StrayUnits }, CrowdSimCommands.Exec);
        session.Commands.Submit<object?>(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.Scatter }, CrowdSimCommands.Exec);
        CrowdUatKit.Step(session, SettleTicks);

        var planner = session.Planner!;
        var list = new List<CrowdNavGroupSet.Group>();
        foreach (var g in session.Groups.Groups)
        {
            if (g?.Flow != null) list.Add(g);
        }

        if (list.Count < MinGroups) Assert.Fail($"场景无效：组数不足（{list.Count}）");
        var before = new int[list.Count];
        for (int i = 0; i < list.Count; i++) before[i] = planner.ExtendByGroup[list[i].Id];
        var served = new HashSet<int>();
        int limit = 2 * list.Count, t = 0;
        for (; t < limit && served.Count < list.Count; t++)
        {
            foreach (var g in list)
            {
                if (!session.Groups.TryGet(g.Id, out var live) || live != g || g.Flow?.Mask == null || planner.Refreshing(g)) continue;
                int outside = CellOutside(session.ResolveNavContext(g.NavId), g.Flow.Mask);
                if (outside >= 0) planner.NoteStray(g.Id, outside);
            }

            CrowdUatKit.Step(session);
            for (int i = 0; i < list.Count; i++)
            {
                if (planner.ExtendByGroup[list[i].Id] > before[i]) served.Add(list[i].Id);
            }
        }

        var starved = list.Where(g => !served.Contains(g.Id)).Select(g => g.Id).ToArray();
        TestContext.Out.WriteLine($"[uat-d52-stray] groups={list.Count} served={served.Count} ticks={t} limit={limit}");
        Assert.That(starved, Is.Empty, $"{limit} tick 内未获预算的组：{string.Join(",", starved)}");
    }

    private static IEnumerable<int> PassableCells(NavContext nav)
    {
        for (int c = 0; c < nav.Passable.Length; c++)
        {
            if (nav.Passable[c] != 0) yield return c;
        }
    }

    private static int CellOutside(NavContext nav, byte[] mask)
    {
        int perTile = nav.Hpa!.NavCellsPerTile, tilesPerSide = nav.Hpa.NavTilesPerSide;
        for (int t = 0; t < mask.Length; t++)
        {
            if (mask[t] != 0) continue;
            int tx = t % tilesPerSide, ty = t / tilesPerSide;
            bool touches = false;
            for (int dy = -1; dy <= 1 && !touches; dy++)
            {
                for (int dx = -1; dx <= 1 && !touches; dx++)
                {
                    int nx = tx + dx, ny = ty + dy;
                    touches = nx >= 0 && ny >= 0 && nx < tilesPerSide && ny < tilesPerSide && mask[ny * tilesPerSide + nx] != 0;
                }
            }

            if (touches) return ty * perTile * nav.NavCellsPerSide + tx * perTile;
        }

        return -1;
    }
}
