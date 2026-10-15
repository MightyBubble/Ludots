using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 调试指令走同一条指令日志。kill 的销毁走 ReleaseDense。
/// scatter 的 Issue、clearCaches 的刷新请求只发生在该指令执行的 tick。
/// </summary>
public sealed class CrowdDebugCommandTests
{
    [Test]
    public void Kill_RemovesInsideRadius_PushesRing_RecyclesHandle()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0), Is.EqualTo(3));
        const int cx = 480000;
        const int cy = 560000;
        Place(session, 0, cx, cy);
        Place(session, 1, cx + 1500, cy);
        Place(session, 2, cx + 4000, cy);
        uint inside = session.Units.HandleAt(0);
        uint ring = session.Units.HandleAt(1);
        uint outside = session.Units.HandleAt(2);
        int groupId = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0)).GroupId;
        int before = session.Groups.Groups[groupId]!.Count;

        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = cx,
            YCm = cy,
            RadiusCm = 1000,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.FromInt(4),
        });

        Assert.That(session.Units.Count, Is.EqualTo(2));
        Assert.That(session.Units.TryResolveHandle(inside, out _), Is.False);
        Assert.That(session.Units.TryResolveHandle(ring, out int ringDense), Is.True);
        Assert.That(session.Units.TryResolveHandle(outside, out int outsideDense), Is.True);
        var pushed = session.World.Get<CrowdSimulationKinematics>(session.Units.EntityAt(ringDense)).Velocity;
        var still = session.World.Get<CrowdSimulationKinematics>(session.Units.EntityAt(outsideDense)).Velocity;
        Assert.That(pushed.X > Fix64.Zero, Is.True);
        Assert.That(pushed.Y, Is.EqualTo(Fix64.Zero));
        Assert.That(still.X, Is.EqualTo(Fix64.Zero));
        Assert.That(still.Y, Is.EqualTo(Fix64.Zero));
        Assert.That(session.Groups.Groups[groupId]!.Count, Is.EqualTo(before - 1));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));

        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(cx), Fix64.FromInt(cy), 1, 1, 0, 0), Is.EqualTo(1));
        Assert.That(session.Units.TryResolveHandle(inside, out _), Is.False);
    }

    [Test]
    public void Scatter_SameSeed_SameGoals()
    {
        int[] first = ScatterGoals();
        int[] second = ScatterGoals();
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Is.Not.Empty);
        Assert.That(first, Has.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Rates_RejectsForeignTickHz_AndRetunesAvoidanceStride()
    {
        using var moving = Moving("s7");
        var session = moving.Session;
        var simDt = session.SimDt.RawValue;
        int subSteps = session.SubSteps;
        int fixedHz = session.Config.FixedHz;
        int stride = session.Movement!.Stride;
        int stepHz = fixedHz * subSteps;

        var bad = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Rates,
            TickHz = fixedHz + 1,
            AvoidHz = Fix64.FromInt(1),
        };
        Assert.Throws<InvalidOperationException>(() => Submit(session, bad));
        Assert.That(session.Commands.Log, Is.Empty);
        Assert.That(session.Movement.Stride, Is.EqualTo(stride));
        Assert.That(session.SimDt.RawValue, Is.EqualTo(simDt));

        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Rates,
            TickHz = fixedHz,
            AvoidHz = Fix64.FromInt(1),
        });
        Assert.That(session.Movement.Stride, Is.EqualTo(stepHz));
        Assert.That(session.Movement.Stride, Is.Not.EqualTo(stride));
        Assert.That(session.Movement.Phase, Is.EqualTo(0));
        Assert.That(session.SimDt.RawValue, Is.EqualTo(simDt));
        Assert.That(session.SubSteps, Is.EqualTo(subSteps));
        Assert.That(session.Config.FixedHz, Is.EqualTo(fixedHz));
        Assert.That(session.AvoidHz, Is.EqualTo(Fix64.FromInt(1)));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
    }

    [Test]
    public void RebakeSlices_OneFinishesInsideCommand_MoreSpreadsLaterTicks()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });
        Submit(session, Place("building", 480000, 560000, 14000));
        Assert.That(session.RebakeJob, Is.Null);
        Assert.That(session.LastRebakeReport, Is.Not.Null);
        Assert.That(session.LastRebakeReport!.ElapsedTicks, Is.GreaterThan(0));

        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 3 });
        Submit(session, Place("building", 520000, 600000, 14000));
        Assert.That(session.RebakeJob, Is.Not.Null);
        int guard = 0;
        while (session.RebakeJob != null)
        {
            Assert.That(session.Step(out _), Is.True);
            Assert.That(++guard, Is.LessThan(8));
        }

        Assert.That(session.LastRebakeReport!.ElapsedTicks, Is.GreaterThan(0));
        session.Reset();
        Assert.That(session.RebakeSliceCount, Is.EqualTo(session.Config.Planning.RebakeSlices));
    }

    [Test]
    public void RebakeSlices_IdleBetweenSteps_IsNotWorkTime()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 3 });
        Submit(session, Place("building", 480000, 560000, 14000));
        Assert.That(session.RebakeJob, Is.Not.Null);
        var clock = Stopwatch.StartNew();
        int sleeps = 0;
        while (session.RebakeJob != null)
        {
            Thread.Sleep(300);
            sleeps++;
            Assert.That(session.Step(out _), Is.True);
            Assert.That(sleeps, Is.LessThan(8));
        }

        clock.Stop();
        double workMs = session.LastRebakeReport!.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        TestContext.Out.WriteLine($"rebake workMs={workMs:0.###} wallMs={clock.Elapsed.TotalMilliseconds:0.###} sleeps={sleeps}");
        Assert.That(sleeps, Is.GreaterThanOrEqualTo(2));
        Assert.That(workMs, Is.GreaterThan(0));
        Assert.That(workMs * 4, Is.LessThan(clock.Elapsed.TotalMilliseconds));
    }

    [Test]
    public void PendingCommandPayload_ChangesCommandsDiagnostic()
    {
        using var moving = Moving("s1337");
        var session = moving.Session;
        session.DiagnosticDigest = true;
        var kill = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = 480000,
            YCm = 560000,
            RadiusCm = 1000,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.FromInt(4),
        };
        ulong radiusA = PendingCommands(session, kill);
        ulong radiusB = PendingCommands(session, kill with { RadiusCm = 3000 });
        ulong ring = PendingCommands(session, kill with { RingScale = Fix64.FromInt(3) });
        ulong impulse = PendingCommands(session, kill with { Impulse = Fix64.FromInt(5) });
        Assert.That(radiusB, Is.Not.EqualTo(radiusA));
        Assert.That(ring, Is.Not.EqualTo(radiusA));
        Assert.That(impulse, Is.Not.EqualTo(radiusA));

        int fixedHz = session.Config.FixedHz;
        var rates = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Rates,
            TickHz = fixedHz,
            AvoidHz = Fix64.FromInt(15),
        };
        ulong avoidA = PendingCommands(session, rates);
        ulong avoidB = PendingCommands(session, rates with { AvoidHz = Fix64.FromInt(10) });
        Assert.That(avoidB, Is.Not.EqualTo(avoidA));

        ulong slicesA = PendingCommands(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });
        ulong slicesB = PendingCommands(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 3 });
        Assert.That(slicesB, Is.Not.EqualTo(slicesA));

        ulong scatter = PendingCommands(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.Scatter });
        ulong clear = PendingCommands(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.ClearCaches });
        Assert.That(clear, Is.Not.EqualTo(scatter));
    }

    private static ulong PendingCommands(CrowdSimSession session, CrowdSimCommand command)
    {
        session.Reset();
        session.DiagnosticDigest = true;
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(session.TickCount + 1000, command),
        });
        Assert.That(session.Step(out _), Is.True);
        Assert.That(session.Commands.PendingCount, Is.EqualTo(1));
        return session.LastDigest.Commands;
    }

    [Test]
    public void Footprints_RectDiscAndPath_ChangeWalkabilityAfterRebake()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });
        var nav = FirstNav(session);
        var (x, y) = LegalCluster(session, nav);
        int size = session.Config.NavCellSizeCm;

        AssertWalkChanged(session, nav, Place("building", x, y, 14000));
        AssertWalkChanged(session, nav, Place("flood", x + size * 6, y, 8000));
        AssertWalkChanged(session, nav, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "road",
            XCm = x,
            YCm = y + size * 6,
            SizeCm = 3000,
            ToXCm = x + size * 4,
            ToYCm = y + size * 8,
        });
    }

    [Test]
    public void RemoveStructureAt_TakesTopmost_IncludingMapBlockerAndBridge()
    {
        var surface = Parity.S1SurfaceTruthTests.LoadMapSurface("s7");
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(surface.Blockers, Is.Not.Empty);
        var blocker = surface.Blockers[0];
        int mapId = session.Structures!.EntityAt(Fix64.FromInt(blocker.XCm), Fix64.FromInt(blocker.YCm));
        Assert.That(mapId, Is.GreaterThanOrEqualTo(0));

        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });
        Submit(session, Place("building", blocker.XCm, blocker.YCm, 4000));
        int placed = session.Structures.EntityAt(Fix64.FromInt(blocker.XCm), Fix64.FromInt(blocker.YCm));
        Assert.That(placed, Is.Not.EqualTo(mapId));
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = blocker.XCm,
            YCm = blocker.YCm,
        });
        Assert.That(session.Structures.EntityAt(Fix64.FromInt(blocker.XCm), Fix64.FromInt(blocker.YCm)), Is.EqualTo(mapId));
        Assert.That(session.LastRebakeReport!.Kind, Is.EqualTo(CrowdRebakeReport.KindRemove));

        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = blocker.XCm,
            YCm = blocker.YCm,
        });
        Assert.That(session.Structures.EntityAt(Fix64.FromInt(blocker.XCm), Fix64.FromInt(blocker.YCm)), Is.EqualTo(-1));

        Assert.That(surface.Bridges, Is.Not.Empty);
        var span = surface.Bridges[0].Span;
        int bx = (span.X0Cm + span.X1Cm) / 2;
        int by = (span.Y0Cm + span.Y1Cm) / 2;
        int bridge = session.Structures.EntityAt(Fix64.FromInt(bx), Fix64.FromInt(by));
        Assert.That(bridge, Is.GreaterThanOrEqualTo(0));
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RemoveStructureAt, XCm = bx, YCm = by });
        Assert.That(session.Structures.EntityAt(Fix64.FromInt(bx), Fix64.FromInt(by)), Is.Not.EqualTo(bridge));
    }

    [Test]
    public void ClearCaches_RebuildsFlowAfterLaterTicks()
    {
        using var moving = Moving("s1337");
        var session = moving.Session;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 8, 1, 0, 0), Is.EqualTo(8));
        int members = session.Units.CopyOwnHandles(1, session.MemberCapture);
        var handles = session.MemberCapture.AsSpan(0, members).ToArray();
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = 800000,
            YCm = 800000,
            ShapeId = "box",
            Members = handles,
        });
        session.Advance(40);
        var group = GroupWithFlow(session);
        var before = group.Flow;
        int revision = before!.BuildRevision;
        int submits = session.Planner!.FlowRefreshSubmits;
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.ClearCaches });
        int wait = session.Config.Planning.RefreshLatencyTicks + session.Config.Planning.MaxRefreshesPerTick + 4;
        session.Advance(wait);
        group = session.Groups.Groups[group.Id]!;
        Assert.That(session.Planner.FlowRefreshSubmits, Is.GreaterThan(submits));
        Assert.That(group.Flow, Is.Not.Null);
        bool rebuilt = !ReferenceEquals(group.Flow, before) || group.Flow!.BuildRevision > revision;
        Assert.That(rebuilt, Is.True);
    }

    [Test]
    public void Script_TwiceAndReplay_MatchTickChecksums()
    {
        var live = RunScript();
        var again = RunScript();
        Assert.That(again, Is.EqualTo(live));

        using var moving = Moving("s1337");
        var session = moving.Session;
        var script = Script();
        script.Advance(session, live.Count, null);
        var log = session.Commands.Log.Select(entry => new CrowdCommand(entry.Tick, entry.Cmd)).ToArray();
        using var replay = Moving("s1337");
        replay.Session.Commands.Schedule(replay.Session, log);
        var hashes = new List<ulong>();
        replay.Session.Advance(live.Count, hashes);
        Assert.That(hashes, Is.EqualTo(live));
    }

    [Test]
    public void PlaybackHeld_FlushesDueOrders_WithoutAdvancingTick()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0), Is.EqualTo(4));
        int members = session.Units.CopyOwnHandles(1, session.MemberCapture);
        var handles = session.MemberCapture.AsSpan(0, members).ToArray();
        session.PlaybackHeld = true;
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(session.TickCount, new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.Order,
                Player = 1,
                XCm = 700000,
                YCm = 700000,
                ShapeId = "box",
                Members = handles,
            }),
        });
        session.FlushDueCommands();
        Assert.That(session.TickCount, Is.EqualTo(0));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        Assert.That(session.Orders.List, Is.Not.Empty);
    }

    private static List<ulong> RunScript()
    {
        using var moving = Moving("s1337");
        var hashes = new List<ulong>();
        Script().Advance(moving.Session, 30, hashes);
        return hashes;
    }

    private static CrowdReferenceScript Script()
    {
        var script = new JsonArray
        {
            new JsonObject
            {
                ["tick"] = 0,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "spawnAt", ["player"] = 1, ["xCm"] = 480000, ["yCm"] = 560000,
                    ["count"] = 12, ["unitType"] = 0, ["rIdx"] = 0,
                },
            },
            new JsonObject
            {
                ["tick"] = 1,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "kill", ["xCm"] = 480000, ["yCm"] = 560000,
                    ["radiusCm"] = 2000, ["ringScale"] = 2, ["impulse"] = 4,
                },
            },
            new JsonObject { ["tick"] = 2, ["cmd"] = new JsonObject { ["type"] = "scatter" } },
            new JsonObject
            {
                ["tick"] = 3,
                ["cmd"] = new JsonObject { ["type"] = "rates", ["tickHz"] = 30, ["avoidHz"] = 1 },
            },
            new JsonObject { ["tick"] = 4, ["cmd"] = new JsonObject { ["type"] = "clearCaches" } },
            new JsonObject { ["tick"] = 5, ["cmd"] = new JsonObject { ["type"] = "rebakeSlices", ["slices"] = 1 } },
            new JsonObject
            {
                ["tick"] = 6,
                ["cmd"] = new JsonObject
                {
                    ["type"] = "placeStructure", ["template"] = "building",
                    ["xCm"] = 503125, ["yCm"] = 565625, ["sizeCm"] = 14000,
                },
            },
        };
        return CrowdReferenceScript.Load(script, 64);
    }

    private static int[] ScatterGoals()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        CrowdDeployment.Spawn(session, 24);
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.Scatter });
        var goals = new int[session.Orders.List.Count];
        for (int i = 0; i < goals.Length; i++) goals[i] = session.Orders.List[i].ClickCell;
        return goals;
    }

    private static void AssertWalkChanged(CrowdSimSession session, NavContext nav, CrowdSimCommand place)
    {
        var passable = (byte[])nav.Passable.Clone();
        var cost = new long[nav.Cost.Length];
        for (int i = 0; i < cost.Length; i++) cost[i] = nav.Cost[i].RawValue;
        Submit(session, place);
        var report = session.LastRebakeReport;
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Contexts, Is.GreaterThan(0));
        bool passableChanged = false;
        bool costChanged = false;
        for (int i = 0; i < passable.Length; i++)
        {
            if (nav.Passable[i] != passable[i]) passableChanged = true;
            if (nav.Cost[i].RawValue != cost[i]) costChanged = true;
        }

        Assert.That(passableChanged || costChanged, Is.True,
            $"{place.TemplateId} @ {place.XCm},{place.YCm}→{place.ToXCm},{place.ToYCm} reject={session.LastPlacementReject}");
        if (place.TemplateId == "building") Assert.That(report.Contexts, Is.GreaterThan(report.CostOnly));
        else Assert.That(report.CostOnly, Is.GreaterThan(0));
    }

    private static CrowdSimCommand Place(string template, int x, int y, int size) => new()
    {
        Kind = CrowdSimCommandKind.PlaceStructure,
        TemplateId = template,
        XCm = x,
        YCm = y,
        SizeCm = size,
    };

    private static void Place(CrowdSimSession session, int dense, int x, int y)
    {
        session.World.Set(session.Units.EntityAt(dense), new WorldPositionCm { Value = new Fix64Vec2(Fix64.FromInt(x), Fix64.FromInt(y)) });
    }

    private static void Submit(CrowdSimSession session, CrowdSimCommand command) =>
        session.Commands.Submit<object?>(session, command, CrowdSimCommands.Exec);

    private static NavContext FirstNav(CrowdSimSession session)
    {
        foreach (var nav in session.Navs.Values) return nav;
        throw new InvalidOperationException("会话没有导航。");
    }

    private static (int X, int Y) LegalCluster(CrowdSimSession session, NavContext nav)
    {
        var store = session.Structures ?? throw new InvalidOperationException("没有结构仓。");
        int cell = session.Config.NavCellSizeCm;
        int side = nav.NavCellsPerSide;
        var building = TemplateOf(session, "building");
        var flood = TemplateOf(session, "flood");
        var road = TemplateOf(session, "road");
        var cells = System.Array.Empty<int>();
        var flags = System.Array.Empty<byte>();
        for (int gy = 0; gy < side; gy++)
        {
            for (int gx = 0; gx < side; gx++)
            {
                if (nav.Passable[gy * side + gx] == 0) continue;
                int x = gx * cell + cell / 2;
                int y = gy * cell + cell / 2;
                if (!Accepts(store, building, x, y, 14000, 0, 0, ref cells, ref flags, out _)) continue;
                if (!Accepts(store, flood, x + cell * 6, y, 8000, 0, 0, ref cells, ref flags, out int floodCount)) continue;
                if (!ShiftsCost(session, nav, flood, cells, floodCount)) continue;
                if (!Accepts(store, road, x, y + cell * 6, 3000, x + cell * 4, y + cell * 8, ref cells, ref flags, out int roadCount)) continue;
                if (!ShiftsCost(session, nav, road, cells, roadCount)) continue;
                if (HitsPlanned(store, building, flood, x, y, cell, side, cells, roadCount)) continue;
                return (x, y);
            }
        }

        throw new InvalidOperationException("没有能同时放下建筑、泛洪和道路的点。");
    }

    private static RuntimeStructureTemplate TemplateOf(CrowdSimSession session, string id)
    {
        int index = session.Structures!.TemplateIndexOf(id);
        if (index < 0) throw new InvalidOperationException($"没有结构模板 {id}。");
        return session.Config.Structures.Templates[index];
    }

    private static bool Accepts(
        CrowdStructuresStore store,
        RuntimeStructureTemplate template,
        int x,
        int y,
        int size,
        int toX,
        int toY,
        ref int[] cells,
        ref byte[] flags,
        out int count)
    {
        var footprint = CrowdStructureFootprint.Make(
            template, Fix64.FromInt(x), Fix64.FromInt(y), Fix64.FromInt(size), Fix64.FromInt(toX), Fix64.FromInt(toY));
        bool legal = CrowdStructurePlacement.Judge(store, template, footprint, ref cells, ref flags, out count) == CrowdPlacementReject.None;
        return legal && count > 0;
    }

    private static bool ShiftsCost(CrowdSimSession session, NavContext nav, RuntimeStructureTemplate template, int[] cells, int count)
    {
        if (template.AreaIndex is not int area) return false;
        var row = session.Config.AgentTypes[nav.LayerIndex].CostByArea;
        if ((uint)area >= (uint)row.Length) return false;
        var want = row[area];
        for (int i = 0; i < count; i++)
        {
            if (nav.Cost[cells[i]] != want) return true;
        }

        return false;
    }

    private static bool HitsPlanned(
        CrowdStructuresStore store,
        RuntimeStructureTemplate building,
        RuntimeStructureTemplate flood,
        int x,
        int y,
        int cell,
        int side,
        int[] painted,
        int count)
    {
        var box = CrowdStructureFootprint.Make(building, Fix64.FromInt(x), Fix64.FromInt(y), Fix64.FromInt(14000), Fix64.Zero, Fix64.Zero);
        var disc = CrowdStructureFootprint.Make(flood, Fix64.FromInt(x + cell * 6), Fix64.FromInt(y), Fix64.FromInt(8000), Fix64.Zero, Fix64.Zero);
        for (int i = 0; i < count; i++)
        {
            int cx = painted[i] % side;
            int cy = painted[i] / side;
            if (store.BaseLayerCellHit(building, box, cx, cy) || store.BaseLayerCellHit(flood, disc, cx, cy)) return true;
        }

        return false;
    }

    private static CrowdNavGroupSet.Group GroupWithFlow(CrowdSimSession session)
    {
        var groups = session.Groups.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i] is { Flow: not null } group) return group;
        }

        throw new InvalidOperationException("没有流场。");
    }

    private static MovingSession Moving(string seed)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession(seed);
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        return new MovingSession(session, service);
    }

    private sealed class MovingSession : IDisposable
    {
        public MovingSession(CrowdSimSession session, PathQueryService service)
        {
            Session = session;
            Service = service;
        }

        public CrowdSimSession Session { get; }
        public PathQueryService Service { get; }
        public void Dispose() => Service.Dispose();
    }
}
