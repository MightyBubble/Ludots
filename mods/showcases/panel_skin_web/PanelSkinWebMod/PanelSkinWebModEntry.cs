using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace PanelSkinWebMod;

/// <summary>
/// Real CEF web skin: the fireball status panel rendered by the Ludots web UI stack —
/// BrowserRuntime surface + DataPlane topic push + composited canvas in the surface host.
/// Headless hosts without IBrowserRuntime skip the overlay (ControlPlane precedent).
/// </summary>
public sealed class PanelSkinWebModEntry : IMod
{
    private const string Topic = "ludots.showcase.fireball.status";
    private const string SessionId = "fireball-web-skin";
    private const string AssetIndexPath = "PanelSkinWebMod:Assets/overlay-app/index.html";
    private const int CanvasWidth = 320;
    private const int CanvasHeight = 220;
    private const int CanvasMargin = 24;

    private IBrowserSurface? _surface;
    private FireballWebSkinCanvasContent? _canvasContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private UiSurfaceLeaseHandle _lease;
    private bool _leased;

    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinWebMod] Loaded — CEF web UI skin");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
        }

        _canvasContent?.Dispose();
        _canvasContent = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("PanelSkinWebMod requires GameEngine in ScriptContext.");
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("PanelSkinWebMod requires UiSurfaceHost in ScriptContext.");
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("PanelSkinWebMod requires UIRoot in ScriptContext.");

        if (!TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            Ludots.Core.Diagnostics.Log.Info(
                in Ludots.Core.Diagnostics.LogChannels.Engine,
                "[PanelSkinWebMod] host provides no BrowserRuntime; CEF overlay skipped (headless branch).");
            return;
        }

        var panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelSkinWebMod requires PanelHost.");

        string assetRoot = ResolveAssetRoot(engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        var viewport = new BrowserViewport(CanvasWidth, CanvasHeight);
        _surface = await runtime.CreateSurfaceAsync(viewport, resolver).ConfigureAwait(false);

        _dataPlaneRuntime = new WebUiDataPlaneRuntime();
        _dataPlaneRuntime.RegisterTopic(new FireballWebSkinTopicProducer(panelHost));
        _dataPlaneRuntime.AttachSession(SessionId, new BrowserMessageBridgeDataTransport(_surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime);
        pump.TrackTopic(Topic);

        _canvasContent = new FireballWebSkinCanvasContent(_surface, root, CanvasWidth, CanvasHeight, CanvasMargin);
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest("fireball-web-skin", UiSurfaceSegment.Main, priority: 100));
        _leased = true;
        surfaceHost.Publish(_lease, UiSurfaceContribution.FromBuilder(
            () => Ui.Canvas(_canvasContent)
                .Id("fireball-web-skin-surface")
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(24)));
        engine.RegisterSystem(new FireballWebSkinDataPlaneSystem(pump, surfaceHost, _lease), SystemGroup.InputCollection);

        await _surface.NavigateAsync(new BrowserNavigationRequest(
            BrowserLocalAppUri.Create("/", "topic=" + Uri.EscapeDataString(Topic)))).ConfigureAwait(false);
        Ludots.Core.Diagnostics.Log.Info(
            in Ludots.Core.Diagnostics.LogChannels.Engine,
            "[PanelSkinWebMod] CEF web skin surface mounted.");
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

        throw new DirectoryNotFoundException($"Fireball web skin overlay app assets were not found: {AssetIndexPath}");
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        runtime = context.Get(key)!;
        return runtime != null;
    }
}
