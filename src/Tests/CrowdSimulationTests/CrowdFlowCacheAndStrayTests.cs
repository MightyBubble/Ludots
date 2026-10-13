using System;
using System.Collections.Generic;
using System.Reflection;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

public sealed class CrowdFlowCacheAndStrayTests
{
    [Test]
    public void Cache_HitRequiresKeyAndMaskCoverage_LruEvictsOldest()
    {
        var cache = new FlowFieldCache(2, 4);
        var wide = Field(3, 1, new byte[] { 1, 1, 1, 0 });
        var narrow = Field(3, 1, new byte[] { 1, 1, 0, 0 });
        cache.Admit(narrow);
        Assert.That(cache.TryHit(cache.KeyOf(3, 1), new byte[] { 1, 1, 1, 0 }, out _), Is.False);
        Assert.That(cache.Misses, Is.EqualTo(1));
        Assert.That(cache.TryHit(cache.KeyOf(3, 1), new byte[] { 1, 0, 0, 0 }, out var hit), Is.True);
        Assert.That(hit, Is.SameAs(narrow));
        Assert.That(cache.TryHit(cache.KeyOf(9, 1), new byte[] { 1, 0, 0, 0 }, out _), Is.False);

        cache.Admit(wide);
        Assert.That(cache.TryHit(cache.KeyOf(3, 1), new byte[] { 1, 1, 1, 0 }, out hit), Is.True);
        Assert.That(hit, Is.SameAs(wide));
        Assert.That(narrow.Cached, Is.False);

        var a = Field(1, 0, new byte[] { 1, 0, 0, 0 });
        var b = Field(1, 1, new byte[] { 1, 0, 0, 0 });
        var c = Field(1, 2, new byte[] { 1, 0, 0, 0 });
        var d = Field(1, 3, new byte[] { 1, 0, 0, 0 });
        var lru = new FlowFieldCache(2, 4);
        lru.Admit(a);
        lru.Admit(b);
        lru.Admit(c);
        Assert.That(a.Cached, Is.False);
        Assert.That(lru.Evicts, Is.EqualTo(1));
        Assert.That(lru.TryHit(lru.KeyOf(1, 1), new byte[] { 1, 0, 0, 0 }, out _), Is.True);
        lru.Admit(d);
        Assert.That(c.Cached, Is.False, "命中过的条目留到最后才被逐出");
        Assert.That(b.Cached, Is.True);
        var order = new long[2];
        Assert.That(lru.CopyOldestFirst(order), Is.EqualTo(2));
        Assert.That(order[0], Is.EqualTo(lru.KeyOf(1, 1)));
        Assert.That(order[1], Is.EqualTo(lru.KeyOf(1, 3)));
        Assert.That(lru.InvalidReason(), Is.Null);
    }

    [Test]
    public void Cache_RecyclesOnlyWhenRefsAndCachedAreBothClear()
    {
        var pool = new FlowPool(2, 2);
        var flow = pool.Take();
        flow.NavId = 1;
        flow.Goal = 0;
        flow.Mask = new byte[] { 1, 0, 0, 0 };
        flow.Refs = 1;
        var cache = new FlowFieldCache(1, 4);
        cache.Admit(flow);
        cache.DropAll();
        Assert.That(flow.Cached, Is.False);
        Assert.That(pool.Holds(flow), Is.False);
        flow.Refs = 0;
        cache.Release(flow);
        Assert.That(pool.Holds(flow), Is.True);
        int free = pool.FreeCount;
        pool.Give(flow);
        Assert.That(pool.FreeCount, Is.EqualTo(free));
    }

    [Test]
    public void Cache_EvictsDirtyTilesAndDeletedBeliefSlot()
    {
        int stride = CrowdBeliefNavs.BeliefStride;
        var cache = new FlowFieldCache(4, 4);
        var truth = Field(2, 0, new byte[] { 0, 1, 0, 0 });
        var belief = Field(stride + 2, 1, new byte[] { 1, 0, 0, 0 });
        var other = Field(stride * 2 + 2, 1, new byte[] { 1, 0, 0, 0 });
        cache.Admit(truth);
        cache.Admit(belief);
        cache.Admit(other);
        cache.EvictTouching(2, new[] { 0 });
        Assert.That(truth.Cached, Is.True);
        cache.EvictTouching(2, new[] { 1 });
        Assert.That(truth.Cached, Is.False);
        cache.EvictSlot(1, stride);
        Assert.That(belief.Cached, Is.False);
        Assert.That(other.Cached, Is.True);
        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void ClearCaches_DropsEntriesButKeepsTheGroupFlow()
    {
        using var harness = Open();
        var session = harness.Session;
        var group = OrderOne(session, 480000, 560000, 700000, 700000);
        Assert.That(harness.Planner.FlowCacheCount, Is.GreaterThan(0));
        Assert.That(group.Flow!.Cached, Is.True);
        var kept = group.Flow;
        harness.Planner.InvalidateFlows();
        Assert.That(harness.Planner.FlowCacheCount, Is.EqualTo(0));
        Assert.That(group.Flow, Is.SameAs(kept));
        Assert.That(kept.Cached, Is.False);
        Assert.That(kept.Refs, Is.GreaterThan(0));
        int before = harness.Planner.FlowRefreshSubmits;
        harness.Planner.ProcessRefreshes(session.TickCount);
        Assert.That(harness.Planner.FlowRefreshSubmits, Is.EqualTo(before + 1));
        Assert.That(harness.Planner.ApplyDue(session.TickCount + session.Config.Planning.RefreshLatencyTicks, true), Is.True);
        Assert.That(harness.Service.OutstandingReplyCount, Is.EqualTo(0));
    }

    [Test]
    public void RefreshHit_DoesNotSpendThePerTickBudget()
    {
        using var harness = Open();
        var session = harness.Session;
        var nav = First(session);
        int tiles = harness.PlannerTiles;
        var covered = new byte[tiles];
        for (int i = 0; i < covered.Length; i++) covered[i] = 1;
        var goal = FirstPassable(nav);
        var cached = Field(nav.Id, goal, covered);
        cached.Refs = 0;
        harness.Planner.FlowCache.Admit(cached);

        var mask = new byte[tiles];
        int tile = NavGridSteps.TileOf(goal, nav.NavCellsPerSide, nav.Hpa!.NavCellsPerTile, nav.Hpa.NavTilesPerSide);
        mask[tile] = 1;
        var a = Dummy(session, nav, goal, mask);
        var b = Dummy(session, nav, goal, (byte[])mask.Clone());
        harness.Planner.RefreshFlow(a);
        harness.Planner.RefreshFlow(b);
        harness.Planner.ProcessRefreshes(1);
        Assert.That(harness.Planner.FlowRefreshSubmits, Is.EqualTo(0));
        Assert.That(a.Flow, Is.SameAs(cached));
        Assert.That(b.Flow, Is.SameAs(cached));
        Assert.That(cached.Refs, Is.EqualTo(2));
        Assert.That(harness.Planner.FlowCacheHits, Is.GreaterThan(0));
    }

    [Test]
    public void Stray_SubmitsOneExtensionPerTick_AndSkipsAClosedMask()
    {
        using var harness = Open();
        var session = harness.Session;
        var nav = First(session);
        int tiles = harness.PlannerTiles;
        var goal = FirstPassable(nav);
        int tile = NavGridSteps.TileOf(goal, nav.NavCellsPerSide, nav.Hpa!.NavCellsPerTile, nav.Hpa.NavTilesPerSide);
        var mask = new byte[tiles];
        mask[tile] = 1;
        var a = Dummy(session, nav, goal, mask);
        var b = Dummy(session, nav, goal, (byte[])mask.Clone());
        harness.Planner.NoteStray(a.Id, goal);
        harness.Planner.NoteStray(b.Id, goal);
        harness.Planner.ProcessRefreshes(3);
        Assert.That(harness.Planner.ExtendSubmits, Is.EqualTo(1));
        Assert.That((a.Extending ? 1 : 0) + (b.Extending ? 1 : 0), Is.EqualTo(1));

        harness.Planner.NoteStray(a.Id, goal);
        harness.Planner.NoteStray(b.Id, goal);
        harness.Planner.ProcessRefreshes(4);
        Assert.That(harness.Planner.ExtendSubmits, Is.EqualTo(2));
        Assert.That(a.Extending, Is.True);
        Assert.That(b.Extending, Is.True);

        var closed = Dummy(session, nav, goal, Full(tiles));
        int before = harness.Planner.ExtendSubmits;
        harness.Planner.NoteStray(closed.Id, goal);
        harness.Planner.ProcessRefreshes(5);
        Assert.That(harness.Planner.ExtendSubmits, Is.EqualTo(before));
        Assert.That(closed.Extending, Is.False);
    }

    [Test]
    public void StrayReply_RejectsStaleSeqAndStaleVersion_RebakeClearsExtending()
    {
        using var harness = Open();
        var session = harness.Session;
        var nav = First(session);
        int tiles = harness.PlannerTiles;
        var goal = FirstPassable(nav);
        int tile = NavGridSteps.TileOf(goal, nav.NavCellsPerSide, nav.Hpa!.NavCellsPerTile, nav.Hpa.NavTilesPerSide);
        var mask = new byte[tiles];
        mask[tile] = 1;
        var staleSeq = Dummy(session, nav, goal, mask);
        harness.Planner.NoteStray(staleSeq.Id, goal);
        harness.Planner.ProcessRefreshes(1);
        var original = staleSeq.Flow;
        staleSeq.StateSeq++;
        Assert.That(harness.Planner.ApplyDue(1 + session.Config.Planning.RefreshLatencyTicks, true), Is.True);
        Assert.That(staleSeq.Flow, Is.SameAs(original));
        Assert.That(harness.Service.OutstandingReplyCount, Is.EqualTo(0));

        var staleVersion = Dummy(session, nav, goal, (byte[])mask.Clone());
        harness.Planner.NoteStray(staleVersion.Id, goal);
        harness.Planner.ProcessRefreshes(2);
        Assert.That(staleVersion.Extending, Is.True);
        var keptVersion = staleVersion.Flow;
        nav.Version++;
        Assert.That(harness.Planner.ApplyDue(2 + session.Config.Planning.RefreshLatencyTicks, true), Is.True);
        Assert.That(staleVersion.Extending, Is.False);
        Assert.That(staleVersion.Flow, Is.SameAs(keptVersion));
        Assert.That(harness.Planner.FlowCacheContains(keptVersion!), Is.False);

        // 版本失配只把刷新放进队列。先消化掉，重烘用例只剩这一次扩展。
        harness.Planner.ProcessRefreshes(2);
        Assert.That(harness.Planner.ApplyDue(2 + session.Config.Planning.RefreshLatencyTicks, true), Is.True);
        Assert.That(harness.Service.OutstandingReplyCount, Is.EqualTo(0));

        // 上一次刷新已经按原目标准入。换一个目标，扩展才会未命中并留下在途请求。
        int other = NextPassable(nav, goal);
        int otherTile = NavGridSteps.TileOf(other, nav.NavCellsPerSide, nav.Hpa.NavCellsPerTile, nav.Hpa.NavTilesPerSide);
        var otherMask = new byte[tiles];
        otherMask[otherTile] = 1;
        var rebaked = Dummy(session, nav, other, otherMask);
        var keptRebake = rebaked.Flow;
        harness.Planner.NoteStray(rebaked.Id, other);
        harness.Planner.ProcessRefreshes(3);
        Assert.That(rebaked.Extending, Is.True);
        var dirty = new List<CrowdNavRebakeContextResult>
        {
            new() { NavId = nav.Id, DirtyTiles = new List<int> { tile }, Kind = 2 },
        };
        harness.Planner.ReactRebake(dirty);
        Assert.That(rebaked.Extending, Is.False);
        Assert.That(rebaked.Flow, Is.SameAs(keptRebake));
        Assert.That(harness.Service.OutstandingReplyCount, Is.EqualTo(0));
    }

    [Test]
    public void Validator_RejectsReleasedRefsAndACachedFlagThatIsNotInTheCache()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, 1, 0, 0), Is.EqualTo(1));
        var entity = session.Units.EntityAt(0);
        var state = session.World.Get<CrowdSimulationUnitState>(entity);
        var group = session.Groups.Groups[state.GroupId]!;
        group.Leader = new CrowdLeader(1);
        var nav = First(session);
        group.Flow = new FlowField
        {
            Integ = Array.Empty<Fix64>(),
            Len = Array.Empty<Fix64>(),
            Wp = Array.Empty<int>(),
            NavId = group.NavId,
            Goal = 0,
            Mask = new byte[4],
        };
        var refs = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(refs!.Message, Does.Contain("引用已释放"));

        group.Flow.Refs = 1;
        group.Flow.Cached = true;
        var cached = Assert.Throws<CrowdSimulationInvariantException>(() => CrowdSimulationValidator.ValidateSim(session));
        Assert.That(cached!.Message, Does.Contain("缓存标记与缓存不一致"));
        Assert.That(nav.Id, Is.EqualTo(group.NavId));
    }

    [Test]
    public void OffCorridor_ExtensionReachesTheGoal_DirectSteeringDoesNot()
    {
        using var with = Open();
        using var without = Open();
        int cm = with.Session.Config.NavCellSizeCm;
        int spawnX = CellCenter(70, cm), spawnY = CellCenter(90, cm);
        int goalX = CellCenter(96, cm), goalY = CellCenter(112, cm);
        PlaceWall(with.Session, CellCenter(96, cm), CellCenter(100, cm), 100000);
        PlaceWall(without.Session, CellCenter(96, cm), CellCenter(100, cm), 100000);
        var groupA = OrderOne(with.Session, spawnX, spawnY, goalX, goalY);
        var groupB = OrderOne(without.Session, spawnX, spawnY, goalX, goalY);
        int cell = OffCorridorCell(with.Session, groupA);
        Assert.That(cell, Is.GreaterThanOrEqualTo(0), "墙外没有贴着走廊、正对阻挡、且与目标同列的场外格");
        Park(with.Session, groupA, cell);
        Park(without.Session, groupB, cell);
        without.Session.Movement!.OnStray = null;

        int limit = 800;
        bool arrivedA = false;
        bool arrivedB = false;
        for (int i = 0; i < limit; i++)
        {
            Assert.That(with.Session.Step(out _), Is.True);
            Assert.That(without.Session.Step(out _), Is.True);
            arrivedA = Arrived(with.Session, 0);
            arrivedB = Arrived(without.Session, 0);
            if (arrivedA) break;
        }

        TestContext.Out.WriteLine(
            $"extend submits {with.Planner.ExtendSubmits} hits {with.Planner.FlowCacheHits} " +
            $"arrived {arrivedA}/{arrivedB} dist {Dist(with.Session, groupA.Goal):F0}/{Dist(without.Session, groupB.Goal):F0}");
        Assert.That(with.Planner.ExtendSubmits, Is.GreaterThan(0));
        Assert.That(arrivedA, Is.True);
        Assert.That(arrivedB, Is.False);
        Assert.That(Dist(without.Session, groupB.Goal), Is.GreaterThan(Dist(with.Session, groupA.Goal) + 4000));
    }

    private static int CellCenter(int cell, int cm) => cell * cm + cm / 2;

    private static void PlaceWall(CrowdSimSession session, int xCm, int yCm, int sizeCm)
    {
        CrowdStructureOps.PlaceStructure(session, "building", Fix64.FromInt(xCm), Fix64.FromInt(yCm), Fix64.FromInt(sizeCm), Fix64.Zero, Fix64.Zero);
        // 切片重烘要等单位出现之前收尾。生成会先增加单位数，内核静动表要到下一步才扩容。
        CrowdStructureOps.FinishRebake(session);
    }

    private static int OffCorridorCell(CrowdSimSession session, CrowdNavGroupSet.Group group)
    {
        var nav = session.ResolveNavContext(group.NavId);
        var flow = group.Flow!;
        var mask = flow.Mask!;
        int side = nav.NavCellsPerSide;
        int per = nav.Hpa!.NavCellsPerTile;
        int tiles = nav.Hpa.NavTilesPerSide;
        int best = -1;
        int bestDx = int.MaxValue;
        int bestDy = int.MaxValue;
        int gx = group.Goal % side;
        int gy = group.Goal / side;
        int off = 0, near = 0, walled = 0;
        for (int y = 1; y < side - 1; y++)
        {
            for (int x = 1; x < side - 1; x++)
            {
                int cell = y * side + x;
                if (nav.Passable[cell] == 0) continue;
                int tile = NavGridSteps.TileOf(cell, side, per, tiles);
                if (mask[tile] != 0) continue;
                off++;
                if (!TileTouchesMask(mask, tiles, tile)) continue;
                near++;
                if (!nav.CanReach(nav.Comp[cell], group.GoalComp)) continue;
                int stepX = x;
                int stepY = y;
                if (Math.Abs(gy - y) >= Math.Abs(gx - x)) stepY += Math.Sign(gy - y);
                else stepX += Math.Sign(gx - x);
                if (stepX == x && stepY == y) continue;
                if ((uint)stepX >= (uint)side || (uint)stepY >= (uint)side) continue;
                if (nav.Passable[stepY * side + stepX] != 0) continue;
                walled++;
                int dx = Math.Abs(x - gx);
                int dy = Math.Abs(y - gy);
                if (dx < bestDx || (dx == bestDx && dy < bestDy))
                {
                    bestDx = dx;
                    bestDy = dy;
                    best = cell;
                }
            }
        }

        TestContext.Out.WriteLine($"场外 {off} 贴走廊 {near} 正对阻挡 {walled} 选中 {best} 列差 {bestDx} 行差 {bestDy}");
        return bestDx == 0 ? best : -1;
    }

    private static bool TileTouchesMask(byte[] mask, int tiles, int tile)
    {
        int tx = tile % tiles;
        int ty = tile / tiles;
        for (int dy = -1; dy <= 1; dy++)
        {
            int y = ty + dy;
            if ((uint)y >= (uint)tiles) continue;
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = tx + dx;
                if ((uint)x >= (uint)tiles) continue;
                if (mask[y * tiles + x] != 0) return true;
            }
        }

        return false;
    }

    private static void Park(CrowdSimSession session, CrowdNavGroupSet.Group group, int cell)
    {
        int side = session.Config.NavCellsPerSide;
        int cm = session.Config.NavCellSizeCm;
        int x = (cell % side) * cm + cm / 2;
        int y = (cell / side) * cm + cm / 2;
        var entity = session.Units.EntityAt(0);
        session.World.Set(entity, new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(x), Fix64.FromInt(y)) });
        var kin = session.World.Get<CrowdSimulationKinematics>(entity);
        kin.Velocity = default;
        session.World.Set(entity, kin);
        var leader = group.Leader!;
        int gx = (group.Goal % side) * cm + cm / 2;
        int gy = (group.Goal / side) * cm + cm / 2;
        Set(leader, "XCm", Fix64.FromInt(gx));
        Set(leader, "YCm", Fix64.FromInt(gy));
        Set(leader, "Vx", Fix64.Zero);
        Set(leader, "Vy", Fix64.Zero);
        Set(leader, "Done", true);
    }

    private static void Set(CrowdLeader leader, string name, object value)
    {
        typeof(CrowdLeader).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(leader, value);
    }

    private static bool Arrived(CrowdSimSession session, int dense) =>
        session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(dense)).State == (byte)CrowdUnitState.Arrived;

    private static double Dist(CrowdSimSession session, int goal)
    {
        int side = session.Config.NavCellsPerSide;
        int cm = session.Config.NavCellSizeCm;
        double gx = (goal % side + 0.5) * cm;
        double gy = (goal / side + 0.5) * cm;
        var p = session.World.Get<WorldPositionCm>(session.Units.EntityAt(0)).Value;
        double dx = p.X.ToDouble() - gx;
        double dy = p.Y.ToDouble() - gy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static CrowdNavGroupSet.Group OrderOne(CrowdSimSession session, int x, int y, int goalX, int goalY)
    {
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(x), Fix64.FromInt(y), 1, 1, 0, 0), Is.EqualTo(1));
        var members = new uint[1];
        int n = session.Units.CopyOwnHandles(1, members);
        session.Commands.Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = goalX,
            YCm = goalY,
            ShapeId = "box",
            Members = members.AsSpan(0, n).ToArray(),
        }, CrowdSimCommands.Exec);
        session.Advance(session.Config.Planning.LatencyTicks + 2);
        for (int i = 0; i < session.Groups.Groups.Count; i++)
        {
            if (session.Groups.Groups[i] is { Flow: not null, Leader: not null } group) return group;
        }

        Assert.Fail("指令没有挂上流场");
        return null!;
    }

    private static CrowdNavGroupSet.Group Dummy(CrowdSimSession session, NavContext nav, int goal, byte[] mask)
    {
        var group = session.Groups.Alloc(0, 0, 0, nav.Id);
        group.Count = 1;
        group.Goal = goal;
        group.Flow = new FlowField
        {
            Integ = Array.Empty<Fix64>(),
            Len = Array.Empty<Fix64>(),
            Wp = Array.Empty<int>(),
            Mask = mask,
            NavId = nav.Id,
            Goal = goal,
        };
        return group;
    }

    private static byte[] Full(int tiles)
    {
        var mask = new byte[tiles];
        for (int i = 0; i < mask.Length; i++) mask[i] = 1;
        return mask;
    }

    private static FlowField Field(int navId, int goal, byte[] mask) => new()
    {
        Integ = Array.Empty<Fix64>(),
        Len = Array.Empty<Fix64>(),
        Wp = Array.Empty<int>(),
        Mask = mask,
        NavId = navId,
        Goal = goal,
    };

    private static NavContext First(CrowdSimSession session)
    {
        foreach (var nav in session.Navs.Values) return nav;
        throw new InvalidOperationException("会话没有导航");
    }

    private static int NextPassable(NavContext nav, int after)
    {
        for (int i = after + 1; i < nav.Passable.Length; i++)
        {
            if (nav.Passable[i] != 0) return i;
        }

        throw new InvalidOperationException("导航没有第二个可走格");
    }

    private static int FirstPassable(NavContext nav)
    {
        for (int i = 0; i < nav.Passable.Length; i++)
        {
            if (nav.Passable[i] != 0) return i;
        }

        throw new InvalidOperationException("导航没有可走格");
    }

    private static Harness Open()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        var planner = new CrowdSimPlanner(session, service);
        session.EnableMovement(CrowdMovementKernel.Create(session), planner);
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true;
        int per = session.Config.Hpa.NavCellsPerTile;
        int tilesPerSide = per > 0 ? session.Config.NavCellsPerSide / per : 1;
        return new Harness(session, service, planner, tilesPerSide * tilesPerSide);
    }

    private sealed class Harness : IDisposable
    {
        public Harness(CrowdSimSession session, PathQueryService service, CrowdSimPlanner planner, int tiles)
        {
            Session = session;
            Service = service;
            Planner = planner;
            PlannerTiles = tiles;
        }

        public CrowdSimSession Session { get; }
        public PathQueryService Service { get; }
        public CrowdSimPlanner Planner { get; }
        public int PlannerTiles { get; }
        public void Dispose() => Service.Dispose();
    }
}
