using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Persistence;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// The authoritative input snapshot, refrozen once per logic tick from
    /// <see cref="AuthoritativeInputAccumulator"/>. Edge reads here are tick edges — every
    /// visual frame since the previous freeze is folded in — so fixed-step consumers keep
    /// their edges even when the pacemaker skips logic ticks between visual frames.
    /// </summary>
    public sealed class FrozenInputActionReader : IInputActionReader
    {
        private readonly Dictionary<string, ActionState> _states = new(StringComparer.Ordinal);
        private IReadOnlyList<AuthoritativeAction>? _replayOverride;
        public bool ReplayInputIsolation { get; private set; }

        public void SetReplayInputIsolation(bool enabled)
        {
            ReplayInputIsolation = enabled;
            if (!enabled) _replayOverride = null;
        }

        public void QueueReplayActions(IReadOnlyList<AuthoritativeAction> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (_replayOverride != null) throw new SaveContextException("Replay input frame is still pending consumption.");
            _replayOverride = actions.Count == 0 ? Array.Empty<AuthoritativeAction>() : new List<AuthoritativeAction>(actions).ToArray();
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
            _replayOverride = null;
            ReplayInputIsolation = false;
        }

        public void ClearSnapshot()
        {
            _states.Clear();
            _replayOverride = null;
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

        /// <summary>
        /// Authoritative press edge for logic-tick consumers: true when the action was
        /// pressed in any visual frame folded into this frozen snapshot. Supersedes the
        /// live handler's single-visual-frame edge, which is lost whenever the pacemaker
        /// skips the logic tick that would consume it.
        /// </summary>
        public bool PressedThisTick(string actionId) => PressedThisFrame(actionId);

        /// <summary>
        /// Authoritative release edge for logic-tick consumers; see <see cref="PressedThisTick"/>.
        /// </summary>
        public bool ReleasedThisTick(string actionId) => ReleasedThisFrame(actionId);

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
