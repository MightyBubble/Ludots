using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Ludots Agent Debug Bridge — MCP stdio adapter.
// Speaks MCP (JSON-RPC 2.0, newline-delimited stdio) on the agent side and
// forwards to the in-process bridge HTTP endpoint. Zero external dependencies.
//
// Bridge address resolution order:
//   1. positional URL argument (e.g. http://127.0.0.1:47921)
//   2. LUDOTS_AGENT_BRIDGE_URL
//   3. --instance <selector> against the instance registry:
//        label:<name> | host:<kind> | map:<mapId> | pid:<n> | latest
//      registry dir from --registry <dir> or LUDOTS_AGENT_BRIDGE_REGISTRY,
//      else auto-located by walking up from CWD looking for global.json.
//      Only alive instances (probed via /health) are eligible; ambiguity is
//      an explicit error listing the candidates.
//   4. http://127.0.0.1:47921 (AgentBridgeConfig.DefaultPort)

string baseUrl = await ResolveBaseUrlAsync(args);
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
    catch (JsonException)
    {
        SendError(null, -32700, "Parse error");
        continue;
    }

    if (root is not JsonObject request) continue;

    string? method = request["method"] is JsonValue m && m.TryGetValue(out string? ms) ? ms : null;
    JsonNode? id = request["id"]?.DeepClone();

    // Notifications (no id) never get a response.
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

static async Task<string> ResolveBaseUrlAsync(string[] argv)
{
    string? selector = null;
    string? registryDir = null;
    for (int i = 0; i < argv.Length; i++)
    {
        if (argv[i] == "--instance" && i + 1 < argv.Length) selector = argv[++i];
        else if (argv[i] == "--registry" && i + 1 < argv.Length) registryDir = argv[++i];
        else if (!argv[i].StartsWith("--", StringComparison.Ordinal)) return argv[i];
    }

    string? env = Environment.GetEnvironmentVariable("LUDOTS_AGENT_BRIDGE_URL");
    if (!string.IsNullOrWhiteSpace(env)) return env;

    registryDir ??= Environment.GetEnvironmentVariable("LUDOTS_AGENT_BRIDGE_REGISTRY") ?? LocateRegistryFromCwd();

    if (registryDir != null && Directory.Exists(registryDir))
    {
        var alive = await ProbeAliveAsync(registryDir);
        if (alive.Count > 0)
        {
            var selected = ApplySelector(alive, selector);
            if (selected.Count == 1)
            {
                return $"http://127.0.0.1:{selected[0].Port}";
            }

            if (selected.Count == 0)
            {
                Fail(2, $"no alive bridge instance matches selector '{selector ?? "<any>"}' in {registryDir} ({alive.Count} alive instance(s))");
            }
            else
            {
                Fail(3, $"selector '{selector ?? "<any>"}' is ambiguous; {selected.Count} alive instances match:\n" +
                    string.Join("\n", selected.Select(Describe)) +
                    "\nRe-run with --instance label:<name> | host:<kind> | map:<mapId> | pid:<n>");
            }
        }

        if (selector != null)
        {
            Fail(2, $"no alive bridge instances in {registryDir} (selector '{selector}')");
        }
    }
    else if (selector != null)
    {
        Fail(2, "no instance registry found; pass --registry <sessions dir> or set LUDOTS_AGENT_BRIDGE_REGISTRY");
    }

    return "http://127.0.0.1:47921";

    static void Fail(int exitCode, string message)
    {
        Console.Error.WriteLine($"[ludots-mcp] {message}");
        Environment.Exit(exitCode);
    }

    static string Describe(InstanceEntry e) =>
        $"  pid={e.Pid} port={e.Port} host={e.Host} label={e.Label ?? "-"} map={e.MapId ?? "-"} started={e.StartedAtUtc:O}";
}

static List<InstanceEntry> ApplySelector(List<InstanceEntry> alive, string? selector)
{
    if (string.IsNullOrWhiteSpace(selector)) return alive;
    int split = selector.IndexOf(':');
    string kind = split < 0 ? selector : selector[..split];
    string value = split < 0 ? string.Empty : selector[(split + 1)..];

    return kind switch
    {
        "latest" => new List<InstanceEntry> { alive[^1] },
        "pid" when int.TryParse(value, out int pid) => alive.Where(e => e.Pid == pid).ToList(),
        "label" => alive.Where(e => string.Equals(e.Label, value, StringComparison.Ordinal)).ToList(),
        "host" => alive.Where(e => string.Equals(e.Host, value, StringComparison.OrdinalIgnoreCase)).ToList(),
        "map" => alive.Where(e => string.Equals(e.MapId, value, StringComparison.Ordinal)).ToList(),
        _ => new List<InstanceEntry>(),
    };
}

static async Task<List<InstanceEntry>> ProbeAliveAsync(string registryDir)
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
    var tasks = Directory.EnumerateFiles(registryDir, "*.json").Select(async file =>
    {
        InstanceEntry? entry = null;
        try
        {
            var o = JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject;
            if (o == null) return (InstanceEntry?)null;
            entry = new InstanceEntry(
                o["pid"]!.GetValue<int>(),
                o["port"]!.GetValue<int>(),
                o["host"]?.GetValue<string>() ?? "unknown",
                o["label"]?.GetValue<string>(),
                o["mapId"]?.GetValue<string>(),
                o["startedAtUtc"]?.GetValue<DateTime>() ?? DateTime.MinValue);

            // Liveness = the port answers /health AND reports the same pid.
            var health = await probe.GetFromJsonAsync<JsonObject>($"http://127.0.0.1:{entry.Port}/health");
            return health?["pid"]?.GetValue<int>() == entry.Pid ? entry : null;
        }
        catch
        {
            return (InstanceEntry?)null;
        }
    }).ToArray();

    return (await Task.WhenAll(tasks)).Where(e => e != null).Select(e => e!).OrderBy(e => e.StartedAtUtc).ToList();
}

static string? LocateRegistryFromCwd()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            return Path.Combine(dir.FullName, "artifacts", "agent-bridge", "sessions");
        }

        dir = dir.Parent;
    }

    return null;
}


sealed record InstanceEntry(int Pid, int Port, string Host, string? Label, string? MapId, DateTime StartedAtUtc);
