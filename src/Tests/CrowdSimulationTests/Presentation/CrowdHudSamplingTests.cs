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
    public void ClosedPanels_NeverRead_AndHaveNoNodes()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();

        for (int i = 0; i < 40; i++) ui.FrameAfterAllPeriods(feed);

        Assert.That(debug.NavPanel || debug.TelemetryPanel, Is.False, "出厂配置两个面板都关着");
        Assert.That(feed.NavReads, Is.Zero, "导航面板关着不该读");
        Assert.That(feed.TelemetryReads, Is.Zero, "遥测面板关着不该读");
        Assert.That(feed.PlayerReads, Is.Zero, "没有玩家视图就没有玩家面板");
        Assert.That(feed.DeployReads, Is.EqualTo(40));
        Assert.That(ui.Has("crowd-nav"), Is.False);
        Assert.That(ui.Has("crowd-telemetry"), Is.False);
        Assert.That(ui.Has("crowd-player"), Is.False);
    }

    [Test]
    public void NavPanel_SamplesOnOpenThenAtItsOwnRate_AndStopsWhenClosed()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        int navPeriod = ui.Settings.SampleMs.Nav;

        debug.NavPanel = true;
        ui.Frame(feed, 1);
        Assert.That(feed.NavReads, Is.EqualTo(1), "打开当帧就采一次");
        Assert.That(ui.Text("crowd-nav-0"), Is.EqualTo("导航缓存 / 路径服务"));

        int framesInsidePeriod = (navPeriod - 1) / 16;
        for (int i = 0; i < framesInsidePeriod; i++) ui.Frame(feed, 16);
        Assert.That(feed.NavReads, Is.EqualTo(1), $"{navPeriod} ms 周期内不该再读");

        ui.Frame(feed, navPeriod);
        Assert.That(feed.NavReads, Is.EqualTo(2), "过了周期再读一次");
        Assert.That(feed.TelemetryReads, Is.Zero, "遥测仍关着");

        debug.NavPanel = false;
        ui.Frame(feed, 1);
        Assert.That(ui.Has("crowd-nav"), Is.False);
        int reads = feed.NavReads;
        for (int i = 0; i < 20; i++) ui.FrameAfterAllPeriods(feed);
        Assert.That(feed.NavReads, Is.EqualTo(reads), "关掉后不再读");
    }

    [Test]
    public void TelemetryPanel_SamplesOnOpenThenAtItsOwnRate_AndStopsWhenClosed()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = ShippedDebugView();
        feed.UseDebugView(debug);
        using var ui = new CrowdHudTestHost();
        ui.Frame(feed);
        int period = ui.Settings.SampleMs.Telemetry;

        debug.TelemetryPanel = true;
        ui.Frame(feed, 1);
        Assert.That(feed.TelemetryReads, Is.EqualTo(1));
        Assert.That(ui.Text("crowd-tel-0"), Does.StartWith($"遥测 采样 {period} ms"));
        Assert.That(ui.Text("crowd-tel-5"), Does.StartWith("UI 重绘峰值"));

        for (int i = 0; i < (period - 1) / 16; i++) ui.Frame(feed, 16);
        Assert.That(feed.TelemetryReads, Is.EqualTo(1));
        ui.Frame(feed, period);
        Assert.That(feed.TelemetryReads, Is.EqualTo(2));
        Assert.That(feed.NavReads, Is.Zero, "导航仍关着");

        debug.TelemetryPanel = false;
        ui.Frame(feed, 1);
        Assert.That(ui.Has("crowd-telemetry"), Is.False);
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
        string path = Path.Combine(RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationDebugView.json");
        var merged = CrowdPresentationPaletteTestMerge.MergedDebugView(
            JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        return new CrowdDebugViewFrame(CrowdSimulationDebugViewConfig.Load(merged, path));
    }

    internal static CrowdPlayerViewHost ShippedPlayerView(CrowdSimSession session)
    {
        string path = Path.Combine(RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationPlayerView.json");
        var merged = CrowdPresentationPaletteTestMerge.MergedPlayerView(
            JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(merged, path));
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
