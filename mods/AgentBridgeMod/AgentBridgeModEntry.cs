using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

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
            var recording = new RecordingController();

            context.SystemFactoryRegistry.RegisterPresentation("AgentBridge", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return null!;

                string discoveryDir = AgentBridgeConfig.ResolveDiscoveryDirectory(AppContext.BaseDirectory);
                var runtime = new AgentBridgeRuntime(engine, tools) { ArtifactsRoot = discoveryDir };
                runtime.FrameTick += recording.Tick;
                tools.Register(new SessionInfoTool(runtime));
                tools.Register(new InstancesListTool(runtime));
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

                engine.SetService(ToolRegistryKey, tools);

                var capabilities = new List<string>();
                string host = Environment.GetEnvironmentVariable(AgentBridgeConfig.HostEnvVar) ?? string.Empty;
                if (engine.TryGetService(CoreServiceKeys.HostFrameCapture, out IHostFrameCapture? frameCapture))
                {
                    capabilities.Add("frameCapture");
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        // e.g. "Ludots.Adapter.Raylib.Services" → "raylib"
                        string ns = frameCapture.GetType().Namespace ?? string.Empty;
                        var segments = ns.Split('.');
                        host = segments.Length >= 3 ? segments[2].ToLowerInvariant() : "unknown";
                    }
                }

                if (engine.TryGetService(CoreServiceKeys.SyntheticInput, out _)) capabilities.Add("syntheticInput");
                if (string.IsNullOrWhiteSpace(host)) host = "unknown";

                runtime.HostKind = host;
                runtime.Label = config.Label;
                runtime.Capabilities = capabilities.ToArray();

                string? mapId = engine.CurrentMapSession?.MapId.Value;
                string[] mods = engine.ModLoader.LoadedModIds.ToArray();

                var server = new AgentBridgeHttpServer(runtime, config, discoveryDir, host, runtime.Capabilities, mods, mapId);
                server.Start();
                runtime.BoundPort = server.Port;
                _server = server;

                // MapId is unknown at GameStart activation; patch the session file on the first frame.
                bool identityRefreshed = false;
                runtime.FrameTick += () =>
                {
                    if (identityRefreshed) return;
                    identityRefreshed = true;
                    server.UpdateMapId(engine.CurrentMapSession?.MapId.Value);
                };

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
