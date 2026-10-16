using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 群体 HUD 控制器：跟着 <see cref="CrowdHudFeed"/> 换页面，把 hud.html 通过表面宿主的租约发布出去。
/// 每帧把时间交给 <see cref="CrowdHudPage"/>；页面改了 class 或名单才让租约重建，文字和画布改写只重画。
/// </summary>
internal sealed class CrowdHudController
{
    public const string OwnerId = "CrowdSimulation.Hud";

    private readonly CrowdHudSettings _settings;
    private readonly string _html;
    private readonly string _css;

    private CrowdHudPage? _page;
    private IUiSurfaceHost? _host;
    private UiSurfaceLeaseHandle _lease;
    private CrowdHudFeed? _feed;
    private CrowdHudActionSink? _sink;
    private double _composeMs = double.NaN;

    public CrowdHudController(CrowdHudSettings settings, string html, string css)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _html = string.IsNullOrWhiteSpace(html) ? throw new ArgumentException("hud.html 为空。", nameof(html)) : html;
        _css = css ?? throw new ArgumentNullException(nameof(css));
    }

    public bool Mounted => _lease.IsValid;

    /// <summary>租约发布或重建的次数（挂载算一次）。</summary>
    public int ComposeCount { get; private set; }

    public double LastComposeMs => _composeMs;

    internal CrowdHudPage? Page => _page;

    public void Tick(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        CrowdHudFeed? feed = engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out var runtime) ? runtime?.HudFeed : null;
        Tick(engine, feed, Stopwatch.GetTimestamp());
    }

    /// <param name="now"><see cref="Stopwatch.GetTimestamp"/> 刻度。</param>
    internal void Tick(GameEngine engine, CrowdHudFeed? feed, long now)
    {
        long started = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(feed, _feed))
        {
            Unmount();
            _feed = feed;
        }

        if (feed == null) return;
        bool mounting = _page == null;
        if (mounting)
        {
            if (!engine.TryGetService(CoreServiceKeys.UiSurfaceHost, out object? host) || host is not IUiSurfaceHost surface) return;
            _host = surface;
            _sink ??= new CrowdHudActionSink(engine);
            _page = new CrowdHudPage(_html, _css, feed, _settings, _sink, Swatches(engine));
        }

        engine.TryGetService(CoreServiceKeys.PresentationTimingDiagnostics, out PresentationTimingDiagnostics? timing);
        float width = engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? view) && view != null ? view.Resolution.X : 0f;
        _page!.Frame(now, timing, width, _composeMs);
        bool dirty = _page.TakeStructureDirty();
        if (mounting)
        {
            Publish();
        }
        else if (dirty)
        {
            _host!.Invalidate(_lease);
            ComposeCount++;
        }

        _composeMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }

    public void Unmount()
    {
        if (_host != null && _lease.IsValid) _host.Release(_lease);
        _lease = default;
        _page = null;
        _host = null;
    }

    private void Publish()
    {
        CrowdHudPage page = _page!;
        _lease = _host!.Acquire(new UiSurfaceLeaseRequest(OwnerId, UiSurfaceSegment.Overlay, priority: 30));
        _host.Publish(_lease, UiSurfaceContribution.FromBuilder(() => UiElementBuilder.FromElement(page.Root), styleSheets: page.Document.StyleSheets));
        ComposeCount++;
    }

    /// <summary>队伍色按玩家号缓存成 CSS 颜色串，只在换数据口时算一次。</summary>
    private static string?[] Swatches(GameEngine engine)
    {
        var swatches = new string?[CrowdHudPage.MaxPlayers + 1];
        if (!engine.TryGetService(CoreServiceKeys.TeamColorPalette, out TeamColorPalette? palette) || palette == null) return swatches;
        for (int p = 1; p < swatches.Length; p++)
        {
            if (palette.TryGet(p, out Vector4 c)) swatches[p] = Rgba(c);
        }

        return swatches;
    }

    private static string Rgba(Vector4 c) => string.Create(CultureInfo.InvariantCulture,
        $"rgba({Channel(c.X)}, {Channel(c.Y)}, {Channel(c.Z)}, {Math.Clamp(c.W, 0f, 1f):0.###})");

    private static int Channel(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
