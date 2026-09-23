using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using SanguoGrandStrategyMod.Runtime;

namespace SanguoGrandStrategyMod.Web;

internal sealed class SanguoBrowserPanel : IDisposable
{
    private const string AssetIndexPath = "SanguoGrandStrategyMod:Assets/sanguo-web/index.html";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private SanguoDataPlaneSystem? _dataPlaneSystem;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public async Task TryStartAsync(ScriptContext context, SanguoGrandStrategyRuntime runtime)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            runtime.SetWebUiStatus("WebUI DataPlane: engine service missing.");
            return;
        }

        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost ||
            engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            runtime.SetWebUiStatus("WebUI DataPlane: UiSurfaceHost missing; native/headless runtime active.");
            return;
        }

        if (!TryGetBrowserRuntime(context, engine, out IBrowserRuntime browserRuntime))
        {
            runtime.SetWebUiStatus("WebUI DataPlane: browser runtime missing; native HUD remains playable.");
            return;
        }

        _surfaceHost = surfaceHost;
        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));

        _surface = await browserRuntime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        SetupDataPlane(engine, runtime, _surface);

        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "Showcase.SanguoGrandStrategy.Browser",
            UiSurfaceSegment.Main,
            priority: 8,
            exclusive: false));
        _browserContent = new BrowserSurfaceCanvasContent(_surface, BrowserSurfaceHitTestOptions.Alpha());
        BrowserSurfaceCanvasContent browserContent = _browserContent;
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

        runtime.SetWebUiStatus($"WebUI DataPlane: publishing `{SanguoGrandStrategyIds.TopicName}`.");
        await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _dataPlaneSystem?.Dispose();
        _dataPlaneSystem = null;
        _dataPlaneRuntime?.Dispose();
        _dataPlaneRuntime = null;
        _commandDispatcher?.Dispose();
        _commandDispatcher = null;
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

    private void SetupDataPlane(GameEngine engine, SanguoGrandStrategyRuntime runtime, IBrowserSurface surface)
    {
        var topic = new SanguoWorldTopicProducer(engine, runtime);
        var router = new WebUiCommandRouter(
            new SanguoWorldGenerationResolver(),
            new SanguoWorldPermissionValidator());
        var handler = new SanguoWorldCommandHandler(topic);
        router.Register("selectCity", handler);
        router.Register("nextCity", handler);
        router.Register("nextUnit", handler);
        router.Register("conscript", handler);
        router.Register("trainElite", handler);
        router.Register("develop", handler);
        router.Register("tax", handler);
        router.Register("harvest", handler);
        router.Register("march", handler);
        router.Register("resolveBattle", handler);
        router.Register("research", handler);
        router.Register("diplomacy", handler);

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(topic);
        _dataPlaneRuntime.AttachSession(
            "sanguo-grand-strategy",
            new BrowserMessageBridgeDataTransport(surface.Messages));

        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(SanguoGrandStrategyIds.TopicName);
        _dataPlaneSystem = new SanguoDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("sanguo-grand-strategy-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(15);
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, GameEngine engine, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string ResolveAssetRoot(GameEngine engine)
    {
        if (engine.VFS != null &&
            engine.VFS.TryResolveFullPath(AssetIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException($"Sanguo browser app assets were not found: {AssetIndexPath}");
    }
}
