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
            .WithAll<VisualRuntimeState, AnimatorPackedState, AnimatorRuntimeState, AnimatorParameterBuffer>();

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

                for (int i = 0; i < chunk.Count; i++)
                {
                    UpdateAnimator(ref visuals[i], ref packedStates[i], ref runtimeStates[i], ref parameterBuffers[i], dt);
                }
            }
        }

        private void UpdateAnimator(
            ref VisualRuntimeState visual,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimatorParameterBuffer parameters,
            float dt)
        {
            PresentationRenderContract.ValidateRuntimeState("AnimatorRuntimeSystem", visual, hasAnimatorComponent: true, packed);

            int controllerId = visual.AnimatorControllerId > 0 ? visual.AnimatorControllerId : packed.GetControllerId();
            if (controllerId <= 0)
            {
                return;
            }

            packed.SetControllerId(controllerId);
            packed.Word1 = parameters.BuildPackedBits();

            if (!_controllers.TryGet(controllerId, out AnimatorControllerDefinition definition))
            {
                return;
            }

            if (!runtime.Initialized || runtime.ControllerId != controllerId)
            {
                runtime = AnimatorRuntimeState.Create(controllerId);
                runtime.CurrentStateIndex = definition.ResolveDefaultStateIndex();
                runtime.Initialized = runtime.CurrentStateIndex != AnimatorRuntimeState.NoState;
            }

            if (!runtime.Initialized || !definition.TryGetState(runtime.CurrentStateIndex, out AnimatorStateDefinition currentState))
            {
                runtime = AnimatorRuntimeState.Create(controllerId);
                runtime.CurrentStateIndex = definition.ResolveDefaultStateIndex();
                runtime.Initialized = runtime.CurrentStateIndex != AnimatorRuntimeState.NoState;
                if (!runtime.Initialized || !definition.TryGetState(runtime.CurrentStateIndex, out currentState))
                {
                    packed.SetPrimaryStateIndex(0);
                    packed.SetSecondaryStateIndex(0);
                    packed.SetNormalizedTime01(0f);
                    packed.SetTransitionProgress01(0f);
                    packed.SetFlags(AnimatorPackedStateFlags.Active);
                    return;
                }
            }

            float currentDuration = ResolveDuration(currentState.DurationSeconds);
            float currentSpeed = currentState.PlaybackSpeed <= 0f ? 1f : currentState.PlaybackSpeed;
            runtime.StateElapsedSeconds += dt * currentSpeed;

            float currentNormalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, currentDuration, currentState.Loop);
            if (!runtime.IsTransitioning &&
                TryStartTransition(definition, ref parameters, ref runtime, currentNormalizedTime))
            {
                if (!runtime.IsTransitioning && !definition.TryGetState(runtime.CurrentStateIndex, out currentState))
                {
                    return;
                }

                currentDuration = ResolveDuration(currentState.DurationSeconds);
                currentNormalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, currentDuration, currentState.Loop);
            }

            float transitionProgress = 0f;
            AnimatorStateDefinition targetState = default;
            bool hasTargetState = runtime.IsTransitioning && definition.TryGetState(runtime.NextStateIndex, out targetState);

            if (runtime.IsTransitioning)
            {
                runtime.TransitionElapsedSeconds += dt;
                float transitionDuration = runtime.TransitionDurationSeconds <= 0f ? 0f : runtime.TransitionDurationSeconds;
                transitionProgress = transitionDuration <= 0f
                    ? 1f
                    : Math.Clamp(runtime.TransitionElapsedSeconds / transitionDuration, 0f, 1f);

                if (!hasTargetState || transitionProgress >= 1f)
                {
                    if (hasTargetState)
                    {
                        runtime.CurrentStateIndex = runtime.NextStateIndex;
                        currentState = targetState;
                    }

                    runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = 0f;
                    runtime.StateElapsedSeconds = 0f;
                    currentNormalizedTime = 0f;
                    transitionProgress = 0f;
                    hasTargetState = false;
                }
            }

            packed.SetPrimaryStateIndex(ClampPackedStateIndex(currentState.PackedStateIndex));
            packed.SetSecondaryStateIndex(hasTargetState ? ClampPackedStateIndex(targetState.PackedStateIndex) : 0);
            packed.SetNormalizedTime01(currentNormalizedTime);
            packed.SetTransitionProgress01(transitionProgress);

            var flags = AnimatorPackedStateFlags.Active;
            if (currentState.Loop)
            {
                flags |= AnimatorPackedStateFlags.Looping;
            }

            if (runtime.IsTransitioning)
            {
                flags |= AnimatorPackedStateFlags.InTransition;
            }

            if (parameters.TriggerBits != 0)
            {
                flags |= AnimatorPackedStateFlags.PendingTrigger;
            }

            packed.SetFlags(flags);
            packed.Word1 = parameters.BuildPackedBits();
        }

        private static float ResolveDuration(float durationSeconds)
        {
            return durationSeconds <= 0f ? 1f : durationSeconds;
        }

        private static float ResolveNormalizedTime(float elapsedSeconds, float durationSeconds, bool loop)
        {
            if (durationSeconds <= 0f)
            {
                return 0f;
            }

            if (!loop)
            {
                return Math.Clamp(elapsedSeconds / durationSeconds, 0f, 1f);
            }

            float cycles = elapsedSeconds / durationSeconds;
            return cycles - MathF.Floor(cycles);
        }

        private static int ClampPackedStateIndex(int packedStateIndex)
        {
            if (packedStateIndex < 0)
            {
                return 0;
            }

            return Math.Min(packedStateIndex, AnimatorPackedState.MaxStateIndex);
        }

        private static bool TryStartTransition(
            AnimatorControllerDefinition definition,
            ref AnimatorParameterBuffer parameters,
            ref AnimatorRuntimeState runtime,
            float currentNormalizedTime)
        {
            if (definition.Transitions == null || definition.Transitions.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < definition.Transitions.Length; i++)
            {
                ref readonly var transition = ref definition.Transitions[i];
                if (transition.FromStateIndex != runtime.CurrentStateIndex)
                {
                    continue;
                }

                if (!ConditionMatches(transition, ref parameters, currentNormalizedTime))
                {
                    continue;
                }

                if (transition.ConsumeTrigger && transition.ConditionKind == AnimatorConditionKind.Trigger)
                {
                    parameters.ConsumeTrigger(transition.ParameterIndex);
                }

                if (transition.DurationSeconds <= 0f)
                {
                    runtime.CurrentStateIndex = transition.ToStateIndex;
                    runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                    runtime.StateElapsedSeconds = 0f;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = 0f;
                }
                else
                {
                    runtime.NextStateIndex = transition.ToStateIndex;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = transition.DurationSeconds;
                }

                return true;
            }

            return false;
        }

        private static bool ConditionMatches(
            in AnimatorTransitionDefinition transition,
            ref AnimatorParameterBuffer parameters,
            float currentNormalizedTime)
        {
            return transition.ConditionKind switch
            {
                AnimatorConditionKind.None => true,
                AnimatorConditionKind.Trigger => parameters.HasTrigger(transition.ParameterIndex),
                AnimatorConditionKind.BoolTrue => parameters.GetBool(transition.ParameterIndex),
                AnimatorConditionKind.BoolFalse => !parameters.GetBool(transition.ParameterIndex),
                AnimatorConditionKind.FloatGreaterOrEqual => parameters.GetFloat(transition.ParameterIndex) >= transition.Threshold,
                AnimatorConditionKind.FloatLessOrEqual => parameters.GetFloat(transition.ParameterIndex) <= transition.Threshold,
                AnimatorConditionKind.AutoOnNormalizedTime => currentNormalizedTime >= transition.Threshold,
                _ => false,
            };
        }
    }
}
