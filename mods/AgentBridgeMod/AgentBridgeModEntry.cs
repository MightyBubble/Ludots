using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
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

        public static ServiceKey<AgentToolRegistry> ToolRegistryKey { get; } = new("AgentToolRegistry");

        public void OnLoad(IModContext context)
        {
            AgentBridgeConfig config = AgentBridgeConfig.FromEnvironment();
            if (!config.Enabled)
            {
                context.Log("[AgentBridgeMod] Disabled via LUDOTS_AGENT_BRIDGE=0.");
                return;
            }

            var tools = new AgentToolRegistry();
            var time = new AgentTimeController();

            context.SystemFactoryRegistry.RegisterPresentation("AgentBridge", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return null!;

                var runtime = new AgentBridgeRuntime(engine, tools);

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

                engine.SetService(ToolRegistryKey, tools);

                string discoveryDir = AgentBridgeConfig.ResolveDiscoveryDirectory(AppContext.BaseDirectory);
                var server = new AgentBridgeHttpServer(runtime, config, discoveryDir);
                server.Start();
                _server = server;

                context.Log($"[AgentBridgeMod] Agent bridge active: http://127.0.0.1:{server.Port}/ ({tools.Tools.Count} tools)");
                return new AgentBridgeSystem(runtime);
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

        public void OnUnload()
        {
            _server?.Dispose();
            _server = null;
        }
    }
}
