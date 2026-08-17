using System.Text.Json.Nodes;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Drives frame recording as repeated one-shot captures on top of
    /// <see cref="IHostFrameCapture"/>, keeping the host port minimal.
    /// Ticked from the bridge pump (game thread); file writes happen on
    /// continuations off the game thread.
    /// </summary>
    public sealed class RecordingController
    {
        private IHostFrameCapture? _capture;
        private string? _directory;
        private int _intervalMs;
        private int _maxFrames;
        private int _frameCount;
        private bool _captureInFlight;
        private DateTime _startedUtc;
        private DateTime _nextDueUtc;

        public bool IsActive => _capture != null;

        public JsonObject Start(IHostFrameCapture capture, string artifactsRoot, int intervalMs, int maxFrames)
        {
            if (IsActive)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "Recording already active; call ludots.recording.stop first.");
            }

            _capture = capture;
            _intervalMs = intervalMs;
            _maxFrames = maxFrames;
            _frameCount = 0;
            _captureInFlight = false;
            _startedUtc = DateTime.UtcNow;
            _nextDueUtc = _startedUtc;
            _directory = Path.Combine(artifactsRoot, "recordings", _startedUtc.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(_directory);

            return Status("started");
        }

        public JsonObject Stop()
        {
            if (!IsActive || _directory == null)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "No recording is active.");
            }

            var summary = Status("stopped");
            var manifest = new JsonObject
            {
                ["startedAtUtc"] = _startedUtc.ToString("O"),
                ["stoppedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["intervalMs"] = _intervalMs,
                ["frames"] = _frameCount,
            };
            File.WriteAllText(Path.Combine(_directory, "manifest.json"), manifest.ToJsonString());

            _capture = null;
            _directory = null;
            return summary;
        }

        /// <summary>Game-thread tick from the bridge pump.</summary>
        public void Tick()
        {
            if (!IsActive || _capture == null || _directory == null || _captureInFlight) return;
            if (_frameCount >= _maxFrames || DateTime.UtcNow < _nextDueUtc) return;

            _nextDueUtc = DateTime.UtcNow.AddMilliseconds(_intervalMs);
            int frameNo = ++_frameCount;
            string path = Path.Combine(_directory, $"frame-{frameNo:000000}.png");
            _captureInFlight = true;

            _capture.CapturePngAsync().ContinueWith(t =>
            {
                _captureInFlight = false;
                if (t.IsCompletedSuccessfully)
                {
                    try { File.WriteAllBytes(path, t.Result); } catch { /* frame dropped, recording continues */ }
                }
            }, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public JsonObject Status(string state)
        {
            return new JsonObject
            {
                ["state"] = state,
                ["directory"] = _directory,
                ["intervalMs"] = _intervalMs,
                ["maxFrames"] = _maxFrames,
                ["framesWritten"] = _frameCount,
                ["hint"] = "Frames are PNGs; read individual frames as images or sample them at a stride.",
            };
        }
    }

    public sealed class ScreenshotTool : IAgentTool
    {
        private readonly AgentBridgeRuntime _runtime;

        public ScreenshotTool(AgentBridgeRuntime runtime) => _runtime = runtime;

        public string Name => "ludots.screenshot";

        public string Description =>
            "Capture the next presented frame as a PNG via the host frame-capture port. " +
            "Params: {name?: string} (file name under artifacts/agent-bridge/shots/). " +
            "Fulfilled at end of frame; works while the simulation is paused.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "optional file name, e.g. before-fix.png" },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context) =>
            throw new InvalidOperationException("ludots.screenshot is async-only; the pump calls ExecuteAsync.");

        public async Task<JsonNode?> ExecuteAsync(JsonObject? args, AgentToolContext context, CancellationToken cancellationToken)
        {
            var capture = context.RequireService(CoreServiceKeys.HostFrameCapture);

            string name = AgentToolContext.OptionalString(args, "name") ?? $"shot-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png";
            name = Path.GetFileName(name);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";

            byte[] png = await capture.CapturePngAsync(cancellationToken).ConfigureAwait(false);

            string dir = Path.Combine(_runtime.ArtifactsRoot, "shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);

            var result = new JsonObject
            {
                ["path"] = path,
                ["bytes"] = png.Length,
            };
            if (context.TryGetService(CoreServiceKeys.ViewController, out var view))
            {
                result["width"] = view.Resolution.X;
                result["height"] = view.Resolution.Y;
            }

            return result;
        }
    }

    public sealed class RecordingStartTool : IAgentTool
    {
        private readonly RecordingController _recording;
        private readonly AgentBridgeRuntime _runtime;

        public RecordingStartTool(RecordingController recording, AgentBridgeRuntime runtime)
        {
            _recording = recording;
            _runtime = runtime;
        }

        public string Name => "ludots.recording.start";

        public string Description =>
            "Start recording presented frames as a PNG sequence under artifacts/agent-bridge/recordings/<timestamp>/. " +
            "Params: {intervalMs?=200, maxFrames?=300}. Stop with ludots.recording.stop (writes manifest.json).";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["intervalMs"] = new JsonObject { ["type"] = "integer", ["minimum"] = 50, ["maximum"] = 60000 },
                ["maxFrames"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10000 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var capture = context.RequireService(CoreServiceKeys.HostFrameCapture);
            int intervalMs = AgentToolContext.OptionalInt(args, "intervalMs", 200);
            int maxFrames = AgentToolContext.OptionalInt(args, "maxFrames", 300);
            if (intervalMs < 50 || intervalMs > 60000)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "intervalMs must be in 50..60000.");
            }

            if (maxFrames < 1 || maxFrames > 10000)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "maxFrames must be in 1..10000.");
            }

            return _recording.Start(capture, _runtime.ArtifactsRoot, intervalMs, maxFrames);
        }
    }

    public sealed class RecordingStopTool : IAgentTool
    {
        private readonly RecordingController _recording;

        public RecordingStopTool(RecordingController recording) => _recording = recording;

        public string Name => "ludots.recording.stop";
        public string Description => "Stop the active frame recording and write manifest.json into the recording directory. No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context) => _recording.Stop();
    }
}
