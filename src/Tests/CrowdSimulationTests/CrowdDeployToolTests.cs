using System.Linq;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 部署页只提交指令。满容量那条会按 sim.maxUnits 生成，执行当 tick 有分配。
/// </summary>
public sealed class CrowdDeployToolTests
{
    [Test]
    public void Clicks_SubmitKillSpawnAndThreeFootprints()
    {
        using var host = Open(seat: true, movement: false);
        var session = host.Session;
        var demo = host.Demo;
        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });

        demo.EnqueueTool(CrowdClickTool.Boom);
        demo.EnqueueMapClick(480000, 560000);
        demo.Tick();
        var kill = Last(session);
        Assert.That(kill.Kind, Is.EqualTo(CrowdSimCommandKind.Kill));
        Assert.That(kill.RadiusCm, Is.EqualTo(12500));
        Assert.That(kill.RingScale, Is.EqualTo(Fix64.FromInt(2)));
        Assert.That(kill.Impulse, Is.EqualTo(Fix64.FromInt(4)));
        Assert.That(demo.InteractiveMode, Is.True);

        int beforeSpawn = session.Units.Count;
        demo.EnqueueTool(CrowdClickTool.Spawn);
        demo.EnqueueMapClick(500000, 560000);
        demo.Tick();
        var spawned = Last(session);
        Assert.That(spawned.Kind, Is.EqualTo(CrowdSimCommandKind.SpawnAt));
        Assert.That(spawned.Player, Is.EqualTo(1));
        Assert.That(spawned.Count, Is.EqualTo(24));
        Assert.That(session.Units.Count, Is.GreaterThan(beforeSpawn));

        var nav = FirstNav(session);
        var (x, y) = LegalCluster(session, nav);
        int size = session.Config.NavCellSizeCm;

        demo.EnqueueTool(CrowdClickTool.Structure);
        demo.EnqueueSelectTemplate(0);
        demo.EnqueueMapClick(x, y);
        demo.Tick();
        AssertPlaced(session, "building", x, y, 0, 0, 14000);
        Assert.That(session.LastRebakeReport!.Contexts, Is.GreaterThan(session.LastRebakeReport.CostOnly));

        demo.EnqueueSelectTemplate(1);
        demo.EnqueueMapClick(x + size * 6, y);
        demo.Tick();
        AssertPlaced(session, "flood", x + size * 6, y, 0, 0, 8000);
        Assert.That(session.LastRebakeReport!.CostOnly, Is.GreaterThan(0));

        demo.EnqueueSelectTemplate(3);
        int logged = session.Commands.Log.Count;
        demo.EnqueueMapClick(x, y + size * 6);
        demo.Tick();
        Assert.That(demo.HasSpanAnchor, Is.True);
        Assert.That(session.Commands.Log.Count, Is.EqualTo(logged));
        demo.EnqueueMapClick(x + size * 4, y + size * 8);
        demo.Tick();
        Assert.That(demo.HasSpanAnchor, Is.False);
        AssertPlaced(session, "road", x, y + size * 6, x + size * 4, y + size * 8, 3000);
        Assert.That(session.LastRebakeReport!.CostOnly, Is.GreaterThan(0));

        int top = session.Structures!.EntityAt(Fix64.FromInt(x), Fix64.FromInt(y));
        demo.EnqueueTool(CrowdClickTool.Demolish);
        demo.EnqueueMapClick(x, y);
        demo.Tick();
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.RemoveStructureAt));
        Assert.That(session.Structures.EntityAt(Fix64.FromInt(x), Fix64.FromInt(y)), Is.Not.EqualTo(top));
    }

    [Test]
    public void Spawn_WithoutSeat_ThrowsAndStaysOutOfTheLog()
    {
        using var host = Open(seat: false, movement: false);
        host.Demo.EnqueueTool(CrowdClickTool.Spawn);
        host.Demo.EnqueueMapClick(480000, 560000);
        var ex = Assert.Throws<InvalidOperationException>(() => host.Demo.Tick());
        Assert.That(ex!.Message, Does.Contain("没有本机座位玩家"));
        Assert.That(host.Session.Commands.Log.Any(entry => entry.Cmd.Kind == CrowdSimCommandKind.SpawnAt), Is.False);
    }

    [Test]
    public void OffClick_SubmitsNothing_ToggleBuildSelectsStructure()
    {
        using var host = Open(seat: false, movement: false);
        int before = host.Session.Commands.Log.Count;
        host.Demo.EnqueueMapClick(1, 1);
        host.Demo.Tick();
        Assert.That(host.Session.Commands.Log.Count, Is.EqualTo(before));
        host.Demo.EnqueueToggleBuild();
        host.Demo.Tick();
        Assert.That(host.Demo.Tool, Is.EqualTo(CrowdClickTool.Structure));
        host.Demo.EnqueueToggleBuild();
        host.Demo.Tick();
        Assert.That(host.Demo.Tool, Is.EqualTo(CrowdClickTool.Off));
    }

    [Test]
    public void Page_DeployScatterClearCachesAndRetunesAvoidance()
    {
        using var host = Open(seat: false, movement: true);
        var session = host.Session;
        var beforeHz = session.AvoidHz;
        int beforeStride = session.Movement!.Stride;

        host.Demo.EnqueueDeploy();
        host.Demo.Tick();
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.Spawn));
        Assert.That(Last(session).Count, Is.EqualTo(24));
        Assert.That(session.Units.Count, Is.GreaterThan(0));

        host.Demo.EnqueueScatter();
        host.Demo.Tick();
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.Scatter));
        Assert.That(session.Orders.List, Is.Not.Empty);

        host.Demo.EnqueueClearCaches();
        host.Demo.Tick();
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.ClearCaches));

        host.Demo.EnqueueAvoid(1);
        host.Demo.Tick();
        var rates = Last(session);
        Assert.That(rates.Kind, Is.EqualTo(CrowdSimCommandKind.Rates));
        Assert.That(rates.TickHz, Is.EqualTo(session.Config.FixedHz));
        Assert.That(rates.AvoidHz, Is.EqualTo(beforeHz + Fix64.OneValue));
        Assert.That(session.AvoidHz, Is.EqualTo(rates.AvoidHz));
        Assert.That(session.Movement.Stride, Is.EqualTo(
            CrowdMovementKernel.AvoidanceStride(session.Config.FixedHz * session.SubSteps, rates.AvoidHz.ToDouble())));
        Assert.That(session.Movement.Phase, Is.EqualTo(0));
        Assert.That(session.SimDt.RawValue, Is.EqualTo(host.SimDt));
        Assert.That(session.SubSteps, Is.EqualTo(host.SubSteps));
        Assert.That(beforeStride, Is.GreaterThan(0));

        int stepHz = session.Config.FixedHz * session.SubSteps;
        Fix64 cap = Fix64.FromInt(Math.Min(stepHz, 30));
        session.SetAvoidHz(cap);
        int logged = session.Commands.Log.Count;
        host.Demo.EnqueueAvoid(1);
        host.Demo.Tick();
        Assert.That(session.Commands.Log.Count, Is.EqualTo(logged));
        Assert.That(session.AvoidHz, Is.EqualTo(cap));
    }

    [Test]
    public void Pause_StillAcceptsCommands_SpeedUsesTimeFlow()
    {
        using var host = Open(seat: false, movement: false);
        var session = host.Session;
        int tick = session.TickCount;
        host.Demo.EnqueuePause();
        host.Demo.EnqueueTool(CrowdClickTool.Boom);
        host.Demo.EnqueueMapClick(480000, 560000);
        host.Demo.Tick();
        Assert.That(host.Demo.PlaybackHeld, Is.True);
        Assert.That(session.PlaybackHeld, Is.True);
        Assert.That(session.TickCount, Is.EqualTo(tick));
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.Kill));

        var flow = host.Engine.GetService(CoreServiceKeys.TimeFlow)!;
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(1000));
        host.Demo.EnqueueSpeed(1);
        host.Demo.Tick();
        Assert.That(host.Demo.SpeedPermille, Is.EqualTo(2000));
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(2000));
        host.Demo.EnqueueSpeed(-1);
        host.Demo.Tick();
        Assert.That(host.Demo.SpeedPermille, Is.EqualTo(1000));
        Assert.That(flow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(1000));
        host.Demo.ReleasePlayback();
    }

    [Test]
    public void Stress_SubmitsSpawnAtMaxUnits()
    {
        using var host = Open(seat: false, movement: false);
        var session = host.Session;
        host.Demo.EnqueueStress();
        host.Demo.Tick();
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.Spawn));
        Assert.That(Last(session).Count, Is.EqualTo(session.Config.Sim.MaxUnits));
        Assert.That(session.Units.Count + session.SpawnSkippedTotal, Is.EqualTo(session.Config.Sim.MaxUnits));
    }

    [Test]
    public void Hud_ShowsToolLineAndRebakeReport()
    {
        using var host = Open(seat: false, movement: false);
        var session = host.Session;
        var presentation = new CrowdPresentationConfig
        {
            Kind = CrowdPresentationConfig.KindDeploy,
            Title = "S4",
            ViewLabels = new[] { "路线" },
            Hud = new CrowdHudPanel
            {
                OriginXPx = 16,
                OriginYPx = 16,
                WidthPx = 640,
                HeightPx = 176,
                Keys = "左键 {tool} · {activity}{pending}",
            },
            Deploy = new CrowdDeployPresentation
            {
                Actions = new CrowdDeployActions
                {
                    CycleView = "a", ToggleBuild = "b", Remove = "c", Place = "d",
                    ToolOff = "e", ToolBoom = "f", ToolSpawn = "g", ToolStructure = "h", ToolDemolish = "i",
                    Deploy = "j", Scatter = "k", Stress = "l", ClearCaches = "m",
                    AvoidDown = "n", AvoidUp = "o", SliceDown = "s", SliceUp = "u", Pause = "p", SpeedDown = "q", SpeedUp = "r",
                },
                BuildActivity = "建造 · {label}{placeHint}",
                ObserveActivity = "观察",
                PlaceOk = "可放",
                PlaceBlocked = "不可放",
                PlaceOffMap = "界外",
                PendingNav = "待导航",
                Structures = new[]
                {
                    new CrowdStructureTemplateDto
                    {
                        Id = "building", Label = "建筑", Shape = S4DeployDemoRuntime.ShapePoint, SizeCm = 14000, SelectAction = "t",
                    },
                },
            },
        };
        var overlay = new ScreenOverlayBuffer();
        var system = new S4DeployDemoPresentationSystem(
            host.Demo, new RouteVisualBuffer(), overlay, () => null, () => session, presentation);
        overlay.Clear();
        system.DrawHud(session, null, null);
        string idle = Join(overlay);
        Assert.That(idle, Does.Contain("工具 关闭"));
        Assert.That(idle, Does.Contain(host.Demo.TickHzWindow));
        Assert.That(idle, Does.Contain("×1"));

        Submit(session, new CrowdSimCommand { Kind = CrowdSimCommandKind.RebakeSlices, Slices = 1 });
        host.Demo.EnqueueTool(CrowdClickTool.Structure);
        var nav = FirstNav(session);
        int size = session.Config.NavCellSizeCm;
        var (x, y) = LegalCluster(session, nav);
        host.Demo.EnqueueMapClick(x, y);
        host.Demo.Tick();
        overlay.Clear();
        system.DrawHud(session, null, null);
        string after = Join(overlay);
        Assert.That(after, Does.Contain("工具 建筑"));
        Assert.That(after, Does.Contain("脏 tile"));
        Assert.That(after, Does.Contain("仅代价"));
        Assert.That(after, Does.Contain("可通行"));
        Assert.That(after, Does.Contain("耗时"));
        Assert.That(after, Does.Contain("左键 建筑"));
        Assert.That(after, Does.Contain("切片"));
    }

    [Test]
    public void SliceKeys_SubmitRebakeSlicesWithinMax()
    {
        using var host = Open(seat: false, movement: false);
        var session = host.Session;
        int max = CrowdSimulationConfigValidator.MaxRebakeSlices;
        int start = session.RebakeSliceCount;
        Assert.That(start, Is.InRange(1, max));
        host.Demo.EnqueueSlices(1);
        host.Demo.Tick();
        if (start == max)
        {
            Assert.That(session.Commands.Log, Is.Empty);
            Assert.That(session.RebakeSliceCount, Is.EqualTo(max));
        }

        for (int i = 0; i < max + 2; i++)
        {
            host.Demo.EnqueueSlices(-1);
            host.Demo.Tick();
        }

        Assert.That(session.RebakeSliceCount, Is.EqualTo(1));
        int logged = session.Commands.Log.Count;
        host.Demo.EnqueueSlices(-1);
        host.Demo.Tick();
        Assert.That(session.RebakeSliceCount, Is.EqualTo(1));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(logged));
        host.Demo.EnqueueSlices(1);
        host.Demo.Tick();
        Assert.That(session.RebakeSliceCount, Is.EqualTo(2));
        Assert.That(Last(session).Kind, Is.EqualTo(CrowdSimCommandKind.RebakeSlices));
        Assert.That(Last(session).Slices, Is.EqualTo(2));
    }

    private static void AssertPlaced(CrowdSimSession session, string template, int x, int y, int toX, int toY, int size)
    {
        var cmd = Last(session);
        Assert.That(cmd.Kind, Is.EqualTo(CrowdSimCommandKind.PlaceStructure));
        Assert.That(cmd.TemplateId, Is.EqualTo(template));
        Assert.That(cmd.XCm, Is.EqualTo(x));
        Assert.That(cmd.YCm, Is.EqualTo(y));
        Assert.That(cmd.ToXCm, Is.EqualTo(toX));
        Assert.That(cmd.ToYCm, Is.EqualTo(toY));
        Assert.That(cmd.SizeCm, Is.EqualTo(size));
        Assert.That(session.LastRebakeReport, Is.Not.Null);
    }

    private static CrowdSimCommand Last(CrowdSimSession session) => session.Commands.Log[^1].Cmd;

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

    private static string Join(ScreenOverlayBuffer overlay)
    {
        var parts = new List<string>();
        foreach (var item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text) continue;
            string? text = overlay.GetString(item.StringId);
            if (text != null) parts.Add(text);
        }

        return string.Join('\n', parts);
    }

    private static CrowdDebugTools Tools() => new()
    {
        BoomRadiusCm = 12500,
        RingScale = 2,
        Impulse = 4,
        SpawnCount = 24,
        SpawnUnitType = 0,
        SpawnRadiusClass = 0,
        PlaceTemplate = "building",
        RectSizeCm = 14000,
        DiscRadiusCm = 8000,
        PathWidthCm = 3000,
        SpeedStepsPermille = new[] { 500, 1000, 2000, 4000 },
    };

    private static CrowdDebugRates Rates() => new()
    {
        TickHzMin = 10,
        TickHzMax = 60,
        AvoidHzMin = 1,
        AvoidHzMax = 30,
        AvoidStep = 1,
    };

    private static Host Open(bool seat, bool movement)
    {
        var (config, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        PathQueryService? service = null;
        if (movement)
        {
            service = new PathQueryService(session.Navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
            session.BlockOnDueReplies = true;
        }

        var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.CrowdSimulationSession, session);
        engine.SetService(CoreServiceKeys.CrowdSimulationRuntime, new CrowdSimulationRuntime());
        engine.SetService(CoreServiceKeys.TimeFlow, new TimeFlowService());
        if (seat)
        {
            var seats = new ClientLocalSeatRegistry();
            var local = new ClientLocalSeat("local") { PossessedPlayerId = 1 };
            seats.Add(local);
            engine.SetService(CoreServiceKeys.ClientLocalSeatRegistry, seats);
        }

        var demo = new S4DeployDemoRuntime(engine);
        demo.UseViewLabels(new[] { "路线" });
        demo.UseStructures(new[]
        {
            new S4DeployDemoRuntime.StructureChoice("building", "建筑", S4DeployDemoRuntime.ShapePoint, 14000, 0, "t1"),
            new S4DeployDemoRuntime.StructureChoice("flood", "泛洪", S4DeployDemoRuntime.ShapeDisc, 8000, 0, "t2"),
            new S4DeployDemoRuntime.StructureChoice("road", "道路", S4DeployDemoRuntime.ShapeRoad, 3000, 25000, "t3"),
            new S4DeployDemoRuntime.StructureChoice("road", "连线", S4DeployDemoRuntime.ShapeSpan, 3000, 0, "t4"),
        });
        demo.UseDebugTools(Tools(), Rates());
        demo.ArmSession(session);
        return new Host(engine, session, demo, service, session.SimDt.RawValue, session.SubSteps);
    }

    private sealed class Host : IDisposable
    {
        public Host(GameEngine engine, CrowdSimSession session, S4DeployDemoRuntime demo, PathQueryService? service, long simDt, int subSteps)
        {
            Engine = engine;
            Session = session;
            Demo = demo;
            Service = service;
            SimDt = simDt;
            SubSteps = subSteps;
        }

        public GameEngine Engine { get; }
        public CrowdSimSession Session { get; }
        public S4DeployDemoRuntime Demo { get; }
        public PathQueryService? Service { get; }
        public long SimDt { get; }
        public int SubSteps { get; }

        public void Dispose()
        {
            Service?.Dispose();
            Engine.Dispose();
        }
    }
}
