using System.Diagnostics;
using System.Numerics;
using System.Text;
using CrowdSimulationUiMod;
using CrowdSimulationUiMod.UI;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;
using SkiaSharp;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// 走真实 UI 链路的 HUD 宿主：UIRoot + Skia 渲染 + UiSurfaceHost，样式与采样配置读 mod 里的正本。
/// 每帧按 Raylib 合成器的规则推进：控制器 Tick，UIRoot 脏了才 Render。Raylib 宿主不调 UIRoot.Update，这里也不调。
/// </summary>
internal sealed class CrowdHudTestHost : IDisposable
{
    private readonly SKSurface _surface;
    private long _clock;

    public CrowdHudTestHost(GameEngine? engine = null, int width = 1920, int height = 1080)
    {
        Width = width;
        Height = height;
        string modRoot = Path.Combine(RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod");
        Settings = CrowdHudSettings.Parse(
            File.ReadAllText(Path.Combine(modRoot, CrowdHudSettings.RelativePath)),
            CrowdHudSettings.RelativePath);
        var styleSheet = UiCssParser.ParseStyleSheet(File.ReadAllText(Path.Combine(modRoot, CrowdSimulationUiModEntry.StyleSheetPath)));

        OwnsEngine = engine == null;
        Engine = engine ?? new GameEngine();
        var renderer = new SkiaUiRenderer();
        _surface = SKSurface.Create(new SKImageInfo((int)Width, (int)Height));
        renderer.SetCanvas(_surface.Canvas);
        Root = new UIRoot(renderer);
        Root.Resize(Width, Height);
        var textMeasurer = new SkiaTextMeasurer();
        TextMeasurer = textMeasurer;
        var imageSizeProvider = new SkiaImageSizeProvider();
        Host = new UiSurfaceHost(Root, textMeasurer, imageSizeProvider);
        Engine.SetService(CoreServiceKeys.UIRoot, Root);
        Engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
        Engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
        Engine.SetService(CoreServiceKeys.UiSurfaceHost, Host);
        if (!Engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? _))
        {
            Engine.SetService(CoreServiceKeys.ViewController, new FixedView(width, height));
        }

        if (!Engine.TryGetService(CoreServiceKeys.TeamColorPalette, out TeamColorPalette? _))
        {
            Engine.SetService(CoreServiceKeys.TeamColorPalette, new TeamColorPalette(new[]
            {
                Vector4.Zero,
                TeamColorPalette.ParseHex("#4db2ff", "test"),
                TeamColorPalette.ParseHex("#ff5a4c", "test"),
                TeamColorPalette.ParseHex("#73ff8c", "test"),
                TeamColorPalette.ParseHex("#ffd966cc", "test"),
            }));
        }

        Controller = new CrowdHudController(Settings, styleSheet);
        _clock = Stopwatch.Frequency;
    }

    public IUiTextMeasurer TextMeasurer { get; }
    public float Width { get; }
    public float Height { get; }
    public GameEngine Engine { get; }
    public bool OwnsEngine { get; }
    public UIRoot Root { get; }
    public UiSurfaceHost Host { get; }
    public CrowdHudSettings Settings { get; }
    public CrowdHudController Controller { get; }
    public int Renders { get; private set; }

    /// <summary>推进一帧：时钟前进 <paramref name="ms"/> 毫秒后跑控制器，UIRoot 脏了才清屏重画（渲染器直接画进目标画布）。</summary>
    public void Frame(CrowdHudFeed? feed, double ms = 16d)
    {
        _clock += (long)(ms * Stopwatch.Frequency / 1000d);
        Controller.Tick(Engine, feed, _clock);
        if (!Root.IsDirty) return;
        _surface.Canvas.Clear(SKColors.Transparent);
        Root.Render();
        Renders++;
    }

    /// <summary>跨过所有面板的采样周期，保证下一帧每个开着的面板都到点。</summary>
    public void FrameAfterAllPeriods(CrowdHudFeed? feed) => Frame(feed, MaxPeriodMs() + 1);

    public int MaxPeriodMs()
    {
        var r = Settings.SampleMs;
        return Math.Max(Math.Max(Math.Max(r.Status, r.Player), Math.Max(r.Inspect, r.Nav)), r.Telemetry);
    }

    public int MinPeriodMs()
    {
        var r = Settings.SampleMs;
        return Math.Min(Math.Min(Math.Min(r.Status, r.Player), Math.Min(r.Inspect, r.Nav)), r.Telemetry);
    }

    public UiNode? Find(string id) => Host.Scene?.FindByElementId(id);

    public UiRect Rect(string id) => (Find(id) ?? throw new AssertionException($"UI 文档里没有 #{id}")).LayoutRect;

    public bool Has(string id) => Find(id) != null;

    /// <summary>节点及其子孙的文字，按文档顺序用换行拼起来。</summary>
    public string Text(string id)
    {
        UiNode node = Find(id) ?? throw new AssertionException($"UI 文档里没有 #{id}");
        var sb = new StringBuilder();
        Append(node, sb);
        return sb.ToString();
    }

    public string AllText() => Text(CrowdHudView.RootId);

    /// <summary>把最后一次渲染的 HUD 叠在战场底色上存成 PNG。</summary>
    public void SavePng(string path)
    {
        using var image = _surface.Snapshot();
        using var composed = SKSurface.Create(new SKImageInfo((int)Width, (int)Height));
        composed.Canvas.Clear(new SKColor(96, 104, 88));
        composed.Canvas.DrawImage(image, 0, 0);
        using var snapshot = composed.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }

    public void Dispose()
    {
        Controller.Unmount();
        _surface.Dispose();
        if (OwnsEngine) Engine.Dispose();
    }

    private static void Append(UiNode node, StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(node.TextContent))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++) Append(node.Children[i], sb);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private sealed class FixedView(int width, int height) : IViewController
    {
        public Vector2 Resolution { get; } = new(width, height);
        public float Fov => 60f;
        public float AspectRatio => (float)width / height;
    }
}
