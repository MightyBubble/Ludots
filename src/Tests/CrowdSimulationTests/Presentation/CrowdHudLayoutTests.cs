using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// HUD 分区对齐 Web 沙盒（Sandbox.jsx）：左右两栏距边 12px、全高、宽 max(15%, 272px)；
/// 指令坞夹在两栏之间、各留 12px、贴底；队伍条水平居中；指令卡贴右栏左侧 12px。
/// 面板全开时战场中部留空，任何文字不越出所在面板。
/// </summary>
public sealed class CrowdHudLayoutTests
{
    private const float Inset = 12f;
    private const float Tolerance = 0.5f;

    private static readonly string[] PanelIds =
    {
        "crowd-header", "crowd-player", "crowd-debugview", "crowd-playerbar", "crowd-legend",
        "crowd-order", "crowd-dock", "crowd-telemetry",
    };

    [TestCase(1920, 1080)]
    [TestCase(1280, 720)]
    public void AllPanelsOpen_MatchWebRegions_AndLeavePlayAreaClear(int width, int height)
    {
        using var ui = AllPanelsOpen(width, height);
        float rail = MathF.Max(0.15f * width, 272f);

        UiRect left = ui.Rect("crowd-left");
        UiRect right = ui.Rect("crowd-right");
        Assert.That(left.X, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(left.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(left.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));
        Assert.That(left.Width, Is.EqualTo(rail).Within(Tolerance));
        Assert.That(right.Right, Is.EqualTo(width - Inset).Within(Tolerance));
        Assert.That(right.Width, Is.EqualTo(rail).Within(Tolerance));

        UiRect dock = ui.Rect("crowd-dock");
        Assert.That(dock.X, Is.EqualTo(left.Right + Inset).Within(Tolerance));
        Assert.That(dock.Right, Is.EqualTo(right.X - Inset).Within(Tolerance));
        Assert.That(dock.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));

        UiRect bar = ui.Rect("crowd-playerbar");
        Assert.That(bar.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(bar.X + bar.Width / 2f, Is.EqualTo(width / 2f).Within(1f));

        UiRect order = ui.Rect("crowd-order");
        Assert.That(order.Right, Is.EqualTo(right.X - Inset).Within(Tolerance));
        Assert.That(order.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(order.Width, Is.GreaterThanOrEqualTo(240f - Tolerance));

        UiRect telemetry = ui.Rect("crowd-telemetry");
        Assert.That(telemetry.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(telemetry.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));

        float topBottom = MathF.Max(ui.Rect("crowd-top-middle").Bottom, order.Bottom);
        float playHeight = dock.Y - topBottom;
        Assert.That(playHeight, Is.GreaterThan(0.4f * height), $"中部留空 {playHeight:0} px，顶栏到 {topBottom:0}，指令坞从 {dock.Y:0} 起");
    }

    [TestCase(1920, 1080)]
    [TestCase(1280, 720)]
    public void AllPanelsOpen_NoTextSpillsOutOfItsPanel(int width, int height)
    {
        using var ui = AllPanelsOpen(width, height);
        var spills = new List<string>();
        foreach (string id in PanelIds)
        {
            UiNode panel = ui.Find(id) ?? throw new AssertionException($"UI 文档里没有 #{id}");
            UiRect bounds = panel.LayoutRect;
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(width - Inset + Tolerance), $"#{id} 越出屏幕右缘");
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(height - Inset + Tolerance), $"#{id} 越出屏幕下缘");
            CollectSpills(ui.TextMeasurer, panel, bounds, id, spills);
        }

        ui.SavePng(Path.Combine(ArtifactDir(), $"hud-layout-{width}x{height}.png"));
        Assert.That(spills, Is.Empty, string.Join("\n", spills));
    }

    /// <summary>布局把文字节点拉伸到父宽，不换行的字是否画出界要拿字宽比。</summary>
    private static void CollectSpills(IUiTextMeasurer measurer, UiNode node, UiRect bounds, string panelId, List<string> spills)
    {
        for (int i = 0; i < node.Children.Count; i++)
        {
            UiNode child = node.Children[i];
            UiRect r = child.LayoutRect;
            if (!string.IsNullOrEmpty(child.TextContent))
            {
                float inkRight = child.Style.WhiteSpace == UiWhiteSpace.NoWrap
                    ? r.X + measurer.MeasureWidth(child.TextContent, child.Style)
                    : r.Right;
                if (inkRight > bounds.Right + Tolerance || r.Bottom > bounds.Bottom + Tolerance)
                    spills.Add($"#{panelId}: \"{child.TextContent}\" 右 {inkRight:0} / 面板 {bounds.Right:0}，下 {r.Bottom:0} / 面板 {bounds.Bottom:0}");
            }

            CollectSpills(measurer, child, bounds, panelId, spills);
        }
    }

    private static string ArtifactDir()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "crowd-hud-layout");
        Directory.CreateDirectory(path);
        return path;
    }

    private static CrowdHudTestHost AllPanelsOpen(int width, int height)
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = CrowdHudSamplingTests.ShippedDebugView();
        debug.NavPanel = true;
        debug.TelemetryPanel = true;
        debug.UnitColorMode = CrowdSimulationDebugViewConfig.ColorTeam;
        var player = CrowdHudSamplingTests.ShippedPlayerView(session);
        player.Draw(session, new RouteVisualBuffer(), new ScreenOverlayBuffer());
        feed.UseDebugView(debug);
        feed.UsePlayerView(player);
        var ui = new CrowdHudTestHost(null, width, height);
        for (int i = 0; i < 4; i++)
        {
            session.Step(out _);
            ui.FrameAfterAllPeriods(feed);
        }

        return ui;
    }
}
