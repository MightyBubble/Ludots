using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    public sealed class FrozenInputActionReader : IInputActionReader
    {
        private readonly Dictionary<string, ActionState> _states = new(StringComparer.Ordinal);

        public void Clear()
        {
            _states.Clear();
        }

        public void SetActionValue(string actionId, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _states[actionId] = new ActionState
            {
                Value = value,
                IsDown = IsNonZero(value),
            };
        }

        public void SetActionState(string actionId, Vector3 value, bool isDown, bool pressedThisFrame, bool releasedThisFrame)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _states[actionId] = new ActionState
            {
                Value = value,
                IsDown = isDown,
                PressedThisFrame = pressedThisFrame,
                ReleasedThisFrame = releasedThisFrame,
            };
        }

        public void AddActionValue(string actionId, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            if (_states.TryGetValue(actionId, out var existing))
            {
                existing.Value += value;
                existing.IsDown = IsNonZero(existing.Value);
                _states[actionId] = existing;
                return;
            }

            _states[actionId] = new ActionState
            {
                Value = value,
                IsDown = IsNonZero(value),
            };
        }

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (string.IsNullOrWhiteSpace(actionId) || !_states.TryGetValue(actionId, out var state))
            {
                return default;
            }

            if (typeof(T) == typeof(bool)) return (T)(object)state.IsDown;
            if (typeof(T) == typeof(float)) return (T)(object)state.Value.X;
            if (typeof(T) == typeof(Vector2)) return (T)(object)new Vector2(state.Value.X, state.Value.Y);
            if (typeof(T) == typeof(Vector3)) return (T)(object)state.Value;
            return default;
        }

        public bool IsDown(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   _states.TryGetValue(actionId, out var state) &&
                   state.IsDown;
        }

        public bool PressedThisFrame(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   _states.TryGetValue(actionId, out var state) &&
                   state.PressedThisFrame;
        }

        public bool ReleasedThisFrame(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   _states.TryGetValue(actionId, out var state) &&
                   state.ReleasedThisFrame;
        }

        private static bool IsNonZero(Vector3 value)
        {
            return value.LengthSquared() > 0.000001f;
        }

        private struct ActionState
        {
            public Vector3 Value;
            public bool IsDown;
            public bool PressedThisFrame;
            public bool ReleasedThisFrame;
        }
    }
}
