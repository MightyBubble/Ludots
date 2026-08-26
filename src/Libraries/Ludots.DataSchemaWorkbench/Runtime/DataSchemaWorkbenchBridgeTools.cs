using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.Core.Engine;
using ConfigurableDataSchemaSharedMod.Runtime;

namespace ConfigurableDataSchemaSharedMod.Runtime;

public sealed class DataSchemaStateTool : IAgentTool
{
    private readonly ConfigurableDataSchemaRuntime _runtime;

    public DataSchemaStateTool(ConfigurableDataSchemaRuntime runtime) => _runtime = runtime;

    public string Name => "ludots.dataschema.state";
    public string Description =>
        "Data structure workbench state: authoring layer, selected pin/path, validation, active panel, record values.";
    public JsonObject? InputSchema => null;

    public JsonNode? Execute(JsonObject? args, AgentToolContext context) => _runtime.BuildBridgeState();
}

public sealed class DataSchemaAuthoringTool : IAgentTool
{
    private readonly ConfigurableDataSchemaRuntime _runtime;
    private readonly GameEngine _engine;

    public DataSchemaAuthoringTool(GameEngine engine, ConfigurableDataSchemaRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public string Name => "ludots.dataschema.author";
    public string Description =>
        "Drive authoring actions: setLayer, selectRecord, nudgeX, cycleRarity, addTag, selectPin, selectBindingPath, setPinSource, save, injectInvalidBinding.";
    public JsonObject? InputSchema => new JsonObject
    {
        ["type"] = "object",
        ["required"] = new JsonArray("action"),
        ["properties"] = new JsonObject
        {
            ["action"] = new JsonObject { ["type"] = "string" },
            ["layer"] = new JsonObject { ["type"] = "string" },
            ["recordId"] = new JsonObject { ["type"] = "string" },
            ["delta"] = new JsonObject { ["type"] = "number" },
            ["pin"] = new JsonObject { ["type"] = "string" },
            ["path"] = new JsonObject { ["type"] = "string" },
            ["source"] = new JsonObject { ["type"] = "string" },
        },
    };

    public JsonNode? Execute(JsonObject? args, AgentToolContext context)
    {
        if (args == null ||
            !args.TryGetPropertyValue("action", out JsonNode? actionNode) ||
            actionNode == null)
        {
            throw new InvalidOperationException("ludots.dataschema.author requires action.");
        }

        string action = actionNode.GetValue<string>();
        switch (action)
        {
            case "setLayer":
                _runtime.SetAuthoringLayer(_engine, ParseLayer(RequireString(args, "layer")));
                break;
            case "selectRecord":
                _runtime.AuthoringSelectRecord(_engine, RequireString(args, "recordId"));
                break;
            case "nudgeX":
                _runtime.AuthoringNudgeX(_engine, args["delta"]?.GetValue<double>() ?? 1d);
                break;
            case "cycleRarity":
                _runtime.AuthoringCycleRarity(_engine);
                break;
            case "addTag":
                _runtime.AuthoringAddTag(_engine);
                break;
            case "selectPin":
                _runtime.AuthoringSelectPin(_engine, RequireString(args, "pin"));
                break;
            case "selectBindingPath":
                _runtime.AuthoringSelectBindingPath(_engine, RequireString(args, "path"));
                break;
            case "setPinSource":
                _runtime.AuthoringSetPinSource(_engine, RequireString(args, "source"));
                break;
            case "save":
                _runtime.SaveAuthoringToMod(_engine);
                break;
            case "injectInvalidBinding":
                _runtime.InjectInvalidBindingPath(_engine, args["path"]?.GetValue<string>() ?? "does.not.exist");
                break;
            case "buildScoutFromScratch":
                _runtime.AuthoringBuildScoutFromScratch(_engine);
                break;
            case "createStruct":
                _runtime.AuthoringCreateStruct(_engine, RequireString(args, "schemaId"));
                break;
            case "createEnum":
                _runtime.AuthoringCreateEnum(_engine, RequireString(args, "schemaId"));
                break;
            case "createRecord":
                _runtime.AuthoringCreateRecord(_engine, RequireString(args, "recordId"), RequireString(args, "schemaId"));
                break;
            case "setEntityRef":
                _runtime.AuthoringSetEntityRef(_engine, RequireString(args, "path"), RequireString(args, "entity"));
                break;
            default:
                throw new InvalidOperationException($"Unknown authoring action '{action}'.");
        }

        return _runtime.BuildBridgeState();
    }

    private static string RequireString(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out JsonNode? node) || node == null)
        {
            throw new InvalidOperationException($"ludots.dataschema.author action requires '{key}'.");
        }

        return node.GetValue<string>();
    }

    private static DataSchemaAuthoringLayer ParseLayer(string layer) =>
        layer.Trim().ToLowerInvariant() switch
        {
            "schema" => DataSchemaAuthoringLayer.Schema,
            "record" => DataSchemaAuthoringLayer.Record,
            "binding" => DataSchemaAuthoringLayer.Binding,
            "preview" => DataSchemaAuthoringLayer.Preview,
            _ => throw new InvalidOperationException($"Unknown authoring layer '{layer}'."),
        };
}
