using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace BrowserReactFlowShowcaseMod;

public sealed class BrowserReactFlowShowcaseModEntry : IMod
{
    private const string BrowserServiceKey = "BrowserRuntime";
    private const string AssetIndexPath = "BrowserReactFlowShowcaseMod:Assets/react-flow-app/index.html";
    private const string PerfModeEnvironmentKey = "LUDOTS_BROWSER_REACT_FLOW_MODE";
    private const string HitTestModeEnvironmentKey = "LUDOTS_BROWSER_REACT_FLOW_HIT_TEST";
    private const int WorldSharedBufferCapacityBytes = 1024 * 1024;

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiDataPlaneSession? _dataPlaneSession;
    private BrowserReactFlowShowcaseWorldTopicProducer? _worldTopic;
    private CancellationTokenSource? _publisherCts;
    private Task? _publisherTask;
    private IModContext? _modContext;
    private bool _perfBaselineMode;
    private int _alphaPassThroughClicks;

    public void OnLoad(IModContext context)
    {
        _modContext = context ?? throw new ArgumentNullException(nameof(context));
        context.Log("[BrowserReactFlowShowcaseMod] Loaded.");
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

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            root.MountScene(BuildMissingRuntimeScene(textMeasurer, imageSizeProvider));
            root.IsDirty = true;
            return;
        }

        string assetRoot = ResolveAssetRoot(context);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(
            Math.Max(1280, (int)MathF.Ceiling(root.Width)),
            Math.Max(720, (int)MathF.Ceiling(root.Height)));
        _perfBaselineMode = IsPerfBaselineMode();

        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        if (!_perfBaselineMode)
        {
            SetupDataPlane(_surface);
        }

        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: ResolveHitTestOptions());
        root.MountScene(BuildBrowserScene(
            textMeasurer,
            imageSizeProvider,
            _browserContent,
            BuildAlphaPassThroughPanel(root, textMeasurer, imageSizeProvider)));
        root.IsDirty = true;

        Uri navigationUri = _perfBaselineMode
            ? BrowserLocalAppUri.Create("/", "perf=baseline")
            : BrowserLocalAppUri.Root;
        await _surface.NavigateAsync(new BrowserNavigationRequest(navigationUri)).ConfigureAwait(false);
    }

    private void SetupDataPlane(IBrowserSurface surface)
    {
        _worldTopic = new BrowserReactFlowShowcaseWorldTopicProducer();
        var router = new WebUiCommandRouter(
            new BrowserReactFlowShowcaseGenerationResolver(),
            new BrowserReactFlowShowcasePermissionValidator());
        router.Register("inspectEntity", new BrowserReactFlowShowcaseCommandHandler(_worldTopic));
        router.Register("issueMoveOrder", new BrowserReactFlowShowcaseCommandHandler(_worldTopic));

        if (surface is not IBrowserSharedBufferSurface sharedBufferSurface)
        {
            throw new InvalidOperationException(
                "BrowserReactFlowShowcaseMod requires a browser surface with shared-buffer support.");
        }

        var store = new BrowserSharedMemoryBufferStore(sharedBufferSurface.SharedBuffers);
        var transport = new BrowserSharedMemoryDataTransport(
            surface.Messages,
            store,
            new[]
            {
                new BrowserSharedMemoryTopicBuffer(
                    BrowserReactFlowShowcaseWorldTopicProducer.TopicName,
                    "browser-react-flow.world.0",
                    WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
                    WorldSharedBufferCapacityBytes)
            });
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(router);
        _dataPlaneRuntime.RegisterTopic(_worldTopic);
        _dataPlaneSession = _dataPlaneRuntime.AttachSession("browser-react-flow-showcase", transport);
        StartPublisher();
    }

    private void StartPublisher()
    {
        if (_dataPlaneRuntime == null || _dataPlaneSession == null || _worldTopic == null)
        {
            return;
        }

        _publisherCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _publisherCts.Token;
        WebUiDataPlaneRuntime runtime = _dataPlaneRuntime;
        WebUiDataPlaneSession session = _dataPlaneSession;
        BrowserReactFlowShowcaseWorldTopicProducer worldTopic = _worldTopic;
        _publisherTask = Task.Run(async () =>
        {
            int tick = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                tick++;
                await runtime.PublishAsync(worldTopic.CreateDeltaPacket(session.SessionId), cancellationToken).ConfigureAwait(false);
                if (tick % 12 == 0)
                {
                    await runtime.PublishAsync(worldTopic.CreateBinarySnapshotPacket(session.SessionId), cancellationToken).ConfigureAwait(false);
                }
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
        BrowserSurfaceCanvasContent browserContent,
        UiElementBuilder passThroughPanel)
    {
        UiElementBuilder root = Ui.Panel(
                passThroughPanel,
                Ui.Canvas(browserContent)
                    .Id("react-flow-browser-surface")
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Absolute(0f, 0f)
                    .ZIndex(20))
            .Id("react-flow-browser-stack")
            .WidthPercent(100f)
            .HeightPercent(100f);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root);
    }

    private UiElementBuilder BuildAlphaPassThroughPanel(
        UIRoot root,
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider)
    {
        return Ui.Column(
                Ui.Text("Native Ludots hit-test layer").FontSize(16f).Bold(),
                Ui.Text($"Transparent web pixels pass through here: {_alphaPassThroughClicks}").FontSize(13f),
                Ui.Button("Native Click Target", _ =>
                {
                    _alphaPassThroughClicks++;
                    BrowserSurfaceCanvasContent browserContent = _browserContent
                        ?? throw new InvalidOperationException("Browser content is not mounted.");
                    root.MountScene(BuildBrowserScene(
                        textMeasurer,
                        imageSizeProvider,
                        browserContent,
                        BuildAlphaPassThroughPanel(root, textMeasurer, imageSizeProvider)));
                    root.IsDirty = true;
                }))
            .Id("react-flow-alpha-pass-through-target")
            .Width(320f)
            .Height(160f)
            .Padding(14f)
            .Gap(10f)
            .Background("#113026")
            .Outline(2f, RequireUiColor("#7EE7B2"))
            .Absolute(520f, 300f)
            .ZIndex(5);
    }

    private static UiColor RequireUiColor(string color)
    {
        if (!UiColor.TryParse(color, out UiColor parsed))
        {
            throw new InvalidOperationException($"Invalid UI color literal: {color}");
        }

        return parsed;
    }

    private static UiScene BuildMissingRuntimeScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        UiElementBuilder root = Ui.Column(
                Ui.Text("Browser runtime missing").FontSize(32f).Bold(),
                Ui.Text("Run this showcase with the CEF runtime preset to load the packaged React Flow web app."))
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

        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine != null &&
            engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string ResolveAssetRoot(ScriptContext context)
    {
        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine?.VFS != null &&
            engine.VFS.TryResolveFullPath(AssetIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException($"React Flow browser app assets were not found: {AssetIndexPath}");
    }

    private static bool IsPerfBaselineMode()
    {
        string? mode = Environment.GetEnvironmentVariable(PerfModeEnvironmentKey);
        return string.Equals(mode, "baseline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "perf", StringComparison.OrdinalIgnoreCase);
    }

    private static BrowserSurfaceHitTestOptions ResolveHitTestOptions()
    {
        string? mode = Environment.GetEnvironmentVariable(HitTestModeEnvironmentKey);
        return string.Equals(mode, "bounds", StringComparison.OrdinalIgnoreCase)
            ? BrowserSurfaceHitTestOptions.Bounds
            : BrowserSurfaceHitTestOptions.Alpha();
    }
}
