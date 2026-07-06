using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using LiveMapEditorMod.Runtime;
using LiveMapEditorMod.WebUi;

namespace LiveMapEditorMod.UI;

internal sealed class LiveMapEditorPanelController : IAsyncDisposable, IDisposable
{
    private readonly LiveMapEditorRuntime _runtime;
    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private bool _initialized;

    public LiveMapEditorPanelController(LiveMapEditorRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task InitializeAsync(ScriptContext context)
    {
        if (_initialized)
        {
            return;
        }

        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("LiveMapEditorPanelController requires GameEngine.");
        _surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing.");
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing.");
        if (!TryGetBrowserRuntime(context, engine, out IBrowserRuntime browserRuntime))
        {
            throw new InvalidOperationException("LiveMapEditorMod requires a host-owned IBrowserRuntime service.");
        }

        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1024, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));
        _surface = await browserRuntime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        SetupDataPlane(engine, _surface);
        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            BrowserSurfaceHitTestOptions.Alpha());
        await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
        _initialized = true;
    }

    public void Toggle()
    {
        if (_runtime.PanelOpen)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (!_initialized ||
            _surfaceHost == null ||
            _browserContent == null)
        {
            return;
        }

        if (!_lease.IsValid || !_surfaceHost.Revalidate(_lease))
        {
            _lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                LiveMapEditorIds.OwnerId,
                UiSurfaceSegment.Main,
                priority: 100,
                exclusive: true));
        }

        _surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(_browserContent)));
        _runtime.PanelOpen = true;
        Ludots.Core.Diagnostics.Log.Info(
            in Ludots.Core.Diagnostics.LogChannels.Presentation,
            "[LiveMapEditorMod] Panel shown (exclusive Main lease published).");
    }

    public void Hide()
    {
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _runtime.PanelOpen = false;
    }

    public void Dispose()
    {
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _browserContent?.Dispose();
        _browserContent = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _browserContent?.Dispose();
        _browserContent = null;
        if (_surface != null)
        {
            await _surface.DisposeAsync().ConfigureAwait(false);
            _surface = null;
        }
    }

    private void SetupDataPlane(GameEngine engine, IBrowserSurface surface)
    {
        var router = new WebUiCommandRouter(
            new LiveMapEditorGenerationResolver(engine),
            new LiveMapEditorPermissionValidator(engine));
        var handler = new LiveMapEditorCommandHandler(engine, _runtime);
        router.Register("setTool", handler);
        router.Register("setBrush", handler);
        router.Register("paintTerrain", handler);
        router.Register("bucketFillWater", handler);
        router.Register("placeEntity", handler);
        router.Register("selectEntity", handler);
        router.Register("removeEntity", handler);
        router.Register("setObstacle", handler);
        router.Register("placeObstacle", handler);
        router.Register("eraseObstacle", handler);
        router.Register("setEntityOverride", handler);
        router.Register("deleteEntityOverride", handler);
        router.Register("navConfigReload", handler);
        router.Register("navConfigSave", handler);
        router.Register("navAddProfile", handler);
        router.Register("navDeleteProfile", handler);
        router.Register("navAddBakeProfile", handler);
        router.Register("navDeleteBakeProfile", handler);
        router.Register("navAddLayer", handler);
        router.Register("navDeleteLayer", handler);
        router.Register("navAddArea", handler);
        router.Register("navDeleteArea", handler);
        router.Register("navSetMode", handler);
        router.Register("navSetAlgorithm", handler);
        router.Register("navSetRuntimeField", handler);
        router.Register("setBakeOptions", handler);
        router.Register("estimateNavBake", handler);
        router.Register("rebakeNav", handler);
        router.Register("rebakeDirty", handler);
        router.Register("clearNavTiles", handler);
        router.Register("setPathOptions", handler);
        router.Register("queryPath", handler);
        router.Register("setViewToggle", handler);
        router.Register("cameraPanTo", handler);
        router.Register("previewBoardAllocation", handler);
        router.Register("createMap", handler);
        router.Register("addBoard", handler);
        router.Register("deleteBoard", handler);
        router.Register("updateBoard", handler);
        router.Register("selectBoard", handler);
        router.Register("reloadMap", handler);
        router.Register("saveMap", handler);
        router.Register("transportSetMode", handler);
        router.Register("transportSetRoot", handler);
        router.Register("transportAddNode", handler);
        router.Register("transportSelectNode", handler);
        router.Register("transportMoveNode", handler);
        router.Register("transportUpdateNode", handler);
        router.Register("transportDeleteNode", handler);
        router.Register("transportBeginSegment", handler);
        router.Register("transportAppendSegmentPoint", handler);
        router.Register("transportUndoSegmentPoint", handler);
        router.Register("transportCommitSegment", handler);
        router.Register("transportSelectSegment", handler);
        router.Register("transportUpdateSegment", handler);
        router.Register("transportInsertSegmentPoint", handler);
        router.Register("transportMoveSegmentPoint", handler);
        router.Register("transportDeleteSegmentPoint", handler);
        router.Register("transportDeleteSegment", handler);
        router.Register("transportRebake", handler);
        router.Register("transportSetRouteAgent", handler);
        router.Register("transportQueryRoute", handler);
        router.Register("transportSave", handler);

        var queued = new WebUiQueuedCommandDispatcher(router);
        var dataPlane = new WebUiDataPlaneRuntime(queued);
        dataPlane.RegisterTopic(new LiveMapEditorStateTopicProducer(engine, _runtime));
        dataPlane.AttachSession(
            "live-map-editor",
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var tickPump = new WebUiDataPlaneTickPump(dataPlane, queued);
        tickPump.TrackTopic(LiveMapEditorIds.StateTopic);

        _runtime.QueuedCommandDispatcher = queued;
        _runtime.DataPlane = dataPlane;
        _runtime.DataPlaneTickPump = tickPump;
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Panel(
                Ui.Canvas(browserContent)
                    .Id("live-map-editor-browser-surface")
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Absolute(0f, 0f)
                    .ZIndex(80))
            .Id("live-map-editor-browser-stack")
            .WidthPercent(100f)
            .HeightPercent(100f);
    }

    private static bool TryGetBrowserRuntime(
        ScriptContext context,
        GameEngine engine,
        out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(LiveMapEditorIds.BrowserServiceKey);
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
        if (engine.VFS.TryResolveFullPath(LiveMapEditorIds.AssetIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException(
            $"Live map editor web assets were not found: {LiveMapEditorIds.AssetIndexPath}");
    }
}
