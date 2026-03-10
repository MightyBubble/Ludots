using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Accumulates visual-frame input samples so fixed-step systems can consume
    /// one authoritative snapshot per logic tick without reading live input directly.
    /// </summary>
    public sealed class AuthoritativeInputAccumulator
    {
        private readonly Dictionary<string, AccumulatedActionState> _states = new(StringComparer.Ordinal);

        public void Clear()
        {
            _states.Clear();
        }

        public void CaptureAction(string actionId, Vector3 value, bool isDown, bool pressedThisFrame, bool releasedThisFrame)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            if (!_states.TryGetValue(actionId, out var state))
            {
                state = new AccumulatedActionState();
                _states[actionId] = state;
            }

            state.Value = value;
            state.IsDown = isDown;
            state.PressedThisTick |= pressedThisFrame;
            state.ReleasedThisTick |= releasedThisFrame;
        }

        public void CaptureVisualFrame(PlayerInputHandler input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            input.CaptureFrame(this);
        }

        public void BuildTickSnapshot(FrozenInputActionReader snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            snapshot.Clear();
            foreach (var pair in _states)
            {
                var state = pair.Value;
                snapshot.SetActionState(pair.Key, state.Value, state.IsDown, state.PressedThisTick, state.ReleasedThisTick);
                state.PressedThisTick = false;
                state.ReleasedThisTick = false;
            }
        }

        private sealed class AccumulatedActionState
        {
            public Vector3 Value;
            public bool IsDown;
            public bool PressedThisTick;
            public bool ReleasedThisTick;
        }
    }
}
