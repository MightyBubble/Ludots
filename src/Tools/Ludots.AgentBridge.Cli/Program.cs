using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ludots.AgentBridge;

// Ludots Agent Bridge CLI — same JSON-RPC methods as MCP / Inspector / curl.
//
//   ludots-bridge health
//   ludots-bridge tools
//   ludots-bridge call ludots.session.info
//   ludots-bridge call ludots.entities.query --params '{"nameFilter":"Hero"}'
//
// Address resolution: --url > LUDOTS_AGENT_BRIDGE_URL > --discovery /
// LUDOTS_AGENT_BRIDGE_DISCOVERY > http://127.0.0.1:47921

var options = CliOptions.Parse(args);
if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Command))
{
    PrintHelp();
    return options.ShowHelp ? 0 : 1;
}

string baseUrl;
try
{
    baseUrl = AgentBridgeEndpoint.Resolve(options.Url, options.Discovery);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using var client = new AgentBridgeRpcClient(baseUrl);
var serializer = new JsonSerializerOptions
{
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

try
{
    switch (options.Command)
    {
        case "health":
        {
            JsonObject health = await client.GetHealthAsync();
            Console.WriteLine(health.ToJsonString(serializer));
            return health["ok"]?.GetValue<bool>() == true ? 0 : 1;
        }
        case "tools":
        {
            JsonArray tools = await client.ListToolsAsync();
            if (options.NamesOnly)
            {
                foreach (JsonNode? tool in tools)
                {
                    Console.WriteLine(tool?["name"]?.GetValue<string>());
                }
            }
            else
            {
                Console.WriteLine(new JsonObject { ["tools"] = tools.DeepClone(), ["count"] = tools.Count }.ToJsonString(serializer));
            }

            return 0;
        }
        case "call":
        {
            if (string.IsNullOrWhiteSpace(options.Method))
            {
                Console.Error.WriteLine("call requires a method name, e.g. ludots.session.info");
                return 1;
            }

            JsonObject? parameters = null;
            if (!string.IsNullOrWhiteSpace(options.ParamsJson))
            {
                if (JsonNode.Parse(options.ParamsJson) is not JsonObject parsed)
                {
                    Console.Error.WriteLine("--params must be a JSON object.");
                    return 1;
                }

                parameters = parsed;
            }

            JsonObject response = await client.CallAsync(options.Method, parameters);
            Console.WriteLine(response.ToJsonString(serializer));
            return response["error"] == null ? 0 : 1;
        }
        default:
            Console.Error.WriteLine($"Unknown command '{options.Command}'.");
            PrintHelp();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{client.BaseUrl}] {ex.Message}");
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Ludots Agent Bridge CLI — same methods as MCP / Inspector / curl

        Usage:
          Ludots.AgentBridge.Cli health [--url URL] [--discovery PATH]
          Ludots.AgentBridge.Cli tools [--names] [--url URL] [--discovery PATH]
          Ludots.AgentBridge.Cli call <method> [--params JSON] [--url URL] [--discovery PATH]

        Examples:
          Ludots.AgentBridge.Cli health
          Ludots.AgentBridge.Cli tools --names
          Ludots.AgentBridge.Cli call ludots.session.info
          Ludots.AgentBridge.Cli call ludots.time.control --params '{"action":"pause"}'
          Ludots.AgentBridge.Cli call ludots.entities.query --params '{"limit":5}'

        Address resolution order: --url > LUDOTS_AGENT_BRIDGE_URL > --discovery /
        LUDOTS_AGENT_BRIDGE_DISCOVERY > http://127.0.0.1:47921
        """);
}

internal sealed class CliOptions
{
    public string Command { get; init; } = "";
    public string? Method { get; init; }
    public string? ParamsJson { get; init; }
    public string? Url { get; init; }
    public string? Discovery { get; init; }
    public bool NamesOnly { get; init; }
    public bool ShowHelp { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string command = "";
        string? method = null;
        string? paramsJson = null;
        string? url = null;
        string? discovery = null;
        bool namesOnly = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "help":
                    showHelp = true;
                    break;
                case "--url":
                    url = RequireValue(args, ref i, arg);
                    break;
                case "--discovery":
                    discovery = RequireValue(args, ref i, arg);
                    break;
                case "--params":
                    paramsJson = RequireValue(args, ref i, arg);
                    break;
                case "--names":
                    namesOnly = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new InvalidOperationException($"Unknown flag '{arg}'.");
                    }

                    if (string.IsNullOrEmpty(command))
                    {
                        command = arg;
                    }
                    else if (command == "call" && method == null)
                    {
                        method = arg;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected argument '{arg}'.");
                    }

                    break;
            }
        }

        return new CliOptions
        {
            Command = command,
            Method = method,
            ParamsJson = paramsJson,
            Url = url,
            Discovery = discovery,
            NamesOnly = namesOnly,
            ShowHelp = showHelp,
        };
    }

    private static string RequireValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"{flag} requires a value.");
        }

        index++;
        return args[index];
    }
}
