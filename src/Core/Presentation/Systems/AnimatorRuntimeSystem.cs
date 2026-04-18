using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class AnimatorRuntimeSystem : BaseSystem<World, float>
    {
        private const int FeedbackKindOffset = 1;
        private const int FeedbackFromStateOffset = 2;
        private const int FeedbackToStateOffset = 3;
        private const int FeedbackNormalizedTimeOffset = 4;
        private const int FeedbackValue0Offset = 5;

        private readonly AnimatorControllerRegistry _controllers;
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PerformerAnimatorStateBuffer _animatorStates;

        public AnimatorRuntimeSystem(
            World world,
            AnimatorControllerRegistry controllers,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            PerformerAnimatorStateBuffer animatorStates)
            : base(world)
        {
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _animatorStates = animatorStates ?? throw new ArgumentNullException(nameof(animatorStates));
        }

        public override void Update(in float dt)
        {
            float tickDt = dt;
            _instances.ProcessActive(0f, (int handle, ref PerformerInstance instance) =>
            {
                if (!_definitions.TryGet(instance.DefId, out PerformerDefinition definition))
                {
                    return;
                }

                BehaviorSlot[] behaviors = definition.Behaviors;
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (slot.Kind != BehaviorKind.Animator ||
                        slot.Animator.AnimatorControllerId <= 0 ||
                        !IsBehaviorActive(instance.BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    UpdateAnimator(handle, slot.Animator, tickDt);
                }
            });
        }

        private void UpdateAnimator(int handle, in AnimatorConfig config, float dt)
        {
            _animatorStates.Ensure(handle, config.AnimatorControllerId);
            ref AnimatorPackedState packed = ref _animatorStates.GetPackedState(handle);
            ref AnimatorRuntimeState runtime = ref _animatorStates.GetRuntimeState(handle);
            ref AnimatorFeedbackBuffer feedback = ref _animatorStates.GetFeedbackBuffer(handle);

            int controllerId = packed.GetControllerId() > 0 ? packed.GetControllerId() : config.AnimatorControllerId;
            if (controllerId <= 0)
            {
                return;
            }

            packed.SetControllerId(controllerId);

            if (!_controllers.TryGet(controllerId, out AnimatorControllerDefinition definition))
            {
                if (runtime.ReportedMissingControllerId != controllerId)
                {
                    runtime.ReportedMissingControllerId = controllerId;
                    var evt = new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.ControllerMissing,
                        ControllerId = controllerId,
                        FromStateIndex = runtime.CurrentStateIndex,
                        ToStateIndex = runtime.NextStateIndex,
                    };
                    feedback.Push(evt);
                    WriteFeedbackToBlackboard(handle, config, evt);
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
                    var evt = new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.Initialized,
                        ControllerId = controllerId,
                        ToStateIndex = runtime.CurrentStateIndex,
                    };
                    feedback.Push(evt);
                    WriteFeedbackToBlackboard(handle, config, evt);
                }
            }

            if (!runtime.Initialized || !definition.TryGetState(runtime.CurrentStateIndex, out AnimatorStateDefinition currentState))
            {
                return;
            }

            float speed = config.SpeedParamKey >= 0
                ? _instances.ResolveFloat(handle, config.SpeedParamKey, 0f)
                : 1f;
            if (speed <= 0f)
            {
                speed = currentState.PlaybackSpeed <= 0f ? 1f : currentState.PlaybackSpeed;
            }

            runtime.StateElapsedSeconds += dt * speed;
            float duration = ResolveDuration(currentState.DurationSeconds);
            float normalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, duration, currentState.Loop);

            TryStartTransition(handle, config, definition, ref runtime, ref feedback, normalizedTime);
            if (!runtime.IsTransitioning && definition.TryGetState(runtime.CurrentStateIndex, out AnimatorStateDefinition resolvedState))
            {
                currentState = resolvedState;
                duration = ResolveDuration(currentState.DurationSeconds);
                normalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, duration, currentState.Loop);
            }

            if (runtime.IsTransitioning && definition.TryGetState(runtime.NextStateIndex, out AnimatorStateDefinition nextState))
            {
                runtime.TransitionElapsedSeconds += dt;
                float transitionDuration = runtime.TransitionDurationSeconds <= 0f ? 0f : runtime.TransitionDurationSeconds;
                float transitionProgress = transitionDuration <= 0f ? 1f : Math.Clamp(runtime.TransitionElapsedSeconds / transitionDuration, 0f, 1f);
                packed.SetSecondaryStateIndex(ClampPackedStateIndex(nextState.PackedStateIndex));
                packed.SetTransitionProgress01(transitionProgress);

                if (transitionProgress >= 1f)
                {
                    var evt = new AnimatorFeedbackEvent
                    {
                        Kind = AnimatorFeedbackKind.TransitionCompleted,
                        ControllerId = controllerId,
                        FromStateIndex = runtime.CurrentStateIndex,
                        ToStateIndex = runtime.NextStateIndex,
                        NormalizedTime01 = 1f,
                    };
                    feedback.Push(evt);
                    WriteFeedbackToBlackboard(handle, config, evt);
                    runtime.CurrentStateIndex = runtime.NextStateIndex;
                    runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                    runtime.StateElapsedSeconds = 0f;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = 0f;
                    packed.SetSecondaryStateIndex(0);
                    packed.SetTransitionProgress01(0f);
                    currentState = nextState;
                    normalizedTime = 0f;
                }
            }
            else
            {
                packed.SetSecondaryStateIndex(0);
                packed.SetTransitionProgress01(0f);
            }

            if (!currentState.Loop && normalizedTime >= 0.999f && runtime.LastCompletedStateIndex != runtime.CurrentStateIndex)
            {
                runtime.LastCompletedStateIndex = runtime.CurrentStateIndex;
                var evt = new AnimatorFeedbackEvent
                {
                    Kind = AnimatorFeedbackKind.StateCompleted,
                    ControllerId = controllerId,
                    FromStateIndex = runtime.CurrentStateIndex,
                    ToStateIndex = runtime.CurrentStateIndex,
                    NormalizedTime01 = normalizedTime,
                };
                feedback.Push(evt);
                WriteFeedbackToBlackboard(handle, config, evt);
            }

            packed.SetPrimaryStateIndex(ClampPackedStateIndex(currentState.PackedStateIndex));
            packed.SetNormalizedTime01(normalizedTime);

            AnimatorPackedStateFlags flags = AnimatorPackedStateFlags.Active;
            if (currentState.Loop)
            {
                flags |= AnimatorPackedStateFlags.Looping;
            }

            if (runtime.IsTransitioning)
            {
                flags |= AnimatorPackedStateFlags.InTransition;
            }

            packed.SetFlags(flags);

            if (config.StateParamKey >= 0)
            {
                _instances.SetParam(handle, config.StateParamKey, ParamLane.Int, 0f, runtime.CurrentStateIndex, default);
            }
        }

        private void TryStartTransition(
            int handle,
            in AnimatorConfig config,
            AnimatorControllerDefinition definition,
            ref AnimatorRuntimeState runtime,
            ref AnimatorFeedbackBuffer feedback,
            float normalizedTime)
        {
            if (runtime.IsTransitioning)
            {
                return;
            }

            AnimatorTransitionDefinition[] transitions = definition.Transitions ?? Array.Empty<AnimatorTransitionDefinition>();
            for (int i = 0; i < transitions.Length; i++)
            {
                ref readonly AnimatorTransitionDefinition transition = ref transitions[i];
                if (transition.FromStateIndex != runtime.CurrentStateIndex)
                {
                    continue;
                }

                int intParam = transition.ParameterIndex >= 0
                    ? _instances.ResolveInt(handle, transition.ParameterIndex, 0)
                    : 0;
                float floatParam = transition.ParameterIndex >= 0
                    ? _instances.ResolveFloat(handle, transition.ParameterIndex, 0f)
                    : 0f;
                bool matches = transition.ConditionKind switch
                {
                    AnimatorConditionKind.None => true,
                    AnimatorConditionKind.Trigger => intParam != 0,
                    AnimatorConditionKind.BoolTrue => intParam != 0,
                    AnimatorConditionKind.BoolFalse => intParam == 0,
                    AnimatorConditionKind.FloatGreaterOrEqual => floatParam >= transition.Threshold,
                    AnimatorConditionKind.FloatLessOrEqual => floatParam <= transition.Threshold,
                    AnimatorConditionKind.AutoOnNormalizedTime => normalizedTime >= transition.Threshold,
                    _ => false,
                };

                if (!matches)
                {
                    continue;
                }

                if (transition.ConditionKind == AnimatorConditionKind.Trigger && transition.ConsumeTrigger && transition.ParameterIndex >= 0)
                {
                    _instances.SetParam(handle, transition.ParameterIndex, ParamLane.Int, 0f, 0, default);
                }

                var evt = new AnimatorFeedbackEvent
                {
                    Kind = AnimatorFeedbackKind.TransitionStarted,
                    ControllerId = definition.ControllerId,
                    FromStateIndex = runtime.CurrentStateIndex,
                    ToStateIndex = transition.ToStateIndex,
                    NormalizedTime01 = normalizedTime,
                    Value0 = transition.DurationSeconds,
                };
                feedback.Push(evt);
                WriteFeedbackToBlackboard(handle, config, evt);

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

                return;
            }
        }

        private void WriteFeedbackToBlackboard(int handle, in AnimatorConfig config, in AnimatorFeedbackEvent feedback)
        {
            if (config.StateParamKey < 0)
            {
                return;
            }

            _instances.SetParam(handle, config.StateParamKey + FeedbackKindOffset, ParamLane.Int, 0f, (int)feedback.Kind, default);
            _instances.SetParam(handle, config.StateParamKey + FeedbackFromStateOffset, ParamLane.Int, 0f, feedback.FromStateIndex, default);
            _instances.SetParam(handle, config.StateParamKey + FeedbackToStateOffset, ParamLane.Int, 0f, feedback.ToStateIndex, default);
            _instances.SetParam(handle, config.StateParamKey + FeedbackNormalizedTimeOffset, ParamLane.Float, feedback.NormalizedTime01, 0, default);
            _instances.SetParam(handle, config.StateParamKey + FeedbackValue0Offset, ParamLane.Float, feedback.Value0, 0, default);
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

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }
    }
}
