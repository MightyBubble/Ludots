using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Minimap
{
    public sealed class MinimapPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly MinimapRuntime _runtime;
        private readonly MinimapMarkerBuffer? _markers;
        private readonly MinimapScreenMarkerBuffer? _screenMarkers;

        public MinimapPresentationSystem(GameEngine engine, MinimapRuntime runtime)
            : this(engine, runtime, null, null)
        {
        }

        public MinimapPresentationSystem(
            GameEngine engine,
            MinimapRuntime runtime,
            MinimapMarkerBuffer? markers,
            MinimapScreenMarkerBuffer? screenMarkers)
        {
            _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
            _markers = markers;
            _screenMarkers = screenMarkers;
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

            _runtime.Refresh(_engine, markers, screenMarkers);
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
        private bool _prevToggle;
        private bool _prevCenterOnSelection;
        private bool _prevZoomIn;
        private bool _prevZoomOut;
        private bool _prevPresetToggle;
        private bool _prevRotateToggle;
        private bool _dragging;

        public MinimapInputConsumer(MinimapRuntime runtime)
        {
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
        }

        public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
        {
            if (engine.GetService(CoreServiceKeys.UiCaptured))
            {
                return;
            }

            bool toggle = input.PressedThisFrame(MinimapInputActions.Toggle);
            if (toggle && !_prevToggle)
            {
                _runtime.Visible = !_runtime.Visible;
            }

            _prevToggle = toggle;
            if (!_runtime.Visible)
            {
                return;
            }

            bool presetToggle = input.PressedThisFrame(MinimapInputActions.TogglePreset);
            if (presetToggle && !_prevPresetToggle)
            {
                if (_runtime.Preset == MinimapPreset.RtsFullMap)
                {
                    _runtime.UseFollowCameraPreset();
                }
                else
                {
                    _runtime.UseRtsFullMapPreset();
                }
            }

            _prevPresetToggle = presetToggle;

            bool rotateToggle = input.PressedThisFrame(MinimapInputActions.ToggleRotateWithCamera);
            if (rotateToggle && !_prevRotateToggle)
            {
                _runtime.ToggleRotateWithCamera();
            }

            _prevRotateToggle = rotateToggle;

            bool zoomIn = input.PressedThisFrame(MinimapInputActions.ZoomIn);
            bool zoomOut = input.PressedThisFrame(MinimapInputActions.ZoomOut);
            if (zoomIn && !_prevZoomIn)
            {
                _runtime.CycleZoom(-1);
            }

            if (zoomOut && !_prevZoomOut)
            {
                _runtime.CycleZoom(1);
            }

            _prevZoomIn = zoomIn;
            _prevZoomOut = zoomOut;

            bool centerOnSelection = input.PressedThisFrame(MinimapInputActions.CenterOnSelection);
            if (centerOnSelection && !_prevCenterOnSelection)
            {
                _runtime.CenterOnSelected(engine);
            }

            _prevCenterOnSelection = centerOnSelection;

            Vector2 pan = input.ReadAction<Vector2>(MinimapInputActions.Pan);
            if (pan.X != 0f || pan.Y != 0f)
            {
                _runtime.PanNormalized(pan.X * deltaTime * 0.9f, pan.Y * deltaTime * 0.9f);
            }

            HandlePointerClick(engine, input);
        }

        private void HandlePointerClick(GameEngine engine, PlayerInputHandler input)
        {
            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(engine.GlobalContext, nameof(MinimapInputConsumer));
            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            bool inside = _runtime.ContainsField(pointer);
            bool confirmDown = input.IsDown(bindings.ConfirmActionId);
            bool confirmPressed = input.PressedThisFrame(bindings.ConfirmActionId);
            bool confirmReleased = input.ReleasedThisFrame(bindings.ConfirmActionId);
            float wheelDelta = input.ReadAction<float>(MinimapInputActions.Zoom);

            if (inside)
            {
                engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
                if (wheelDelta != 0f)
                {
                    _runtime.ApplyWheelZoom(wheelDelta, pointer);
                    SuppressCameraZoom(input);
                }
            }

            if (confirmReleased)
            {
                _dragging = false;
            }

            if (inside && confirmPressed)
            {
                _dragging = true;
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
            input.SuppressActionThisFrame(bindings.ConfirmActionId);
            if (engine.GetService(CoreServiceKeys.AuthoritativePointerButtons) is AuthoritativePointerButtonSnapshot pointerButtons)
            {
                pointerButtons.SuppressAction(bindings.ConfirmActionId);
            }
        }

        private static void SuppressCameraZoom(PlayerInputHandler input)
        {
            input.SuppressActionThisFrame("Zoom");
        }
    }

    public static class MinimapInputActions
    {
        public const string Toggle = "Minimap.Toggle";
        public const string TogglePreset = "Minimap.TogglePreset";
        public const string ToggleRotateWithCamera = "Minimap.ToggleRotateWithCamera";
        public const string Zoom = "Minimap.Zoom";
        public const string ZoomIn = "Minimap.ZoomIn";
        public const string ZoomOut = "Minimap.ZoomOut";
        public const string Pan = "Minimap.Pan";
        public const string CenterOnSelection = "Minimap.CenterOnSelection";
    }
}
