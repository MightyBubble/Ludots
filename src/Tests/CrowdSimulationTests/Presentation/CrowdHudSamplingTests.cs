using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// 面板各按各的节奏采样；关着的面板不读数据口，也不在 UI 文档里出节点。
/// </summary>
public sealed class CrowdHudSamplingTests
{
    [Test]
    public void ClosedPanels_NeverRead_AndStayHidden()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        Assert.That(debug.NavPanel, Is.False, "出厂配置导航页关着");
        Assert.That(debug.TelemetryPanel, Is.True, "出厂配置照原型常开遥测");
        debug.TelemetryPanel = false;
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();

        for (int i = 0; i < 40; i++) ui.FrameAfterAllPeriods(feed);

        Assert.That(feed.NavReads, Is.Zero, "缓存页没打开不该读");
        Assert.That(feed.InspectReads, Is.Zero, "检视页没打开不该读");
        Assert.That(feed.TelemetryReads, Is.Zero, "遥测关着不该读");
        Assert.That(feed.PlayerReads, Is.Zero, "没有玩家视图就没有玩家面板");
        Assert.That(feed.DeployReads, Is.EqualTo(40));
        Assert.That(ui.Visible("crowd-dv-layers"), Is.True, "调试视图默认停在图层页");
        Assert.That(ui.Visible("crowd-dv-cache"), Is.False);
        Assert.That(ui.Visible("crowd-dv-inspect"), Is.False);
        Assert.That(ui.Visible("crowd-telemetry"), Is.False);
        Assert.That(ui.Visible("crowd-pr"), Is.False);
        Assert.That(ui.Visible("crowd-playerbar"), Is.False);
    }

    [Test]
    public void NavPanelKey_OpensCacheTab_SamplesAtItsOwnRate_AndStopsWhenClosed()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        debug.TelemetryPanel = false;
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        int navPeriod = ui.Settings.SampleMs.Nav;

        debug.NavPanel = true;
        ui.Frame(feed, 1);
        Assert.That(feed.NavReads, Is.EqualTo(1), "打开当帧就采一次");
        Assert.That(ui.Visible("crowd-dv-cache"), Is.True);
        Assert.That(ui.Find("crowd-dv-tab-cache")!.HasClass("nf-tab-on"), Is.True);

        int framesInsidePeriod = (navPeriod - 1) / 16;
        for (int i = 0; i < framesInsidePeriod; i++) ui.Frame(feed, 16);
        Assert.That(feed.NavReads, Is.EqualTo(1), $"{navPeriod} ms 周期内不该再读");

        ui.Frame(feed, navPeriod);
        Assert.That(feed.NavReads, Is.EqualTo(2), "过了周期再读一次");
        Assert.That(feed.TelemetryReads, Is.Zero, "遥测仍关着");

        debug.NavPanel = false;
        ui.Frame(feed, 1);
        Assert.That(ui.Visible("crowd-dv-cache"), Is.False);
        Assert.That(ui.Visible("crowd-dv-layers"), Is.True, "关掉缓存页回到图层页");
        int reads = feed.NavReads;
        for (int i = 0; i < 20; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.NavReads, Is.EqualTo(reads), "关掉后不再读");
    }

    [Test]
    public void InspectTab_ClickedOpen_SamplesOnlyWhileShown()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        debug.TelemetryPanel = false;
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();
        for (int i = 0; i < 4; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.InspectReads, Is.Zero);

        Assert.That(ui.Click("crowd-dv-tab-inspect"), Is.True, "点页签要被 HUD 接住");
        ui.Frame(feed, 1);
        Assert.That(ui.Visible("crowd-dv-inspect"), Is.True);
        for (int i = 0; i < 3; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.InspectReads, Is.GreaterThanOrEqualTo(3));
        Assert.That(ui.Text("crowd-dv-inspect"), Does.Contain("关").Or.Contain("队伍"), "检视页写了单位着色");

        ui.Click("crowd-dv-tab-layers");
        ui.Frame(feed, 1);
        int reads = feed.InspectReads;
        for (int i = 0; i < 10; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.InspectReads, Is.EqualTo(reads), "切走后不再读");
    }

    [Test]
    public void TelemetryPanel_SamplesOnOpenThenAtItsOwnRate_AndStopsWhenClosed()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        debug.TelemetryPanel = false;
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        int period = ui.Settings.SampleMs.Telemetry;

        debug.TelemetryPanel = true;
        ui.Frame(feed, 1);
        Assert.That(feed.TelemetryReads, Is.EqualTo(1));
        Assert.That(ui.Visible("crowd-telemetry"), Is.True);
        Assert.That(ui.Text("crowd-telemetry"), Does.Contain("TELEMETRY").And.Contain("UI 重绘峰值"));

        for (int i = 0; i < (period - 1) / 16; i++) ui.Frame(feed, 16);
        Assert.That(feed.TelemetryReads, Is.EqualTo(1));
        ui.Frame(feed, period);
        Assert.That(feed.TelemetryReads, Is.EqualTo(2));
        Assert.That(feed.NavReads, Is.Zero, "导航仍关着");

        debug.TelemetryPanel = false;
        ui.Frame(feed, 1);
        Assert.That(ui.Visible("crowd-telemetry"), Is.False);
        int reads = feed.TelemetryReads;
        for (int i = 0; i < 20; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.TelemetryReads, Is.EqualTo(reads));
    }

    [Test]
    public void StatusPanel_DoesNotReadInsideItsPeriod()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        int period = ui.Settings.SampleMs.Status;
        Assert.That(feed.DeployReads, Is.EqualTo(1));

        for (int i = 0; i < (period - 1) / 16; i++) ui.Frame(feed, 16);
        Assert.That(feed.DeployReads, Is.EqualTo(1));
        ui.Frame(feed, period);
        Assert.That(feed.DeployReads, Is.EqualTo(2));
    }

    [Test]
    public void NoFeed_UnmountsAndReadsNothing()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        Assert.That(ui.Controller.Mounted, Is.True);
        Assert.That(ui.Has("crowd-hud"), Is.True);

        ui.FrameAfterAllPeriods(null);
        Assert.That(ui.Controller.Mounted, Is.False);
        Assert.That(ui.Host.Scene?.FindByElementId("crowd-hud"), Is.Null);
        int reads = feed.DeployReads;
        for (int i = 0; i < 5; i++) ui.FrameAfterAllPeriods(null);
        Assert.That(feed.DeployReads, Is.EqualTo(reads));
    }

    internal static CrowdDebugViewFrame ShippedDebugView()
    {
        string path = Path.Combine(RepoRoot(), "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "CrowdSimulationDebugView.json");
        return new CrowdDebugViewFrame(CrowdSimulationDebugViewConfig.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject(), path));
    }

    internal static CrowdPlayerViewHost ShippedPlayerView(CrowdSimSession session)
    {
        string path = Path.Combine(RepoRoot(), "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "CrowdSimulationPlayerView.json");
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject(), path));
        host.Bind(session);
        return host;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
