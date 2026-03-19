using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimatorControllerDefinition
    {
        public int ControllerId;
        public int DefaultStateIndex;
        public AnimatorStateDefinition[] States = Array.Empty<AnimatorStateDefinition>();
        public AnimatorTransitionDefinition[] Transitions = Array.Empty<AnimatorTransitionDefinition>();

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
    }
}
