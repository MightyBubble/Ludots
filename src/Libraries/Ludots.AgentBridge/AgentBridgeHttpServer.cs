using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ludots.Core.Diagnostics;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Loopback-only HTTP JSON-RPC transport for <see cref="AgentBridgeRuntime"/>.
    /// Listener threads never touch game state; requests are enqueued and
    /// executed by the game-thread pump.
    /// </summary>
    public sealed class AgentBridgeHttpServer : IDisposable
    {
        // Rolled-forward runtimes (net9/net10) refuse to serialize non-primitive
        // JsonValue payloads without an explicitly assigned resolver.
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        private readonly AgentBridgeRuntime _runtime;
        private readonly AgentBridgeConfig _config;
        private readonly string _discoveryDirectory;
        private HttpListener? _listener;
        private Thread? _thread;
        private volatile bool _stopping;
        private string? _discoveryFile;

        public AgentBridgeHttpServer(AgentBridgeRuntime runtime, AgentBridgeConfig config, string discoveryDirectory)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _discoveryDirectory = discoveryDirectory ?? throw new ArgumentNullException(nameof(discoveryDirectory));
        }

        public int Port { get; private set; }

        public void Start()
        {
            Exception? lastError = null;
            for (int probe = 0; probe < AgentBridgeConfig.MaxPortProbes; probe++)
            {
                int candidate = _config.RequestedPort + probe;
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
                try
                {
                    listener.Start();
                    _listener = listener;
                    Port = candidate;
                    break;
                }
                catch (HttpListenerException ex)
                {
                    lastError = ex;
                    try { listener.Close(); } catch { /* best effort */ }
                }
            }

            if (_listener == null)
            {
                throw new InvalidOperationException(
                    $"AgentBridge could not bind any port in {_config.RequestedPort}..{_config.RequestedPort + AgentBridgeConfig.MaxPortProbes - 1}: {lastError?.Message}");
            }

            _thread = new Thread(ListenLoop) { IsBackground = true, Name = "AgentBridgeHttp" };
            _thread.Start();
            WriteDiscoveryFile();
            Log.Info(in LogChannels.Engine, $"[AgentBridge] listening on http://127.0.0.1:{Port}/ (discovery: {_discoveryFile})");
        }

        private void ListenLoop()
        {
            while (!_stopping && _listener != null)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (HttpListenerException) when (_stopping)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                string method = context.Request.HttpMethod;

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    WriteJson(context.Response, 200, BuildIndex());
                    return;
                }

                if (method == "GET" && path == "/health")
                {
                    WriteJson(context.Response, 200, new JsonObject
                    {
                        ["ok"] = true,
                        ["pid"] = Environment.ProcessId,
                        ["port"] = Port,
                        ["pendingRequests"] = _runtime.PendingCount,
                        ["pumpCount"] = _runtime.PumpCount,
                        ["lastPumpUtc"] = _runtime.LastPumpUtc == DateTime.MinValue ? null : _runtime.LastPumpUtc.ToString("O"),
                    });
                    return;
                }

                if (method == "GET" && path == "/tools")
                {
                    WriteJson(context.Response, 200, new JsonObject { ["tools"] = _runtime.Tools.DescribeAll() });
                    return;
                }

                if (method == "POST" && path == "/rpc")
                {
                    HandleRpc(context);
                    return;
                }

                WriteJson(context.Response, 404, new JsonObject
                {
                    ["error"] = "not.found",
                    ["message"] = "Endpoints: GET /, GET /health, GET /tools, POST /rpc",
                });
            }
            catch (Exception ex)
            {
                Log.Error(in LogChannels.Engine, $"[AgentBridge] unhandled transport error: {ex}");
                TryWriteError(context.Response, 500, "internal", $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { context.Response.Close(); } catch { /* best effort */ }
            }
        }

        private void HandleRpc(HttpListenerContext context)
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(body);
            }
            catch (JsonException ex)
            {
                WriteJsonRpcError(context.Response, null, -32700, $"Parse error: {ex.Message}", null);
                return;
            }

            if (root is not JsonObject request)
            {
                WriteJsonRpcError(context.Response, null, -32600, "Request must be a JSON-RPC object.", null);
                return;
            }

            JsonNode? id = request["id"]?.DeepClone();
            string? rpcMethod = request["method"] is JsonValue m && m.TryGetValue(out string? ms) ? ms : null;
            if (string.IsNullOrWhiteSpace(rpcMethod))
            {
                WriteJsonRpcError(context.Response, id, -32600, "Missing 'method'.", null);
                return;
            }

            JsonObject? parameters = request["params"] as JsonObject;

            if (string.Equals(rpcMethod, "ludots.tools.list", StringComparison.Ordinal))
            {
                WriteJson(context.Response, 200, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject { ["tools"] = _runtime.Tools.DescribeAll() },
                });
                return;
            }

            try
            {
                JsonNode? result = _runtime
                    .InvokeAsync(rpcMethod, parameters, _config.RequestTimeout, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                WriteJson(context.Response, 200, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = result,
                });
            }
            catch (AgentToolException ex)
            {
                int code = ex.Code == "method.not_found" ? -32601 : (ex.Code == AgentBridgeErrorCodes.InvalidParams ? -32602 : -32000);
                WriteJsonRpcError(context.Response, id, code, ex.Message, ex.Code);
            }
            catch (Exception ex)
            {
                Log.Error(in LogChannels.Engine, $"[AgentBridge] rpc '{rpcMethod}' failed: {ex}");
                WriteJsonRpcError(context.Response, id?.DeepClone(), -32000, ex.Message, AgentBridgeErrorCodes.ToolFailed);
            }
        }

        private JsonObject BuildIndex()
        {
            return new JsonObject
            {
                ["name"] = "Ludots Agent Debug Bridge",
                ["version"] = 1,
                ["port"] = Port,
                ["pid"] = Environment.ProcessId,
                ["discoveryFile"] = _discoveryFile,
                ["endpoints"] = new JsonObject
                {
                    ["GET /health"] = "liveness probe",
                    ["GET /tools"] = "self-describing tool catalog (name/description/inputSchema)",
                    ["POST /rpc"] = "JSON-RPC 2.0: {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"<tool>\",\"params\":{...}}",
                },
                ["tools"] = _runtime.Tools.DescribeAll(),
            };
        }

        private void WriteDiscoveryFile()
        {
            Directory.CreateDirectory(_discoveryDirectory);
            _discoveryFile = Path.Combine(_discoveryDirectory, "session.json");
            var payload = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["port"] = Port,
                ["version"] = 1,
                ["startedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["processPath"] = Environment.ProcessPath,
                ["tools"] = _runtime.Tools.DescribeAll(),
            };
            File.WriteAllText(_discoveryFile, payload.ToJsonString(SerializerOptions));
        }

        private static void WriteJson(HttpListenerResponse response, int status, JsonObject payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(SerializerOptions));
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteJsonRpcError(HttpListenerResponse response, JsonNode? id, int code, string message, string? domainCode)
        {
            var error = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            };
            if (!string.IsNullOrEmpty(domainCode))
            {
                error["data"] = new JsonObject { ["code"] = domainCode };
            }

            WriteJson(response, 200, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = error,
            });
        }

        private static void TryWriteError(HttpListenerResponse response, int status, string code, string message)
        {
            try
            {
                WriteJson(response, status, new JsonObject { ["error"] = code, ["message"] = message });
            }
            catch { /* response may already be committed */ }
        }

        public void Dispose()
        {
            _stopping = true;
            try { _listener?.Stop(); } catch { /* best effort */ }
            try { _listener?.Close(); } catch { /* best effort */ }
            if (_discoveryFile != null)
            {
                try { File.Delete(_discoveryFile); } catch { /* best effort */ }
            }
        }
    }
}
