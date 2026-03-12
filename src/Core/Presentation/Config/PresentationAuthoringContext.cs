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
        private readonly PerformerDefinitionRegistry _performers;
        private readonly AnimatorControllerRegistry _animators;
        private readonly PresentationStableIdAllocator _stableIds;

        public PresentationAuthoringContext(
            VisualTemplateRegistry visualTemplates,
            PerformerDefinitionRegistry performers,
            AnimatorControllerRegistry animators,
            PresentationStableIdAllocator stableIds)
        {
            _visualTemplates = visualTemplates ?? throw new ArgumentNullException(nameof(visualTemplates));
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
                Upsert(entity, AnimatorPackedState.Create(template.AnimatorControllerId));
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

            AnimatorPackedState packed = entity.Has<AnimatorPackedState>()
                ? entity.Get<AnimatorPackedState>()
                : default;

            string controllerKey = obj["controllerId"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(controllerKey))
            {
                packed.SetControllerId(_animators.Register(controllerKey));
            }

            if (packed.GetControllerId() <= 0)
                throw new InvalidOperationException("Presentation animator block requires a controllerId or a visual template with animatorControllerId.");

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
                    if (!Enum.TryParse(flagText, ignoreCase: true, out AnimatorPackedStateFlags parsed))
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

            if (entity.Has<VisualRuntimeState>())
            {
                var visual = entity.Get<VisualRuntimeState>();
                visual.AnimatorControllerId = packed.GetControllerId();
                visual.Flags |= VisualRuntimeFlags.HasAnimator;
                entity.Set(visual);
            }
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
