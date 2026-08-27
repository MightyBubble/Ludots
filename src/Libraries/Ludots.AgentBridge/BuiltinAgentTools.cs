using Ludots.AgentBridge.Tools;
using Ludots.Core.Diagnostics;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// SSOT for built-in bridge tools: every concrete <see cref="IAgentTool"/> in this
    /// assembly is registered here. Clients (HTTP /tools, MCP, CLI, Inspector) all
    /// consume the live registry — never a parallel hand-written list.
    /// </summary>
    public static class BuiltinAgentTools
    {
        public static IReadOnlyList<string> ExpectedNames { get; } = new[]
        {
            "ludots.session.info",
            "ludots.time.get",
            "ludots.time.control",
            "ludots.camera.control",
            "ludots.logs.tail",
            "ludots.events.fire",
            "ludots.entities.query",
            "ludots.entities.pick",
            "ludots.spatial.query",
            "ludots.nav.project",
            "ludots.nav.findPath",
            "ludots.ui.tree",
            "ludots.ui.query",
            "ludots.ui.click",
            "ludots.gas.entity",
            "ludots.gas.diagnostics",
            "ludots.orders.inspect",
            "ludots.orders.issue",
            "ludots.input.state",
            "ludots.input.inject",
            "ludots.input.raw",
            "ludots.screenshot",
            "ludots.recording.start",
            "ludots.recording.stop",
            "ludots.graph.debug",
            "ludots.presenters.query",
            "ludots.presenters.desync",
            "ludots.presenters.screen",
        };

        public static void RegisterAll(
            AgentToolRegistry tools,
            AgentBridgeRuntime runtime,
            AgentTimeController time,
            RecordingController recording,
            AgentLogRingBackend logRing)
        {
            ArgumentNullException.ThrowIfNull(tools);
            ArgumentNullException.ThrowIfNull(runtime);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(recording);
            ArgumentNullException.ThrowIfNull(logRing);

            tools.Register(new SessionInfoTool(runtime));
            tools.Register(new TimeGetTool(time));
            tools.Register(new TimeControlTool(time));
            tools.Register(new CameraControlTool());
            tools.Register(new LogsTailTool(logRing));
            tools.Register(new EventsFireTool());
            tools.Register(new EntitiesQueryTool());
            tools.Register(new EntitiesPickTool());
            tools.Register(new SpatialQueryTool());
            tools.Register(new NavProjectTool());
            tools.Register(new NavFindPathTool());
            tools.Register(new UiTreeTool());
            tools.Register(new UiQueryTool());
            tools.Register(new UiClickTool());
            tools.Register(new GasEntityTool());
            tools.Register(new GasDiagnosticsTool());
            tools.Register(new OrdersInspectTool());
            tools.Register(new OrdersIssueTool());
            tools.Register(new InputStateTool(runtime));
            tools.Register(new InputInjectTool(runtime));
            tools.Register(new InputRawTool());
            tools.Register(new ScreenshotTool(runtime));
            tools.Register(new RecordingStartTool(recording, runtime));
            tools.Register(new RecordingStopTool(recording));
            tools.Register(new GraphDebugTool());
            tools.Register(new PresentersQueryTool());
            tools.Register(new PresentersDesyncTool());
            tools.Register(new PresentersScreenTool());
            tools.Register(new SaveSlotsTool());
            tools.Register(new SaveCaptureTool());
            tools.Register(new SaveWriteTool());
            tools.Register(new SaveReadTool());
            tools.Register(new SaveRestoreTool());
        }

        /// <summary>
        /// Install the log ring once when the bridge activates so
        /// <c>ludots.logs.tail</c> sees entries from that moment onward.
        /// </summary>
        public static void InstallLogRing(AgentLogRingBackend logRing)
        {
            ArgumentNullException.ThrowIfNull(logRing);
            Log.AddBackend(logRing);
        }
    }
}

