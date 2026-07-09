using System;
using System.IO;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace ControlPlaneProjectionShowcaseMod.DataPlane
{
    /// <summary>
    /// Capability-gated activation of the dataplane contract. When the host does not provide an
    /// <see cref="IBrowserRuntime"/> service, the dataplane simply does not activate — that is a
    /// capability declaration, not a fallback: the topic/command contract stays dormant until a
    /// browser-capable host launches the mod.
    /// </summary>
    public static class ControlPlaneProjectionDataPlaneInstaller
    {
        private const string AssetIndexPath = "ControlPlaneProjectionShowcaseMod:assets/control-plane-app/index.html";
        private const string CefAutoTimelineEnvKey = "LUDOTS_CONTROL_PLANE_PROJECTION_CEF_AUTO_TIMELINE";
        private const string RefereeUatQuery = "uat=referee-palette";
        private const int BrowserPanelWidth = 420;
        private const int BrowserPanelHeight = 420;

        public static async Task<ControlPlaneProjectionDataPlaneInstallation?> TryInstallAsync(
            GameEngine engine,
            ControlPlaneProjectionScenarioState state,
            IModContext modContext)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(modContext);

            var runtimeKey = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
            if (!engine.TryGetService(runtimeKey, out IBrowserRuntime browserRuntime) || browserRuntime == null)
            {
                modContext.Log("[ControlPlaneProjectionShowcaseMod] No browser runtime capability; dataplane stays inactive.");
                return null;
            }

            IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
                ?? throw new InvalidOperationException("UiSurfaceHost service is missing.");
            EntityCollectionStore store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            ControlPlaneView controlPlaneView = engine.GetService(CoreServiceKeys.ControlPlaneView)
                ?? throw new InvalidOperationException("ControlPlaneView is missing.");

            string assetRoot = ResolveAssetRoot(engine);
            var producer = new ControlPlaneProjectionDataPlane(engine.World, store, controlPlaneView, state);
            var router = new WebUiCommandRouter(
                new ControlPlaneProjectionGenerationResolver(),
                new ControlPlaneProjectionPermissionValidator());
            router.Register(ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, new ControlPlaneProjectionCommandHandler(producer));

            var dispatcher = new WebUiQueuedCommandDispatcher(router);
            var dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
            dataPlaneRuntime.RegisterTopic(producer);

            var resolver = new BrowserAppResourceResolver(assetRoot);
            var viewport = new BrowserViewport(BrowserPanelWidth, BrowserPanelHeight);
            IBrowserSurface surface = await browserRuntime
                .CreateSurfaceAsync(viewport, resolver)
                .ConfigureAwait(false);
            dataPlaneRuntime.AttachSession(
                ControlPlaneProjectionShowcaseIds.WebUiSessionId,
                new BrowserMessageBridgeDataTransport(surface.Messages));

            var pump = new WebUiDataPlaneTickPump(dataPlaneRuntime, dispatcher);
            pump.TrackTopic(ControlPlaneProjectionShowcaseIds.WebUiTopic);
            var pumpSystem = new ControlPlaneProjectionDataPlanePumpSystem(pump);
            engine.RegisterSystem(pumpSystem, SystemGroup.InputCollection);

            var browserContent = new BrowserSurfaceCanvasContent(
                surface,
                hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "ControlPlaneProjection.Showcase",
                UiSurfaceSegment.Overlay,
                priority: 45));
            surfaceHost.Publish(
                lease,
                UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent)));

            Uri navigationUri = IsEnabled(ControlPlaneProjectionShowcaseIds.RefereeUatEnvKey)
                ? BrowserLocalAppUri.Create("/", RefereeUatQuery)
                : IsEnabled(CefAutoTimelineEnvKey)
                ? BrowserLocalAppUri.Create("/", "uat=toggle-revoke")
                : BrowserLocalAppUri.Root;
            await surface.NavigateAsync(new BrowserNavigationRequest(navigationUri)).ConfigureAwait(false);

            modContext.Log("[ControlPlaneProjectionShowcaseMod] Dataplane active: topic " + ControlPlaneProjectionShowcaseIds.WebUiTopic);
            return new ControlPlaneProjectionDataPlaneInstallation(
                surface,
                browserContent,
                dataPlaneRuntime,
                dispatcher,
                pumpSystem,
                surfaceHost,
                lease);
        }

        private static UiElementBuilder BuildBrowserRoot(BrowserSurfaceCanvasContent browserContent)
        {
            return Ui.Canvas(browserContent)
                .Id("control-plane-projection-browser-surface")
                .Width(BrowserPanelWidth)
                .Height(BrowserPanelHeight)
                .Absolute(18f, 96f)
                .ZIndex(35);
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

            throw new DirectoryNotFoundException($"Control plane projection browser app assets were not found: {AssetIndexPath}");
        }

        private static bool IsEnabled(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ControlPlaneProjectionDataPlanePumpSystem : ISystem<float>
    {
        private const float TopicPublishIntervalSeconds = 0.1f;

        private readonly WebUiDataPlaneTickPump _pump;
        private float _secondsSincePublish;
        private bool _disposed;

        public ControlPlaneProjectionDataPlanePumpSystem(WebUiDataPlaneTickPump pump)
        {
            _pump = pump ?? throw new ArgumentNullException(nameof(pump));
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
            if (_secondsSincePublish < TopicPublishIntervalSeconds)
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

    public sealed class ControlPlaneProjectionDataPlaneInstallation : IDisposable
    {
        private readonly IBrowserSurface _surface;
        private readonly BrowserSurfaceCanvasContent _browserContent;
        private readonly WebUiDataPlaneRuntime _dataPlaneRuntime;
        private readonly WebUiQueuedCommandDispatcher _dispatcher;
        private readonly ControlPlaneProjectionDataPlanePumpSystem _pumpSystem;
        private IUiSurfaceHost? _surfaceHost;
        private UiSurfaceLeaseHandle _lease;
        private bool _disposed;

        internal ControlPlaneProjectionDataPlaneInstallation(
            IBrowserSurface surface,
            BrowserSurfaceCanvasContent browserContent,
            WebUiDataPlaneRuntime dataPlaneRuntime,
            WebUiQueuedCommandDispatcher dispatcher,
            ControlPlaneProjectionDataPlanePumpSystem pumpSystem,
            IUiSurfaceHost surfaceHost,
            UiSurfaceLeaseHandle lease)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _browserContent = browserContent ?? throw new ArgumentNullException(nameof(browserContent));
            _dataPlaneRuntime = dataPlaneRuntime ?? throw new ArgumentNullException(nameof(dataPlaneRuntime));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
}
