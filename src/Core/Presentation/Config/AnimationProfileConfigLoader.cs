using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Config
{
    public sealed class AnimationProfileConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly AnimationProfileRegistry _profiles;
        private readonly AnimatorControllerRegistry _controllers;
        private readonly AnimationClipRegistry _clips;

        public AnimationProfileConfigLoader(
            ConfigPipeline configs,
            AnimationProfileRegistry profiles,
            AnimatorControllerRegistry controllers,
            AnimationClipRegistry clips)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/animation_profiles.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Animation profile is missing required 'id'.");

                _profiles.Register(key, ParseProfile(node, key));
            }
        }

        private AnimationProfileDefinition ParseProfile(JsonNode node, string key)
        {
            string controllerKey = node["animatorControllerId"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(controllerKey))
            {
                throw new InvalidOperationException($"Animation profile '{key}' requires animatorControllerId.");
            }

            int animatorControllerId = _controllers.GetId(controllerKey);
            if (animatorControllerId <= 0)
            {
                throw new InvalidOperationException(
                    $"Animation profile '{key}' references unknown animatorControllerId '{controllerKey}'.");
            }

            var stateClips = ParseStateClips(node["stateClips"], key);
            var builtinClips = ParseBuiltinClips(node["builtinClips"], key);

            ValidateUniqueStateBindings(key, stateClips);
            ValidateUniqueBuiltinBindings(key, builtinClips);

            return new AnimationProfileDefinition
            {
                AnimatorControllerId = animatorControllerId,
                StateClips = stateClips,
                BuiltinClips = builtinClips,
            };
        }

        private AnimationStateClipBinding[] ParseStateClips(JsonNode? node, string key)
        {
            if (node is not JsonArray array || array.Count == 0)
            {
                return Array.Empty<AnimationStateClipBinding>();
            }

            var bindings = new AnimationStateClipBinding[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject bindingNode)
                {
                    throw new InvalidOperationException($"Animation profile '{key}' stateClips[{i}] must be an object.");
                }

                string clipKey = bindingNode["clipAssetId"]?.GetValue<string>() ?? string.Empty;
                int clipAssetId = ResolveClipId(clipKey, key, $"stateClips[{i}]");
                bindings[i] = new AnimationStateClipBinding
                {
                    PackedStateIndex = bindingNode["packedStateIndex"]?.GetValue<int>() ?? 0,
                    ClipAssetId = clipAssetId,
                };
            }

            return bindings;
        }

        private AnimationBuiltinClipBinding[] ParseBuiltinClips(JsonNode? node, string key)
        {
            if (node is not JsonArray array || array.Count == 0)
            {
                return Array.Empty<AnimationBuiltinClipBinding>();
            }

            var bindings = new AnimationBuiltinClipBinding[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject bindingNode)
                {
                    throw new InvalidOperationException($"Animation profile '{key}' builtinClips[{i}] must be an object.");
                }

                string builtinClipText = bindingNode["builtinClipId"]?.GetValue<string>() ?? string.Empty;
                if (!Enum.TryParse(builtinClipText, ignoreCase: false, out AnimatorBuiltinClipId builtinClipId) ||
                    builtinClipId == AnimatorBuiltinClipId.None)
                {
                    throw new InvalidOperationException(
                        $"Animation profile '{key}' builtinClips[{i}] has invalid builtinClipId '{builtinClipText}'. Enum values are case-sensitive.");
                }

                string clipKey = bindingNode["clipAssetId"]?.GetValue<string>() ?? string.Empty;
                int clipAssetId = ResolveClipId(clipKey, key, $"builtinClips[{i}]");
                bindings[i] = new AnimationBuiltinClipBinding
                {
                    BuiltinClipId = builtinClipId,
                    ClipAssetId = clipAssetId,
                };
            }

            return bindings;
        }

        private int ResolveClipId(string clipKey, string profileKey, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new InvalidOperationException($"Animation profile '{profileKey}' {fieldPath} requires clipAssetId.");
            }

            int clipAssetId = _clips.GetId(clipKey);
            if (clipAssetId <= 0)
            {
                throw new InvalidOperationException(
                    $"Animation profile '{profileKey}' {fieldPath} references unknown clipAssetId '{clipKey}'.");
            }

            return clipAssetId;
        }

        private static void ValidateUniqueStateBindings(string key, AnimationStateClipBinding[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                for (int j = i + 1; j < bindings.Length; j++)
                {
                    if (bindings[i].PackedStateIndex == bindings[j].PackedStateIndex)
                    {
                        throw new InvalidOperationException(
                            $"Animation profile '{key}' defines duplicate state clip binding for packedStateIndex={bindings[i].PackedStateIndex}.");
                    }
                }
            }
        }

        private static void ValidateUniqueBuiltinBindings(string key, AnimationBuiltinClipBinding[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                for (int j = i + 1; j < bindings.Length; j++)
                {
                    if (bindings[i].BuiltinClipId == bindings[j].BuiltinClipId)
                    {
                        throw new InvalidOperationException(
                            $"Animation profile '{key}' defines duplicate builtin clip binding for '{bindings[i].BuiltinClipId}'.");
                    }
                }
            }
        }
    }
}
