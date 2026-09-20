using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Thin HTTP JSON-RPC client over the same contract MCP / Inspector use.
    /// One semantic layer, many frontends.
    /// </summary>
    public sealed class AgentBridgeRpcClient : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        public AgentBridgeRpcClient(string baseUrl, HttpClient? http = null)
        {
            BaseUrl = AgentBridgeEndpoint.Resolve(baseUrl);
            if (http != null)
            {
                _http = http;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient
                {
                    BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/"),
                    Timeout = TimeSpan.FromSeconds(30),
                };
                _ownsHttp = true;
            }
        }

        public string BaseUrl { get; }

        public async Task<JsonObject> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            JsonObject? health = await _http.GetFromJsonAsync<JsonObject>("health", SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return health ?? throw new InvalidOperationException("GET /health returned empty body.");
        }

        public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            JsonObject? catalog = await _http.GetFromJsonAsync<JsonObject>("tools", SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            if (catalog?["tools"] is not JsonArray tools)
            {
                throw new InvalidOperationException("GET /tools did not return a 'tools' array.");
            }

            return tools;
        }

        public async Task<JsonObject> CallAsync(string method, JsonObject? parameters = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("RPC method must not be empty.", nameof(method));
            }

            var payload = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters?.DeepClone() ?? new JsonObject(),
            };

            using HttpResponseMessage response = await _http.PostAsJsonAsync("rpc", payload, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (JsonNode.Parse(body) is not JsonObject root)
            {
                throw new InvalidOperationException($"POST /rpc returned non-object JSON: {body}");
            }

            return root;
        }

        public void Dispose()
        {
            if (_ownsHttp)
            {
                _http.Dispose();
            }
        }
    }
}
