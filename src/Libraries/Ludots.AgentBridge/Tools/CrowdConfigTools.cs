using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools;

public sealed class CrowdConfigApplyDebugTool : IAgentTool
{
    public string Name => "ludots.crowd.config.applyDebug";

    public string Description =>
        "Re-read CrowdSimulationDebug.json through the config pipeline and apply it to the running crowd session. " +
        "Validation failure leaves the session unchanged.";

    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        _ = args;
        CrowdSimulationRuntime runtime = context.RequireService(CoreServiceKeys.CrowdSimulationRuntime);
        return runtime.ApplyDebugFromPipeline();
    }
}

public sealed class CrowdConfigApplyDebugViewTool : IAgentTool
{
    public string Name => "ludots.crowd.config.applyDebugView";

    public string Description =>
        "Re-read CrowdSimulationDebugView.json and apply overlays and unit color to the running deploy presentation. " +
        "Validation failure leaves the view unchanged.";

    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        _ = args;
        CrowdSimulationRuntime runtime = context.RequireService(CoreServiceKeys.CrowdSimulationRuntime);
        return runtime.ApplyDebugViewFromPipeline();
    }
}

public sealed class CrowdConfigRebuildWorldTool : IAgentTool
{
    public string Name => "ludots.crowd.config.rebuildWorld";

    public string Description =>
        "Validate the merged crowd config, then rebuild the session. Commands, runtime placements, and exploration memory are dropped. " +
        "Validation failure does not rebuild.";

    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        _ = args;
        CrowdSimulationRuntime runtime = context.RequireService(CoreServiceKeys.CrowdSimulationRuntime);
        return runtime.RebuildWorld();
    }
}
