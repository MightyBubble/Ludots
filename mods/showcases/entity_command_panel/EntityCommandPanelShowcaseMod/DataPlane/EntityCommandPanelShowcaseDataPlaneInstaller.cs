using System;
using System.IO;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace EntityCommandPanelShowcaseMod.DataPlane
{
    public static class EntityCommandPanelShowcaseDataPlaneInstaller
    {
        private const int BrowserPanelWidth = 1280;
        private const int BrowserPanelHeight = 356;
        private const int BrowserPanelMarginBottom = 0;
        private const int DefaultViewportWidth = 1600;
        private const int DefaultViewportHeight = 900;

        public static async Task<EntityCommandPanelShowcaseDataPlaneInstallation?> TryInstallAsync(
            GameEngine engine,
            IModContext modContext)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(modContext);

            var runtimeKey = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
            if (!engine.TryGetService(runtimeKey, out IBrowserRuntime browserRuntime) || browserRuntime == null)
            {
                modContext.Log("[EntityCommandPanelShowcaseMod] No browser runtime capability; WebUI dataplane stays inactive.");
                return null;
            }

            IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
                ?? throw new InvalidOperationException("UiSurfaceHost service is missing.");
            string assetRoot = ResolveAssetRoot(engine);
            var producer = new EntityCommandPanelShowcaseDataPlane(engine);
            var router = new WebUiCommandRouter(
                new EntityCommandPanelShowcaseGenerationResolver(),
                new EntityCommandPanelShowcasePermissionValidator());
            router.Register(
                EntityCommandPanelShowcaseIds.SetProfileCommand,
                new EntityCommandPanelShowcaseCommandHandler(producer));

            var dispatcher = new WebUiQueuedCommandDispatcher(router);
            var dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
            dataPlaneRuntime.RegisterTopic(producer);

            var resolver = new BrowserAppResourceResolver(assetRoot);
            var viewport = new BrowserViewport(ResolveBrowserSurfaceWidth(engine), BrowserPanelHeight);
            IBrowserSurface surface = await browserRuntime
                .CreateSurfaceAsync(viewport, resolver)
                .ConfigureAwait(false);
            dataPlaneRuntime.AttachSession(
                EntityCommandPanelShowcaseIds.WebUiSessionId,
                new BrowserMessageBridgeDataTransport(surface.Messages));

            var pump = new WebUiDataPlaneTickPump(dataPlaneRuntime, dispatcher);
            pump.TrackTopic(EntityCommandPanelShowcaseIds.WebUiTopic);
            var pumpSystem = new EntityCommandPanelShowcaseDataPlanePumpSystem(pump);
            engine.RegisterPresentationSystem(pumpSystem);

            var browserContent = new BrowserSurfaceCanvasContent(
                surface,
                hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "EntityCommandPanel.Showcase.WebUI",
                UiSurfaceSegment.Overlay,
                priority: 96));
            surfaceHost.Publish(
                lease,
                UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(engine, browserContent)));

            await surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
            modContext.Log("[EntityCommandPanelShowcaseMod] WebUI dataplane active: topic " + EntityCommandPanelShowcaseIds.WebUiTopic);
            return new EntityCommandPanelShowcaseDataPlaneInstallation(
                surface,
                browserContent,
                dataPlaneRuntime,
                dispatcher,
                pumpSystem,
                surfaceHost,
                lease);
        }

        private static UiElementBuilder BuildBrowserRoot(GameEngine engine, BrowserSurfaceCanvasContent browserContent)
        {
            (float viewportWidth, float viewportHeight) = ResolveVisibleViewport(engine);
            int surfaceWidth = ResolveBrowserSurfaceWidth(engine);
            float left = MathF.Max(0f, (viewportWidth - surfaceWidth) * 0.5f);
            float top = MathF.Max(0f, viewportHeight - BrowserPanelHeight - BrowserPanelMarginBottom);
            return Ui.Canvas(browserContent)
                .Id("entity-command-panel-showcase-browser-surface")
                .Width(surfaceWidth)
                .Height(BrowserPanelHeight)
                .Absolute(left, top)
                .ZIndex(96);
        }

        private static int ResolveBrowserSurfaceWidth(GameEngine engine)
        {
            return Math.Max(
                BrowserPanelWidth,
                engine.MergedConfig.WindowWidth > 0 ? engine.MergedConfig.WindowWidth : DefaultViewportWidth);
        }

        private static (float Width, float Height) ResolveVisibleViewport(GameEngine engine)
        {
            float viewportWidth = engine.MergedConfig.WindowWidth > 0 ? engine.MergedConfig.WindowWidth : DefaultViewportWidth;
            float viewportHeight = engine.MergedConfig.WindowHeight > 0 ? engine.MergedConfig.WindowHeight : DefaultViewportHeight;
            if ((viewportWidth <= 0f || viewportHeight <= 0f) &&
                engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                viewportWidth = root.Width > 0f ? root.Width : viewportWidth;
                viewportHeight = root.Height > 0f ? root.Height : viewportHeight;
            }

            return (viewportWidth, viewportHeight);
        }

        private static string ResolveAssetRoot(GameEngine engine)
        {
            if (engine.VFS != null &&
                engine.VFS.TryResolveFullPath(EntityCommandPanelShowcaseIds.AssetIndexPath, out string indexPath))
            {
                string? root = Path.GetDirectoryName(indexPath);
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    return root;
                }
            }

            throw new DirectoryNotFoundException(
                $"Entity command panel browser app assets were not found: {EntityCommandPanelShowcaseIds.AssetIndexPath}");
        }
    }

    internal sealed class EntityCommandPanelShowcaseDataPlanePumpSystem : ISystem<float>
    {
        private readonly WebUiDataPlaneTickPump _pump;
        private bool _disposed;

        public EntityCommandPanelShowcaseDataPlanePumpSystem(WebUiDataPlaneTickPump pump)
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

    public sealed class EntityCommandPanelShowcaseDataPlaneInstallation : IDisposable
    {
        private readonly IBrowserSurface _surface;
        private readonly BrowserSurfaceCanvasContent _browserContent;
        private readonly WebUiDataPlaneRuntime _dataPlaneRuntime;
        private readonly WebUiQueuedCommandDispatcher _dispatcher;
        private readonly EntityCommandPanelShowcaseDataPlanePumpSystem _pumpSystem;
        private IUiSurfaceHost? _surfaceHost;
        private UiSurfaceLeaseHandle _lease;
        private bool _disposed;

        internal EntityCommandPanelShowcaseDataPlaneInstallation(
            IBrowserSurface surface,
            BrowserSurfaceCanvasContent browserContent,
            WebUiDataPlaneRuntime dataPlaneRuntime,
            WebUiQueuedCommandDispatcher dispatcher,
            EntityCommandPanelShowcaseDataPlanePumpSystem pumpSystem,
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
