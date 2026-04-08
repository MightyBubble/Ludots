using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

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

        internal static PointerActionSnapshot CreateFallback(string actionId, Vector2 pointer, IInputActionReader input)
        {
            return new PointerActionSnapshot(
                actionId,
                pointer,
                default,
                default,
                default,
                input.IsDown(actionId),
                input.PressedThisFrame(actionId),
                input.ReleasedThisFrame(actionId),
                hasPressPointer: false,
                hasReleasePointer: false,
                hasLastDownPointer: false);
        }
    }

    /// <summary>
    /// Shared pointer interaction facts for confirm/command/cancel consumers.
    /// Pointer is aligned to the confirm action when an authoritative pointer-button
    /// snapshot is available so fixed-step consumers resolve the same screen point.
    /// </summary>
    public readonly struct PointerInteractionSnapshot
    {
        public PointerInteractionSnapshot(
            Vector2 pointer,
            PointerActionSnapshot confirm,
            PointerActionSnapshot command,
            PointerActionSnapshot cancel,
            bool hasGroundPoint,
            WorldCmInt2 groundWorldCm)
        {
            Pointer = pointer;
            Confirm = confirm;
            Command = command;
            Cancel = cancel;
            HasGroundPoint = hasGroundPoint;
            GroundWorldCm = groundWorldCm;
        }

        public Vector2 Pointer { get; }
        public PointerActionSnapshot Confirm { get; }
        public PointerActionSnapshot Command { get; }
        public PointerActionSnapshot Cancel { get; }
        public bool HasGroundPoint { get; }
        public WorldCmInt2 GroundWorldCm { get; }
    }

    public static class PointerInteractionSnapshotReader
    {
        private static readonly InteractionActionBindings DefaultBindings = new();

        public static bool TryRead(IReadOnlyDictionary<string, object> globals, out PointerInteractionSnapshot snapshot)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));

            snapshot = default;
            if (!globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return false;
            }

            InteractionActionBindings bindings = ResolveBindings(globals);
            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);

            PointerActionSnapshot confirm = ReadActionSnapshot(globals, input, bindings.ConfirmActionId, pointer);
            PointerActionSnapshot command = ReadActionSnapshot(globals, input, bindings.CommandActionId, pointer);
            PointerActionSnapshot cancel = ReadActionSnapshot(globals, input, bindings.CancelActionId, pointer);

            Vector2 interactionPointer = confirm.Pointer;
            bool hasGroundPoint = TryResolveGroundPoint(globals, input, interactionPointer, out WorldCmInt2 groundWorldCm);

            snapshot = new PointerInteractionSnapshot(
                interactionPointer,
                confirm,
                command,
                cancel,
                hasGroundPoint,
                groundWorldCm);
            return true;
        }

        private static PointerActionSnapshot ReadActionSnapshot(
            IReadOnlyDictionary<string, object> globals,
            IInputActionReader input,
            string actionId,
            Vector2 pointer)
        {
            if (globals.TryGetValue(CoreServiceKeys.AuthoritativePointerButtons.Name, out var snapshotObj) &&
                snapshotObj is AuthoritativePointerButtonSnapshot buttonSnapshot &&
                buttonSnapshot.TryGetState(actionId, out PointerButtonState state))
            {
                return PointerActionSnapshot.FromState(actionId, state);
            }

            return PointerActionSnapshot.CreateFallback(actionId, pointer, input);
        }

        private static bool TryResolveGroundPoint(
            IReadOnlyDictionary<string, object> globals,
            IInputActionReader input,
            Vector2 pointer,
            out WorldCmInt2 worldCm)
        {
            if (AuthoritativeGroundPointerHelper.TryRead(input, out worldCm))
            {
                return true;
            }

            return AuthoritativeGroundPointerHelper.TryResolveFromScreen(globals, pointer, out worldCm);
        }

        private static InteractionActionBindings ResolveBindings(IReadOnlyDictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) &&
                obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }
    }
}
