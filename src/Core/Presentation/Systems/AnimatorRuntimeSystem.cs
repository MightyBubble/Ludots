using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class AnimatorRuntimeSystem : BaseSystem<World, float>
    {
        private readonly AnimatorControllerRegistry _controllers;
        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<VisualRuntimeState, AnimatorPackedState, AnimatorRuntimeState, AnimatorParameterBuffer, AnimatorFeedbackBuffer>();

        public AnimatorRuntimeSystem(World world, AnimatorControllerRegistry controllers)
            : base(world)
        {
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
        }

        public override void Update(in float dt)
        {
            var query = World.Query(in _query);
            foreach (var chunk in query)
            {
                var visuals = chunk.GetArray<VisualRuntimeState>();
                var packedStates = chunk.GetArray<AnimatorPackedState>();
                var runtimeStates = chunk.GetArray<AnimatorRuntimeState>();
                var parameterBuffers = chunk.GetArray<AnimatorParameterBuffer>();
                var feedbackBuffers = chunk.GetArray<AnimatorFeedbackBuffer>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    PresentationRenderContract.ValidateRuntimeState(
                        "AnimatorRuntimeSystem",
                        visuals[i],
                        hasAnimatorComponent: true,
                        packedStates[i],
                        default);

                    AnimatorRuntimeEvaluator.Update(
                        _controllers,
                        visuals[i].AnimatorControllerId,
                        ref packedStates[i],
                        ref runtimeStates[i],
                        ref parameterBuffers[i],
                        ref feedbackBuffers[i],
                        dt);
                }
            }
        }
    }
}
