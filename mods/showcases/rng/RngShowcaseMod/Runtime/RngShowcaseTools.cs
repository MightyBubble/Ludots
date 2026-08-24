using System.Text.Json.Nodes;
using Ludots.AgentBridge;

namespace RngShowcaseMod.Runtime;

public sealed class RngStateTool : IAgentTool
{
    private readonly RngShowcaseRuntime _runtime;

    public RngStateTool(RngShowcaseRuntime runtime) => _runtime = runtime;

    public string Name => "ludots.rng.state";
    public string Description => "Distribution showcase state: entries with actual vs expected percentages, knobs, stream position.";
    public JsonObject? InputSchema => null;

    public JsonNode? Execute(JsonObject? args, AgentToolContext context) => _runtime.BuildState();
}

public sealed class RngDrawTool : IAgentTool
{
    private readonly RngShowcaseRuntime _runtime;

    public RngDrawTool(RngShowcaseRuntime runtime) => _runtime = runtime;

    public string Name => "ludots.rng.draw";
    public string Description => "Draw a burst of weighted picks now; returns the deterministic pick sequence and updated state.";
    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000 },
        },
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        var count = 10;
        if (args != null && args.TryGetPropertyValue("count", out var countNode) && countNode != null)
        {
            count = Math.Clamp(countNode.GetValue<int>(), 1, 1000);
        }

        var picks = _runtime.DrawBurst(count);
        return new JsonObject
        {
            ["count"] = count,
            ["picks"] = new JsonArray(picks.Select(p => (JsonNode)p!).ToArray()),
            ["state"] = _runtime.BuildState(),
        };
    }
}

public sealed class RngKnobTool : IAgentTool
{
    private readonly RngShowcaseRuntime _runtime;

    public RngKnobTool(RngShowcaseRuntime runtime) => _runtime = runtime;

    public string Name => "ludots.rng.knob";
    public string Description => "Set showcase knobs: modulationPermille [-1000,1000], burstSize, intervalTicks, autoRun, distribution. Returns updated state.";
    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["modulationPermille"] = new JsonObject { ["type"] = "integer", ["minimum"] = -1000, ["maximum"] = 1000 },
            ["burstSize"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000 },
            ["intervalTicks"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 600 },
            ["autoRun"] = new JsonObject { ["type"] = "boolean" },
            ["resetStats"] = new JsonObject { ["type"] = "boolean" },
            ["distribution"] = new JsonObject { ["type"] = "string" },
        },
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        string? distribution = null;
        int? modulation = null;
        int? burst = null;
        int? interval = null;
        bool? autoRun = null;
        var resetStats = false;

        if (args != null)
        {
            if (args.TryGetPropertyValue("distribution", out var distNode) && distNode != null) distribution = distNode.GetValue<string>();
            if (args.TryGetPropertyValue("modulationPermille", out var modNode) && modNode != null) modulation = modNode.GetValue<int>();
            if (args.TryGetPropertyValue("burstSize", out var burstNode) && burstNode != null) burst = burstNode.GetValue<int>();
            if (args.TryGetPropertyValue("intervalTicks", out var intervalNode) && intervalNode != null) interval = intervalNode.GetValue<int>();
            if (args.TryGetPropertyValue("autoRun", out var autoNode) && autoNode != null) autoRun = autoNode.GetValue<bool>();
            if (args.TryGetPropertyValue("resetStats", out var resetNode) && resetNode != null) resetStats = resetNode.GetValue<bool>();
        }

        return _runtime.SetKnobs(distribution, modulation, burst, interval, autoRun, resetStats);
    }
}

public sealed class RngReplayTool : IAgentTool
{
    private readonly RngShowcaseRuntime _runtime;

    public RngReplayTool(RngShowcaseRuntime runtime) => _runtime = runtime;

    public string Name => "ludots.rng.replay";
    public string Description => "Determinism proof: restore the stream snapshot and redraw the recorded segment; matched=true means identical sequence.";
    public JsonObject? InputSchema => null;

    public JsonNode? Execute(JsonObject? args, AgentToolContext context) => _runtime.VerifyReplay();
}
