using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using CrowdSimulationUiMod.UI;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public class CrowdTelemetryPanelTests
{
    [Test]
    public void ShippedView_KeepsOnlyPanelTogglesAndActions()
    {
        var cfg = LoadShipped();
        Assert.That(cfg.Panels.Nav, Is.False);
        Assert.That(cfg.Panels.Telemetry, Is.True, "原型右栏常驻遥测");
        Assert.That(cfg.Actions.ToggleNavPanel, Is.EqualTo("CrowdSimulation.S4.ToggleNavPanel"));
        Assert.That(cfg.Actions.ProbePathFault, Is.EqualTo("CrowdSimulation.S4.ProbePathFault"));
        Assert.That(Shipped().ContainsKey("telemetry"), Is.False, "采样与告警归 UI mod 的 hud.json");
    }

    [Test]
    public void ShippedHudSettings_LoadSampleRatesHistoryAndAlerts()
    {
        var settings = ParseHud(Hud());
        Assert.That(settings.SampleMs.Telemetry, Is.EqualTo(250));
        Assert.That(settings.SampleMs.Nav, Is.EqualTo(500));
        Assert.That(settings.SampleMs.Status, Is.EqualTo(250));
        Assert.That(settings.History, Is.EqualTo(90), "照原型 debugView.js telemetry.history");
        Assert.That(settings.Alerts.LowFps, Is.EqualTo(30));
        Assert.That(settings.Alerts.SimMs, Is.EqualTo(16));
        Assert.That(settings.Alerts.AvoidMs, Is.EqualTo(12));
        Assert.That(settings.Alerts.HeadingDeg, Is.EqualTo(8));
        Assert.That(settings.Alerts.FlipPct, Is.EqualTo(10));
    }

    [Test]
    public void MissingHudField_NamesPathAndField()
    {
        var node = Hud();
        node["sampleMs"]!.AsObject().Remove("telemetry");
        var ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain(CrowdHudSettings.RelativePath));
        Assert.That(ex.Message, Does.Contain("telemetry"));
    }

    [Test]
    public void UnknownHudKey_NamesField()
    {
        var node = Hud();
        node["sampleMs"]!.AsObject()["extra"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("extra"));
    }

    [Test]
    public void HistoryOutOfRange_NamesField()
    {
        var node = Hud();
        node["history"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("history"));

        node = Hud();
        node["history"] = CrowdHudSettings.MaxHistory + 1;
        ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("history"));
    }

    [Test]
    public void SampleRateOutOfRange_NamesField()
    {
        var node = Hud();
        node["sampleMs"]!["nav"] = 0;
        var ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("sampleMs.nav"));
    }

    [Test]
    public void AlertOutOfRange_NamesField()
    {
        var node = Hud();
        node["alerts"]!["flipPct"] = 0;
        var ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("flipPct"));

        node = Hud();
        node["alerts"]!["headingDeg"] = 181;
        ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("headingDeg"));

        node = Hud();
        node["alerts"]!["simMs"] = -1;
        ex = Assert.Throws<InvalidOperationException>(() => ParseHud(node));
        Assert.That(ex!.Message, Does.Contain("simMs"));
    }

    [Test]
    public void NavText_ShowsCacheCountsAndFlowMissAsUnavailable()
    {
        var reading = new CrowdNavReadout
        {
            HasTileCache = true,
            TileCapacity = 32,
            TileCount = 7,
            TileHits = 3,
            TileMisses = 1,
            TileEvicts = 2,
            FlowCapacity = 80,
            ResidentNavs = 5,
            HasPath = true,
            PlanRequests = 9,
            PendingReplies = 1,
            RoundTripLastMs = double.NaN,
            WorkerLastMs = 4.2,
            WaitTicks = 6,
            Tick = 12,
            CommandLog = 4,
            PlanTimeoutMs = 500,
        };
        using var ui = Mounted(out CrowdHudPage page);
        page.WriteNav(in reading);
        Assert.That(page.TileCacheHead.ToString(), Is.EqualTo("7/32 · 命中 75%"));
        Assert.That(page.TileCacheDetail.ToString(), Is.EqualTo("hit 3 · miss 1 · evict 2"));
        Assert.That(page.FlowCacheHead.ToString(), Is.EqualTo(CrowdHudFormat.Missing), "流场没有结果缓存");
        Assert.That(page.FlowCacheDetail.ToString(), Is.Empty);
        Assert.That(page.NavResident.ToString(), Is.EqualTo("5"));
        Assert.That(page.NavRequests.ToString(), Is.EqualTo("9 / 1"));
        Assert.That(page.NavRoundTrip.ToString(), Is.EqualTo("— / 4.2 ms"));
        Assert.That(page.NavWaits.ToString(), Is.EqualTo("6"));
        Assert.That(page.NavTick.ToString(), Is.EqualTo("12 / 4"));
    }

    [Test]
    public void TelemetryText_NamesUnavailableCorridorAndHeap()
    {
        var reading = new CrowdTelemetryReadout
        {
            SimMs = 1,
            AvoidMs = 2,
            HashMs = 3,
            HasHeading = false,
            HasFlip = false,
            FixedHz = 20,
            NavMeshMs = 8,
            HpaMs = 0,
            FlowMs = 1.5,
            PlanMs = 0.4,
            NavBakeMs = 10,
            RebakeMs = 0,
            NavCellCm = 100,
            TileCount = 16,
        };
        using var ui = Mounted(out CrowdHudPage page);
        page.WriteTelemetry(in reading, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        Assert.That(page.Fps.ToString(), Is.EqualTo(CrowdHudFormat.Missing));
        Assert.That(page.FrameMs.ToString(), Is.EqualTo("FPS · — ms"));
        Assert.That(page.HeapRate.ToString(), Is.EqualTo("N/A"));
        Assert.That(page.HeapUnit.ToString(), Is.Empty);
        Assert.That(page.Extend.ToString(), Is.EqualTo(CrowdHudFormat.Missing));
        Assert.That(page.UiMs.ToString(), Is.EqualTo("— / —"));
        Assert.That(page.RenderMs.ToString(), Is.EqualTo(CrowdHudFormat.Missing));
        Assert.That(page.NavMeshMs.ToString(), Is.EqualTo("8.00"));
        Assert.That(page.FlowMs.ToString(), Is.EqualTo("1.5"));
        Assert.That(page.NavCell.ToString(), Is.EqualTo("1.0"));
        Assert.That(page.Clusters.ToString(), Is.EqualTo("4²"));
        Assert.That(page.FixedHz.ToString(), Is.EqualTo("20"));
        Assert.That(page.FixedUnit.ToString(), Is.EqualTo("Hz · 0.05 s/tick"));
        Assert.That(page.Heading.ToString(), Is.EqualTo(CrowdHudFormat.Missing));
    }

    [Test]
    public void NavText_ShowsFlowCacheCountsWhenPresent()
    {
        var reading = new CrowdNavReadout
        {
            HasFlowCache = true,
            FlowCapacity = 80,
            FlowCount = 4,
            FlowHits = 3,
            FlowMisses = 1,
            FlowEvicts = 2,
        };
        using var ui = Mounted(out CrowdHudPage page);
        page.WriteNav(in reading);
        Assert.That(page.FlowCacheHead.ToString(), Is.EqualTo("4/80 · 命中 75%"));
        Assert.That(page.FlowCacheDetail.ToString(), Is.EqualTo("hit 3 · miss 1 · evict 2"));
    }

    [Test]
    public void TelemetryText_ShowsCorridorExtensionCounts()
    {
        var reading = new CrowdTelemetryReadout
        {
            HasExtend = true,
            ExtendCount = 6,
            SimMs = 1,
            AvoidMs = 1,
            HashMs = 1,
            HasHeading = true,
            HasFlip = true,
            FixedHz = 20,
        };
        using var ui = Mounted(out CrowdHudPage page);
        page.WriteTelemetry(in reading, 60, 16.7, 2, 0.4, 0.1, 0);
        Assert.That(page.Extend.ToString(), Is.EqualTo("6"));
        Assert.That(page.Fps.ToString(), Is.EqualTo("60"));
        Assert.That(page.FrameMs.ToString(), Is.EqualTo("FPS · 16.7 ms"));
        Assert.That(page.UiMs.ToString(), Is.EqualTo("0.40 / 0.10"));
        Assert.That(page.HeapRate.ToString(), Is.EqualTo("0 KB"));
        Assert.That(page.HeapUnit.ToString(), Is.EqualTo("/s"));
    }

    [Test]
    public void Alerts_MoveTheReadingIntoTheRedSpanOnlyWhenItCrossesItsThreshold()
    {
        using var ui = Mounted(out CrowdHudPage page);
        var quiet = new CrowdTelemetryReadout
        {
            SimMs = 1,
            AvoidMs = 1,
            HeadingDeg = 1,
            HasHeading = true,
            FlipPct = 1,
            HasFlip = true,
        };
        page.WriteTelemetry(in quiet, 120, 8.3, 1, 0, 0, double.NaN);
        Assert.That(new[] { page.FpsLow, page.SimMsAlert, page.AvoidMsAlert, page.HeadingAlert, page.FlipAlert }.Select(b => b.ToString()), Has.All.Empty);
        Assert.That(page.Fps.ToString(), Is.EqualTo("120"));
        Assert.That(page.SimMs.ToString(), Is.EqualTo("1.00"));

        var loud = quiet;
        loud.SimMs = 40;
        loud.AvoidMs = 20;
        loud.HeadingDeg = 40;
        loud.FlipPct = 80;
        page.WriteTelemetry(in loud, 10, 100, 1, 0, 0, double.NaN);
        Assert.That(page.FpsLow.ToString(), Is.EqualTo("10"));
        Assert.That(page.SimMsAlert.ToString(), Is.EqualTo("40.00"));
        Assert.That(page.AvoidMsAlert.ToString(), Is.EqualTo("20.00"));
        Assert.That(page.HeadingAlert.ToString(), Is.EqualTo("40.00"));
        Assert.That(page.FlipAlert.ToString(), Is.EqualTo("80.0"));
        Assert.That(new[] { page.Fps, page.SimMs, page.AvoidMs, page.Heading, page.Flip }.Select(b => b.ToString()), Has.All.Empty);

        var missing = loud;
        missing.HasHeading = false;
        missing.HasFlip = false;
        page.WriteTelemetry(in missing, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        Assert.That(page.FpsLow.ToString(), Is.Empty, "没有帧时长不报低帧");
        Assert.That(page.HeadingAlert.ToString(), Is.Empty);
        Assert.That(page.FlipAlert.ToString(), Is.Empty);
        Assert.That(page.SimMsAlert.ToString(), Is.EqualTo("40.00"));
    }

    [Test]
    public void HeadingAndFlip_UseAdjacentSteps()
    {
        double sin2 = Math.Sin(0.035);
        sin2 *= sin2;
        var px = Fix64.Zero;
        var py = Fix64.Zero;
        sbyte steer = 0;
        double sum = 0;
        int samples = 0, flips = 0, pairs = 0;
        Observe(Fix64.FromInt(1), Fix64.Zero, ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(samples, Is.EqualTo(0));
        Observe(Fix64.Zero, Fix64.FromInt(1), ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(samples, Is.EqualTo(1));
        Assert.That(sum, Is.EqualTo(90).Within(0.01));
        Assert.That(steer, Is.EqualTo((sbyte)1));
        Assert.That(pairs, Is.EqualTo(0));

        Observe(Fix64.FromInt(1), Fix64.FromInt(100), ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(steer, Is.EqualTo((sbyte)1));
        Assert.That(pairs, Is.EqualTo(0));
        Assert.That(samples, Is.EqualTo(2));

        Observe(Fix64.FromInt(1), Fix64.Zero, ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(steer, Is.EqualTo((sbyte)-1));
        Assert.That(flips, Is.EqualTo(1));
        Assert.That(pairs, Is.EqualTo(1));

        Observe(Fix64.Zero, Fix64.Zero, ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(px.RawValue, Is.EqualTo(0));
        Assert.That(steer, Is.EqualTo((sbyte)0));
    }

    [Test]
    public void Observe_DoesNotAllocateAfterWarmup()
    {
        double sin2 = Math.Sin(0.035);
        sin2 *= sin2;
        var px = Fix64.FromInt(1);
        var py = Fix64.Zero;
        sbyte steer = 1;
        double sum = 0;
        int samples = 0, flips = 0, pairs = 0;
        Observe(Fix64.Zero, Fix64.FromInt(1), ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        for (int i = 0; i < 32; i++)
        {
            var vx = (i & 1) == 0 ? Fix64.FromInt(1) : Fix64.Zero;
            var vy = (i & 1) == 0 ? Fix64.Zero : Fix64.FromInt(1);
            Observe(vx, vy, ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            var vx = (i & 1) == 0 ? Fix64.FromInt(1) : Fix64.Zero;
            var vy = (i & 1) == 0 ? Fix64.Zero : Fix64.FromInt(1);
            Observe(vx, vy, ref px, ref py, ref steer, sin2, ref sum, ref samples, ref flips, ref pairs);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void IdleSlot_DropsStoredHeading()
    {
        var states = new[] { new CrowdSimulationUnitState { Slot = 0, State = (byte)CrowdUnitState.Idle } };
        var kins = new[] { new CrowdSimulationKinematics { Velocity = new Fix64Vec2(Fix64.FromInt(1), Fix64.Zero) } };
        var px = new[] { Fix64.FromInt(4) };
        var py = new[] { Fix64.FromInt(5) };
        var steer = new sbyte[] { 1 };
        double sum = 0;
        int samples = 0, flips = 0, pairs = 0;
        CrowdMotionQuality.Scan(1, 0, states, kins, px, py, steer, 0, ref sum, ref samples, ref flips, ref pairs);
        Assert.That(px[0].RawValue, Is.EqualTo(0));
        Assert.That(steer[0], Is.EqualTo((sbyte)0));
        Assert.That(samples, Is.EqualTo(0));
    }

    [Test]
    public void ControllingIndex_UsesPlayerTableNotBaseOrder()
    {
        int[] index = { 0, 2, 0, 1 };
        Assert.That(CrowdDebugViewFrame.ControllingIndex(index, 1), Is.EqualTo(2));
        Assert.That(CrowdDebugViewFrame.ControllingIndex(index, 2), Is.EqualTo(0));
        Assert.That(CrowdDebugViewFrame.ControllingIndex(index, 3), Is.EqualTo(1));
        Assert.That(CrowdDebugViewFrame.ControllingIndex(index, 0), Is.EqualTo(-1));
        Assert.That(CrowdDebugViewFrame.ControllingIndex(index, 4), Is.EqualTo(-1));
    }

    [Test]
    public void WorkerFault_StaysOnTheWorkerAndNonBlockingSimWaits()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using (var broken = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromMilliseconds(200), session.ResolveNavContext))
        {
            int id = broken.Request(new PathQuery(-1, 0, 1, 0), 0);
            var ex = Assert.Throws<PathServiceFaultException>(() => broken.AwaitDue(id));
            Assert.That(ex, Is.TypeOf<PathServiceFaultException>());
            Assert.That(ex!.Message, Does.Contain("内部错误"));
            Assert.That(broken.Faulted, Is.True);
        }

        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromMilliseconds(80), session.ResolveNavContext);
        service.TestDelayPerJob = TimeSpan.FromSeconds(2);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = false;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0), Is.EqualTo(4));
        session.Commands.Submit(session, MoveOrder(session, 1, 1200000, 800000), CrowdSimCommands.Exec);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!service.Faulted && watch.ElapsedMilliseconds < 2000)
        {
            Assert.That(() => session.Step(out _), Throws.Nothing);
            if (!service.Faulted) Thread.Sleep(10);
        }

        Assert.That(service.Faulted, Is.True);
        Assert.That(service.FaultMessage, Does.Contain("超过"));
        var stats = service.CopyStats();
        Assert.That(stats.Requests, Is.GreaterThan(0));
        Assert.That(double.IsNaN(stats.RoundTripLastMs), Is.True);
        var nav = CrowdNavReadout.From(session);
        using (var ui = Mounted(out CrowdHudPage page))
        {
            page.WriteNav(in nav);
            Assert.That(page.TileCacheHead.ToString(), Does.Contain("命中"));
            Assert.That(page.FlowCacheDetail.ToString(), Does.Contain("hit").And.Contain("miss").And.Contain("evict"));
            Assert.That(page.NavRequests.ToString(), Does.Match(@"^\d+ / \d+$"));
            Assert.That(page.NavRoundTrip.ToString(), Does.StartWith("— /"), "往返超时没有结果");
        }

        Assert.That(() => session.Step(out _), Throws.Nothing);
        session.BlockOnDueReplies = true;
        Assert.Throws<PathServiceFaultException>(() => session.Step(out _));
    }

    private static CrowdHudTestHost Mounted(out CrowdHudPage page)
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        feed.UseDebugView(CrowdHudSamplingTests.ShippedDebugView());
        var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        page = ui.Controller.Page ?? throw new AssertionException("HUD 没挂上");
        return ui;
    }

    private static void Observe(
        Fix64 vx, Fix64 vy, ref Fix64 px, ref Fix64 py, ref sbyte steer, double sin2,
        ref double sum, ref int samples, ref int flips, ref int pairs)
    {
        CrowdMotionQuality.Observe(vx, vy, ref px, ref py, ref steer, sin2, true, ref sum, ref samples, ref flips, ref pairs);
    }

    private static JsonObject MoveOrder(CrowdSimSession session, int player, int xCm, int yCm)
    {
        var handles = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(player, handles);
        var members = new JsonArray();
        for (int i = 0; i < count; i++) members.Add((long)handles[i]);
        return new JsonObject
        {
            ["type"] = "order",
            ["player"] = player,
            ["xCm"] = xCm,
            ["yCm"] = yCm,
            ["shape"] = "box",
            ["members"] = members,
        };
    }

    private static CrowdSimulationDebugViewConfig LoadShipped()
    {
        string path = ShippedPath();
        return CrowdSimulationDebugViewConfig.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject(), path);
    }

    private static JsonObject Shipped() => JsonNode.Parse(File.ReadAllText(ShippedPath()))!.AsObject();

    private static JsonObject Hud() => JsonNode.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", CrowdHudSettings.RelativePath)))!.AsObject();

    private static CrowdHudSettings ParseHud(JsonObject node) =>
        CrowdHudSettings.Parse(node.ToJsonString(), CrowdHudSettings.RelativePath);

    private static string ShippedPath() => Path.Combine(
        RepoRoot(), "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "CrowdSimulationDebugView.json");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根");
    }
}
