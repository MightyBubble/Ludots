using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class InputStateTool : IAgentTool
    {
        public string Name => "ludots.input.state";

        public string Description =>
            "Current input pipeline state: handler blocked flag, update revision, UI capture flags " +
            "(uiCaptured, uiWheelCaptured, pointerInputCaptured). No parameters.";

        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var handler = context.RequireService(CoreServiceKeys.InputHandler);

            return new JsonObject
            {
                ["inputBlocked"] = handler.InputBlocked,
                ["updateRevision"] = handler.UpdateRevision,
                ["uiCaptured"] = ReadFlag(context, CoreServiceKeys.UiCaptured.Name),
                ["uiWheelCaptured"] = ReadFlag(context, CoreServiceKeys.UiWheelCaptured.Name),
                ["pointerInputCaptured"] = ReadFlag(context, CoreServiceKeys.PointerInputCaptured.Name),
            };
        }

        private static bool ReadFlag(AgentToolContext context, string key)
        {
            return context.Engine.GlobalContext.TryGetValue(key, out object? value) && value is bool flag && flag;
        }
    }

    public sealed class InputInjectTool : IAgentTool
    {
        public string Name => "ludots.input.inject";

        public string Description =>
            "Inject a synthetic input action into the player input handler (same path as game input bindings). " +
            "Params: {actionId: string, mode: 'press'|'release'|'set', value?: {x,y,z}}. " +
            "press = InjectButtonPress (held until release), release = InjectButtonRelease, set = InjectAction with vector value.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["actionId"] = new JsonObject { ["type"] = "string" },
                ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("press", "release", "set") },
                ["value"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["x"] = new JsonObject { ["type"] = "number" },
                        ["y"] = new JsonObject { ["type"] = "number" },
                        ["z"] = new JsonObject { ["type"] = "number" },
                    },
                },
            },
            ["required"] = new JsonArray("actionId", "mode"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string actionId = AgentToolContext.RequireString(args, "actionId");
            string mode = AgentToolContext.RequireString(args, "mode");
            var handler = context.RequireService(CoreServiceKeys.InputHandler);

            if (!handler.HasAction(actionId))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown input action '{actionId}'. Check the input config schema for valid action ids.");
            }

            switch (mode)
            {
                case "press":
                    handler.InjectButtonPress(actionId);
                    break;
                case "release":
                    handler.InjectButtonRelease(actionId);
                    break;
                case "set":
                    Vector3 value = ReadVector(args?["value"] as JsonObject);
                    handler.InjectAction(actionId, value);
                    break;
                default:
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Unknown mode '{mode}'. Expected press | release | set.");
            }

            return new JsonObject
            {
                ["actionId"] = actionId,
                ["mode"] = mode,
                ["injected"] = true,
            };
        }

        private static Vector3 ReadVector(JsonObject? value)
        {
            if (value == null) return Vector3.One;
            return new Vector3(ReadAxis(value, "x", 1f), ReadAxis(value, "y", 1f), ReadAxis(value, "z", 1f));
        }

        private static float ReadAxis(JsonObject value, string name, float fallback)
        {
            return value[name] is JsonValue node && node.TryGetValue(out double d) ? (float)d : fallback;
        }
    }
}
