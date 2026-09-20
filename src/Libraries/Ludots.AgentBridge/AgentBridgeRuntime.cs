using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

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
        private readonly object _inputEventLock = new();
        private readonly Queue<JsonObject> _inputEvents = new();
        private const int InputEventLogCapacity = 32;

        public AgentBridgeRuntime(GameEngine engine, AgentToolRegistry tools)
        {
            Tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _context = new AgentToolContext(engine ?? throw new ArgumentNullException(nameof(engine)));
        }

        public AgentToolRegistry Tools { get; }

        public int PendingCount => _queue.Count;

        public long PumpCount { get; private set; }
        public DateTime LastPumpUtc { get; private set; } = DateTime.MinValue;

        /// <summary>Latest map id seen by the game-thread pump; null before first pump or map load.</summary>
        public string? MapId => _mapId;
        private volatile string? _mapId;

        /// <summary>Latest simulation tick seen by the game-thread pump; readable while the loop is stalled.</summary>
        public long LastTick => System.Threading.Interlocked.Read(ref _lastTick);
        private long _lastTick;

        /// <summary>Artifacts root for tool-produced files (shots/recordings). Set by the host mod.</summary>
        public string ArtifactsRoot { get; set; } = Path.Combine("artifacts", "agent-bridge");

        /// <summary>Frame-synchronous drivers (e.g. recording) tick here, right after the pump.</summary>
        public event Action? FrameTick;

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
                        $"Tool '{method}' did not execute within {timeout.TotalSeconds:F0}s. {DescribeLoopHealth(out JsonObject data)}",
                        data);
                }
            }
        }

        /// <summary>Game-thread entry point called every frame by AgentBridgeSystem.</summary>
        public int Pump(int maxPerFrame = 32)
        {
            PumpCount++;
            LastPumpUtc = DateTime.UtcNow;
            PublishVitals();
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

                    Task<JsonNode?> call = tool.ExecuteAsync(request.Params, _context, CancellationToken.None);
                    if (call.IsCompleted)
                    {
                        SettleCompletion(request.Completion, call);
                    }
                    else
                    {
                        // Frame-bound tools (screenshot) complete after the pump
                        // returns; continuation settles the HTTP caller off-thread.
                        call.ContinueWith(
                            (t, state) => SettleCompletion((TaskCompletionSource<JsonNode?>)state!, t),
                            request.Completion,
                            CancellationToken.None,
                            TaskContinuationOptions.RunContinuationsAsynchronously,
                            TaskScheduler.Default);
                    }
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

            FrameTick?.Invoke();
            return executed;
        }

        private void PublishVitals()
        {
            // Vitals are best-effort telemetry: the pump must never crash on a
            // partially booted engine (GameSession exists only after startup).
            if (_context.Engine.GameSession is { } session)
            {
                System.Threading.Interlocked.Exchange(ref _lastTick, session.CurrentTick);
            }

            if (_context.Engine.GlobalContext.TryGetValue(CoreServiceKeys.MapId.Name, out object? mapId) && mapId != null)
            {
                _mapId = mapId.ToString();
            }
        }

        /// <summary>
        /// Loop health readable from any thread; distinguishes a stalled game
        /// loop (pump not advancing) from a slow frame-bound tool (pump alive).
        /// </summary>
        public string DescribeLoopHealth(out JsonObject data)
        {
            double loopAgeMs = LastPumpUtc == DateTime.MinValue
                ? -1
                : (DateTime.UtcNow - LastPumpUtc).TotalMilliseconds;
            data = new JsonObject
            {
                ["loopAgeMs"] = Math.Round(loopAgeMs, 0),
                ["pumpCount"] = PumpCount,
                ["pendingRequests"] = _queue.Count,
                ["lastTick"] = _lastTick,
            };

            return loopAgeMs >= 0 && loopAgeMs < 2000
                ? $"Game loop is pumping (pump #{PumpCount}); the tool itself is long-running or frame-bound."
                : $"Game loop has not pumped for {(int)loopAgeMs}ms (pump #{PumpCount}, tick {_lastTick}) — stalled or paused; tools cannot execute until it resumes.";
        }

        public void RecordInputEvent(string eventId, string actionId, string mode, string? seatId = null)
        {
            lock (_inputEventLock)
            {
                var entry = new JsonObject
                {
                    ["eventId"] = eventId,
                    ["actionId"] = actionId,
                    ["mode"] = mode,
                    ["pumpCount"] = PumpCount,
                    ["tick"] = _lastTick,
                };

                if (seatId != null)
                {
                    entry["seatId"] = seatId;
                }

                _inputEvents.Enqueue(entry);
                while (_inputEvents.Count > InputEventLogCapacity)
                {
                    _inputEvents.Dequeue();
                }
            }
        }

        public JsonArray InputEventLog()
        {
            lock (_inputEventLock)
            {
                var snapshot = new JsonArray();
                foreach (JsonObject entry in _inputEvents)
                {
                    snapshot.Add(entry.DeepClone());
                }

                return snapshot;
            }
        }

        private static void SettleCompletion(TaskCompletionSource<JsonNode?> completion, Task<JsonNode?> call)
        {
            if (call.IsFaulted)
            {
                Exception ex = call.Exception?.GetBaseException() ?? new Exception("unknown tool failure");
                if (ex is AgentToolException bridgeEx)
                {
                    completion.TrySetException(bridgeEx);
                }
                else
                {
                    completion.TrySetException(new AgentToolException(
                        AgentBridgeErrorCodes.ToolFailed,
                        $"tool failed: {ex.GetType().Name}: {ex.Message}"));
                }
            }
            else if (call.IsCanceled)
            {
                completion.TrySetCanceled();
            }
            else
            {
                completion.TrySetResult(call.Result);
            }
        }
    }
}
