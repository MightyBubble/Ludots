using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using Ludots.Core.Gameplay.Activities;

namespace ActivityDispatchShowcaseMod;

public sealed class ActivityDispatchShowcaseModEntry : IMod
{
    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private ActivityDispatchDataPlaneSystem? _dataPlaneSystem;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private WebUiPanelKitSurfaceBinder? _panelBinder;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log("[ActivityDispatchShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _dataPlaneSystem?.Dispose();
        _dataPlaneSystem = null;
        _panelBinder?.Dispose();
        _panelBinder = null;
        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
        }

        _commandDispatcher?.Dispose();
        _commandDispatcher = null;
        _browserContent?.Dispose();
        _browserContent = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            // Headless hosts (acceptance runs, Agent Bridge without a browser surface) have no
            // panel to wire: the activity rail itself is pure config and stays fully live.
            Console.WriteLine("[ActivityDispatchShowcaseMod] No browser host: panel wiring skipped; activity dispatch rail remains live.");
            return;
        }

        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");

        SetupDataPlane(engine);
        WebUiPanelKitManifest manifest = LoadPanelKitManifest(engine);

        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        int screenWidth = Math.Max(640, (int)MathF.Ceiling(root.Width));
        int screenHeight = Math.Max(480, (int)MathF.Ceiling(root.Height));
        var viewport = new BrowserViewport(screenWidth, screenHeight);

        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        AttachDataPlaneSession(engine, _surface, manifest);
        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Bounds);
        _panelBinder = new WebUiPanelKitSurfaceBinder(surfaceHost, manifest);
        _panelBinder.Bind(CreatePanelContribution);

        await _surface.NavigateAsync(new BrowserNavigationRequest(CreateNavigationUri(manifest))).ConfigureAwait(false);
    }

    private UiSurfaceContribution CreatePanelContribution(WebUiPanelDeclaration panel)
    {
        if (!string.Equals(panel.PanelId, ActivityDispatchShowcaseIds.PanelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown activity panel id '{panel.PanelId}'.");
        }

        BrowserSurfaceCanvasContent browserContent = _browserContent
            ?? throw new InvalidOperationException("Browser content must be created before binding the activity panel.");
        return UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent));
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        ArgumentNullException.ThrowIfNull(browserContent);
        return Ui.Canvas(browserContent)
            .Id("activity-dispatch-showcase-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(24);
    }

    private static Uri CreateNavigationUri(WebUiPanelKitManifest manifest)
    {
        string topic = manifest.DeclaredTopics.Single();
        string query =
            "route=activity-dispatch" +
            "&panelId=" + Uri.EscapeDataString(ActivityDispatchShowcaseIds.PanelId) +
            "&topic=" + Uri.EscapeDataString(topic);
        return BrowserLocalAppUri.Create("/", query);
    }

    private void SetupDataPlane(GameEngine engine)
    {
        var router = new WebUiCommandRouter(
            new ActivityDispatchGenerationResolver(),
            new ActivityDispatchPermissionValidator());
        router.Register(
            ActivityDispatchShowcaseIds.TriggerCommand,
            new ActivityDispatchTriggerCommandHandler(engine));
        router.Register(
            ActivityDispatchShowcaseIds.ConfirmCommand,
            new ActivityDispatchConfirmCommandHandler(engine));
        router.Register(
            ActivityDispatchShowcaseIds.SetAttributeCommand,
            new ActivityDispatchSetAttributeCommandHandler(engine));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        if (engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
        {
            throw new InvalidOperationException(
                "ActivityDispatchShowcaseMod requires the engine ActivityRuntimeService.");
        }

        _dataPlaneRuntime.RegisterTopic(new ActivityWebUiTopicProducer(
            ActivityDispatchShowcaseIds.Topic,
            activities,
            ActivityPanelProfile.CreateGeneric()));
    }

    private void AttachDataPlaneSession(
        GameEngine engine,
        IBrowserSurface surface,
        WebUiPanelKitManifest manifest)
    {
        WebUiDataPlaneRuntime runtime = _dataPlaneRuntime
            ?? throw new InvalidOperationException("DataPlane runtime must be created before attaching the browser session.");
        WebUiQueuedCommandDispatcher dispatcher = _commandDispatcher
            ?? throw new InvalidOperationException("Command dispatcher must be created before attaching the browser session.");
        runtime.AttachSession(
            ActivityDispatchShowcaseIds.SessionId,
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(runtime, dispatcher);
        foreach (string topic in manifest.DeclaredTopics)
        {
            pump.TrackTopic(topic);
        }

        _dataPlaneSystem = new ActivityDispatchDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
    }

    private WebUiPanelKitManifest LoadPanelKitManifest(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        WebUiDataPlaneRuntime runtime = _dataPlaneRuntime
            ?? throw new InvalidOperationException("DataPlane runtime must be created before loading the activity panel kit manifest.");
        if (engine.VFS == null ||
            !engine.VFS.TryResolveFullPath(ActivityDispatchShowcaseIds.AssetManifestPath, out string manifestPath))
        {
            throw new FileNotFoundException(
                $"Activity panel kit manifest was not found: {ActivityDispatchShowcaseIds.AssetManifestPath}");
        }

        WebUiPanelKitReferenceCatalog catalog =
            ActivityDispatchPanelKitCatalog.Create(runtime.IsTopicRegistered);
        WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(manifestPath, catalog);
        if (!string.Equals(manifest.ManifestId, ActivityDispatchShowcaseIds.ManifestId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected activity panel kit manifest id '{manifest.ManifestId}'.");
        }

        return manifest;
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
            engine.VFS.TryResolveFullPath(ActivityDispatchShowcaseIds.AssetIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException(
            $"Activity dispatch browser app assets were not found: {ActivityDispatchShowcaseIds.AssetIndexPath}");
    }
}
