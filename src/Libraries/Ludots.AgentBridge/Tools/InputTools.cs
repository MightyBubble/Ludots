using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class InputStateTool : IAgentTool
    {
        private readonly AgentBridgeRuntime _runtime;

        public InputStateTool(AgentBridgeRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public string Name => "ludots.input.state";

        public string Description =>
            "Current input pipeline state: handler blocked flag, update revision, UI capture flags " +
            "(uiCaptured, uiWheelCaptured, pointerInputCaptured), the window-level synthetic device " +
            "state when present (pointer override, held buttons/keys), and the recent ludots.input.inject " +
            "event ledger (eventId/mode/pumpCount/tick). No parameters.";

        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var handler = context.RequireService(CoreServiceKeys.InputHandler);

            var result = new JsonObject
            {
                ["inputBlocked"] = handler.InputBlocked,
                ["updateRevision"] = handler.UpdateRevision,
                ["uiCaptured"] = ReadFlag(context, CoreServiceKeys.UiCaptured.Name),
                ["uiWheelCaptured"] = ReadFlag(context, CoreServiceKeys.UiWheelCaptured.Name),
                ["pointerInputCaptured"] = ReadFlag(context, CoreServiceKeys.PointerInputCaptured.Name),
            };

            if (context.TryGetService(CoreServiceKeys.SyntheticInput, out SyntheticInputDevice device))
            {
                result["synthetic"] = new JsonObject
                {
                    ["pointerOverride"] = device.HasPointerOverride,
                    ["pointer"] = device.HasPointerOverride
                        ? new JsonObject { ["x"] = device.PointerPosition.X, ["y"] = device.PointerPosition.Y }
                        : null,
                    ["buttonsDown"] = new JsonArray(device.ButtonsDown.Select(b => (JsonNode)b.ToString()).ToArray()),
                    ["keysDown"] = new JsonArray(device.KeysDown.Select(k => (JsonNode)k).ToArray()),
                };
            }

            result["injectionLedger"] = _runtime.InputEventLog();
            return result;
        }

        private static bool ReadFlag(AgentToolContext context, string key)
        {
            return context.Engine.GlobalContext.TryGetValue(key, out object? value) && value is bool flag && flag;
        }
    }

    /// <summary>
    /// Window-level input injection: events enter at the host's polling point
    /// (SyntheticInputDevice), flowing through the same UI hit-test / capture /
    /// binding pipeline as physical input — unlike ludots.input.inject, which
    /// targets the semantic action layer (PlayerInputHandler) directly.
    /// </summary>
    public sealed class InputRawTool : IAgentTool
    {
        public string Name => "ludots.input.raw";

        public string Description =>
            "Inject window-level input (as if from the physical mouse/keyboard, in window pixels): " +
            "{op: 'pointerMove'|'pointerDown'|'pointerUp'|'click'|'pointerClear'|'scroll'|'keyDown'|'keyUp'|'press'|'type'|'releaseAll', " +
            "x?, y?, button?: 'left'|'right'|'middle', deltaY?, key?: string, text?: string}. " +
            "Applied on the next frame; use ludots.input.state to observe. For semantic game actions use ludots.input.inject instead.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["op"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("pointerMove", "pointerDown", "pointerUp", "click", "pointerClear", "scroll", "keyDown", "keyUp", "press", "type", "releaseAll"),
                },
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
                ["button"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("left", "right", "middle") },
                ["deltaY"] = new JsonObject { ["type"] = "number" },
                ["key"] = new JsonObject { ["type"] = "string", ["description"] = "e.g. A, F5, Space, PageUp" },
                ["text"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("op"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string op = AgentToolContext.RequireString(args, "op");
            var device = context.RequireService(CoreServiceKeys.SyntheticInput);

            switch (op)
            {
                case "pointerMove":
                    device.MovePointer(RequireFloat(args, "x"), RequireFloat(args, "y"));
                    break;
                case "pointerDown":
                    device.PointerDown(RequireButton(args));
                    break;
                case "pointerUp":
                    device.PointerUp(RequireButton(args));
                    break;
                case "click":
                    if (args?["x"] is JsonValue || args?["y"] is JsonValue)
                    {
                        device.MovePointer(RequireFloat(args, "x"), RequireFloat(args, "y"));
                    }

                    device.Click(RequireButton(args));
                    break;
                case "pointerClear":
                    device.ClearPointerOverride();
                    break;
                case "scroll":
                    device.Scroll(RequireFloat(args, "deltaY"));
                    break;
                case "keyDown":
                    device.KeyDown(AgentToolContext.RequireString(args, "key"));
                    break;
                case "keyUp":
                    device.KeyUp(AgentToolContext.RequireString(args, "key"));
                    break;
                case "press":
                    device.PressKey(AgentToolContext.RequireString(args, "key"));
                    break;
                case "type":
                    device.TypeText(AgentToolContext.RequireString(args, "text"));
                    break;
                case "releaseAll":
                    device.ReleaseAll();
                    break;
                default:
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Unknown op '{op}'. Expected pointerMove | pointerDown | pointerUp | click | pointerClear | scroll | keyDown | keyUp | press | type | releaseAll.");
            }

            return new JsonObject
            {
                ["op"] = op,
                ["queued"] = true,
                ["note"] = "Event applies on the next frame at the host input polling point.",
            };
        }

        private static SyntheticPointerButton RequireButton(JsonObject? args)
        {
            return AgentToolContext.OptionalString(args, "button")?.ToLowerInvariant() switch
            {
                null or "left" => SyntheticPointerButton.Left,
                "right" => SyntheticPointerButton.Right,
                "middle" => SyntheticPointerButton.Middle,
                string other => throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown button '{other}'. Expected left | right | middle."),
            };
        }

        private static float RequireFloat(JsonObject? args, string name)
        {
            if (args?[name] is JsonValue node && node.TryGetValue(out double d)) return (float)d;
            throw new AgentToolException(
                AgentBridgeErrorCodes.InvalidParams,
                $"Parameter '{name}' (number) is required.");
        }
    }

    public sealed class InputInjectTool : IAgentTool
    {
        private readonly AgentBridgeRuntime _runtime;
        private static long _eventCounter;

        public InputInjectTool(AgentBridgeRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public string Name => "ludots.input.inject";

        public string Description =>
            "Inject a synthetic input action into the player input handler (same path as game input bindings). " +
            "Params: {actionId: string, mode: 'press'|'release'|'set', value?: {x,y,z}, seatId?: string}. " +
            "seatId routes to that seat's own input channel (split-screen: ClientLocalSeatInputRuntime; the sole seat " +
            "keeps the engine-global handler). press = InjectButtonPress (held until release), release = InjectButtonRelease, " +
            "set = InjectAction with vector value. Response carries eventId + injectionHeld for causal confirmation; " +
            "the ledger is readable via ludots.input.state.";

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
                ["seatId"] = new JsonObject { ["type"] = "string", ["description"] = "route to this seat's input channel; default = engine-global handler" },
            },
            ["required"] = new JsonArray("actionId", "mode"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string actionId = AgentToolContext.RequireString(args, "actionId");
            string mode = AgentToolContext.RequireString(args, "mode");
            string? seatId = AgentToolContext.OptionalString(args, "seatId");
            PlayerInputHandler handler = seatId != null
                ? SeatRouting.ResolveSeatInputHandler(context, seatId)
                : context.RequireService(CoreServiceKeys.InputHandler);

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

            string eventId = $"inj-{Interlocked.Increment(ref _eventCounter)}";
            string? resolvedSeatId = seatId?.Trim();
            _runtime.RecordInputEvent(eventId, actionId, mode, resolvedSeatId);
            var result = new JsonObject
            {
                ["actionId"] = actionId,
                ["mode"] = mode,
                ["injected"] = true,
                ["eventId"] = eventId,
                ["pumpCount"] = _runtime.PumpCount,
                ["injectionHeld"] = handler.IsInjectionActive(actionId),
                ["note"] = "Injection applies at the next InputCollection phase; injectionHeld is the post-inject handler state.",
            };

            if (resolvedSeatId != null)
            {
                result["seatId"] = resolvedSeatId;
            }

            return result;
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
