using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
using CityRallyWebUiShowcaseMod.Runtime;
using CityRallyWebUiShowcaseMod.Systems;

namespace CityRallyWebUiShowcaseMod;

/// <summary>
/// 城池集结点纯 Web UI showcase 根入口。
/// 自包含：浏览器 surface + 数据平面（实体/命令卡/生产队列）+ 集结点右键路由 + Knowledge 投影。
/// 不依赖 RtsDemoMod / BrowserRtsProductionShowcaseMod，无 Skia 指令面板。
/// </summary>
public sealed class CityRallyWebUiShowcaseModEntry : IMod
{
    private const string AssetIndexPath = "CityRallyWebUiShowcaseMod:Assets/rts-production-app/index.html";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private CityRallyDataPlaneSystem? _dataPlaneSystem;
    private CityRallyTopicProducer? _topic;
    private IModContext? _modContext;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _modContext = context;
        context.Log("[CityRallyWebUiShowcaseMod] Loaded.");
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
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost;
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        InstallCoreSystems(engine);

        // Headless（测试）环境下无浏览器运行时：只注册玩法系统，跳过 Web UI surface。
        if (surfaceHost == null || root == null || !TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            _modContext?.Log("[CityRallyWebUiShowcaseMod] Browser runtime unavailable; gameplay systems installed without Web UI.");
            return;
        }

        _surfaceHost = surfaceHost;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "CityRallyWebUi.Showcase",
            UiSurfaceSegment.Main,
            priority: 10,
            exclusive: true));

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

        await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
    }

    private void InstallCoreSystems(GameEngine engine)
    {
        OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("CityRallyWebUiShowcaseMod requires Core OrderQueue.");
        IModContext modContext = _modContext
            ?? throw new InvalidOperationException("CityRallyWebUiShowcaseMod requires an active ModContext.");

        engine.RegisterSystem(
            new CityRallyLocalOrderSourceSystem(engine.World, engine.GlobalContext, orderQueue, modContext),
            SystemGroup.InputCollection);
        engine.RegisterSystem(
            new CityRallyGarrisonSystem(engine, orderQueue),
            SystemGroup.EffectProcessing);
        engine.RegisterSystem(new CityRallyKnowledgeProjectionSystem(engine), SystemGroup.InputCollection);
        engine.RegisterPresentationSystem(new CityRallySelectionFeedbackPresentationSystem(engine));
    }

    private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
    {
        _topic = new CityRallyTopicProducer(engine);
        var router = new WebUiCommandRouter(
            new CityRallyGenerationResolver(),
            new CityRallyPermissionValidator());
        router.Register("selectEntity", new CityRallyCommandHandler(_topic));
        router.Register("activateAbilitySlot", new CityRallyCommandHandler(_topic));
        router.Register("switchParticipantView", new CityRallyCommandHandler(_topic));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(_topic);
        _dataPlaneRuntime.AttachSession(
            "city-rally-webui-showcase",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(CityRallyTopicProducer.TopicName);
        _dataPlaneSystem = new CityRallyDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("city-rally-browser-surface")
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

        throw new DirectoryNotFoundException($"City rally browser app assets were not found: {AssetIndexPath}");
    }
}
