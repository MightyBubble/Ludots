using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationAuthoringContext
    {
        private readonly VisualTemplateRegistry _visualTemplates;
        private readonly PresentationImageRegistry _images;
        private readonly PerformerDefinitionRegistry _performers;
        private readonly AnimatorControllerRegistry _animators;
        private readonly PresentationStableIdAllocator _stableIds;

        public PresentationAuthoringContext(
            VisualTemplateRegistry visualTemplates,
            PresentationImageRegistry images,
            PerformerDefinitionRegistry performers,
            AnimatorControllerRegistry animators,
            PresentationStableIdAllocator stableIds)
        {
            _visualTemplates = visualTemplates ?? throw new ArgumentNullException(nameof(visualTemplates));
            _images = images ?? throw new ArgumentNullException(nameof(images));
            _performers = performers ?? throw new ArgumentNullException(nameof(performers));
            _animators = animators ?? throw new ArgumentNullException(nameof(animators));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
        }

        public void Apply(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
                throw new InvalidOperationException("Presentation authoring block must be a JSON object.");

            int stableId = 0;
            if (obj.TryGetPropertyValue("visualTemplateId", out var visualTemplateNode) && visualTemplateNode != null)
            {
                string templateKey = visualTemplateNode.GetValue<string>();
                int templateId = _visualTemplates.GetId(templateKey);
                if (templateId <= 0 || !_visualTemplates.TryGet(templateId, out var template))
                    throw new InvalidOperationException($"Presentation authoring references unknown visualTemplateId '{templateKey}'.");

                bool? visibleOverride = obj["visible"]?.GetValue<bool>();
                ApplyVisual(entity, templateId, in template, visibleOverride);
                stableId = EnsureStableId(entity);
            }

            if (obj.TryGetPropertyValue("imageBindings", out var imageBindingsNode) && imageBindingsNode is JsonArray imageBindingsArray)
            {
                ApplyImageBindings(entity, imageBindingsArray);
            }

            if (obj.TryGetPropertyValue("startupPerformerIds", out var startupNode) && startupNode is JsonArray startupArray && startupArray.Count > 0)
            {
                ApplyStartupPerformers(entity, startupArray);
                stableId = stableId != 0 ? stableId : EnsureStableId(entity);
            }

            if (obj.TryGetPropertyValue("animator", out var animatorNode) && animatorNode != null)
            {
                ApplyAnimator(entity, animatorNode);
                stableId = stableId != 0 ? stableId : EnsureStableId(entity);
            }
        }

        private void ApplyVisual(Entity entity, int templateId, in VisualTemplateDefinition template, bool? visibleOverride)
        {
            Upsert(entity, new VisualTemplateRef { TemplateId = templateId });
            Upsert(entity, template.ToRuntimeState(visibleOverride));

            if (template.AnimatorControllerId > 0)
            {
                Upsert(entity, CreateDefaultPackedState(template.AnimatorControllerId, "Presentation visual template"));
                Upsert(entity, AnimatorRuntimeState.Create(template.AnimatorControllerId));
                Upsert(entity, default(AnimatorParameterBuffer));
                Upsert(entity, default(AnimationOverlayRequest));
                Upsert(entity, default(AnimatorFeedbackBuffer));
            }
        }

        private void ApplyStartupPerformers(Entity entity, JsonArray startupArray)
        {
            if (startupArray.Count > PresentationStartupPerformers.MaxCount)
            {
                throw new InvalidOperationException(
                    $"Presentation startup performer count {startupArray.Count} exceeds max {PresentationStartupPerformers.MaxCount}.");
            }

            var performers = default(PresentationStartupPerformers);
            performers.Count = (byte)startupArray.Count;

            for (int i = 0; i < startupArray.Count; i++)
            {
                string performerKey = startupArray[i]?.GetValue<string>() ?? string.Empty;
                int performerId = _performers.GetId(performerKey);
                if (performerId <= 0)
                    throw new InvalidOperationException($"Presentation authoring references unknown startup performer '{performerKey}'.");

                performers.Set(i, performerId);
            }

            Upsert(entity, performers);
            Upsert(entity, new PresentationStartupState { Initialized = false });
        }

        private void ApplyAnimator(Entity entity, JsonNode animatorNode)
        {
            if (animatorNode is not JsonObject obj)
                throw new InvalidOperationException("Presentation animator block must be a JSON object.");

            bool hasPackedState = entity.Has<AnimatorPackedState>();
            AnimatorPackedState packed = hasPackedState
                ? entity.Get<AnimatorPackedState>()
                : default;

            string controllerKey = obj["controllerId"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(controllerKey))
            {
                int controllerId = ResolveRequiredAnimatorControllerId(controllerKey, "Presentation animator block");
                if (!hasPackedState || packed.GetControllerId() != controllerId)
                {
                    packed = CreateDefaultPackedState(controllerId, "Presentation animator block");
                    hasPackedState = true;
                }
            }

            if (packed.GetControllerId() <= 0)
            {
                int visualControllerId = entity.Has<VisualRuntimeState>()
                    ? entity.Get<VisualRuntimeState>().AnimatorControllerId
                    : 0;
                if (visualControllerId > 0)
                {
                    packed = CreateDefaultPackedState(visualControllerId, "Presentation animator block");
                    hasPackedState = true;
                }
            }

            if (packed.GetControllerId() <= 0)
                throw new InvalidOperationException("Presentation animator block requires a controllerId or a visual template with animatorControllerId.");

            bool hasVisualRuntimeState = entity.Has<VisualRuntimeState>();
            if (!hasVisualRuntimeState)
            {
                throw new InvalidOperationException(
                    "Presentation animator block requires a visualTemplateId or existing VisualRuntimeState. Animator authoring cannot synthesize visual assets.");
            }

            var visual = entity.Get<VisualRuntimeState>();
            PresentationRenderContract.ValidateAnimatorAuthoring("Presentation animator block", visual.RenderPath);

            if (obj.TryGetPropertyValue("primaryStateIndex", out var primaryStateNode) && primaryStateNode != null)
                packed.SetPrimaryStateIndex(primaryStateNode.GetValue<int>());

            if (obj.TryGetPropertyValue("secondaryStateIndex", out var secondaryStateNode) && secondaryStateNode != null)
                packed.SetSecondaryStateIndex(secondaryStateNode.GetValue<int>());

            if (obj.TryGetPropertyValue("normalizedTime", out var normalizedTimeNode) && normalizedTimeNode != null)
                packed.SetNormalizedTime01(normalizedTimeNode.GetValue<float>());

            if (obj.TryGetPropertyValue("transitionProgress", out var transitionNode) && transitionNode != null)
                packed.SetTransitionProgress01(transitionNode.GetValue<float>());

            if (obj.TryGetPropertyValue("flagsMask", out var flagsMaskNode) && flagsMaskNode != null)
            {
                packed.SetFlags((AnimatorPackedStateFlags)flagsMaskNode.GetValue<int>());
            }
            else if (obj.TryGetPropertyValue("flags", out var flagsNode) && flagsNode is JsonArray flagsArray)
            {
                var flags = AnimatorPackedStateFlags.None;
                for (int i = 0; i < flagsArray.Count; i++)
                {
                    string flagText = flagsArray[i]?.GetValue<string>() ?? string.Empty;
                    if (!Enum.TryParse(flagText, ignoreCase: false, out AnimatorPackedStateFlags parsed))
                        throw new InvalidOperationException($"Presentation animator flag '{flagText}' is invalid.");
                    flags |= parsed;
                }

                packed.SetFlags(flags);
            }

            if (obj.TryGetPropertyValue("parameterBits", out var bitsNode) && bitsNode is JsonArray bitsArray)
            {
                for (int i = 0; i < bitsArray.Count; i++)
                {
                    int bitIndex = bitsArray[i]?.GetValue<int>() ?? -1;
                    packed.SetParameterBit(bitIndex, true);
                }
            }

            Upsert(entity, packed);

            if (!entity.Has<AnimatorRuntimeState>())
                entity.Add(AnimatorRuntimeState.Create(packed.GetControllerId()));

            if (!entity.Has<AnimatorParameterBuffer>())
                entity.Add(default(AnimatorParameterBuffer));

            if (!entity.Has<AnimationOverlayRequest>())
                entity.Add(default(AnimationOverlayRequest));

            if (!entity.Has<AnimatorFeedbackBuffer>())
                entity.Add(default(AnimatorFeedbackBuffer));

            visual.AnimatorControllerId = packed.GetControllerId();
            visual.Flags |= VisualRuntimeFlags.HasAnimator;
            PresentationRenderContract.ValidateRuntimeState(
                "Presentation animator block",
                visual,
                hasAnimatorComponent: true,
                packed,
                entity.Get<AnimationOverlayRequest>());
            entity.Set(visual);
        }

        private int ResolveRequiredAnimatorControllerId(string controllerKey, string sourceName)
        {
            int controllerId = _animators.GetId(controllerKey);
            if (controllerId <= 0)
            {
                throw new InvalidOperationException($"{sourceName} references unknown animator controller '{controllerKey}'.");
            }

            string registeredKey = _animators.GetName(controllerId);
            if (!string.Equals(registeredKey, controllerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{sourceName} references animator controller '{controllerKey}' with casing that does not match registered key '{registeredKey}'.");
            }

            return controllerId;
        }

        private AnimatorPackedState CreateDefaultPackedState(int controllerId, string sourceName)
        {
            if (!_animators.TryGet(controllerId, out var definition))
            {
                throw new InvalidOperationException(
                    $"{sourceName} references animator controller id {controllerId}, but no controller definition is registered.");
            }

            var packed = AnimatorPackedState.Create(controllerId);
            int defaultStateIndex = definition.ResolveDefaultStateIndex();
            if (defaultStateIndex == AnimatorRuntimeState.NoState)
            {
                return packed;
            }

            if (!definition.TryGetState(defaultStateIndex, out var state))
            {
                throw new InvalidOperationException(
                    $"{sourceName} animator controller '{_animators.GetName(controllerId)}' resolves missing default state index {defaultStateIndex}.");
            }

            packed.SetPrimaryStateIndex(state.PackedStateIndex);
            if (state.Loop)
            {
                packed.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);
            }

            return packed;
        }

        private void ApplyImageBindings(Entity entity, JsonArray bindingsArray)
        {
            if (bindingsArray.Count == 0)
            {
                throw new InvalidOperationException("Presentation imageBindings must define at least one binding.");
            }

            PresentationImageBinding bindings = entity.Has<PresentationImageBinding>()
                ? entity.Get<PresentationImageBinding>()
                : default;

            for (int i = 0; i < bindingsArray.Count; i++)
            {
                if (bindingsArray[i] is not JsonObject bindingNode)
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] must be an object.");
                }

                string roleText = bindingNode["role"]?.GetValue<string>() ?? string.Empty;
                if (!Enum.TryParse(roleText, ignoreCase: false, out PresentationImageRole role))
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] uses invalid role '{roleText}'. Enum values are case-sensitive.");
                }

                string stateText = bindingNode["state"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(stateText))
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] must define non-empty 'state'.");
                }

                if (!Enum.TryParse(stateText, ignoreCase: false, out PresentationImageState state))
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] uses invalid state '{stateText}'. Enum values are case-sensitive.");
                }

                string imageAssetKey = bindingNode["imageAsset"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageAssetKey))
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] must define non-empty 'imageAsset'.");
                }

                int imageAssetId = _images.GetId(imageAssetKey);
                if (imageAssetId <= 0)
                {
                    throw new InvalidOperationException($"Presentation imageBindings[{i}] references unknown image asset '{imageAssetKey}'.");
                }

                bindings.Set(role, state, imageAssetId);
            }

            Upsert(entity, bindings);
        }

        private int EnsureStableId(Entity entity)
        {
            if (entity.Has<PresentationStableId>())
                return entity.Get<PresentationStableId>().Value;

            int stableId = _stableIds.Allocate();
            entity.Add(new PresentationStableId { Value = stableId });
            return stableId;
        }

        private static void Upsert<T>(Entity entity, in T component)
        {
            if (entity.Has<T>())
                entity.Set(component);
            else
                entity.Add(component);
        }
    }
}
