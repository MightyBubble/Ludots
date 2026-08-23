using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AgentBridgeMod
{
    /// <summary>
    /// Hosts the Agent Debug Bridge inside a running Ludots process.
    /// Enable by adding this mod to ModPaths; disable at runtime with
    /// LUDOTS_AGENT_BRIDGE=0. See docs/rfcs/RFC-0066-agent-debug-bridge.md.
    /// </summary>
    public sealed class AgentBridgeModEntry : IMod
    {
        private AgentBridgeHttpServer? _server;
        private readonly AgentLogRingBackend _logRing = new();
        private bool _logRingInstalled;

        public static ServiceKey<AgentToolRegistry> ToolRegistryKey { get; } = new("AgentToolRegistry");

        public void OnLoad(IModContext context)
        {
            AgentBridgeConfig config = AgentBridgeConfig.FromEnvironment();
            if (!config.Enabled)
            {
                context.Log("[AgentBridgeMod] Disabled via LUDOTS_AGENT_BRIDGE=0.");
                return;
            }

            context.SystemFactoryRegistry.RegisterPresentation("AgentBridge", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return null!;
                return Activate(engine, config, context);
            });

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.ModLoader.SystemFactoryRegistry.TryActivate("AgentBridge", ctx, engine);
                }

                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        private AgentBridgeSystem Activate(GameEngine engine, AgentBridgeConfig config, IModContext context)
        {
            // Registry, runtime, and controllers are per-activation state: they
            // capture the engine. Rebuild them on every activation and tear
            // down any previous listener first, so a re-activated entry never
            // double-registers tools or leaks a zombie port.
            _server?.Dispose();

            var tools = new AgentToolRegistry();
            var time = new AgentTimeController();
            var recording = new RecordingController();

            string discoveryDir = AgentBridgeConfig.ResolveDiscoveryDirectory(AppContext.BaseDirectory);
            var runtime = new AgentBridgeRuntime(engine, tools) { ArtifactsRoot = discoveryDir };
            runtime.FrameTick += recording.Tick;

            // Process-wide ring survives re-activation; wrap the host backend once.
            if (!_logRingInstalled)
            {
                Log.AddBackend(_logRing);
                _logRingInstalled = true;
            }

            tools.Register(new SessionInfoTool(runtime));
            tools.Register(new TimeGetTool(time));
            tools.Register(new TimeControlTool(time));
            tools.Register(new EntitiesQueryTool());
            tools.Register(new UiTreeTool());
            tools.Register(new UiQueryTool());
            tools.Register(new UiClickTool());
            tools.Register(new GasEntityTool());
            tools.Register(new GasDiagnosticsTool());
            tools.Register(new OrdersInspectTool());
            tools.Register(new OrdersIssueTool());
            tools.Register(new InputStateTool());
            tools.Register(new InputInjectTool());
            tools.Register(new InputRawTool());
            tools.Register(new ScreenshotTool(runtime));
            tools.Register(new RecordingStartTool(recording, runtime));
            tools.Register(new RecordingStopTool(recording));
            tools.Register(new CameraControlTool());
            tools.Register(new LogsTailTool(_logRing));
            tools.Register(new EventsFireTool());
            tools.Register(new EntitiesPickTool());
            tools.Register(new SpatialQueryTool());
            tools.Register(new PresentersQueryTool());
            tools.Register(new PresentersDesyncTool());
            tools.Register(new PresentersScreenTool());
            tools.Register(new NavProjectTool());
            tools.Register(new NavFindPathTool());

            engine.SetService(ToolRegistryKey, tools);

            var server = new AgentBridgeHttpServer(runtime, config, discoveryDir);
            server.Start();
            _server = server;

            context.Log($"[AgentBridgeMod] Agent bridge active: http://127.0.0.1:{server.Port}/ ({tools.Tools.Count} tools)");
            return new AgentBridgeSystem(runtime);
        }

        public void OnUnload()
        {
            _server?.Dispose();
            _server = null;
        }
    }
}
