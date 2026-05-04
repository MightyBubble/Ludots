using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Captures per-button pointer lifecycle within one authoritative logic tick so
    /// fixed-step consumers can recover press/release screen positions even when
    /// multiple visual frames collapse into a single logic snapshot.
    /// </summary>
    public sealed class AuthoritativePointerButtonAccumulator
    {
        private readonly Dictionary<string, PendingPointerButtonState> _states = new(StringComparer.Ordinal);

        public void Capture(string actionId, Vector2 pointer, bool isDown, bool pressedThisFrame, bool releasedThisFrame)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            if (!_states.TryGetValue(actionId, out var state))
            {
                state = new PendingPointerButtonState();
            }

            state.Pointer = pointer;
            state.IsDown = isDown;

            if (isDown)
            {
                state.LastDownPointer = pointer;
                state.HasLastDownPointer = true;
            }

            if (pressedThisFrame)
            {
                state.PressedThisTick = true;
                if (!state.HasPressPointer)
                {
                    state.PressPointer = pointer;
                    state.HasPressPointer = true;
                }
            }

            if (releasedThisFrame)
            {
                state.ReleasedThisTick = true;
                state.ReleasePointer = pointer;
                state.HasReleasePointer = true;
            }

            _states[actionId] = state;
        }

        public void BuildTickSnapshot(AuthoritativePointerButtonSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            snapshot.Clear();
            foreach (var pair in _states)
            {
                PendingPointerButtonState state = pair.Value;
                snapshot.SetState(
                    pair.Key,
                    new PointerButtonState(
                        state.Pointer,
                        state.PressPointer,
                        state.ReleasePointer,
                        state.LastDownPointer,
                        isDown: state.IsDown,
                        pressedThisFrame: state.PressedThisTick,
                        releasedThisFrame: state.ReleasedThisTick,
                        hasPressPointer: state.HasPressPointer,
                        hasReleasePointer: state.HasReleasePointer,
                        hasLastDownPointer: state.HasLastDownPointer));

                state.PressedThisTick = false;
                state.ReleasedThisTick = false;
                state.HasPressPointer = false;
                state.HasReleasePointer = false;
                if (!state.IsDown)
                {
                    state.HasLastDownPointer = false;
                }

                _states[pair.Key] = state;
            }
        }

        public void SuppressActionThisTick(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !_states.TryGetValue(actionId, out var state))
            {
                return;
            }

            state.IsDown = false;
            state.PressedThisTick = false;
            state.ReleasedThisTick = false;
            state.HasPressPointer = false;
            state.HasReleasePointer = false;
            state.HasLastDownPointer = false;
            _states[actionId] = state;
        }

        private struct PendingPointerButtonState
        {
            public Vector2 Pointer;
            public Vector2 PressPointer;
            public Vector2 ReleasePointer;
            public Vector2 LastDownPointer;
            public bool IsDown;
            public bool PressedThisTick;
            public bool ReleasedThisTick;
            public bool HasPressPointer;
            public bool HasReleasePointer;
            public bool HasLastDownPointer;
        }
    }

    public sealed class AuthoritativePointerButtonSnapshot
    {
        private readonly Dictionary<string, PointerButtonState> _states = new(StringComparer.Ordinal);

        public void Clear()
        {
            _states.Clear();
        }

        public void SetState(string actionId, in PointerButtonState state)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _states[actionId] = state;
        }

        public bool TryGetState(string actionId, out PointerButtonState state)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                state = default;
                return false;
            }

            return _states.TryGetValue(actionId, out state);
        }

        public void SuppressAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !_states.TryGetValue(actionId, out PointerButtonState state))
            {
                return;
            }

            _states[actionId] = state.Suppress();
        }
    }

    public readonly struct PointerButtonState
    {
        public PointerButtonState(
            Vector2 pointer,
            Vector2 pressPointer,
            Vector2 releasePointer,
            Vector2 lastDownPointer,
            bool isDown,
            bool pressedThisFrame,
            bool releasedThisFrame,
            bool hasPressPointer,
            bool hasReleasePointer,
            bool hasLastDownPointer)
        {
            Pointer = pointer;
            PressPointer = pressPointer;
            ReleasePointer = releasePointer;
            LastDownPointer = lastDownPointer;
            IsDown = isDown;
            PressedThisFrame = pressedThisFrame;
            ReleasedThisFrame = releasedThisFrame;
            HasPressPointer = hasPressPointer;
            HasReleasePointer = hasReleasePointer;
            HasLastDownPointer = hasLastDownPointer;
        }

        public Vector2 Pointer { get; }
        public Vector2 PressPointer { get; }
        public Vector2 ReleasePointer { get; }
        public Vector2 LastDownPointer { get; }
        public bool IsDown { get; }
        public bool PressedThisFrame { get; }
        public bool ReleasedThisFrame { get; }
        public bool HasPressPointer { get; }
        public bool HasReleasePointer { get; }
        public bool HasLastDownPointer { get; }

        public PointerButtonState Suppress()
        {
            return new PointerButtonState(
                Pointer,
                PressPointer,
                ReleasePointer,
                LastDownPointer,
                isDown: false,
                pressedThisFrame: false,
                releasedThisFrame: false,
                hasPressPointer: false,
                hasReleasePointer: false,
                hasLastDownPointer: false);
        }
    }
}
