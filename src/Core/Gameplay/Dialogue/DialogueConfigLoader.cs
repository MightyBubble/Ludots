using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Story;

namespace Ludots.Core.Gameplay.Dialogue
{
    public sealed class DialogueConfigLoader
    {
        public const string DialoguesPath = "Dialogue/dialogues.json";

        private readonly ConfigPipeline _pipeline;
        private readonly DialogueDefinitionRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public DialogueConfigLoader(ConfigPipeline pipeline, DialogueDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            LegacyNarrativeConfigGuard.RejectIfPresent(catalog);
            _registry.Clear();
            var entry = ConfigPipeline.RequireEntry(catalog, DialoguesPath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonObject node = merged[i].Node;
                RejectLegacyNarrativeShape(node, i);
                var definition = JsonSerializer.Deserialize<DialogueDefinition>(node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize dialogue at '{DialoguesPath}' index {i}.");
                _registry.Register(definition);
            }
        }

        private static void RejectLegacyNarrativeShape(JsonObject node, int index)
        {
            if (node.ContainsKey("startNodeId"))
            {
                throw new InvalidOperationException(
                    $"Dialogue entry index {index} uses legacy field 'startNodeId'. Use 'entryNode'. {LegacyNarrativeConfigGuard.MigrationMessage}");
            }

            if (node.TryGetPropertyValue("nodes", out JsonNode? nodesNode) && nodesNode is JsonArray nodes)
            {
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (nodes[n] is not JsonObject nodeObj)
                    {
                        continue;
                    }

                    if (nodeObj.ContainsKey("text") || nodeObj.ContainsKey("speakerName") || nodeObj.ContainsKey("speakerAlias"))
                    {
                        throw new InvalidOperationException(
                            $"Dialogue entry index {index} node embeds legacy inline text/speaker fields. Use lineId + Story/lines.json. {LegacyNarrativeConfigGuard.MigrationMessage}");
                    }

                    if (nodeObj.ContainsKey("onEnter"))
                    {
                        throw new InvalidOperationException(
                            $"Dialogue entry index {index} uses legacy 'onEnter' action enums. Use onEnterActionGraphId TriggerGraph. {LegacyNarrativeConfigGuard.MigrationMessage}");
                    }

                    if (nodeObj.TryGetPropertyValue("choices", out JsonNode? choicesNode) && choicesNode is JsonArray choices)
                    {
                        for (int c = 0; c < choices.Count; c++)
                        {
                            if (choices[c] is not JsonObject choiceObj)
                            {
                                continue;
                            }

                            if (choiceObj.ContainsKey("text") || choiceObj.ContainsKey("conditions") || choiceObj.ContainsKey("actions") || choiceObj.ContainsKey("nextNodeId"))
                            {
                                throw new InvalidOperationException(
                                    $"Dialogue choice at entry {index} uses legacy NarrativeConditionKind/NarrativeActionKind or inline text. Use lineId, conditionGraphId, actionGraphId, nextNode. {LegacyNarrativeConfigGuard.MigrationMessage}");
                            }
                        }
                    }
                }
            }
        }
    }
}
