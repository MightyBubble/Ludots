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
        private static readonly InteractionActionBindings DefaultBindings = new();
        private static readonly SelectionProfile DefaultProfile = new() { Id = "Default" };

        private readonly Dictionary<string, object> _globals;
        private readonly IInputActionReader _input;
        private readonly SelectionInteractionState _interaction;
        private readonly Queue<SelectionInputCommand> _commands = new();
        private float _elapsedSec;
        private bool _trackingSelect;
        private Vector2 _lastClickScreen;
        private float _lastClickTimeSec = float.NegativeInfinity;

        public ScreenSelectionInputHandler(Dictionary<string, object> globals, IInputActionReader input, SelectionInteractionState interaction)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        }

        public void Update(float dt)
        {
            _elapsedSec += dt;

            if (_globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var uiCapturedObj) &&
                uiCapturedObj is bool uiCaptured &&
                uiCaptured)
            {
                CancelTracking();
                return;
            }

            var bindings = ResolveBindings();
            var profile = ResolveProfile();
            if (IsSuppressed(profile))
            {
                CancelTracking();
                return;
            }

            var pointer = _input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            ProcessGroupCommands(profile);

            if (_input.PressedThisFrame(bindings.ConfirmActionId))
            {
                _trackingSelect = true;
                _interaction.Begin(pointer, profile.DragSelectionShape);
            }

            if (_trackingSelect)
            {
                _interaction.UpdatePointer(pointer);

                if (!_interaction.IsDragging)
                {
                    float dragThreshold = Math.Max(0f, profile.DragThresholdPx);
                    if (Vector2.DistanceSquared(_interaction.AnchorScreen, pointer) >= dragThreshold * dragThreshold)
                    {
                        _interaction.StartDrag(pointer, profile.DragSelectionShape);
                    }
                }
                else if (_interaction.PreviewShape == SelectionPreviewShapeKind.Polygon)
                {
                    float spacing = Math.Max(1f, profile.PolygonPointSpacingPx);
                    var polygon = _interaction.PolygonScreen;
                    var lastPoint = polygon.Length > 0 ? polygon[polygon.Length - 1] : _interaction.AnchorScreen;
                    if (Vector2.DistanceSquared(lastPoint, pointer) >= spacing * spacing)
                    {
                        _interaction.AppendPolygonPoint(pointer);
                    }
                }
            }

            if (_trackingSelect && _input.ReleasedThisFrame(bindings.ConfirmActionId))
            {
                EmitSelectionCommand(profile, pointer);
                _trackingSelect = false;
                _interaction.Reset();
            }
        }

        public bool Poll(out SelectionInputCommand command)
        {
            if (_commands.Count > 0)
            {
                command = _commands.Dequeue();
                return true;
            }

            command = default;
            return false;
        }

        private void EmitSelectionCommand(SelectionProfile profile, Vector2 pointer)
        {
            var applyMode = ResolveApplyMode(profile);
            if (_interaction.IsDragging)
            {
                if (_interaction.PreviewShape == SelectionPreviewShapeKind.Polygon)
                {
                    var polygon = _interaction.PolygonScreen;
                    if (polygon.Length < 3)
                    {
                        polygon = new[] { _interaction.AnchorScreen, _interaction.CurrentScreen, pointer };
                    }

                    _commands.Enqueue(SelectionInputCommand.CreatePolygon(polygon, applyMode));
                    return;
                }

                var min = Vector2.Min(_interaction.AnchorScreen, _interaction.CurrentScreen);
                var max = Vector2.Max(_interaction.AnchorScreen, _interaction.CurrentScreen);
                _commands.Enqueue(SelectionInputCommand.CreateRectangle(min, max, applyMode));
                return;
            }

            bool expandSameClass = false;
            if (profile.PointExpansion == SelectionPointExpansionKind.SameClassDoubleClick)
            {
                float timeDelta = _elapsedSec - _lastClickTimeSec;
                float maxDistance = Math.Max(0f, profile.DoubleClickMaxDistancePx);
                if (timeDelta <= profile.DoubleClickWindowSec &&
                    Vector2.DistanceSquared(_lastClickScreen, pointer) <= maxDistance * maxDistance)
                {
                    expandSameClass = true;
                }
            }

            _commands.Enqueue(SelectionInputCommand.CreatePoint(pointer, Math.Max(1f, profile.PickRadiusPx), applyMode, expandSameClass));
            _lastClickScreen = pointer;
            _lastClickTimeSec = _elapsedSec;
        }

        private void ProcessGroupCommands(SelectionProfile profile)
        {
            if (profile.GroupHotkeyActionIds == null || profile.GroupHotkeyActionIds.Length == 0)
            {
                return;
            }

            bool assignGroup = !string.IsNullOrWhiteSpace(profile.GroupAssignModifierActionId) &&
                               _input.IsDown(profile.GroupAssignModifierActionId);
            for (int i = 0; i < profile.GroupHotkeyActionIds.Length; i++)
            {
                var actionId = profile.GroupHotkeyActionIds[i];
                if (string.IsNullOrWhiteSpace(actionId) || !_input.PressedThisFrame(actionId))
                {
                    continue;
                }

                _commands.Enqueue(assignGroup
                    ? SelectionInputCommand.CreateSaveGroup(i + 1)
                    : SelectionInputCommand.CreateRecallGroup(i + 1));
            }
        }

        private SelectionApplyMode ResolveApplyMode(SelectionProfile profile)
        {
            if (profile.EnableToggleSelection &&
                !string.IsNullOrWhiteSpace(profile.ToggleModifierActionId) &&
                _input.IsDown(profile.ToggleModifierActionId))
            {
                return SelectionApplyMode.Toggle;
            }

            if (profile.EnableAdditiveSelection &&
                !string.IsNullOrWhiteSpace(profile.AddModifierActionId) &&
                _input.IsDown(profile.AddModifierActionId))
            {
                return SelectionApplyMode.Add;
            }

            return SelectionApplyMode.Replace;
        }

        private bool IsSuppressed(SelectionProfile profile)
        {
            var actions = profile.SuppressWhenActionIdsDown;
            if (actions == null || actions.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                var actionId = actions[i];
                if (!string.IsNullOrWhiteSpace(actionId) && _input.IsDown(actionId))
                {
                    return true;
                }
            }

            return false;
        }

        private void CancelTracking()
        {
            if (_trackingSelect)
            {
                _trackingSelect = false;
                _interaction.Reset();
            }
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) &&
                obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        private SelectionProfile ResolveProfile()
        {
            if (_globals.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var idObj) &&
                idObj is string profileId &&
                !string.IsNullOrWhiteSpace(profileId) &&
                _globals.TryGetValue(CoreServiceKeys.SelectionProfileRegistry.Name, out var registryObj) &&
                registryObj is SelectionProfileRegistry registry)
            {
                var profile = registry.Get(profileId);
                if (profile != null)
                {
                    return profile;
                }
            }

            return DefaultProfile;
        }
    }
}
