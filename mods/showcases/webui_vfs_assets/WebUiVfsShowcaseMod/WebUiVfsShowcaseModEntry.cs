using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Browser.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;

namespace WebUiVfsShowcaseMod;

public sealed class WebUiVfsShowcaseModEntry : IMod
{
    private string? _modId;
    private string? _panelRoot;
    private IBrowserSurface? _surface;
    private BrowserCanvasContent? _browserContent;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public void OnLoad(IModContext context)
    {
        _modId = context.ModId;
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        ReleaseSurface();
    }

    private void ReleaseSurface()
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
        if (!string.IsNullOrWhiteSpace(_panelRoot) && Directory.Exists(_panelRoot))
        {
            Directory.Delete(_panelRoot, recursive: true);
        }

        _panelRoot = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "WebUiVfsShowcase.Picture",
            UiSurfaceSegment.Main,
            priority: 10,
            exclusive: true));

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            surfaceHost.Publish(_lease, UiSurfaceContribution.FromBuilder(BuildMissingBrowser));
            return;
        }

        if (!TryGetEngine(context, out GameEngine engine) || engine.VFS == null)
        {
            ReleaseSurface();
            throw new InvalidOperationException("Web UI VFS showcase requires the mod virtual file system.");
        }

        try
        {
            string panelRoot = PreparePanelRoot(engine);
            _panelRoot = panelRoot;
            IBrowserResourceResolver resolver = WebPanelBrowserResources.Create(panelRoot, engine.VFS);
            IBrowserSurface surface = await runtime.CreateSurfaceAsync(new BrowserViewport(960, 640), resolver)
                .ConfigureAwait(false);
            _surface = surface;
            _browserContent = new BrowserCanvasContent(surface);
            surface.FrameReady += (_, _) =>
            {
                if (_lease.IsValid && _surfaceHost != null)
                {
                    _surfaceHost.InvalidateLease(_lease);
                }
            };
            await surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
            surfaceHost.Publish(
                _lease,
                UiSurfaceContribution.FromBuilder(() =>
                    Ui.Canvas(_browserContent)
                        .WidthPercent(100f)
                        .HeightPercent(100f)));
        }
        catch
        {
            ReleaseSurface();
            throw;
        }
    }

    private string PreparePanelRoot(GameEngine engine)
    {
        string modId = _modId ?? throw new InvalidOperationException("Showcase mod id was not captured at load.");
        string templateUri = modId + ":" + WebUiVfsShowcasePage.BrowserAppDirectory + "/" + WebUiVfsShowcasePage.TemplateFileName;
        if (engine.VFS == null ||
            !engine.VFS.TryResolveFullPath(templateUri, out string templatePath) ||
            string.IsNullOrWhiteSpace(templatePath))
        {
            throw new InvalidOperationException($"Browser page template '{templateUri}' is not in the mounted mod.");
        }

        string? templateDirectory = Path.GetDirectoryName(templatePath);
        if (string.IsNullOrWhiteSpace(templateDirectory))
        {
            throw new InvalidOperationException($"Browser page template '{templateUri}' has no directory.");
        }

        PresentationImageAssetCatalog catalog = engine.GetService(CoreServiceKeys.PresentationImageAssetCatalog)
            ?? throw new InvalidOperationException("Presentation image catalog is not available.");
        string panelRoot = Path.Combine(Path.GetTempPath(), "ludots-webui-vfs-" + Guid.NewGuid().ToString("N"));
        WebUiVfsShowcasePage.Materialize(templateDirectory, panelRoot, catalog);
        return panelRoot;
    }

    private static UiElementBuilder BuildMissingBrowser()
    {
        return Ui.Column(
                Ui.Text("模组里的这张图").FontSize(34).Bold(),
                Ui.Text("要看这张图，请用带浏览器的启动方式打开。现在还没有浏览器，所以图不会出现。"))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Gap(12f);
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (TryGetEngine(context, out GameEngine engine) &&
            engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static bool TryGetEngine(ScriptContext context, out GameEngine engine)
    {
        if (context.TryGet(CoreServiceKeys.Engine, out GameEngine? found) && found != null)
        {
            engine = found;
            return true;
        }

        engine = null!;
        return false;
    }
}
