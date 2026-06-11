using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerAnimatorRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly AnimatorControllerRegistry _controllers;

        public PerformerAnimatorRuntimeSystem(
            World world,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            AnimatorControllerRegistry controllers)
            : base(world)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
        }

        public override void Update(in float dt)
        {
            _instances.ProcessAnimatorSlots(dt, UpdateAnimatorSlot);
        }

        private void UpdateAnimatorSlot(
            int handle,
            ref PerformerInstance instance,
            int behaviorSlot,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            ref AnimatorFeedbackBuffer feedback,
            float dt)
        {
            if (!_definitions.TryGet(instance.DefId, out var definition))
            {
                return;
            }

            AnimatorConfig animator = ResolveAnimatorConfig(definition, behaviorSlot);
            AnimatorRuntimeEvaluator.Update(
                _controllers,
                animator.AnimatorControllerId,
                ref packed,
                ref runtime,
                ref parameters,
                ref feedback,
                dt);
        }

        private static AnimatorConfig ResolveAnimatorConfig(PerformerDefinition definition, int behaviorSlot)
        {
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[i];
                if (slot.SlotIndex == behaviorSlot && slot.Kind == BehaviorKind.Animator)
                {
                    return slot.Animator;
                }
            }

            throw new InvalidOperationException(
                $"Performer '{definition.Key}' is missing Animator behavior slot '{behaviorSlot}' for its runtime animator state.");
        }
    }
}
