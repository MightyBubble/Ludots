using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimatorControllerDefinition
    {
        public int ControllerId;
        public int DefaultStateIndex;
        public AnimatorStateDefinition[] States = Array.Empty<AnimatorStateDefinition>();
        public AnimatorTransitionDefinition[] Transitions = Array.Empty<AnimatorTransitionDefinition>();
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
            if ((uint)DefaultStateIndex < (uint)States.Length)
            {
                return DefaultStateIndex;
            }

            return States.Length > 0 ? 0 : -1;
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

        internal void BuildTransitionIndex()
        {
            int stateCount = States?.Length ?? 0;
            if (stateCount == 0)
            {
                _transitionsByState = Array.Empty<AnimatorTransitionDefinition>();
                _transitionStarts = Array.Empty<int>();
                _transitionCounts = Array.Empty<int>();
                return;
            }

            AnimatorTransitionDefinition[] transitions = Transitions ?? Array.Empty<AnimatorTransitionDefinition>();
            _transitionStarts = new int[stateCount];
            _transitionCounts = new int[stateCount];
            for (int i = 0; i < transitions.Length; i++)
            {
                int from = transitions[i].FromStateIndex;
                if ((uint)from < (uint)stateCount)
                {
                    _transitionCounts[from]++;
                }
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
                if ((uint)from >= (uint)stateCount)
                {
                    continue;
                }

                _transitionsByState[writeOffsets[from]++] = transitions[i];
            }
        }
    }
}
