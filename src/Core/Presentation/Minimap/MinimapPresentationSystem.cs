using System.Numerics;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Attributes;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Minimap
{
    public delegate bool MinimapKnowledgeViewerProvider(GameEngine engine, out Entity viewer);

    public sealed class MinimapPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly MinimapRuntime _runtime;
        private readonly MinimapMarkerBuffer? _markers;
        private readonly MinimapScreenMarkerBuffer? _screenMarkers;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        public MinimapPresentationSystem(GameEngine engine, MinimapRuntime runtime)
            : this(engine, runtime, null, null)
        {
        }

        public MinimapPresentationSystem(
            GameEngine engine,
            MinimapRuntime runtime,
            MinimapMarkerBuffer? markers,
            MinimapScreenMarkerBuffer? screenMarkers,
            PresentationTimingDiagnostics? timingDiagnostics = null)
        {
            _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
            _markers = markers;
            _screenMarkers = screenMarkers;
            _timingDiagnostics = timingDiagnostics;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            MinimapMarkerBuffer? markers = _markers ?? _engine.GetService(CoreServiceKeys.MinimapMarkerBuffer);
            MinimapScreenMarkerBuffer? screenMarkers = _screenMarkers ?? _engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
            if (markers == null || screenMarkers == null)
            {
                return;
            }

            long refreshStart = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _runtime.Refresh(_engine, markers, screenMarkers);
            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObserveMinimapProjection(
                    (Stopwatch.GetTimestamp() - refreshStart) * 1000d / Stopwatch.Frequency,
                    screenMarkers.Count,
                    screenMarkers.DroppedSinceClear);
            }

            _runtime.Render(overlay);
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }

    public sealed class MinimapInputConsumer : IInputFrameConsumer
    {
        private readonly MinimapRuntime _runtime;
        private readonly MinimapActionsConfig _actions;
        private bool _dragging;
        private bool _zoomSliderDragging;

        public MinimapInputConsumer(MinimapRuntime runtime, MinimapActionsConfig actions)
        {
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
            _actions = actions ?? throw new System.ArgumentNullException(nameof(actions));
            _actions.Validate();
        }

        public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
        {
            if (engine.GetService(CoreServiceKeys.UiCaptured) || !_runtime.Visible)
            {
                return;
            }

            HandlePointerClick(engine, input);
        }

        private void HandlePointerClick(GameEngine engine, PlayerInputHandler input)
        {
            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(engine.GlobalContext, nameof(MinimapInputConsumer));
            Vector2 pointer = input.ReadAction<Vector2>(ReservedInputActionIds.PointerPos);
            bool insideField = _runtime.ContainsField(pointer);
            bool insideSlider = _runtime.ContainsZoomSlider(pointer);
            bool insidePresetToggle = _runtime.ContainsPresetToggle(pointer);
            bool insideRotateToggle = _runtime.ContainsRotateToggle(pointer);
            bool insideInteractive = insideField || insideSlider || insidePresetToggle || insideRotateToggle;
            bool confirmDown = input.IsDown(bindings.ConfirmActionId);
            bool confirmPressed = input.PressedThisFrame(bindings.ConfirmActionId);
            bool confirmReleased = input.ReleasedThisFrame(bindings.ConfirmActionId);
            bool commandPressed = input.PressedThisFrame(bindings.CommandActionId);
            float wheelDelta = input.ReadAction<float>(_actions.Zoom);

            if (insideInteractive)
            {
                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                if (wheelDelta != 0f)
                {
                    _runtime.ApplyWheelZoom(wheelDelta, pointer);
                    SuppressCameraZoom(engine, input);
                }
            }

            if (commandPressed && insideField)
            {
                if (_runtime.TryScreenToWorldClamped(pointer, out Vector2 commandWorldCm) &&
                    engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride) is AuthoritativeGroundPointerOverride groundOverride)
                {
                    groundOverride.Set(bindings.CommandActionId, commandWorldCm);
                }

                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                return;
            }

            if (confirmReleased)
            {
                _dragging = false;
                _zoomSliderDragging = false;
            }

            if (confirmPressed && insidePresetToggle)
            {
                _runtime.ToggleRtsFollowCameraPreset();
                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                SuppressConfirm(engine, input, bindings.ConfirmActionId);
                return;
            }

            if (confirmPressed && insideRotateToggle)
            {
                _runtime.ToggleRotateWithCamera();
                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                SuppressConfirm(engine, input, bindings.ConfirmActionId);
                return;
            }

            if (insideSlider && confirmPressed)
            {
                _zoomSliderDragging = true;
                _runtime.SetZoomFromSliderPointer(pointer);
            }
            else if (insideField && confirmPressed)
            {
                _dragging = true;
            }

            if (_zoomSliderDragging)
            {
                if (!confirmDown && !confirmPressed)
                {
                    _zoomSliderDragging = false;
                    return;
                }

                _runtime.SetZoomFromSliderPointer(pointer);
                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                SuppressConfirm(engine, input, bindings.ConfirmActionId);
                return;
            }

            if (!_dragging)
            {
                return;
            }

            if (!confirmDown && !confirmPressed)
            {
                _dragging = false;
                return;
            }

            if (!_runtime.TryScreenToWorldClamped(pointer, out Vector2 worldCm))
            {
                return;
            }

            _runtime.JumpCameraTo(engine, worldCm);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
            SuppressConfirm(engine, input, bindings.ConfirmActionId);
        }

        private static void SuppressConfirm(PlayerInputHandler input, string actionId)
        {
            input.SuppressActionThisFrame(actionId);
        }

        private static void SuppressConfirm(GameEngine engine, PlayerInputHandler input, string actionId)
        {
            SuppressConfirm(input, actionId);
            if (engine.GetService(CoreServiceKeys.AuthoritativePointerButtons) is AuthoritativePointerButtonSnapshot pointerButtons)
            {
                pointerButtons.SuppressAction(actionId);
            }
        }

        private static void SuppressCameraZoom(GameEngine engine, PlayerInputHandler input)
        {
            if (engine.GetService(CoreServiceKeys.InputActionAttributeBindingRegistry) is not InputActionAttributeBindingRegistry registry)
            {
                return;
            }

            InputActionAttributeBindingEntry[] entries = registry.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].SuppressOnUiWheelCaptured)
                {
                    input.SuppressActionThisFrame(entries[i].ActionId);
                }
            }
        }
    }
}
