using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// Derives the expected bridge tool catalog from the concrete <see cref="IAgentTool"/>
    /// implementations in the AgentBridge assembly. A hand-written name list drifts stale
    /// the moment a tool ships without a list sync, so catalog contract tests anchor on
    /// the tool classes themselves instead.
    /// </summary>
    internal static class AgentBridgeToolCatalogProbe
    {
        public static (Type Type, string Name)[] GetConcreteTools(
            AgentBridgeRuntime runtime,
            AgentTimeController time,
            RecordingController recording,
            AgentLogRingBackend logRing)
        {
            Type toolInterface = typeof(IAgentTool);
            return toolInterface.Assembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && toolInterface.IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (t, CreateProbeInstance(t, runtime, time, recording, logRing).Name))
                .ToArray();
        }

        public static string[] GetConcreteToolNames(
            AgentBridgeRuntime runtime,
            AgentTimeController time,
            RecordingController recording,
            AgentLogRingBackend logRing)
        {
            return GetConcreteTools(runtime, time, recording, logRing)
                .Select(tool => tool.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Probes exist only to read <see cref="IAgentTool.Name"/> — constructor
        /// dependencies are never invoked. A new tool with an unfamiliar constructor
        /// signature makes this throw instead of silently passing; extend the mapping
        /// when that happens.
        /// </summary>
        private static IAgentTool CreateProbeInstance(
            Type type,
            AgentBridgeRuntime runtime,
            AgentTimeController time,
            RecordingController recording,
            AgentLogRingBackend logRing)
        {
            if (type == typeof(SessionInfoTool)
                || type == typeof(InputStateTool)
                || type == typeof(InputInjectTool)
                || type == typeof(ScreenshotTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, runtime)!;
            }

            if (type == typeof(TimeGetTool) || type == typeof(TimeControlTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, time)!;
            }

            if (type == typeof(RecordingStartTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, recording, runtime)!;
            }

            if (type == typeof(RecordingStopTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, recording)!;
            }

            if (type == typeof(LogsTailTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, logRing)!;
            }

            return (IAgentTool)Activator.CreateInstance(type)!;
        }
    }
}
