using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace BrowserRtsProductionShowcaseMod;

public sealed class BrowserRtsProductionShowcaseModEntry : IMod
{
    private const string AssetIndexPath = "BrowserRtsProductionShowcaseMod:Assets/rts-production-app/index.html";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private BrowserRtsProductionDataPlaneSystem? _dataPlaneSystem;
    private BrowserRtsProductionShowcaseTopicProducer? _topic;
    private IModContext? _modContext;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _modContext = context;
        context.Log("[BrowserRtsProductionShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _dataPlaneSystem?.Dispose();
        _dataPlaneSystem = null;
        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
        }

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

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "BrowserRtsProduction.Showcase",
            UiSurfaceSegment.Main,
            priority: 10,
            exclusive: true));
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        InstallLocalOrderSource(engine);

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            throw new InvalidOperationException(
                "BrowserRtsProductionShowcaseMod requires a Ludots host-provided BrowserRuntime service.");
        }

        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));

        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        SetupDataPlane(engine, _surface);

        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
        BrowserSurfaceCanvasContent browserContent = _browserContent;
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

        await _surface.NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-app://app/"))).ConfigureAwait(false);
    }

    private void InstallLocalOrderSource(GameEngine engine)
    {
        OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("BrowserRtsProductionShowcaseMod requires Core OrderQueue.");
        IModContext modContext = _modContext
            ?? throw new InvalidOperationException("BrowserRtsProductionShowcaseMod requires an active ModContext.");

        engine.RegisterSystem(
            new BrowserRtsProductionLocalOrderSourceSystem(engine.World, engine.GlobalContext, orderQueue, modContext),
            SystemGroup.InputCollection);
    }

    private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
    {
        _topic = new BrowserRtsProductionShowcaseTopicProducer(engine);
        var router = new WebUiCommandRouter(
            new BrowserRtsProductionShowcaseGenerationResolver(engine),
            new BrowserRtsProductionShowcasePermissionValidator());
        router.Register("selectEntity", new BrowserRtsProductionShowcaseCommandHandler(_topic));
        router.Register("activateAbilitySlot", new BrowserRtsProductionShowcaseCommandHandler(_topic));
        router.Register("switchParticipantView", new BrowserRtsProductionShowcaseCommandHandler(_topic));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(_topic);
        _dataPlaneRuntime.AttachSession(
            "browser-rts-production-showcase",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(BrowserRtsProductionShowcaseTopicProducer.TopicName);
        _dataPlaneSystem = new BrowserRtsProductionDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("rts-production-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(20);
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

        throw new DirectoryNotFoundException($"RTS production browser app assets were not found: {AssetIndexPath}");
    }
}
