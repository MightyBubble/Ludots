using Arch.System;
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

namespace GraphWorkbenchShowcaseMod.DataPlane;

public static class GraphWorkbenchDataPlaneInstaller
{
    private const string AssetIndexPath = "GraphWorkbenchShowcaseMod:Assets/graph-workbench-app/index.html";
    private const float TopicPublishIntervalSeconds = 0.08f;

    public static async Task<GraphWorkbenchDataPlaneInstallation> InstallAsync(
        GameEngine engine,
        IModContext modContext)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(modContext);

        var runtimeKey = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (!engine.TryGetService(runtimeKey, out IBrowserRuntime browserRuntime) || browserRuntime == null)
        {
            throw new InvalidOperationException("GraphWorkbenchShowcaseMod requires the CEF browser runtime.");
        }

        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing.");
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing.");

        string assetRoot = ResolveAssetRoot(engine);
        var producer = new GraphWorkbenchDataPlane(engine.World);
        var router = new WebUiCommandRouter(
            new GraphWorkbenchGenerationResolver(),
            new GraphWorkbenchPermissionValidator());
        var commandHandler = new GraphWorkbenchCommandHandler(producer);
        router.Register(GraphWorkbenchShowcaseIds.SelectEntityCommand, commandHandler);
        router.Register(GraphWorkbenchShowcaseIds.EditDocumentCommand, commandHandler);
        router.Register(GraphWorkbenchShowcaseIds.CompileDocumentCommand, commandHandler);
        router.Register(GraphWorkbenchShowcaseIds.SetActiveDocumentCommand, commandHandler);

        var dispatcher = new WebUiQueuedCommandDispatcher(router);
        var dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
        dataPlaneRuntime.RegisterTopic(producer);

        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));
        IBrowserSurface surface = await browserRuntime
            .CreateSurfaceAsync(viewport, resolver)
            .ConfigureAwait(false);
        dataPlaneRuntime.AttachSession(
            GraphWorkbenchShowcaseIds.WebUiSessionId,
            new BrowserMessageBridgeDataTransport(surface.Messages));

        var runtimeSystem = new GraphWorkbenchRuntimeTickSystem(producer);
        engine.RegisterSystem(runtimeSystem, SystemGroup.InputCollection);

        var pump = new WebUiDataPlaneTickPump(dataPlaneRuntime, dispatcher);
        pump.TrackTopic(GraphWorkbenchShowcaseIds.WebUiTopic);
        var pumpSystem = new GraphWorkbenchDataPlanePumpSystem(pump, TopicPublishIntervalSeconds);
        engine.RegisterSystem(pumpSystem, SystemGroup.InputCollection);

        var browserContent = new BrowserSurfaceCanvasContent(
            surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
        UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "GraphWorkbench.Showcase",
            UiSurfaceSegment.Main,
            priority: 55,
            exclusive: true));
        surfaceHost.Publish(
            lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

        await surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
        modContext.Log("[GraphWorkbenchShowcaseMod] Dataplane active: topic " + GraphWorkbenchShowcaseIds.WebUiTopic);

        return new GraphWorkbenchDataPlaneInstallation(
            surface,
            browserContent,
            dataPlaneRuntime,
            dispatcher,
            runtimeSystem,
            pumpSystem,
            surfaceHost,
            lease);
    }

    private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
    {
        return Ui.Canvas(browserContent)
            .Id("graph-workbench-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(45);
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

        throw new DirectoryNotFoundException($"Graph workbench browser app assets were not found: {AssetIndexPath}");
    }
}

internal sealed class GraphWorkbenchRuntimeTickSystem : ISystem<float>
{
    private readonly GraphWorkbenchDataPlane _dataPlane;
    private bool _disposed;

    public GraphWorkbenchRuntimeTickSystem(GraphWorkbenchDataPlane dataPlane)
    {
        _dataPlane = dataPlane ?? throw new ArgumentNullException(nameof(dataPlane));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!_disposed)
        {
            _dataPlane.AdvanceRuntime(dt);
        }
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

internal sealed class GraphWorkbenchDataPlanePumpSystem : ISystem<float>
{
    private readonly WebUiDataPlaneTickPump _pump;
    private readonly float _publishIntervalSeconds;
    private float _secondsSincePublish;
    private bool _disposed;

    public GraphWorkbenchDataPlanePumpSystem(WebUiDataPlaneTickPump pump, float publishIntervalSeconds)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _publishIntervalSeconds = publishIntervalSeconds > 0f
            ? publishIntervalSeconds
            : throw new ArgumentOutOfRangeException(nameof(publishIntervalSeconds));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        _pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
        _secondsSincePublish += MathF.Max(0f, dt);
        if (_secondsSincePublish < _publishIntervalSeconds)
        {
            return;
        }

        _secondsSincePublish = 0f;
        _pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

public sealed class GraphWorkbenchDataPlaneInstallation : IDisposable
{
    private readonly IBrowserSurface _surface;
    private readonly BrowserSurfaceCanvasContent _browserContent;
    private readonly WebUiDataPlaneRuntime _dataPlaneRuntime;
    private readonly WebUiQueuedCommandDispatcher _dispatcher;
    private readonly GraphWorkbenchRuntimeTickSystem _runtimeSystem;
    private readonly GraphWorkbenchDataPlanePumpSystem _pumpSystem;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private bool _disposed;

    internal GraphWorkbenchDataPlaneInstallation(
        IBrowserSurface surface,
        BrowserSurfaceCanvasContent browserContent,
        WebUiDataPlaneRuntime dataPlaneRuntime,
        WebUiQueuedCommandDispatcher dispatcher,
        GraphWorkbenchRuntimeTickSystem runtimeSystem,
        GraphWorkbenchDataPlanePumpSystem pumpSystem,
        IUiSurfaceHost surfaceHost,
        UiSurfaceLeaseHandle lease)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _browserContent = browserContent ?? throw new ArgumentNullException(nameof(browserContent));
        _dataPlaneRuntime = dataPlaneRuntime ?? throw new ArgumentNullException(nameof(dataPlaneRuntime));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _runtimeSystem = runtimeSystem ?? throw new ArgumentNullException(nameof(runtimeSystem));
        _pumpSystem = pumpSystem ?? throw new ArgumentNullException(nameof(pumpSystem));
        _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
        _lease = lease;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtimeSystem.Dispose();
        _pumpSystem.Dispose();
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _surfaceHost = null;
        _browserContent.Dispose();
        _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dispatcher.Dispose();
        _surface.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
