using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Persistence;

namespace Ludots.Core.Input.Runtime
{
    public sealed class FrozenInputActionReader : IInputActionReader
    {
        private readonly Dictionary<string, ActionState> _states = new(StringComparer.Ordinal);
        private IReadOnlyList<AuthoritativeAction>? _replayOverride;

        public void QueueReplayActions(IReadOnlyList<AuthoritativeAction> actions)
        {
            _replayOverride = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public bool TryConsumeReplayActions(out IReadOnlyList<AuthoritativeAction>? actions)
        {
            actions = _replayOverride;
            _replayOverride = null;
            return actions != null;
        }

        public void ClearReplayActions()
        {
            _replayOverride = null;
        }

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

        public void CopyAuthoritativeActions(List<AuthoritativeAction> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            foreach (KeyValuePair<string, ActionState> pair in _states)
            {
                ActionState state = pair.Value;
                destination.Add(new AuthoritativeAction(
                    pair.Key,
                    state.Value,
                    state.IsDown,
                    state.PressedThisFrame,
                    state.ReleasedThisFrame));
            }

            destination.Sort(static (left, right) => string.CompareOrdinal(left.ActionId, right.ActionId));
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
