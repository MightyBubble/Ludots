using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;

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
            var entry = ConfigPipeline.RequireEntry(catalog, "Presentation/animator_controllers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = RequireString(node["id"], "Animator controller id");

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
                    PackedStateIndex = RequireInt(stateNode["packedStateIndex"], $"Animator controller '{key}' state[{i}].packedStateIndex"),
                    DurationSeconds = RequirePositiveFloat(stateNode["durationSeconds"], $"Animator controller '{key}' state[{i}].durationSeconds"),
                    PlaybackSpeed = RequirePositiveFloat(stateNode["playbackSpeed"], $"Animator controller '{key}' state[{i}].playbackSpeed"),
                    Loop = RequireBool(stateNode["loop"], $"Animator controller '{key}' state[{i}].loop"),
                };

                if (states[i].PackedStateIndex < 0)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' state[{i}].packedStateIndex cannot be negative.");
                }
            }

            if (node["transitions"] is not JsonArray transitionsArray)
            {
                throw new InvalidOperationException($"Animator controller '{key}' must declare explicit transitions array. Use [] when there are no transitions.");
            }

            var transitions = new AnimatorTransitionDefinition[transitionsArray.Count];
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitionsArray[i] is not JsonObject transitionNode)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' transition[{i}] must be an object.");
                }

                AnimatorConditionKind conditionKind = RequireEnum<AnimatorConditionKind>(
                    transitionNode["conditionKind"],
                    $"Animator controller '{key}' transition[{i}].conditionKind");

                transitions[i] = new AnimatorTransitionDefinition
                {
                    FromStateIndex = RequireInt(transitionNode["fromStateIndex"], $"Animator controller '{key}' transition[{i}].fromStateIndex"),
                    ToStateIndex = RequireInt(transitionNode["toStateIndex"], $"Animator controller '{key}' transition[{i}].toStateIndex"),
                    ConditionKind = conditionKind,
                    ParameterIndex = ParseParamKey(transitionNode["parameterIndex"], $"Animator controller '{key}' transition[{i}].parameterIndex"),
                    Threshold = RequireFiniteFloat(transitionNode["threshold"], $"Animator controller '{key}' transition[{i}].threshold"),
                    DurationSeconds = RequireNonNegativeFloat(transitionNode["durationSeconds"], $"Animator controller '{key}' transition[{i}].durationSeconds"),
                    ConsumeTrigger = RequireBool(transitionNode["consumeTrigger"], $"Animator controller '{key}' transition[{i}].consumeTrigger"),
                };

                ValidateTransition(key, i, states.Length, in transitions[i]);
            }

            int defaultStateIndex = RequireInt(node["defaultStateIndex"], $"Animator controller '{key}'.defaultStateIndex");
            if ((uint)defaultStateIndex >= (uint)states.Length)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' defaultStateIndex {defaultStateIndex} is outside states length {states.Length}.");
            }

            return new AnimatorControllerDefinition
            {
                DefaultStateIndex = defaultStateIndex,
                States = states,
                Transitions = transitions,
            };
        }

        private static int ParseParamKey(JsonNode? node, string context)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException($"{context} must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string? key))
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
                    }

                    RequireNoBoundaryWhitespace(key, context);
                    if (string.Equals(key, "none", StringComparison.Ordinal))
                    {
                        return -1;
                    }

                    if (IsNonCanonicalNoneSentinel(key))
                    {
                        throw new InvalidOperationException($"{context} uses invalid sentinel '{key}'. Use lowercase 'none'.");
                    }

                    return PerformerParamKeyRegistry.Register(key);
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string. Field must be explicit.");
        }

        private static void ValidateTransition(
            string key,
            int transitionIndex,
            int stateCount,
            in AnimatorTransitionDefinition transition)
        {
            if ((uint)transition.FromStateIndex >= (uint)stateCount)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].fromStateIndex {transition.FromStateIndex} is outside states length {stateCount}.");
            }

            if ((uint)transition.ToStateIndex >= (uint)stateCount)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].toStateIndex {transition.ToStateIndex} is outside states length {stateCount}.");
            }

            bool requiresParameter = transition.ConditionKind is AnimatorConditionKind.Trigger
                or AnimatorConditionKind.BoolTrue
                or AnimatorConditionKind.BoolFalse
                or AnimatorConditionKind.FloatGreaterOrEqual
                or AnimatorConditionKind.FloatLessOrEqual;
            if (requiresParameter && transition.ParameterIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}] conditionKind '{transition.ConditionKind}' requires a concrete parameterIndex.");
            }

            bool forbidsParameter = transition.ConditionKind is AnimatorConditionKind.None
                or AnimatorConditionKind.AutoOnNormalizedTime;
            if (forbidsParameter && transition.ParameterIndex >= 0)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}] conditionKind '{transition.ConditionKind}' requires parameterIndex 'none'.");
            }

            if (transition.ConditionKind != AnimatorConditionKind.Trigger && transition.ConsumeTrigger)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].consumeTrigger can be true only when conditionKind is Trigger.");
            }
        }

        private static string RequireString(JsonNode? node, string context)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
            {
                RequireNoBoundaryWhitespace(text, context);
                return text;
            }

            throw new InvalidOperationException($"{context} requires a non-empty semantic string. Field must be explicit.");
        }

        private static int RequireInt(JsonNode? node, string context)
        {
            if (node is JsonValue value && value.TryGetValue<int>(out int result))
            {
                return result;
            }

            throw new InvalidOperationException($"{context} requires an explicit integer.");
        }

        private static bool RequireBool(JsonNode? node, string context)
        {
            if (node is JsonValue value && value.TryGetValue<bool>(out bool result))
            {
                return result;
            }

            throw new InvalidOperationException($"{context} requires an explicit boolean.");
        }

        private static float RequireFiniteFloat(JsonNode? node, string context)
        {
            if (node is JsonValue value && value.TryGetValue<float>(out float result) && float.IsFinite(result))
            {
                return result;
            }

            throw new InvalidOperationException($"{context} requires an explicit finite number.");
        }

        private static float RequirePositiveFloat(JsonNode? node, string context)
        {
            float result = RequireFiniteFloat(node, context);
            if (result <= 0f)
            {
                throw new InvalidOperationException($"{context} must be positive.");
            }

            return result;
        }

        private static float RequireNonNegativeFloat(JsonNode? node, string context)
        {
            float result = RequireFiniteFloat(node, context);
            if (result < 0f)
            {
                throw new InvalidOperationException($"{context} cannot be negative.");
            }

            return result;
        }

        private static T RequireEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            string text = RequireString(node, context);
            if (int.TryParse(text, out _))
            {
                throw new InvalidOperationException($"{context} must be an enum string, not numeric value '{text}'.");
            }

            if (!Enum.TryParse(text, ignoreCase: false, out T result) || !Enum.IsDefined(typeof(T), result))
            {
                throw new InvalidOperationException($"{context} has invalid value '{text}'.");
            }

            return result;
        }

        private static void RequireNoBoundaryWhitespace(string text, string context)
        {
            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }
        }

        private static bool IsNonCanonicalNoneSentinel(string key)
        {
            return key.Length == 4 &&
                   (key[0] == 'n' || key[0] == 'N') &&
                   (key[1] == 'o' || key[1] == 'O') &&
                   (key[2] == 'n' || key[2] == 'N') &&
                   (key[3] == 'e' || key[3] == 'E');
        }
    }
}
