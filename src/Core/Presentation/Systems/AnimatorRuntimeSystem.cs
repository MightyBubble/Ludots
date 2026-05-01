using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
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
        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PerformerAnimatorStateBuffer _animatorStates;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly QueryDescription _activeAnimatorQuery = new QueryDescription()
            .WithAll<PerformerState, PerfHasAnimator, PerformerFloatParams, PerformerFloatDefaults, PerformerParent>();
        private PerformerAnimatorSlot _scratchAnimatorSlot;

        public AnimatorRuntimeSystem(
            World world,
            AnimatorControllerRegistry controllers,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PerformerAnimatorStateBuffer animatorStates,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _animatorStates = animatorStates ?? throw new ArgumentNullException(nameof(animatorStates));
            _timingDiagnostics = timingDiagnostics;
            _runtime.BindDefinitions(_definitions);
        }
        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            float tickDt = dt;
            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            int cachedControllerId = -1;
            AnimatorControllerDefinition? cachedController = null;
            foreach (ref var chunk in World.Query(in _activeAnimatorQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerFloatParams> floatParams = chunk.GetSpan<PerformerFloatParams>();
                Span<PerformerFloatDefaults> floatDefaults = chunk.GetSpan<PerformerFloatDefaults>();
                Span<PerformerParent> parents = chunk.GetSpan<PerformerParent>();
                bool hasAnimatorSlots = chunk.Has<PerformerAnimatorSlot>();
                Span<PerformerAnimatorSlot> animatorSlots = hasAnimatorSlots
                    ? chunk.GetSpan<PerformerAnimatorSlot>()
                    : default;
                foreach (int index in chunk)
                {
                    ref PerformerState state = ref states[index];
                    if (state.DefId != cachedDefId)
                    {
                        cachedDefId = state.DefId;
                        cachedDefinition = _definitions.TryGet(state.DefId, out PerformerDefinition definition)
                            ? definition
                            : null;
                    }

                    if (cachedDefinition == null ||
                        !cachedDefinition.HasAnimatorBehavior ||
                        (state.BehaviorActiveMask & cachedDefinition.AnimatorSlotMask) == 0u)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    BehaviorSlot[] behaviors = cachedDefinition.Behaviors;
                    if (cachedDefinition.SupportsSingleAnimatorFastUpdate)
                    {
                        ref readonly BehaviorSlot slot = ref behaviors[cachedDefinition.SingleAnimatorFastBehaviorIndex];
                        if (slot.Animator.AnimatorControllerId > 0 &&
                            IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        {
                            if (!hasAnimatorSlots)
                            {
                                _scratchAnimatorSlot.Value = -1;
                            }

                            ref PerformerAnimatorSlot animatorSlot = ref hasAnimatorSlots
                                ? ref animatorSlots[index]
                                : ref _scratchAnimatorSlot;
                            UpdateAnimator(
                                entity,
                                slot.Animator,
                                ref floatParams[index],
                                ref floatDefaults[index],
                                in parents[index],
                                ref animatorSlot,
                                tickDt,
                                ref cachedControllerId,
                                ref cachedController);
                        }

                        continue;
                    }

                    for (int bi = 0; bi < behaviors.Length; bi++)
                    {
                        ref readonly BehaviorSlot slot = ref behaviors[bi];
                        if (slot.Kind != BehaviorKind.Animator || slot.Animator.AnimatorControllerId <= 0 ||
                            !IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        {
                            continue;
                        }

                        if (!hasAnimatorSlots)
                        {
                            _scratchAnimatorSlot.Value = -1;
                        }

                        ref PerformerAnimatorSlot animatorSlot = ref hasAnimatorSlots
                            ? ref animatorSlots[index]
                            : ref _scratchAnimatorSlot;
                        UpdateAnimator(
                            entity,
                            slot.Animator,
                            ref floatParams[index],
                            ref floatDefaults[index],
                            in parents[index],
                            ref animatorSlot,
                            tickDt,
                            ref cachedControllerId,
                            ref cachedController);
                    }
                }
            }

            if (_timingDiagnostics != null)
                _timingDiagnostics.ObservePerformerAnimator((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }

        private void UpdateAnimator(
            Entity entity,
            in AnimatorConfig config,
            ref PerformerFloatParams floatParams,
            ref PerformerFloatDefaults floatDefaults,
            in PerformerParent parent,
            ref PerformerAnimatorSlot animatorSlot,
            float dt,
            ref int cachedControllerId,
            ref AnimatorControllerDefinition? cachedController)
        {
            int slot = animatorSlot.Value >= 0
                ? animatorSlot.Value
                : _animatorStates.EnsureAndResolveSlot(entity, config.AnimatorControllerId);
            if (animatorSlot.Value != slot)
            {
                animatorSlot.Value = slot;
            }
            ref AnimatorPackedState packed = ref _animatorStates.GetPackedStateBySlot(slot);
            ref AnimatorRuntimeState runtime = ref _animatorStates.GetRuntimeStateBySlot(slot);
            ref AnimatorFeedbackBuffer feedback = ref _animatorStates.GetFeedbackBufferBySlot(slot);

            int controllerId = packed.GetControllerId() > 0 ? packed.GetControllerId() : config.AnimatorControllerId;
            if (controllerId <= 0) return;
            if (packed.GetControllerId() != controllerId)
            {
                packed.SetControllerId(controllerId);
            }

            if (controllerId != cachedControllerId)
            {
                cachedControllerId = controllerId;
                cachedController = _controllers.TryGet(controllerId, out AnimatorControllerDefinition resolved)
                    ? resolved
                    : null;
            }

            if (cachedController == null)
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
                    WriteFeedbackToBlackboard(entity, config, evt);
                }
                return;
            }
            AnimatorControllerDefinition definition = cachedController;
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
                    WriteFeedbackToBlackboard(entity, config, evt);
                }
            }

            if (!runtime.Initialized || !definition.TryGetState(runtime.CurrentStateIndex, out AnimatorStateDefinition currentState))
                return;

            float speed = config.SpeedParamKey >= 0
                ? ResolveFloatFast(entity, config.SpeedParamKey, 0f, ref floatParams, ref floatDefaults, in parent)
                : 1f;
            if (speed <= 0f) speed = currentState.PlaybackSpeed <= 0f ? 1f : currentState.PlaybackSpeed;
            runtime.StateElapsedSeconds += dt * speed;
            float duration = ResolveDuration(currentState.DurationSeconds);
            float normalizedTime = ResolveNormalizedTime(runtime.StateElapsedSeconds, duration, currentState.Loop);

            TryStartTransition(
                entity,
                config,
                definition,
                ref runtime,
                ref feedback,
                ref floatParams,
                ref floatDefaults,
                in parent,
                normalizedTime);
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
                        Kind = AnimatorFeedbackKind.TransitionCompleted, ControllerId = controllerId,
                        FromStateIndex = runtime.CurrentStateIndex, ToStateIndex = runtime.NextStateIndex, NormalizedTime01 = 1f,
                    };
                    feedback.Push(evt);
                    WriteFeedbackToBlackboard(entity, config, evt);
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
                    Kind = AnimatorFeedbackKind.StateCompleted, ControllerId = controllerId,
                    FromStateIndex = runtime.CurrentStateIndex, ToStateIndex = runtime.CurrentStateIndex, NormalizedTime01 = normalizedTime,
                };
                feedback.Push(evt);
                WriteFeedbackToBlackboard(entity, config, evt);
            }

            packed.SetPrimaryStateIndex(ClampPackedStateIndex(currentState.PackedStateIndex));
            packed.SetNormalizedTime01(normalizedTime);
            AnimatorPackedStateFlags flags = AnimatorPackedStateFlags.Active;
            if (currentState.Loop) flags |= AnimatorPackedStateFlags.Looping;
            if (runtime.IsTransitioning) flags |= AnimatorPackedStateFlags.InTransition;
            packed.SetFlags(flags);
            if (config.StateParamKey >= 0)
                _runtime.SetParam(entity, config.StateParamKey, ParamLane.Int, 0f, runtime.CurrentStateIndex, default);
        }

        private void TryStartTransition(
            Entity entity,
            in AnimatorConfig config,
            AnimatorControllerDefinition definition,
            ref AnimatorRuntimeState runtime,
            ref AnimatorFeedbackBuffer feedback,
            ref PerformerFloatParams floatParams,
            ref PerformerFloatDefaults floatDefaults,
            in PerformerParent parent,
            float normalizedTime)
        {
            if (runtime.IsTransitioning) return;
            ReadOnlySpan<AnimatorTransitionDefinition> transitions = definition.GetTransitionsFromState(runtime.CurrentStateIndex);
            for (int i = 0; i < transitions.Length; i++)
            {
                ref readonly AnimatorTransitionDefinition transition = ref transitions[i];
                bool matches = transition.ConditionKind switch
                {
                    AnimatorConditionKind.None => true,
                    AnimatorConditionKind.Trigger => transition.ParameterIndex >= 0 && _runtime.ResolveInt(entity, transition.ParameterIndex, 0) != 0,
                    AnimatorConditionKind.BoolTrue => transition.ParameterIndex >= 0 && _runtime.ResolveInt(entity, transition.ParameterIndex, 0) != 0,
                    AnimatorConditionKind.BoolFalse => transition.ParameterIndex < 0 || _runtime.ResolveInt(entity, transition.ParameterIndex, 0) == 0,
                    AnimatorConditionKind.FloatGreaterOrEqual => transition.ParameterIndex >= 0 &&
                        ResolveFloatFast(entity, transition.ParameterIndex, 0f, ref floatParams, ref floatDefaults, in parent) >= transition.Threshold,
                    AnimatorConditionKind.FloatLessOrEqual => transition.ParameterIndex >= 0 &&
                        ResolveFloatFast(entity, transition.ParameterIndex, 0f, ref floatParams, ref floatDefaults, in parent) <= transition.Threshold,
                    AnimatorConditionKind.AutoOnNormalizedTime => normalizedTime >= transition.Threshold,
                    _ => false,
                };
                if (!matches) continue;
                if (transition.ConditionKind == AnimatorConditionKind.Trigger && transition.ConsumeTrigger && transition.ParameterIndex >= 0)
                    _runtime.SetParam(entity, transition.ParameterIndex, ParamLane.Int, 0f, 0, default);
                var evt = new AnimatorFeedbackEvent
                {
                    Kind = AnimatorFeedbackKind.TransitionStarted, ControllerId = definition.ControllerId,
                    FromStateIndex = runtime.CurrentStateIndex, ToStateIndex = transition.ToStateIndex,
                    NormalizedTime01 = normalizedTime, Value0 = transition.DurationSeconds,
                };
                feedback.Push(evt);
                WriteFeedbackToBlackboard(entity, config, evt);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ResolveFloatFast(
            Entity performer,
            int paramKey,
            float defaultValue,
            ref PerformerFloatParams floatParams,
            ref PerformerFloatDefaults floatDefaults,
            in PerformerParent parent)
        {
            if (floatParams.TryGet(paramKey, out float value) ||
                floatDefaults.TryGet(paramKey, out value))
            {
                return value;
            }

            return parent.Parent == Entity.Null
                ? defaultValue
                : _runtime.ResolveFloat(performer, paramKey, defaultValue);
        }

        private void WriteFeedbackToBlackboard(Entity entity, in AnimatorConfig config, in AnimatorFeedbackEvent feedback)
        {
            if (config.StateParamKey < 0) return;
            _runtime.SetParam(entity, config.StateParamKey + FeedbackKindOffset, ParamLane.Int, 0f, (int)feedback.Kind, default);
            _runtime.SetParam(entity, config.StateParamKey + FeedbackFromStateOffset, ParamLane.Int, 0f, feedback.FromStateIndex, default);
            _runtime.SetParam(entity, config.StateParamKey + FeedbackToStateOffset, ParamLane.Int, 0f, feedback.ToStateIndex, default);
            _runtime.SetParam(entity, config.StateParamKey + FeedbackNormalizedTimeOffset, ParamLane.Float, feedback.NormalizedTime01, 0, default);
            _runtime.SetParam(entity, config.StateParamKey + FeedbackValue0Offset, ParamLane.Float, feedback.Value0, 0, default);
        }

        private static float ResolveDuration(float durationSeconds) => durationSeconds <= 0f ? 1f : durationSeconds;

        private static float ResolveNormalizedTime(float elapsedSeconds, float durationSeconds, bool loop)
        {
            if (durationSeconds <= 0f) return 0f;
            if (!loop) return Math.Clamp(elapsedSeconds / durationSeconds, 0f, 1f);
            float cycles = elapsedSeconds / durationSeconds;
            return cycles - MathF.Floor(cycles);
        }

        private static int ClampPackedStateIndex(int packedStateIndex)
        {
            if (packedStateIndex < 0) return 0;
            return Math.Min(packedStateIndex, AnimatorPackedState.MaxStateIndex);
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }
    }
}
