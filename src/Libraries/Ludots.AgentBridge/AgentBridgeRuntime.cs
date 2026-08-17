using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Transport-neutral semantic core of the agent bridge. Transports
    /// (HTTP, MCP adapter, ...) enqueue requests; the game-thread pump
    /// (<see cref="AgentBridgeSystem"/>) executes them one per queue drain.
    /// Tool code always runs on the game thread.
    /// </summary>
    public sealed class AgentBridgeRuntime
    {
        private sealed class PendingRequest
        {
            public required string Method;
            public JsonObject? Params;
            public required TaskCompletionSource<JsonNode?> Completion;
        }

        private readonly ConcurrentQueue<PendingRequest> _queue = new();
        private readonly AgentToolContext _context;

        public AgentBridgeRuntime(GameEngine engine, AgentToolRegistry tools)
        {
            Tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _context = new AgentToolContext(engine ?? throw new ArgumentNullException(nameof(engine)));
        }

        public AgentToolRegistry Tools { get; }

        public int PendingCount => _queue.Count;

        public long PumpCount { get; private set; }
        public DateTime LastPumpUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Called by transports on their own threads. The task completes when
        /// the game thread has executed the tool (or the timeout elapses).
        /// </summary>
        public async Task<JsonNode?> InvokeAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (!Tools.TryGet(method, out _))
            {
                throw new AgentToolException("method.not_found", $"Unknown tool '{method}'. Call GET /tools or ludots.tools.list for the catalog.");
            }

            var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(new PendingRequest { Method = method, Params = parameters, Completion = completion });

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using (linked.Token.Register(() => completion.TrySetCanceled(linked.Token)))
            {
                try
                {
                    return await completion.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.Timeout,
                        $"Tool '{method}' did not execute within {timeout.TotalSeconds:F0}s. Is the game loop running (not paused without a presentation pump)?");
                }
            }
        }

        /// <summary>Game-thread entry point called every frame by AgentBridgeSystem.</summary>
        public int Pump(int maxPerFrame = 32)
        {
            PumpCount++;
            LastPumpUtc = DateTime.UtcNow;
            int executed = 0;
            while (executed < maxPerFrame && _queue.TryDequeue(out PendingRequest? request))
            {
                executed++;
                try
                {
                    if (!Tools.TryGet(request.Method, out IAgentTool tool))
                    {
                        throw new AgentToolException("method.not_found", $"Unknown tool '{request.Method}'.");
                    }

                    JsonNode? result = tool.Execute(request.Params, _context);
                    request.Completion.TrySetResult(result);
                }
                catch (AgentToolException ex)
                {
                    request.Completion.TrySetException(ex);
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(new AgentToolException(
                        AgentBridgeErrorCodes.ToolFailed,
                        $"{request.Method} failed: {ex.GetType().Name}: {ex.Message}"));
                }
            }

            return executed;
        }
    }
}
