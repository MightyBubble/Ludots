using System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Systems
{
    internal static class AnimatorRuntimeEvaluator
    {
        public static void Update(
            AnimatorControllerRegistry controllers,
            int animatorControllerId,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimatorParameterBuffer parameters,
            ref AnimatorFeedbackBuffer feedback,
            float dt)
        {
            int controllerId = animatorControllerId > 0 ? animatorControllerId : packed.GetControllerId();
            if (controllerId <= 0)
            {
                return;
            }

            packed.SetControllerId(controllerId);
            packed.Word1 = parameters.BuildPackedBits();

            if (!controllers.TryGet(controllerId, out AnimatorControllerDefinition definition))
            {
                if (runtime.ReportedMissingControllerId != controllerId)
                {
                    runtime.ReportedMissingControllerId = controllerId;
                    feedback.Push(new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.ControllerMissing,
                        ControllerId = controllerId,
                        FromStateIndex = runtime.CurrentStateIndex,
                        ToStateIndex = runtime.NextStateIndex,
                    });
                }

                return;
            }

            runtime.ReportedMissingControllerId = 0;

            if (!runtime.Initialized || runtime.ControllerId != controllerId)
            {
                runtime = AnimatorRuntimeState.Create(controllerId);
                runtime.CurrentStateIndex = definition.ResolveDefaultStateIndex();
                runtime.Initialized = runtime.CurrentStateIndex != AnimatorRuntimeState.NoState;
                if (runtime.Initialized)
                {
                    feedback.Push(new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.Initialized,
                        ControllerId = controllerId,
                        ToStateIndex = runtime.CurrentStateIndex,
                    });
                }
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
            int stateBeforeTick = runtime.CurrentStateIndex;
            runtime.StateElapsedSeconds += dt * currentSpeed;

            float currentNormalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, currentDuration, currentState.Loop);
            if (!runtime.IsTransitioning &&
                TryStartTransition(definition, ref parameters, ref runtime, ref feedback, currentNormalizedTime))
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
                        feedback.Push(new AnimatorFeedbackEvent
                        {
                            Kind = AnimatorFeedbackKind.TransitionCompleted,
                            ControllerId = controllerId,
                            FromStateIndex = runtime.CurrentStateIndex,
                            ToStateIndex = runtime.NextStateIndex,
                            NormalizedTime01 = transitionProgress,
                        });
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

            if (!currentState.Loop &&
                currentNormalizedTime >= 0.999f &&
                runtime.LastCompletedStateIndex != stateBeforeTick)
            {
                runtime.LastCompletedStateIndex = stateBeforeTick;
                feedback.Push(new AnimatorFeedbackEvent
                {
                    Kind = AnimatorFeedbackKind.StateCompleted,
                    ControllerId = controllerId,
                    FromStateIndex = stateBeforeTick,
                    ToStateIndex = stateBeforeTick,
                    NormalizedTime01 = currentNormalizedTime,
                });
            }
            else if (currentNormalizedTime < 0.999f && runtime.LastCompletedStateIndex == stateBeforeTick)
            {
                runtime.LastCompletedStateIndex = AnimatorRuntimeState.NoState;
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
            ref AnimatorFeedbackBuffer feedback,
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
                    feedback.Push(new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.TransitionStarted,
                        ControllerId = definition.ControllerId,
                        FromStateIndex = runtime.CurrentStateIndex,
                        ToStateIndex = transition.ToStateIndex,
                        NormalizedTime01 = currentNormalizedTime,
                        Value0 = transition.DurationSeconds,
                    });
                    feedback.Push(new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.TransitionCompleted,
                        ControllerId = definition.ControllerId,
                        FromStateIndex = runtime.CurrentStateIndex,
                        ToStateIndex = transition.ToStateIndex,
                        NormalizedTime01 = currentNormalizedTime,
                    });
                    runtime.CurrentStateIndex = transition.ToStateIndex;
                    runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = 0f;
                    runtime.StateElapsedSeconds = 0f;
                    return true;
                }

                runtime.NextStateIndex = transition.ToStateIndex;
                runtime.TransitionDurationSeconds = transition.DurationSeconds;
                runtime.TransitionElapsedSeconds = 0f;
                feedback.Push(new AnimatorFeedbackEvent
                {
                    Kind = AnimatorFeedbackKind.TransitionStarted,
                    ControllerId = definition.ControllerId,
                    FromStateIndex = runtime.CurrentStateIndex,
                    ToStateIndex = transition.ToStateIndex,
                    NormalizedTime01 = currentNormalizedTime,
                    Value0 = transition.DurationSeconds,
                });
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
                AnimatorConditionKind.FloatGreaterOrEqual => parameters.GetFloat(transition.ParameterIndex) >= transition.Threshold,
                AnimatorConditionKind.FloatLessOrEqual => parameters.GetFloat(transition.ParameterIndex) <= transition.Threshold,
                AnimatorConditionKind.BoolTrue => parameters.GetBool(transition.ParameterIndex),
                AnimatorConditionKind.Trigger => parameters.HasTrigger(transition.ParameterIndex),
                AnimatorConditionKind.AutoOnNormalizedTime => currentNormalizedTime >= transition.Threshold,
                _ => false,
            };
        }
    }
}
