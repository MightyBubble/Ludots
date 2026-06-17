using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimatorControllerDefinition
    {
        public int ControllerId;
        public int DefaultStateIndex;
        public AnimatorStateDefinition[] States = null!;
        public AnimatorTransitionDefinition[] Transitions = null!;
        private AnimatorTransitionDefinition[] _transitionsByState = Array.Empty<AnimatorTransitionDefinition>();
        private int[] _transitionStarts = Array.Empty<int>();
        private int[] _transitionCounts = Array.Empty<int>();

        public bool TryGetState(int stateIndex, out AnimatorStateDefinition state)
        {
            if ((uint)stateIndex < (uint)States.Length)
            {
                state = States[stateIndex];
                return true;
            }

            state = default;
            return false;
        }

        public int ResolveDefaultStateIndex()
        {
            ValidateDefaultStateIndex(KeyForRuntimeValidation);
            return DefaultStateIndex;
        }

        public ReadOnlySpan<AnimatorTransitionDefinition> GetTransitionsFromState(int stateIndex)
        {
            if ((uint)stateIndex >= (uint)_transitionStarts.Length)
            {
                return ReadOnlySpan<AnimatorTransitionDefinition>.Empty;
            }

            int count = _transitionCounts[stateIndex];
            return count == 0
                ? ReadOnlySpan<AnimatorTransitionDefinition>.Empty
                : new ReadOnlySpan<AnimatorTransitionDefinition>(_transitionsByState, _transitionStarts[stateIndex], count);
        }

        internal void ValidateAndBuildTransitionIndex(string key)
        {
            Validate(key);
            BuildTransitionIndex();
        }

        private void Validate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Animator controller key must be a non-empty semantic string.");
            }

            AnimatorStateDefinition[] states = States
                ?? throw new InvalidOperationException($"Animator controller '{key}' states array must be explicitly initialized.");
            if (states.Length == 0)
            {
                throw new InvalidOperationException($"Animator controller '{key}' must define at least one state.");
            }

            ValidateDefaultStateIndex(key);

            for (int i = 0; i < states.Length; i++)
            {
                ref readonly AnimatorStateDefinition state = ref states[i];
                if ((uint)state.PackedStateIndex > AnimatorPackedState.MaxStateIndex)
                {
                    throw new InvalidOperationException(
                        $"Animator controller '{key}' state[{i}].packedStateIndex {state.PackedStateIndex} is outside [0, {AnimatorPackedState.MaxStateIndex}].");
                }

                if (!float.IsFinite(state.DurationSeconds) || state.DurationSeconds <= 0f)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' state[{i}].durationSeconds must be positive and finite.");
                }

                if (!float.IsFinite(state.PlaybackSpeed) || state.PlaybackSpeed <= 0f)
                {
                    throw new InvalidOperationException($"Animator controller '{key}' state[{i}].playbackSpeed must be positive and finite.");
                }
            }

            AnimatorTransitionDefinition[] transitions = Transitions
                ?? throw new InvalidOperationException($"Animator controller '{key}' transitions array must be explicitly initialized.");
            for (int i = 0; i < transitions.Length; i++)
            {
                transitions[i].DefinitionIndex = i;
                ValidateTransition(key, i, states.Length, in transitions[i]);
            }
        }

        private void ValidateDefaultStateIndex(string key)
        {
            AnimatorStateDefinition[] states = States
                ?? throw new InvalidOperationException($"Animator controller '{key}' states array must be explicitly initialized.");
            if ((uint)DefaultStateIndex >= (uint)states.Length)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' defaultStateIndex {DefaultStateIndex} is outside states length {states.Length}.");
            }
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

            if (!Enum.IsDefined(typeof(AnimatorConditionKind), transition.ConditionKind))
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].conditionKind has invalid value '{transition.ConditionKind}'.");
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

            if (!float.IsFinite(transition.Threshold))
            {
                throw new InvalidOperationException($"Animator controller '{key}' transition[{transitionIndex}].threshold must be finite.");
            }

            if (!float.IsFinite(transition.DurationSeconds) || transition.DurationSeconds < 0f)
            {
                throw new InvalidOperationException($"Animator controller '{key}' transition[{transitionIndex}].durationSeconds must be finite and non-negative.");
            }

            if (transition.ConditionKind != AnimatorConditionKind.Trigger && transition.ConsumeTrigger)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].consumeTrigger can be true only when conditionKind is Trigger.");
            }

            if (!Enum.IsDefined(typeof(AnimatorTransitionDurationMode), transition.DurationMode))
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].durationMode has invalid value '{transition.DurationMode}'.");
            }

            if (transition.HasExitTime && (transition.ExitTime < 0f || transition.ExitTime > 1f))
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].exitTime must be in [0, 1] when hasExitTime is true.");
            }

            if (!transition.HasExitTime && transition.ExitTime != 0f)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].exitTime must be 0 when hasExitTime is false.");
            }

            if (!Enum.IsDefined(typeof(AnimatorTransitionInterruptSource), transition.InterruptSource))
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].interruptSource has invalid value '{transition.InterruptSource}'.");
            }

            if (transition.OrderedInterruption && transition.InterruptSource == AnimatorTransitionInterruptSource.None)
            {
                throw new InvalidOperationException(
                    $"Animator controller '{key}' transition[{transitionIndex}].orderedInterruption requires a non-None interruptSource.");
            }
        }

        private void BuildTransitionIndex()
        {
            int stateCount = States.Length;

            AnimatorTransitionDefinition[] transitions = Transitions;
            _transitionStarts = new int[stateCount];
            _transitionCounts = new int[stateCount];
            for (int i = 0; i < transitions.Length; i++)
            {
                _transitionCounts[transitions[i].FromStateIndex]++;
            }

            int offset = 0;
            for (int i = 0; i < stateCount; i++)
            {
                _transitionStarts[i] = offset;
                offset += _transitionCounts[i];
            }

            _transitionsByState = offset == 0
                ? Array.Empty<AnimatorTransitionDefinition>()
                : new AnimatorTransitionDefinition[offset];
            if (offset == 0)
            {
                return;
            }

            int[] writeOffsets = new int[stateCount];
            Array.Copy(_transitionStarts, writeOffsets, stateCount);
            for (int i = 0; i < transitions.Length; i++)
            {
                int from = transitions[i].FromStateIndex;
                _transitionsByState[writeOffsets[from]++] = transitions[i];
            }
        }

        private const string KeyForRuntimeValidation = "runtime-registered";
    }
}
