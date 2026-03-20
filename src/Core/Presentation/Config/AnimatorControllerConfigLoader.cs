using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class AnimatorControllerConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly AnimatorControllerRegistry _controllers;

        public AnimatorControllerConfigLoader(
            ConfigPipeline configs,
            AnimatorControllerRegistry controllers)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/animator_controllers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Animator controller is missing required 'id'.");
                }

                _controllers.Register(key, ParseController(node, key));
            }
        }

        private static AnimatorControllerDefinition ParseController(JsonNode node, string key)
        {
            if (node["states"] is not JsonArray statesArray || statesArray.Count == 0)
            {
                throw new InvalidOperationException($"Animator controller '{key}' must define at least one state.");
            }

            var states = new AnimatorStateDefinition[statesArray.Count];
            for (int i = 0; i < statesArray.Count; i++)
            {
                if (statesArray[i] is not JsonObject stateNode)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' state[{i}] must be an object.");
                }

                states[i] = new AnimatorStateDefinition
                {
                    PackedStateIndex = stateNode["packedStateIndex"]?.GetValue<int>() ?? 0,
                    DurationSeconds = stateNode["durationSeconds"]?.GetValue<float>() ?? 1f,
                    PlaybackSpeed = stateNode["playbackSpeed"]?.GetValue<float>() ?? 1f,
                    Loop = stateNode["loop"]?.GetValue<bool>() ?? false,
                };
            }

            JsonArray? transitionsArray = node["transitions"] as JsonArray;
            var transitions = transitionsArray == null ? Array.Empty<AnimatorTransitionDefinition>() : new AnimatorTransitionDefinition[transitionsArray.Count];
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitionsArray![i] is not JsonObject transitionNode)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' transition[{i}] must be an object.");
                }

                string conditionKindText = transitionNode["conditionKind"]?.GetValue<string>() ?? nameof(AnimatorConditionKind.None);
                if (!Enum.TryParse(conditionKindText, ignoreCase: true, out AnimatorConditionKind conditionKind))
                {
                    throw new InvalidOperationException(
                        $"Animator controller '{key}' transition[{i}] has invalid conditionKind '{conditionKindText}'.");
                }

                transitions[i] = new AnimatorTransitionDefinition
                {
                    FromStateIndex = transitionNode["fromStateIndex"]?.GetValue<int>() ?? 0,
                    ToStateIndex = transitionNode["toStateIndex"]?.GetValue<int>() ?? 0,
                    ConditionKind = conditionKind,
                    ParameterIndex = transitionNode["parameterIndex"]?.GetValue<int>() ?? 0,
                    Threshold = transitionNode["threshold"]?.GetValue<float>() ?? 0f,
                    DurationSeconds = transitionNode["durationSeconds"]?.GetValue<float>() ?? 0f,
                    ConsumeTrigger = transitionNode["consumeTrigger"]?.GetValue<bool>() ?? false,
                };
            }

            return new AnimatorControllerDefinition
            {
                DefaultStateIndex = node["defaultStateIndex"]?.GetValue<int>() ?? 0,
                States = states,
                Transitions = transitions,
            };
        }
    }
}
