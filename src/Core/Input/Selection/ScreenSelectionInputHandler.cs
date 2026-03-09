using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    public sealed class ScreenSelectionInputHandler : ISelectionInputHandler
    {
        private static readonly InteractionActionBindings DefaultBindings = new InteractionActionBindings();
        private static readonly RectangleSelectionDragGesture RectangleGesture = new RectangleSelectionDragGesture();
        private static readonly PolygonSelectionDragGesture PolygonGesture = new PolygonSelectionDragGesture();

        private readonly Dictionary<string, object> _globals;
        private readonly PlayerInputHandler _input;
        private readonly SelectionInteractionState _interactionState;
        private readonly SelectionPointerSessionState _pointerSession = new SelectionPointerSessionState();
        private readonly Queue<SelectionInputCommand> _pending = new();

        public ScreenSelectionInputHandler(
            Dictionary<string, object> globals,
            PlayerInputHandler input,
            SelectionInteractionState interactionState)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _interactionState = interactionState ?? throw new ArgumentNullException(nameof(interactionState));
        }

        public void Update(float dt)
        {
            _pending.Clear();
            _pointerSession.AdvanceTime(dt);

            if (IsUiCaptured())
            {
                ResetPointerInteraction();
                return;
            }

            if (!TryResolveProfile(out var profile))
            {
                ResetPointerInteraction();
                return;
            }

            var bindings = ResolveBindings();
            Vector2 pointer = _input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            _interactionState.LastPointerScreen = pointer;

            if (_input.HasAction(bindings.CancelActionId) && _input.PressedThisFrame(bindings.CancelActionId))
            {
                _pending.Enqueue(SelectionInputCommand.CreateClear());
            }

            EnqueueGroupCommands(profile);

            bool primaryDown = _input.ReadAction<bool>(bindings.ConfirmActionId);
            bool pressed = _input.PressedThisFrame(bindings.ConfirmActionId);
            bool released = _input.ReleasedThisFrame(bindings.ConfirmActionId);
            SelectionApplyMode applyMode = ResolveApplyMode(profile);
            var frame = new SelectionInputFrame(pointer, primaryDown, pressed, released, applyMode);

            if (pressed)
            {
                _pointerSession.BeginPrimary(pointer);
                _interactionState.BeginPointer(pointer);
            }

            if (_pointerSession.IsTrackingPrimary && frame.PrimaryDown)
            {
                _interactionState.CurrentScreen = frame.PointerScreen;
                UpdateDragPreview(in frame, profile);
            }

            if (!frame.ReleasedThisFrame)
            {
                return;
            }

            bool emittedDragCommand = TryEmitDragCommand(in frame, profile);
            if (!emittedDragCommand)
            {
                bool expandSameClass = ShouldExpandSameClass(profile, frame.ApplyMode, frame.PointerScreen);
                _pending.Enqueue(SelectionInputCommand.CreatePoint(frame.PointerScreen, profile.PickRadiusPx, frame.ApplyMode, expandSameClass));
            }

            _interactionState.ClearPreview();
            _pointerSession.FinishPrimary(frame.PointerScreen);
        }

        public bool Poll(out SelectionInputCommand command)
        {
            if (_pending.Count > 0)
            {
                command = _pending.Dequeue();
                return true;
            }

            command = default;
            return false;
        }

        private void UpdateDragPreview(in SelectionInputFrame frame, SelectionProfile profile)
        {
            if (profile.DragSelectionShape == SelectionPreviewShapeKind.None)
            {
                return;
            }

            float dragThresholdSq = profile.DragThresholdPx * profile.DragThresholdPx;
            if (Vector2.DistanceSquared(_pointerSession.PressScreen, frame.PointerScreen) < dragThresholdSq)
            {
                return;
            }

            if (TryResolveDragGesture(profile.DragSelectionShape, out var dragGesture))
            {
                dragGesture.UpdatePreview(in frame, profile, _pointerSession, _interactionState);
            }
        }

        private bool TryEmitDragCommand(in SelectionInputFrame frame, SelectionProfile profile)
        {
            if (!_interactionState.IsDragging)
            {
                return false;
            }

            if (!TryResolveDragGesture(profile.DragSelectionShape, out var dragGesture))
            {
                return false;
            }

            if (!dragGesture.TryCreateCommand(in frame, profile, _pointerSession, _interactionState, out var command))
            {
                return false;
            }

            _pending.Enqueue(command);
            return true;
        }

        private void EnqueueGroupCommands(SelectionProfile profile)
        {
            if (profile.GroupHotkeyActionIds == null || profile.GroupHotkeyActionIds.Length == 0)
            {
                return;
            }

            bool assign = profile.GroupAssignModifierActionId.Length > 0
                && _input.HasAction(profile.GroupAssignModifierActionId)
                && _input.IsDown(profile.GroupAssignModifierActionId);

            for (int i = 0; i < profile.GroupHotkeyActionIds.Length && i + 1 < SelectionGroupBuffer.MAX_GROUPS; i++)
            {
                string actionId = profile.GroupHotkeyActionIds[i];
                if (string.IsNullOrWhiteSpace(actionId) || !_input.HasAction(actionId) || !_input.PressedThisFrame(actionId))
                {
                    continue;
                }

                _pending.Enqueue(assign
                    ? SelectionInputCommand.CreateSaveGroup(i + 1)
                    : SelectionInputCommand.CreateRecallGroup(i + 1));
            }
        }

        private SelectionApplyMode ResolveApplyMode(SelectionProfile profile)
        {
            if (profile.EnableToggleSelection
                && !string.IsNullOrWhiteSpace(profile.ToggleModifierActionId)
                && _input.HasAction(profile.ToggleModifierActionId)
                && _input.IsDown(profile.ToggleModifierActionId))
            {
                return SelectionApplyMode.Toggle;
            }

            if (profile.EnableAdditiveSelection
                && !string.IsNullOrWhiteSpace(profile.AddModifierActionId)
                && _input.HasAction(profile.AddModifierActionId)
                && _input.IsDown(profile.AddModifierActionId))
            {
                return SelectionApplyMode.Add;
            }

            return SelectionApplyMode.Replace;
        }

        private bool ShouldExpandSameClass(SelectionProfile profile, SelectionApplyMode applyMode, Vector2 pointer)
        {
            if (profile.PointExpansion != SelectionPointExpansionKind.SameClassDoubleClick || applyMode != SelectionApplyMode.Replace)
            {
                return false;
            }

            float timeSinceLastRelease = _pointerSession.ElapsedTimeSec - _pointerSession.LastPrimaryReleaseTimeSec;
            if (timeSinceLastRelease < 0f || timeSinceLastRelease > profile.DoubleClickWindowSec)
            {
                return false;
            }

            float distanceSq = Vector2.DistanceSquared(pointer, _pointerSession.LastPrimaryReleaseScreen);
            return distanceSq <= profile.DoubleClickMaxDistancePx * profile.DoubleClickMaxDistancePx;
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj)
                && obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        private bool TryResolveProfile(out SelectionProfile profile)
        {
            profile = null!;
            if (!_globals.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var idObj)
                || idObj is not string profileId
                || string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.SelectionProfileRegistry.Name, out var registryObj)
                || registryObj is not SelectionProfileRegistry registry)
            {
                return false;
            }

            profile = registry.Get(profileId);
            return profile != null;
        }

        private bool IsUiCaptured()
        {
            return _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var obj)
                && obj is bool captured
                && captured;
        }

        private void ResetPointerInteraction()
        {
            _pointerSession.CancelPrimary();
            _interactionState.ClearPreview();
        }

        private static bool TryResolveDragGesture(SelectionPreviewShapeKind shape, out ISelectionDragGesture gesture)
        {
            switch (shape)
            {
                case SelectionPreviewShapeKind.Rectangle:
                    gesture = RectangleGesture;
                    return true;
                case SelectionPreviewShapeKind.Polygon:
                    gesture = PolygonGesture;
                    return true;
                default:
                    gesture = default!;
                    return false;
            }
        }
    }
}
