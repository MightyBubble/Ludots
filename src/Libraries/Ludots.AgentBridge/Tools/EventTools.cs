using System.Text.Json.Nodes;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class EventsFireTool : IAgentTool
    {
        public string Name => "ludots.events.fire";

        public string Description =>
            "Fire a game event through TriggerManager — the same dispatch path as engine lifecycle events (GameStart etc.), " +
            "so mod EventHandlers and event-keyed triggers all run. Params: {event: string}. " +
            "Response carries triggerErrors (handler failures during this fire). " +
            "Discover event keys in mod configs/code; pair with ludots.logs.tail to observe handler output.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["event"] = new JsonObject { ["type"] = "string", ["description"] = "event key, e.g. 'GameStart' or a mod-defined key" },
            },
            ["required"] = new JsonArray("event"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string eventKey = AgentToolContext.RequireString(args, "event");
            var engine = context.Engine;

            int errorsBefore = engine.TriggerManager.Errors.Count;
            // Consistent with the engine's own lifecycle dispatch: handlers are
            // expected to complete synchronously; the bridge timeout bounds a
            // misbehaving handler.
            engine.TriggerManager
                .FireEventAsync(new EventKey(eventKey), engine.CreateContext())
                .GetAwaiter()
                .GetResult();

            return new JsonObject
            {
                ["event"] = eventKey,
                ["fired"] = true,
                ["triggerErrors"] = engine.TriggerManager.Errors.Count - errorsBefore,
            };
        }
    }
}
