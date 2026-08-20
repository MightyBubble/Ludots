using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.Browser;

/// <summary>
/// Engine-side "web" panel skin: renders visible panels in an offscreen browser
/// surface fed by a DataPlane topic. Installed by host composers when a browser
/// runtime is provisioned; selection is game.json "panelSkin": "web" plus
/// "panelWebApp" pointing at the mod's overlay index.html. Authors write zero C#.
/// </summary>
public static class PanelWebSkinInstaller
{
    public static bool TryInstall(GameEngine engine, IBrowserRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(runtime);

        GameConfig config = engine.MergedConfig
            ?? throw new InvalidOperationException("Panel web skin requires merged game config.");
        engine.RegisterPresentationSystem(new PanelWebSkinSystem(engine, runtime, config));
        return true;
    }
}

internal sealed class PanelWebSkinSystem : ISystem<float>
{
    private const float TopicPublishIntervalSeconds = 0.25f;
    private const int SurfaceWidth = 320;
    private const int SurfaceHeight = 220;
    private const float SurfaceMargin = 24f;

    private readonly GameEngine _engine;
    private readonly IBrowserRuntime _runtime;
    private readonly GameConfig _config;
    private readonly PanelHost _panelHost;
    private readonly PanelTemplateRegistry _templates;
    private readonly UiPanelActivationStore _activation;
    private readonly IUiSurfaceHost _surfaceHost;
    private readonly UIRoot _root;

    private readonly Dictionary<string, WebPanel> _panels = new(StringComparer.Ordinal);
    private float _secondsSincePublish;
    private bool _disposed;
    private bool _initFailed;

    public PanelWebSkinSystem(GameEngine engine, IBrowserRuntime runtime, GameConfig config)
    {
        _engine = engine;
        _runtime = runtime;
        _config = config;
        _panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("Panel web skin requires PanelHost.");
        _templates = engine.GetService(CoreServiceKeys.PanelTemplateRegistry)
            ?? throw new InvalidOperationException("Panel web skin requires PanelTemplateRegistry.");
        _activation = engine.GetService(CoreServiceKeys.PanelActivationStore)
            ?? throw new InvalidOperationException("Panel web skin requires PanelActivationStore.");
        _surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Panel web skin requires UiSurfaceHost.");
        _root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Panel web skin requires UIRoot.");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        // Panels appear per template id; one web surface per template (single-instance
        // showcase contract; multi-instance stacking is the W5 slice).
        foreach (PanelHostInstanceInfo info in _panelHost.SnapshotInstances())
        {
            if (!_activation.IsVisible(info.TemplateId) ||
                _panels.ContainsKey(info.TemplateId) ||
                !IsWebRouted(info))
            {
                continue;
            }

            var panel = new WebPanel(info.TemplateId);
            _panels[info.TemplateId] = panel;
            _ = InitializePanelAsync(panel);
        }

        foreach (WebPanel panel in _panels.Values)
        {
            if (panel.Surface != null)
            {
                _surfaceHost.Invalidate(panel.Lease);
            }
        }

        _secondsSincePublish += MathF.Max(0f, dt);
        if (_secondsSincePublish < TopicPublishIntervalSeconds)
        {
            return;
        }

        _secondsSincePublish = 0f;
        foreach (WebPanel panel in _panels.Values)
        {
            if (panel.Pump != null)
            {
                panel.Pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
                panel.Pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (WebPanel panel in _panels.Values)
        {
            TearDownPanel(panel);
        }

        _panels.Clear();
    }

    private async Task InitializePanelAsync(WebPanel panel)
    {
        try
        {
            string assetRoot = ResolveAssetRoot();
            var resolver = new BrowserAppResourceResolver(assetRoot);
            IBrowserSurface surface = await _runtime
                .CreateSurfaceAsync(new BrowserViewport(SurfaceWidth, SurfaceHeight), resolver)
                .ConfigureAwait(false);

            var dataPlane = new WebUiDataPlaneRuntime();
            dataPlane.RegisterTopic(new PanelTemplateTopicProducer(_panelHost, _templates, _activation, panel.TemplateId));
            dataPlane.AttachSession(
                $"panel-web-{panel.TemplateId}",
                new BrowserMessageBridgeDataTransport(surface.Messages));
            var pump = new WebUiDataPlaneTickPump(dataPlane);
            pump.TrackTopic(panel.Topic);

            var canvasContent = new PanelWebCanvasContent(surface, _root);
            UiSurfaceLeaseHandle lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                $"panel-web-skin:{panel.TemplateId}",
                UiSurfaceSegment.Main,
                priority: 100));
            _surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                () => Ui.Canvas(canvasContent)
                    .Id($"panel-web-skin-{panel.TemplateId}")
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Absolute(0f, 0f)
                    .ZIndex(24)));

            await surface.NavigateAsync(new BrowserNavigationRequest(
                BrowserLocalAppUri.Create("/", "topic=" + Uri.EscapeDataString(panel.Topic)))).ConfigureAwait(false);

            panel.Attach(surface, dataPlane, pump, lease, canvasContent);
        }
        catch (Exception ex)
        {
            _initFailed = true;
            Ludots.Core.Diagnostics.Log.Error(
                in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"[PanelWebSkin] failed to initialize web panel '{panel.TemplateId}': {ex.Message}");
        }
    }

    private bool IsWebRouted(PanelHostInstanceInfo info)
    {
        return string.Equals(
            info.Skin ?? _templates.Require(info.TemplateId).Skin ?? _config.PanelSkin,
            "web",
            StringComparison.Ordinal);
    }

    private string ResolveAssetRoot()
    {
        string appIndexPath = _config.PanelWebApp ??
            throw new InvalidOperationException(
                "panelSkin \"web\" requires game.json \"panelWebApp\" — a mod-VFS path to the overlay index.html.");
        if (_engine.VFS != null &&
            _engine.VFS.TryResolveFullPath(appIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new InvalidOperationException(
            $"Panel web skin cannot resolve overlay app '{appIndexPath}' through the mod VFS.");
    }

    private void TearDownPanel(WebPanel panel)
    {
        if (panel.Lease != default)
        {
            _surfaceHost.Release(panel.Lease);
        }

        panel.DataPlane?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        panel.CanvasContent?.Dispose();
        panel.Surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class WebPanel
    {
        public WebPanel(string templateId)
        {
            TemplateId = templateId;
            Topic = $"ludots.panel.{templateId}";
        }

        public string TemplateId { get; }
        public string Topic { get; }
        public IBrowserSurface? Surface { get; private set; }
        public WebUiDataPlaneRuntime? DataPlane { get; private set; }
        public WebUiDataPlaneTickPump? Pump { get; private set; }
        public UiSurfaceLeaseHandle Lease { get; private set; }
        public PanelWebCanvasContent? CanvasContent { get; private set; }

        public void Attach(
            IBrowserSurface surface,
            WebUiDataPlaneRuntime dataPlane,
            WebUiDataPlaneTickPump pump,
            UiSurfaceLeaseHandle lease,
            PanelWebCanvasContent canvasContent)
        {
            Surface = surface;
            DataPlane = dataPlane;
            Pump = pump;
            Lease = lease;
            CanvasContent = canvasContent;
        }
    }

    /// <summary>
    /// Publishes every template variable of the first visible instance as a flat
    /// LatestWins snapshot; the page reads fields by variable name. No per-skin
    /// field mapping exists anywhere.
    /// </summary>
    private sealed class PanelTemplateTopicProducer : IWebUiTopicProducer
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly PanelHost _host;
        private readonly PanelTemplateRegistry _templates;
        private readonly UiPanelActivationStore _activation;
        private readonly string _templateId;

        public PanelTemplateTopicProducer(
            PanelHost host,
            PanelTemplateRegistry templates,
            UiPanelActivationStore activation,
            string templateId)
        {
            _host = host;
            _templates = templates;
            _activation = activation;
            _templateId = templateId;
        }

        public string Topic => $"ludots.panel.{_templateId}";

        public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
        {
            PanelInstanceHandle handle = FindVisibleInstance();
            if (handle == default)
            {
                packet = CreatePacket(new { ready = false }, in context);
                return true;
            }

            if (!_host.TryGetValues(handle, out PanelVariableSet values))
            {
                packet = CreatePacket(new { ready = false }, in context);
                return true;
            }

            PanelTemplate template = _templates.Require(_templateId);
            var payload = new Dictionary<string, object?>(template.Variables.Count + 1, StringComparer.Ordinal)
            {
                ["ready"] = true,
            };

            foreach (PanelTemplateVariable variable in template.Variables)
            {
                payload[variable.Name] = values.Get(variable.Name);
            }

            packet = CreatePacket(payload, in context);
            return true;
        }

        private PanelInstanceHandle FindVisibleInstance()
        {
            foreach (PanelHostInstanceInfo info in _host.SnapshotInstances())
            {
                if (string.Equals(info.TemplateId, _templateId, StringComparison.Ordinal) &&
                    _activation.IsVisible(info.TemplateId))
                {
                    return info.Handle;
                }
            }

            return default;
        }

        private WebUiOutboundPacket CreatePacket(object payload, in WebUiTopicContext context)
        {
            return new WebUiOutboundPacket(
                context.SessionId,
                Topic,
                WebUiPacketKind.Snapshot,
                WebUiDeliverySemantics.LatestWins,
                JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions),
                "application/json",
                context.RequestId);
        }
    }

    private sealed class PanelWebCanvasContent : BrowserSurfaceCanvasContent
    {
        private readonly UIRoot _root;

        public PanelWebCanvasContent(IBrowserSurface surface, UIRoot root)
            : base(surface)
        {
            _root = root;
        }

        public override UiRect GetContentRect(UiNode node)
        {
            float x = MathF.Max(SurfaceMargin, _root.Width - SurfaceWidth - SurfaceMargin);
            return new UiRect(x, SurfaceMargin, SurfaceWidth, SurfaceHeight);
        }
    }
}
