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
        AuthoringRegistry.Register<HfsmState>(nameof(HfsmState), SetHfsmState);
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
            if (property.Key is not ("HfsmId" or "BtId" or "Script" or "ThinkEveryNTicks" or "BlackboardInts" or "BlackboardEntities"))
            {
                throw new InvalidOperationException(
                    $"{context} authoring does not accept property '{property.Key}'; allowed: HfsmId, Script, ThinkEveryNTicks, BlackboardInts, BlackboardEntities.");
            }
        }

        string? hfsmId = ReadOptionalCanonicalKey(obj, "HfsmId", context);
        string? btId = ReadOptionalCanonicalKey(obj, "BtId", context);
        string? script = ReadOptionalCanonicalKey(obj, "Script", context);
        if (hfsmId == null && script == null)
        {
            throw new InvalidOperationException(
                $"{context} requires exactly one of HfsmId (AI/hfsm.json) or Script (GAS graphs).");
        }

        if (hfsmId != null && script != null)
        {
            throw new InvalidOperationException(
                $"{context} accepts HfsmId or Script, not both; an entity carries one behavior source.");
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
            HfsmId = hfsmId ?? string.Empty,
            BtId = btId ?? string.Empty,
            ScriptKey = script ?? string.Empty,
            ThinkEveryNTicks = thinkEveryNTicks,
            BlackboardIntDefaults = intDefaults.ToArray(),
            BlackboardEntityDefaults = entityDefaults.ToArray(),
        });
    }

        private static string? ReadOptionalCanonicalKey(JsonObject obj, string property, string context)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node is null)
            {
                return null;
            }

            string? value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value) || value.Length != value.Trim().Length)
            {
                throw new InvalidOperationException(
                    $"{context}.{property} must be a non-empty canonical key (no leading/trailing whitespace).");
            }

            return value;
        }

        private static string RequireCanonicalKey(string key, string context)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length != key.Trim().Length)
            {
                throw new InvalidOperationException($"{context} blackboard keys must be non-empty canonical strings.");
            }

            return key;
        }
    private static void SetHfsmState(Entity entity, System.Text.Json.Nodes.JsonNode data)
    {
        entity.Add(new HfsmState { LeafIndex = HfsmState.NoState });
    }

}