using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace BrowserRtsProductionShowcaseMod;

public sealed class BrowserRtsProductionShowcaseModEntry : IMod
{
    private const string BrowserServiceKey = "BrowserRuntime";
    private const string AssetIndexPath = "BrowserRtsProductionShowcaseMod:Assets/rts-production-app/index.html";

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiDataPlaneSession? _dataPlaneSession;
    private BrowserRtsProductionShowcaseTopicProducer? _topic;
    private CancellationTokenSource? _publisherCts;
    private Task? _publisherTask;
    private IModContext? _modContext;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _modContext = context;
        context.Log("[BrowserRtsProductionShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        StopPublisher();
        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
            _dataPlaneSession = null;
        }

        _browserContent?.Dispose();
        _browserContent = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IUiTextMeasurer textMeasurer = (IUiTextMeasurer)context.Get(CoreServiceKeys.UiTextMeasurer);
        IUiImageSizeProvider imageSizeProvider = (IUiImageSizeProvider)context.Get(CoreServiceKeys.UiImageSizeProvider);
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        InstallLocalOrderSource(engine);
        ConfigureRenderDebug(engine);

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            root.MountScene(BuildMissingRuntimeScene(textMeasurer, imageSizeProvider));
            root.IsDirty = true;
            return;
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
        root.MountScene(BuildBrowserScene(textMeasurer, imageSizeProvider, _browserContent));
        root.IsDirty = true;

        await _surface.NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-app://app/"))).ConfigureAwait(false);
    }

    private static void ConfigureRenderDebug(GameEngine engine)
    {
        RenderDebugState renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState)
            ?? throw new InvalidOperationException("RenderDebugState service is missing.");
        renderDebug.DrawTerrain = false;
        renderDebug.DrawDebugDraw = true;
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawSkiaUi = true;
    }

    private void InstallLocalOrderSource(GameEngine engine)
    {
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out object? orderQueueObj) &&
            orderQueueObj is OrderQueue orderQueue &&
            _modContext != null)
        {
            engine.RegisterSystem(
                new BrowserRtsProductionLocalOrderSourceSystem(engine.World, engine.GlobalContext, orderQueue, _modContext),
                SystemGroup.InputCollection);
        }
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

        _dataPlaneRuntime = new WebUiDataPlaneRuntime(router);
        _dataPlaneRuntime.RegisterTopic(_topic);
        _dataPlaneSession = _dataPlaneRuntime.AttachSession(
            "browser-rts-production-showcase",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        StartPublisher();
    }

    private void StartPublisher()
    {
        if (_dataPlaneRuntime == null || _dataPlaneSession == null || _topic == null)
        {
            return;
        }

        _publisherCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _publisherCts.Token;
        WebUiDataPlaneRuntime runtime = _dataPlaneRuntime;
        WebUiDataPlaneSession session = _dataPlaneSession;
        BrowserRtsProductionShowcaseTopicProducer topic = _topic;
        _publisherTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                await runtime.PublishAsync(topic.CreateDeltaPacket(session.SessionId), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private void StopPublisher()
    {
        if (_publisherCts == null)
        {
            return;
        }

        _publisherCts.Cancel();
        try
        {
            _publisherTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
        }
        finally
        {
            _publisherCts.Dispose();
            _publisherCts = null;
            _publisherTask = null;
        }
    }

    private static UiScene BuildBrowserScene(
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider,
        BrowserSurfaceCanvasContent browserContent)
    {
        UiElementBuilder root = Ui.Canvas(browserContent)
            .Id("rts-production-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(20);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root);
    }

    private static UiScene BuildMissingRuntimeScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        UiElementBuilder root = Ui.Column(
                Ui.Text("Browser runtime missing").FontSize(32f).Bold(),
                Ui.Text("Launch an RTS production showcase preset with BrowserCefRuntimeMod to load the in-game Web UI."))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(32f)
            .Gap(12f);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root);
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserServiceKey);
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
