using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using AuthoringRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Core.Gameplay.GraphBrains;

internal static class GraphBrainComponentAuthoring
{
    public static void Register()
    {
        AuthoringRegistry.Register<GraphActionBrain>(nameof(GraphActionBrain), SetGraphActionBrain);
    }

    private static void SetGraphActionBrain(Entity entity, JsonNode data)
    {
        const string context = nameof(GraphActionBrain);
        if (data is not JsonObject obj)
        {
            throw new InvalidOperationException($"{context} authoring requires a JSON object.");
        }

        foreach (var property in obj)
        {
            if (property.Key is not ("Script" or "ThinkEveryNTicks" or "BlackboardInts" or "BlackboardEntities"))
            {
                throw new InvalidOperationException(
                    $"{context} authoring does not accept property '{property.Key}'; allowed: Script, ThinkEveryNTicks, BlackboardInts, BlackboardEntities.");
            }
        }

        string? script = null;
        if (obj.TryGetPropertyValue("Script", out JsonNode? scriptNode))
        {
            script = scriptNode?.GetValue<string>();
        }

        if (string.IsNullOrWhiteSpace(script) || script!.Length != script.Trim().Length)
        {
            throw new InvalidOperationException(
                $"{context}.Script must be a non-empty canonical graph key (no leading/trailing whitespace).");
        }

        int thinkEveryNTicks = 1;
        if (obj.TryGetPropertyValue("ThinkEveryNTicks", out JsonNode? tickNode))
        {
            if (tickNode is not JsonValue value || !value.TryGetValue<int>(out thinkEveryNTicks) || thinkEveryNTicks < 1)
            {
                throw new InvalidOperationException(
                    $"{context}.ThinkEveryNTicks must be an integer >= 1.");
            }
        }

        var intDefaults = new List<(string Key, int Value)>();
        if (obj.TryGetPropertyValue("BlackboardInts", out JsonNode? intNode) && intNode is JsonObject ints)
        {
            foreach (var entry in ints)
            {
                if (entry.Value is not JsonValue v || !v.TryGetValue<int>(out int value))
                {
                    throw new InvalidOperationException($"{context}.BlackboardInts['{entry.Key}'] must be an integer.");
                }

                intDefaults.Add((RequireCanonicalKey(entry.Key, context), value));
            }
        }

        var entityDefaults = new List<string>();
        if (obj.TryGetPropertyValue("BlackboardEntities", out JsonNode? entityNode) && entityNode is JsonObject entities)
        {
            foreach (var entry in entities)
            {
                if (entry.Value is JsonValue)
                {
                    throw new InvalidOperationException($"{context}.BlackboardEntities['{entry.Key}'] must be null (birth state clears the key).");
                }

                entityDefaults.Add(RequireCanonicalKey(entry.Key, context));
            }
        }

        entity.Add(new GraphActionBrain
        {
            ScriptKey = script,
            ThinkEveryNTicks = thinkEveryNTicks,
            BlackboardIntDefaults = intDefaults.ToArray(),
            BlackboardEntityDefaults = entityDefaults.ToArray(),
        });
    }

        private static string RequireCanonicalKey(string key, string context)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length != key.Trim().Length)
            {
                throw new InvalidOperationException($"{context} blackboard keys must be non-empty canonical strings.");
            }

            return key;
        }
}
