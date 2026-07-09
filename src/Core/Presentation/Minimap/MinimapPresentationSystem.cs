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
    public delegate bool MinimapFocusCollectionProvider(GameEngine engine, out Entity owner, out string collectionKey);
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
        private readonly MinimapFocusCollectionProvider _focusCollectionProvider;
        private bool _prevToggle;
        private bool _prevCenterOnFocusPrimary;
        private bool _prevZoomIn;
        private bool _prevZoomOut;
        private bool _prevPresetToggle;
        private bool _prevRotateToggle;
        private bool _dragging;
        private bool _zoomSliderDragging;

        public MinimapInputConsumer(
            MinimapRuntime runtime,
            MinimapFocusCollectionProvider focusCollectionProvider)
        {
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
            _focusCollectionProvider = focusCollectionProvider ?? throw new System.ArgumentNullException(nameof(focusCollectionProvider));
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
                _runtime.ToggleRtsFollowCameraPreset();
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

            bool centerOnFocusPrimary = input.PressedThisFrame(MinimapInputActions.CenterOnFocusPrimary);
            if (centerOnFocusPrimary &&
                !_prevCenterOnFocusPrimary &&
                TryResolveFocusPrimary(engine, out Entity focusPrimary))
            {
                _runtime.CenterOnEntity(engine, focusPrimary);
            }

            _prevCenterOnFocusPrimary = centerOnFocusPrimary;

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
            bool insideField = _runtime.ContainsField(pointer);
            bool insideSlider = _runtime.ContainsZoomSlider(pointer);
            bool insidePresetToggle = _runtime.ContainsPresetToggle(pointer);
            bool insideRotateToggle = _runtime.ContainsRotateToggle(pointer);
            bool insideInteractive = insideField || insideSlider || insidePresetToggle || insideRotateToggle;
            bool confirmDown = input.IsDown(bindings.ConfirmActionId);
            bool confirmPressed = input.PressedThisFrame(bindings.ConfirmActionId);
            bool confirmReleased = input.ReleasedThisFrame(bindings.ConfirmActionId);
            bool commandPressed = input.PressedThisFrame(bindings.CommandActionId);
            float wheelDelta = input.ReadAction<float>(MinimapInputActions.Zoom);

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

        private bool TryResolveFocusPrimary(GameEngine engine, out Entity focusPrimary)
        {
            focusPrimary = Entity.Null;
            return _focusCollectionProvider(engine, out Entity owner, out string collectionKey) &&
                   engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore collections) &&
                   collections.TryGet(owner, collectionKey, out EntityCollectionHandle handle) &&
                   collections.TryGetEntityAt(handle, 0, out focusPrimary) &&
                   focusPrimary != Entity.Null &&
                   engine.World.IsAlive(focusPrimary);
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
        public const string CenterOnFocusPrimary = "Minimap.CenterOnFocusPrimary";
    }
}
