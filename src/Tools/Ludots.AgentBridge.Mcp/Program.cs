using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ludots.AgentBridge;

// Ludots Agent Debug Bridge — MCP stdio adapter.
// Speaks MCP (JSON-RPC 2.0, newline-delimited stdio) on the agent side and
// forwards to the in-process bridge HTTP endpoint.
//
// Bridge address resolution: argv[0] > LUDOTS_AGENT_BRIDGE_URL >
// LUDOTS_AGENT_BRIDGE_DISCOVERY > http://127.0.0.1:47921
// (same SSOT as CLI / Inspector via AgentBridgeEndpoint).

string baseUrl = AgentBridgeEndpoint.Resolve(args.Length > 0 ? args[0] : null);
using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
http.Timeout = TimeSpan.FromSeconds(30);

Log($"ludots-agent-bridge-mcp ready, upstream={http.BaseAddress}");

var outLock = new object();
string? line;
while ((line = Console.In.ReadLine()) != null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;

    JsonNode? root;
    try
    {
        root = JsonNode.Parse(line);
    }
    catch (System.Text.Json.JsonException)
    {
        SendError(null, -32700, "Parse error");
        continue;
    }

    if (root is not JsonObject request) continue;

    string? method = request["method"] is JsonValue m && m.TryGetValue(out string? ms) ? ms : null;
    JsonNode? id = request["id"]?.DeepClone();
    bool isNotification = id == null;

    try
    {
        switch (method)
        {
            case "initialize":
                SendResult(id, new JsonObject
                {
                    ["protocolVersion"] = request["params"]?["protocolVersion"]?.DeepClone() ?? "2024-11-05",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "ludots-agent-bridge", ["version"] = "1.0.0" },
                    ["instructions"] = "Ludots runtime debug tools. Call tools/list to discover; every tool is self-describing via inputSchema.",
                });
                break;

            case "notifications/initialized":
            case "notifications/cancelled":
                break;

            case "ping":
                if (!isNotification) SendResult(id, new JsonObject());
                break;

            case "tools/list":
            {
                JsonObject? catalog = await http.GetFromJsonAsync<JsonObject>("tools");
                var tools = new JsonArray();
                if (catalog?["tools"] is JsonArray upstream)
                {
                    foreach (JsonNode? tool in upstream)
                    {
                        if (tool is not JsonObject t) continue;
                        tools.Add(new JsonObject
                        {
                            ["name"] = t["name"]?.DeepClone(),
                            ["description"] = t["description"]?.DeepClone(),
                            ["inputSchema"] = t["inputSchema"]?.DeepClone() ?? new JsonObject { ["type"] = "object" },
                        });
                    }
                }

                SendResult(id, new JsonObject { ["tools"] = tools });
                break;
            }

            case "tools/call":
            {
                string? toolName = request["params"]?["name"] is JsonValue n && n.TryGetValue(out string? ns) ? ns : null;
                JsonObject? toolArgs = request["params"]?["arguments"] as JsonObject;
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    SendError(id, -32602, "tools/call requires params.name");
                    break;
                }

                var rpcPayload = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 1,
                    ["method"] = toolName,
                    ["params"] = toolArgs?.DeepClone() ?? new JsonObject(),
                };

                using var response = await http.PostAsJsonAsync("rpc", rpcPayload);
                string responseBody = await response.Content.ReadAsStringAsync();
                JsonObject? rpcResult = JsonNode.Parse(responseBody) as JsonObject;

                if (rpcResult?["error"] is JsonObject error)
                {
                    SendResult(id, new JsonObject
                    {
                        ["isError"] = true,
                        ["content"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = $"{error["code"]}: {error["message"]}",
                        }),
                    });
                }
                else
                {
                    string text = rpcResult?["result"]?.ToJsonString() ?? "null";
                    SendResult(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                    });
                }

                break;
            }

            default:
                if (!isNotification) SendError(id, -32601, $"Method not found: {method}");
                break;
        }
    }
    catch (Exception ex)
    {
        if (!isNotification) SendError(id, -32000, ex.Message);
        Log($"error handling {method}: {ex.Message}");
    }
}

void SendResult(JsonNode? id, JsonObject result)
{
    Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
}

void SendError(JsonNode? id, int code, string message)
{
    Send(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    });
}

void Send(JsonObject payload)
{
    lock (outLock)
    {
        Console.Out.WriteLine(payload.ToJsonString());
        Console.Out.Flush();
    }
}

static void Log(string message)
{
    Console.Error.WriteLine($"[ludots-mcp] {message}");
    Console.Error.Flush();
}
