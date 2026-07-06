using System;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Browser;
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
        public static async Task<bool> TryInstallAsync(
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
                return false;
            }

            EntityCollectionStore store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            ControlPlaneView controlPlaneView = engine.GetService(CoreServiceKeys.ControlPlaneView)
                ?? throw new InvalidOperationException("ControlPlaneView is missing.");

            var producer = new ControlPlaneProjectionDataPlane(engine.World, store, controlPlaneView, state);
            var router = new WebUiCommandRouter(
                new ControlPlaneProjectionGenerationResolver(),
                new ControlPlaneProjectionPermissionValidator());
            router.Register(ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, new ControlPlaneProjectionCommandHandler(producer));

            var dispatcher = new WebUiQueuedCommandDispatcher(router);
            var dataPlaneRuntime = new WebUiDataPlaneRuntime(dispatcher);
            dataPlaneRuntime.RegisterTopic(producer);

            // No HTML app ships with this showcase; the surface exists so a CEF-hosted client can
            // subscribe to the topic over the message bridge once it navigates its own app.
            IBrowserSurface surface = await browserRuntime
                .CreateSurfaceAsync(new BrowserViewport(1280, 720))
                .ConfigureAwait(false);
            dataPlaneRuntime.AttachSession(
                ControlPlaneProjectionShowcaseIds.WebUiSessionId,
                new BrowserMessageBridgeDataTransport(surface.Messages));

            var pump = new WebUiDataPlaneTickPump(dataPlaneRuntime, dispatcher);
            pump.TrackTopic(ControlPlaneProjectionShowcaseIds.WebUiTopic);
            engine.RegisterSystem(new ControlPlaneProjectionDataPlanePumpSystem(pump), SystemGroup.InputCollection);
            modContext.Log("[ControlPlaneProjectionShowcaseMod] Dataplane active: topic " + ControlPlaneProjectionShowcaseIds.WebUiTopic);
            return true;
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
}
