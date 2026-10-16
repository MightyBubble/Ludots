using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// HUD 分区对齐 Web 沙盒原型（实测 zone-rects）：左右两栏距边 12px、全高、宽 max(15%, 272px)；
/// 玩家条贴顶水平居中，鼠标说明在它下面居中；指令坞夹在两栏之间、各留 12px、贴底；遥测占满右栏。
/// 通知宽 240px 贴右栏左侧 12px：宽屏（≥ 1700px）贴顶，和原型一样；窄屏原型会压住玩家条，这里改排到顶栏下面。
/// 面板全开时可见面板互不重叠，屏幕中心不被任何面板盖住，任何文字不越出所在面板。
/// </summary>
public sealed class CrowdHudLayoutTests
{
    private const float Inset = 12f;
    private const float Tolerance = 0.5f;
    private const float NoticeWidth = 240f;
    /// <summary>s7 脚本在这之前部署完开局单位，计数到四五位数，文案宽度和真机一致。</summary>
    private const int DeployedTick = 125;

    private static readonly string[] PanelIds =
    {
        "crowd-brand-panel", "crowd-dv", "crowd-playerbar", "crowd-hint",
        "crowd-notice-0", "crowd-notice-1", "crowd-notice-2", "crowd-dock", "crowd-telemetry",
    };

    [TestCase(1920, 1080)]
    [TestCase(1280, 720)]
    public void AllPanelsOpen_MatchPrototypeZones_NoOverlap_AndCentreStaysClear(int width, int height)
    {
        using var ui = AllPanelsOpen(width, height, out var paths);
        using var _ = paths;
        float rail = MathF.Max(0.15f * width, 272f);

        UiRect left = ui.Rect("crowd-left");
        UiRect right = ui.Rect("crowd-right");
        Assert.That(left.X, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(left.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(left.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));
        Assert.That(left.Width, Is.EqualTo(rail).Within(Tolerance));
        Assert.That(right.Right, Is.EqualTo(width - Inset).Within(Tolerance));
        Assert.That(right.Width, Is.EqualTo(rail).Within(Tolerance));
        Assert.That(ui.Rect("crowd-brand-panel").Y, Is.EqualTo(Inset).Within(Tolerance));

        UiRect telemetry = ui.Rect("crowd-telemetry");
        Assert.That(telemetry.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(telemetry.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));
        Assert.That(telemetry.Width, Is.EqualTo(rail).Within(Tolerance));

        UiRect dock = ui.Rect("crowd-dock");
        Assert.That(dock.X, Is.EqualTo(left.Right + Inset).Within(Tolerance));
        Assert.That(dock.Right, Is.EqualTo(right.X - Inset).Within(Tolerance));
        Assert.That(dock.Bottom, Is.EqualTo(height - Inset).Within(Tolerance));

        Assert.That(ui.Find("crowd-players")!.Children, Has.Count.GreaterThanOrEqualTo(2), "每个玩家一枚");
        UiRect bar = ui.Rect("crowd-playerbar");
        Assert.That(bar.Y, Is.EqualTo(Inset).Within(Tolerance));
        Assert.That(bar.X + bar.Width / 2f, Is.EqualTo(width / 2f).Within(1f));
        UiRect hint = ui.Rect("crowd-hint");
        Assert.That(hint.Y, Is.EqualTo(bar.Bottom + 6f).Within(Tolerance));
        Assert.That(hint.X + hint.Width / 2f, Is.EqualTo(width / 2f).Within(1f));

        bool wide = width >= 1700;
        float topBottom = hint.Bottom;
        int notices = 0;
        for (int i = 0; i < 3; i++)
        {
            if (!ui.Visible($"crowd-notice-{i}")) continue;
            UiRect n = ui.Rect($"crowd-notice-{i}");
            Assert.That(n.Width, Is.EqualTo(NoticeWidth).Within(Tolerance));
            Assert.That(n.Right, Is.EqualTo(right.X - Inset).Within(Tolerance));
            if (i == 0) Assert.That(n.Y, wide ? Is.EqualTo(Inset).Within(Tolerance) : Is.GreaterThanOrEqualTo(hint.Bottom));
            topBottom = MathF.Max(topBottom, n.Bottom);
            notices++;
        }

        Assert.That(notices, Is.GreaterThan(0), "部署脚本下过本方指令，应有通知");

        var overlaps = new List<string>();
        var visible = PanelIds.Where(ui.Visible).ToArray();
        for (int a = 0; a < visible.Length; a++)
        {
            for (int b = a + 1; b < visible.Length; b++)
            {
                UiRect ra = ui.Rect(visible[a]);
                UiRect rb = ui.Rect(visible[b]);
                if (MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.X, rb.X) > Tolerance &&
                    MathF.Min(ra.Bottom, rb.Bottom) - MathF.Max(ra.Y, rb.Y) > Tolerance)
                    overlaps.Add($"#{visible[a]} {ra} 与 #{visible[b]} {rb}");
            }
        }

        Assert.That(overlaps, Is.Empty, string.Join("\n", overlaps));

        float cx = width / 2f, cy = height / 2f;
        foreach (string id in visible)
        {
            UiRect r = ui.Rect(id);
            Assert.That(cx >= r.X && cx <= r.Right && cy >= r.Y && cy <= r.Bottom, Is.False, $"屏幕中心被 #{id} {r} 盖住");
        }

        float clear = dock.Y - topBottom;
        Assert.That(clear, Is.GreaterThan(0.25f * height), $"中部留空 {clear:0} px，顶部到 {topBottom:0}，指令坞从 {dock.Y:0} 起");
        TestContext.Out.WriteLine($"{width}x{height} left {left} right {right} bar {bar} hint {hint} dock {dock} topBottom {topBottom:0} clear {clear:0}");
    }

    [TestCase(1920, 1080)]
    [TestCase(1280, 720)]
    public void AllPanelsOpen_NoTextSpillsOutOfItsPanel(int width, int height)
    {
        using var ui = AllPanelsOpen(width, height, out var paths);
        using var _ = paths;
        var spills = new List<string>();
        foreach (string id in PanelIds.Where(ui.Visible))
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
    /// <summary>滚动容器里的内容竖向超出是正常的，只查横向。</summary>
    private static void CollectSpills(IUiTextMeasurer measurer, UiNode node, UiRect bounds, string panelId, List<string> spills, bool scrolled = false)
    {
        scrolled |= node.Style.Overflow == UiOverflow.Scroll;
        if (node.Style.Display == UiDisplay.None) return;
        for (int i = 0; i < node.Children.Count; i++)
        {
            UiNode child = node.Children[i];
            UiRect r = child.LayoutRect;
            if (!string.IsNullOrEmpty(child.TextContent) && child.Style.Display != UiDisplay.None)
            {
                float inkRight = child.Style.WhiteSpace == UiWhiteSpace.NoWrap
                    ? r.X + measurer.MeasureWidth(child.TextContent, child.Style)
                    : r.Right;
                if (inkRight > bounds.Right + Tolerance || (!scrolled && r.Bottom > bounds.Bottom + Tolerance))
                    spills.Add($"#{panelId}: \"{child.TextContent}\" 右 {inkRight:0} / 面板 {bounds.Right:0}，下 {r.Bottom:0} / 面板 {bounds.Bottom:0}");
            }

            CollectSpills(measurer, child, bounds, panelId, spills, scrolled);
        }
    }

    private static string ArtifactDir()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "crowd-hud-layout");
        Directory.CreateDirectory(path);
        return path;
    }

    internal static CrowdHudTestHost AllPanelsOpen(int width, int height, out PathQueryService service) =>
        AllPanelsOpen(width, height, out service, out _, out _);

    /// <summary>S4 部署脚本走到开局后三方各下一道移动令，HUD 全部区块打开；返回时仿真还在走，读数会继续变。</summary>
    internal static CrowdHudTestHost AllPanelsOpen(int width, int height, out PathQueryService service, out CrowdSimSession session, out CrowdHudFeed feed)
    {
        (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var live = session;
        feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => live);
        var debug = CrowdHudSamplingTests.ShippedDebugView();
        debug.NavPanel = true;
        debug.TelemetryPanel = true;
        debug.UnitColorMode = CrowdSimulationDebugViewConfig.ColorTeam;
        var player = CrowdHudSamplingTests.ShippedPlayerView(session);
        player.Draw(session, new RouteVisualBuffer(), new ScreenOverlayBuffer());
        feed.UseDebugView(debug);
        feed.UsePlayerView(player);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "s1", "s7", "CrowdSimulation", "parity", "s4-deploy-truth.json")))!;
        var script = CrowdReferenceScript.Load(truth["script"]!.AsArray(), session.Units.Capacity);
        script.Advance(session, DeployedTick);
        Assert.That(session.Units.Count, Is.GreaterThan(1000), "部署脚本没把开局单位放下去");
        for (int i = 0; i < 3; i++)
        {
            session.Commands.Submit(session, MoveOrder(session, 1, 700000 + 60000 * i, 600000), CrowdSimCommands.Exec);
            session.Step(out _);
        }

        player.Draw(session, new RouteVisualBuffer(), new ScreenOverlayBuffer());
        var ui = new CrowdHudTestHost(null, width, height);
        for (int i = 0; i < 4; i++)
        {
            session.Step(out _);
            ui.FrameAfterAllPeriods(feed);
            if (System.Environment.GetEnvironmentVariable("DBG_FRAMES") == "1")
                TestContext.Progress.WriteLine($"DBGF i={i} diff={ui.DiffAgainstFullRepaint(out var b)} {b} full={ui.Renderer.FullPaintCount} dmg={ui.Renderer.DamagePaintCount} fullLayouts={ui.Root.Scene!.FullLayoutCount}");
        }

        return ui;
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
}
