// SangoWebUiMod 入口(M1.c,D1 真 UI mod):CEF surface + WebUI DataPlane 全链(照
// SangoWebUiSpikeMod 样板),叠加两件事:
//   1. GameStart 即启动 sango 内核(SangoKernelBoot.Boot,种子 SangoTurnDriver.DefaultSeed;
//      SangoSimModEntry 的首次 TurnAdvanced 惰性启动与本入口收敛于同一 Scenario.Cur 判空门);
//   2. TurnAdvanced 后(SangoSimMod 已推进回合)立即推一轮话题并落回合摘要消息行。

using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using Sango.Runtime;

namespace Sango.WebUi;

public sealed class SangoWebUiModEntry : IMod
{
    private const string AssetIndexPath = "SangoWebUiMod:Assets/sango-app/index.html";
    private const string SessionId = "sango-webui";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private SangoWebUiTickSystem? _tickSystem;
    private IModContext? _modContext;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private SangoWorldFeed? _feed;
    private WebUiDataPlaneTickPump? _pump;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _modContext = context;
        context.Log("[SangoWebUiMod] Loaded (M1.c: world topics + city commands + endTurn via Manual clock).");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _tickSystem?.Dispose();
        _tickSystem = null;
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
        _pump = null;
        _feed = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IModContext modContext = _modContext
            ?? throw new InvalidOperationException("SangoWebUiMod requires an active ModContext.");
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        if (Sango.Core.Scenario.Cur == null)
        {
            SangoKernelBoot.Boot(engine.VFS, "SangoContentMod", SangoTurnDriver.DefaultSeed);
        }

        var stepPolicy = context.Get(CoreServiceKeys.GasClockStepPolicy) as GasClockStepPolicy
            ?? throw new InvalidOperationException(
                "SangoWebUiMod requires the host GasClockStepPolicy service (Manual clock from assets/GAS/clock.json).");

        var feed = new SangoWorldFeed();
        feed.AttachPlayerMessageSystem();
        feed.AttachCombatAnnals();
        _feed = feed;

        var router = new WebUiCommandRouter(
            new SangoWebUiGenerationResolver(),
            new SangoWebUiPermissionValidator());
        router.Register(SangoCityCommandHandler.CommandName, new SangoCityCommandHandler());
        router.Register(SangoEndTurnCommandHandler.CommandName, new SangoEndTurnCommandHandler(stepPolicy));
        router.Register(SangoSaveCommandHandler.CommandName, new SangoSaveCommandHandler(engine));
        router.Register(SangoLoadCommandHandler.CommandName, new SangoLoadCommandHandler(engine));
        router.Register(SangoCreateTroopCommandHandler.CommandName, new SangoCreateTroopCommandHandler());
        router.Register(SangoMoveTroopCommandHandler.CommandName, new SangoMoveTroopCommandHandler());
        router.Register(SangoSelectPlayerForceCommandHandler.CommandName,
            new SangoSelectPlayerForceCommandHandler(engine, engine.VFS!));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(new SangoWorldCitiesTopic(feed));
        _dataPlaneRuntime.RegisterTopic(new SangoWorldForcesTopic(feed));
        _dataPlaneRuntime.RegisterTopic(new SangoWorldTurnTopic());
        _dataPlaneRuntime.RegisterTopic(new SangoWorldMessagesTopic(feed));
        _dataPlaneRuntime.RegisterTopic(new SangoWorldCityTopic(feed));
        _dataPlaneRuntime.RegisterTopic(new SangoWorldTroopsTopic(feed));
        _dataPlaneRuntime.RegisterTopic(new SangoWorldBattlesTopic(feed));

        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "Sango.WebUi",
            UiSurfaceSegment.Main,
            priority: 10,
            exclusive: true));
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            throw new InvalidOperationException(
                "SangoWebUiMod requires a Ludots host-provided BrowserRuntime service.");
        }

        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(SangoWorldCitiesTopic.TopicName);
        pump.TrackTopic(SangoWorldForcesTopic.TopicName);
        pump.TrackTopic(SangoWorldTurnTopic.TopicName);
        pump.TrackTopic(SangoWorldMessagesTopic.TopicName);
        pump.TrackTopic(SangoWorldTroopsTopic.TopicName);
        pump.TrackTopic(SangoWorldBattlesTopic.TopicName);
        _pump = pump;

        // 回合事件后立即推一轮(turn 话题携带新摘要);摘要消息行真源见 SangoWorldFeed。
        modContext.OnEvent(GameEvents.TurnAdvanced, _ =>
        {
            feed.NoteTurnAdvanced();
            pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
            return Task.CompletedTask;
        });

        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));

        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        _dataPlaneRuntime.AttachSession(
            SessionId,
            new BrowserMessageBridgeDataTransport(_surface.Messages));

        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
        BrowserSurfaceCanvasContent browserContent = _browserContent;
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

        await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);

        _tickSystem = new SangoWebUiTickSystem(pump);
        engine.RegisterSystem(_tickSystem, SystemGroup.InputCollection);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("sango-webui-surface")
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
            string? root = System.IO.Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && System.IO.Directory.Exists(root))
            {
                return root;
            }

            throw new DirectoryNotFoundException(
                $"Sango WebUI app asset root resolved to '{root}' but the directory does not exist ({AssetIndexPath}).");
        }

        throw new DirectoryNotFoundException(
            $"Sango WebUI app assets were not found: {AssetIndexPath} " +
            $"(VFS={(engine.VFS == null ? "null" : "present")})");
    }
}
