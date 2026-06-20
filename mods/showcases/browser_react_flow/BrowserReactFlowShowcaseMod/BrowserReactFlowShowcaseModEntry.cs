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

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiDataPlaneSession? _dataPlaneSession;
    private BrowserReactFlowShowcaseWorldTopicProducer? _worldTopic;
    private CancellationTokenSource? _publisherCts;
    private Task? _publisherTask;
    private IModContext? _modContext;

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

        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);
        SetupDataPlane(_surface);

        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
        root.MountScene(BuildBrowserScene(textMeasurer, imageSizeProvider, _browserContent));
        root.IsDirty = true;

        await _surface.NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-browser-showcase:///"))).ConfigureAwait(false);
    }

    private void SetupDataPlane(IBrowserSurface surface)
    {
        _worldTopic = new BrowserReactFlowShowcaseWorldTopicProducer();
        var router = new WebUiCommandRouter(
            new BrowserReactFlowShowcaseGenerationResolver(),
            new BrowserReactFlowShowcasePermissionValidator());
        router.Register("inspectEntity", new BrowserReactFlowShowcaseCommandHandler(_worldTopic));
        router.Register("issueMoveOrder", new BrowserReactFlowShowcaseCommandHandler(_worldTopic));

        var transport = new BrowserMessageBridgeDataTransport(surface.Messages);
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
        BrowserSurfaceCanvasContent browserContent)
    {
        UiElementBuilder root = Ui.Canvas(browserContent)
            .Id("react-flow-browser-surface")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .FlexGrow(1f);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root);
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
}
