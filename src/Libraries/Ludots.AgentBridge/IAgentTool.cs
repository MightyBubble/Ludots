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

        /// <summary>
        /// Async-capable entry point (e.g. frame capture, which is fulfilled at
        /// end of frame). The default runs <see cref="Execute"/> synchronously on
        /// the game thread. Overrides must never block the game thread; the
        /// returned task may complete on any thread.
        /// </summary>
        Task<JsonNode?> ExecuteAsync(JsonObject? args, AgentToolContext context, CancellationToken cancellationToken)
            => Task.FromResult(Execute(args, context));
    }
}
