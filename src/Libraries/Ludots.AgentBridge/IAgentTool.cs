using System.Text.Json.Nodes;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// One agent-callable tool. Tools execute on the game thread via
    /// <see cref="AgentBridgeRuntime"/>; never touch ECS state off-thread.
    /// </summary>
    public interface IAgentTool
    {
        string Name { get; }
        string Description { get; }

        /// <summary>JSON Schema for the params object. Null means no parameters.</summary>
        JsonObject? InputSchema { get; }

        JsonNode? Execute(JsonObject? args, AgentToolContext context);
    }
}
