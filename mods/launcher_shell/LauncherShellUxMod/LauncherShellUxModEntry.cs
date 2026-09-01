using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace LauncherShellUxMod;

/// <summary>
/// Shell 会话的 UX 载体：独占 Main 表面，把宿主环回站点上的 React launcher 铺满全屏。
/// 皮肤与交互全部在 React 应用内；本 mod 只做表面宿主，不承载任何 launcher 逻辑。
/// </summary>
public sealed class LauncherShellUxModEntry : IMod
{
    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public void OnLoad(IModContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Log("[LauncherShellUxMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _browserContent?.Dispose();
        _browserContent = null;
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _surfaceHost = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "LauncherShell.Main",
            UiSurfaceSegment.Main,
            priority: 100,
            exclusive: true));

        if (!TryGetShellSite(context, out LauncherShellSite? shellSite))
        {
            surfaceHost.Publish(
                _lease,
                UiSurfaceContribution.FromBuilder(BuildMissingSiteRoot));
            return;
        }

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            surfaceHost.Publish(
                _lease,
                UiSurfaceContribution.FromBuilder(BuildMissingRuntimeRoot));
            return;
        }

        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));

        var resolver = new BrowserAppResourceResolver(Path.GetTempPath());
        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        _browserContent = new BrowserSurfaceCanvasContent(_surface, hitTestOptions: BrowserSurfaceHitTestOptions.Bounds);
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildShellRoot(_browserContent)));

        await _surface.NavigateAsync(new BrowserNavigationRequest(new Uri(shellSite.BaseUrl + "/launcher/index.html")))
            .ConfigureAwait(false);
    }

    private static UiElementBuilder BuildShellRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("launcher-shell-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(20);
    }

    private static UiElementBuilder BuildMissingSiteRoot()
    {
        return Ui.Column(
                Ui.Text("Launcher shell site missing").FontSize(32f).Bold(),
                Ui.Text("The host did not inject LauncherShellSite; run this mod through the no-argument shell session."))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(32f)
            .Gap(12f);
    }

    private static UiElementBuilder BuildMissingRuntimeRoot()
    {
        return Ui.Column(
                Ui.Text("Browser runtime missing").FontSize(32f).Bold(),
                Ui.Text("Run the launcher shell with the CEF browser runtime provider enabled."))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(32f)
            .Gap(12f);
    }

    private static bool TryGetShellSite(ScriptContext context, out LauncherShellSite? site)
    {
        var key = new ServiceKey<LauncherShellSite>(LauncherShellSite.ServiceKeyName);
        if (context.TryGet(key, out site))
        {
            return true;
        }

        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine != null &&
            engine.TryGetService(key, out site))
        {
            context.Set(key, site);
            return true;
        }

        site = null;
        return false;
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine != null &&
            engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }
}
