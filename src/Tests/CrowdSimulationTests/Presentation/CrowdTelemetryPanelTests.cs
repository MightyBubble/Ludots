using System;
using System.IO;
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
        Assert.That(cfg.Panels.Telemetry, Is.False);
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
        Assert.That(settings.History, Is.EqualTo(48));
        Assert.That(settings.Alerts.LowFps, Is.EqualTo(90));
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
            RoundTripMeanMs = double.NaN,
            WorkerLastMs = 4.2,
            WorkerMeanMs = 3.1,
            WaitTicks = 6,
            Tick = 12,
            CommandLog = 4,
            PlanTimeoutMs = 500,
        };
        string[] lines = NavText(in reading);
        Assert.That(lines[1], Does.Contain("容量 32").And.Contain("在册 7").And.Contain("hit 3").And.Contain("miss 1").And.Contain("evict 2"));
        Assert.That(lines[1], Does.Contain("75%"));
        Assert.That(lines[2], Does.Contain("流场容量 80").And.Contain("N/A").And.Contain("无结果缓存"));
        Assert.That(lines[3], Does.Contain("常驻导航 5"));
        Assert.That(lines[4], Does.Contain("规划请求 9").And.Contain("待回 1").And.Contain("500"));
        Assert.That(lines[5], Does.Contain("N/A").And.Contain("4.2"));
        Assert.That(lines[6], Does.Contain("6"));
        Assert.That(lines[7], Does.Contain("tick 12").And.Contain("命令日志 4"));
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
        var host = new CrowdHostReadout(double.NaN, double.NaN, double.NaN, double.NaN, 0, 0);
        string[] lines = TelemetryText(in reading, in host);
        var text = string.Join('\n', lines);
        Assert.That(text, Does.Contain("采样 250 ms · 曲线 48"));
        Assert.That(text, Does.Contain("FPS N/A"));
        Assert.That(text, Does.Contain("堆增长 N/A"));
        Assert.That(text, Does.Contain("走廊扩展 N/A"));
        Assert.That(text, Does.Contain("UI 重绘峰值 N/A ms · 面板编排 N/A ms"));
        Assert.That(text, Does.Not.Contain("渲染"), "旧的“渲染”只量叠层文字，已改名为 UI 重绘峰值");
        Assert.That(text, Does.Contain("分辨率 N/A"));
        Assert.That(text, Does.Contain("NavMesh 8.0"));
        Assert.That(text, Does.Contain("nav格 100 cm"));
        Assert.That(text, Does.Contain("tile 16"));
        Assert.That(text, Does.Contain("仿真Worker/宿主RPC N/A"));
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
        string[] lines = NavText(in reading);
        Assert.That(lines[2], Does.Contain("流场容量 80").And.Contain("在册 4").And.Contain("75%").And.Contain("hit 3").And.Contain("miss 1").And.Contain("evict 2"));
        Assert.That(lines[2], Does.Not.Contain("无结果缓存"));
    }

    [Test]
    public void TelemetryText_ShowsCorridorExtensionCounts()
    {
        var reading = new CrowdTelemetryReadout
        {
            HasExtend = true,
            ExtendCount = 6,
            ExtendMs = 1.2,
            SimMs = 1,
            AvoidMs = 1,
            HashMs = 1,
            HasHeading = true,
            HasFlip = true,
            FixedHz = 20,
        };
        var host = new CrowdHostReadout(1, 1, 1, 1, 1920, 1080);
        string[] lines = TelemetryText(in reading, in host);
        Assert.That(lines[10], Is.EqualTo("走廊扩展 6 次 1.2 ms"));
        Assert.That(lines[13], Does.StartWith("分辨率 1920x1080"));
    }

    [Test]
    public void Alerts_FireOnlyWhenTheReadingCrossesItsThreshold()
    {
        var alerts = ParseHud(Hud()).Alerts;
        var hot = new bool[CrowdHudState.TelemetryLineCount];
        var quiet = new CrowdTelemetryReadout
        {
            SimMs = 1,
            AvoidMs = 1,
            HeadingDeg = 1,
            HasHeading = true,
            FlipPct = 1,
            HasFlip = true,
        };
        CrowdHudFormat.MarkAlerts(in quiet, 120, alerts, hot);
        Assert.That(hot, Has.None.True);

        var loud = quiet;
        loud.SimMs = 40;
        loud.AvoidMs = 20;
        loud.HeadingDeg = 40;
        loud.FlipPct = 80;
        CrowdHudFormat.MarkAlerts(in loud, 10, alerts, hot);
        Assert.That(hot[CrowdHudState.FpsLine], Is.True);
        Assert.That(hot[CrowdHudState.SimLine], Is.True);
        Assert.That(hot[CrowdHudState.AvoidLine], Is.True);
        Assert.That(hot[CrowdHudState.HeadingLine], Is.True);
        Assert.That(hot[CrowdHudState.FlipLine], Is.True);

        var missing = loud;
        missing.HasHeading = false;
        missing.HasFlip = false;
        CrowdHudFormat.MarkAlerts(in missing, double.NaN, alerts, hot);
        Assert.That(hot[CrowdHudState.FpsLine], Is.False);
        Assert.That(hot[CrowdHudState.HeadingLine], Is.False);
        Assert.That(hot[CrowdHudState.FlipLine], Is.False);
        Assert.That(hot[CrowdHudState.SimLine], Is.True);

        string[] lines = TelemetryText(in missing, new CrowdHostReadout(double.NaN, double.NaN, double.NaN, double.NaN, 0, 0));
        Assert.That(lines[CrowdHudState.SimLine], Is.EqualTo("Sim 40.0 ms"), "告警走角标绑定，不改行文字");
        Assert.That(CrowdHudState.IsAlertable(CrowdHudState.SimLine), Is.True);
        Assert.That(CrowdHudState.IsAlertable(0), Is.False);
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
        string[] lines = NavText(in nav);
        Assert.That(lines[1], Does.Contain("容量"));
        Assert.That(lines[2], Does.Contain("流场容量").And.Contain("在册").And.Contain("hit").And.Contain("miss").And.Contain("evict"));
        Assert.That(lines[4], Does.Contain("规划请求"));
        Assert.That(() => session.Step(out _), Throws.Nothing);
        session.BlockOnDueReplies = true;
        Assert.Throws<PathServiceFaultException>(() => session.Step(out _));
    }

    private static string[] NavText(in CrowdNavReadout reading)
    {
        HudLine[] lines = HudLine.Many(CrowdHudState.NavLineCount);
        CrowdHudFormat.WriteNav(in reading, lines);
        return Array.ConvertAll(lines, l => l.ToString());
    }

    private static string[] TelemetryText(in CrowdTelemetryReadout reading, in CrowdHostReadout host)
    {
        HudLine[] lines = HudLine.Many(CrowdHudState.TelemetryLineCount);
        CrowdHudFormat.WriteTelemetry(in reading, in host, 250, 48, lines);
        return Array.ConvertAll(lines, l => l.ToString());
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
        RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationDebugView.json");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根");
    }
}
