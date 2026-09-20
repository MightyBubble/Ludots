using System.Text.Json.Nodes;
using Ludots.Core.Diagnostics;

namespace Ludots.AgentBridge.Tools
{
    public sealed class LogsTailTool : IAgentTool
    {
        private readonly AgentLogRingBackend _ring;

        public LogsTailTool(AgentLogRingBackend ring) => _ring = ring;

        public string Name => "ludots.logs.tail";

        public string Description =>
            "Read recent engine log entries from the in-memory ring (installed when the bridge activates; entries before activation are not captured). " +
            "Params: {count?=50 (max 500), minLevel?='Trace'|'Debug'|'Info'|'Warning'|'Error', channel?=substring, contains?=substring}. " +
            "Entries are chronological; totalWritten/capacity indicate how much history was rotated out.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 500, ["default"] = 50 },
                ["minLevel"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Trace", "Debug", "Info", "Warning", "Error") },
                ["channel"] = new JsonObject { ["type"] = "string", ["description"] = "case-insensitive substring on channel name, e.g. 'GAS'" },
                ["contains"] = new JsonObject { ["type"] = "string", ["description"] = "case-insensitive substring on message" },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int count = Math.Clamp(AgentToolContext.OptionalInt(args, "count", 50), 1, 500);
            string? channelFilter = AgentToolContext.OptionalString(args, "channel");
            string? contains = AgentToolContext.OptionalString(args, "contains");

            LogLevel minLevel = LogLevel.Trace;
            string? minLevelArg = AgentToolContext.OptionalString(args, "minLevel");
            if (minLevelArg != null && !Enum.TryParse(minLevelArg, ignoreCase: true, out minLevel))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown minLevel '{minLevelArg}'. Expected Trace | Debug | Info | Warning | Error.");
            }

            // Over-fetch so post-filter results can still reach `count`.
            var snapshot = _ring.Snapshot(_ring.Count);
            var entries = new JsonArray();
            int matched = 0;
            for (int i = snapshot.Count - 1; i >= 0 && matched < count; i--)
            {
                AgentLogRingBackend.Entry e = snapshot[i];
                if (e.Level < minLevel) continue;
                if (channelFilter != null && !e.Channel.Contains(channelFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (contains != null && !e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) continue;

                matched++;
                entries.Insert(0, new JsonObject
                {
                    ["utc"] = e.Utc.ToString("O"),
                    ["level"] = e.Level.ToString(),
                    ["channel"] = e.Channel,
                    ["message"] = e.Message,
                });
            }

            return new JsonObject
            {
                ["returned"] = matched,
                ["ringCount"] = _ring.Count,
                ["capacity"] = _ring.Capacity,
                ["totalWritten"] = _ring.TotalWritten,
                ["entries"] = entries,
            };
        }
    }
}
