using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Pointer-aware action state normalized from the authoritative input snapshot.
    /// </summary>
    public readonly struct PointerActionSnapshot
    {
        public PointerActionSnapshot(
            string actionId,
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
            ActionId = actionId ?? string.Empty;
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

        public string ActionId { get; }
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

        public Vector2 ResolvePressPointerOrCurrent()
        {
            return HasPressPointer ? PressPointer : Pointer;
        }

        public Vector2 ResolveDownPointerOrCurrent()
        {
            return HasLastDownPointer ? LastDownPointer : Pointer;
        }

        public Vector2 ResolveReleasePointerOrCurrent()
        {
            return HasReleasePointer ? ReleasePointer : Pointer;
        }

        internal static PointerActionSnapshot FromState(string actionId, in PointerButtonState state)
        {
            return new PointerActionSnapshot(
                actionId,
                state.Pointer,
                state.PressPointer,
                state.ReleasePointer,
                state.LastDownPointer,
                state.IsDown,
                state.PressedThisFrame,
                state.ReleasedThisFrame,
                state.HasPressPointer,
                state.HasReleasePointer,
                state.HasLastDownPointer);
        }

        internal static PointerActionSnapshot CreateInactive(string actionId, Vector2 pointer)
        {
            return new PointerActionSnapshot(
                actionId,
                pointer,
                default,
                default,
                default,
                isDown: false,
                pressedThisFrame: false,
                releasedThisFrame: false,
                hasPressPointer: false,
                hasReleasePointer: false,
                hasLastDownPointer: false);
        }
    }

    /// <summary>
    /// Shared pointer and ground facts from the authoritative input snapshot.
    /// </summary>
    public readonly struct PointerInteractionSnapshot
    {
        public PointerInteractionSnapshot(
            Vector2 pointer,
            bool hasGroundPoint,
            WorldCmInt2 groundWorldCm)
        {
            Pointer = pointer;
            HasGroundPoint = hasGroundPoint;
            GroundWorldCm = groundWorldCm;
        }

        public Vector2 Pointer { get; }
        public bool HasGroundPoint { get; }
        public WorldCmInt2 GroundWorldCm { get; }
    }

    public static class PointerInteractionSnapshotReader
    {
        public static bool TryRead(IReadOnlyDictionary<string, object> globals, out PointerInteractionSnapshot snapshot)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));

            snapshot = default;
            if (!globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return false;
            }

            Vector2 pointer = input.ReadAction<Vector2>(ReservedInputActionIds.PointerPos);
            bool hasGroundPoint = AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 groundWorldCm);
            snapshot = new PointerInteractionSnapshot(pointer, hasGroundPoint, groundWorldCm);
            return true;
        }

        public static bool TryReadAction(
            IReadOnlyDictionary<string, object> globals,
            string actionId,
            out PointerActionSnapshot snapshot)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("Action id is required.", nameof(actionId));
            }

            Vector2 pointer = default;
            if (globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) &&
                inputObj is IInputActionReader input)
            {
                pointer = input.ReadAction<Vector2>(ReservedInputActionIds.PointerPos);
            }

            if (TryReadActionSnapshot(globals, actionId, pointer, out snapshot))
            {
                return true;
            }

            snapshot = PointerActionSnapshot.CreateInactive(actionId, pointer);
            return false;
        }

        private static bool TryReadActionSnapshot(
            IReadOnlyDictionary<string, object> globals,
            string actionId,
            Vector2 pointer,
            out PointerActionSnapshot snapshot)
        {
            if (globals.TryGetValue(CoreServiceKeys.AuthoritativePointerButtons.Name, out var snapshotObj) &&
                snapshotObj is AuthoritativePointerButtonSnapshot buttonSnapshot &&
                buttonSnapshot.TryGetState(actionId, out PointerButtonState state))
            {
                snapshot = PointerActionSnapshot.FromState(actionId, state);
                return true;
            }

            snapshot = default;
            return false;
        }

    }
}
