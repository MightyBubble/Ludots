using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using CncTriNationFullGameMod.Systems;
using CncTriNationFullGameMod.Triggers;

namespace CncTriNationFullGameMod;

public sealed class CncTriNationFullGameModEntry : IMod
{
    private const string AssetIndexPath = "CncTriNationFullGameMod:assets/cnc-tri-nation-app/index.html";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private CncTriNationDataPlaneSystem? _dataPlaneSystem;
    private CncTriNationTopicProducer? _topic;
    private IModContext? _modContext;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _modContext = context;
        TagRegistry.Register("Status.Cnc.Training");
        TagRegistry.Register("Status.Cnc.Building");
        TagRegistry.Register("State.Cnc.Constructing");
        TagRegistry.Register("Equip.Slot.CncUpgrade");
        context.OnEvent(GameEvents.MapLoaded, ctx => new CncTriNationMapLoadedTrigger(context).ExecuteAsync(ctx));
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
        context.Log("[CncTriNationFullGameMod] Loaded - tri-nation C&C full game.");
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
            "CncTriNation.FullGame",
            UiSurfaceSegment.Main,
            priority: 10,
            exclusive: true));
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        engine.RegisterSystem(new CncTriNationGraphProjectionSystem(engine), SystemGroup.InputCollection);

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            throw new InvalidOperationException(
                "CncTriNationFullGameMod requires IBrowserRuntime. Launch cnc_tri_nation_showcase with the CEF browser runtime enabled.");
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

    private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
    {
        _topic = new CncTriNationTopicProducer(engine);
        var router = new WebUiCommandRouter(
            new CncTriNationGenerationResolver(engine),
            new CncTriNationPermissionValidator());
        router.Register("selectEntity", new CncTriNationCommandHandler(_topic));
        router.Register("activateAbilitySlot", new CncTriNationCommandHandler(_topic));
        router.Register("switchParticipantView", new CncTriNationCommandHandler(_topic));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(_topic);
        _dataPlaneRuntime.AttachSession(
            "cnc-tri-nation-full-game",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(CncTriNationTopicProducer.TopicName);
        _dataPlaneSystem = new CncTriNationDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("cnc-tri-nation-browser-surface")
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

        throw new DirectoryNotFoundException($"C&C tri-nation browser app assets were not found: {AssetIndexPath}");
    }
}
