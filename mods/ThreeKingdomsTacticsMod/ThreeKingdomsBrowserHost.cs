using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using ThreeKingdomsTacticsMod.Runtime;
using ThreeKingdomsTacticsMod.Systems;

namespace ThreeKingdomsTacticsMod;

internal sealed class ThreeKingdomsBrowserHost : IDisposable
{
    private const string AssetIndexPath = "ThreeKingdomsTacticsMod:Assets/tactics-app/index.html";
    private readonly ThreeKingdomsTacticsRuntime _runtime;

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private ThreeKingdomsDataPlaneSystem? _dataPlaneSystem;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public ThreeKingdomsBrowserHost(ThreeKingdomsTacticsRuntime runtime)
    {
        _runtime = runtime;
    }

    public void TryInstall(ScriptContext context)
    {
        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            return;
        }

        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
            context.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost ||
            context.Get(CoreServiceKeys.UIRoot) is not Ludots.UI.UIRoot root)
        {
            return;
        }

        string assetRoot;
        try
        {
            assetRoot = ResolveAssetRoot(engine);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "ThreeKingdomsTactics.BrowserHud",
            UiSurfaceSegment.Debug,
            priority: 1,
            exclusive: false));

        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));
        _surface = runtime.CreateSurfaceAsync(viewport, new BrowserAppResourceResolver(assetRoot))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        SetupDataPlane(engine, _surface);
        _browserContent = new BrowserSurfaceCanvasContent(_surface, BrowserSurfaceHitTestOptions.Alpha());
        BrowserSurfaceCanvasContent browserContent = _browserContent;
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => Ui.Canvas(browserContent)
                .Id("three-kingdoms-browser-hud")
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(60)));
        _surface.NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-app://app/")))
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose()
    {
        _dataPlaneSystem?.Dispose();
        _dataPlaneSystem = null;
        _dataPlaneRuntime?.Dispose();
        _dataPlaneRuntime = null;
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

    private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
    {
        var topic = new ThreeKingdomsTacticsTopicProducer(engine, _runtime);
        var router = new WebUiCommandRouter(new ThreeKingdomsGenerationResolver(), new ThreeKingdomsPermissionValidator());
        var handler = new ThreeKingdomsTacticsCommandHandler(topic);
        router.Register("selectNext", handler);
        router.Register("move", handler);
        router.Register("attack", handler);
        router.Register("skill", handler);
        router.Register("troop", handler);
        router.Register("endTurn", handler);

        var dispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
        _dataPlaneRuntime.RegisterTopic(topic);
        _dataPlaneRuntime.AttachSession(
            "three-kingdoms-tactics",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, dispatcher);
        pump.TrackTopic(ThreeKingdomsTacticsIds.DataPlaneTopic);
        _dataPlaneSystem = new ThreeKingdomsDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) &&
            engine != null &&
            engine.TryGetService(key, out runtime))
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

        throw new DirectoryNotFoundException($"Three Kingdoms tactics browser assets were not found: {AssetIndexPath}");
    }
}
