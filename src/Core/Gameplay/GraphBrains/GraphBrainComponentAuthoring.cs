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
            if (property.Key is not ("Script" or "ThinkEveryNTicks"))
            {
                throw new InvalidOperationException(
                    $"{context} authoring does not accept property '{property.Key}'; allowed: Script, ThinkEveryNTicks.");
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

        entity.Add(new GraphActionBrain
        {
            ScriptKey = script,
            ThinkEveryNTicks = thinkEveryNTicks,
        });
    }
}
