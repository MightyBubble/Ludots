using Arch.System;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using MinimapControlMod.Runtime;

namespace MinimapControlMod.Systems;

internal sealed class MinimapControlPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MinimapControlRuntime _runtime;
    private float _lastWheel;
    private bool _prevToggle;
    private bool _prevCenterOnSelection;
    private bool _prevZoomIn;
    private bool _prevZoomOut;
    private bool _prevLeftButton;

    public MinimapControlPresentationSystem(GameEngine engine, MinimapControlRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
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

        HandlePresentationInput(t);
        if (_engine.GetService(CoreServiceKeys.ViewController) is IViewController view)
        {
            SyncCameraView(view);
            _runtime.Refresh(_engine);
            _runtime.Render(overlay, (int)view.Resolution.X, (int)view.Resolution.Y);
        }
        else
        {
            _runtime.Refresh(_engine);
            _runtime.Render(overlay);
        }
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }

    private void HandlePresentationInput(float deltaTime)
    {
        if (_engine.GetService(CoreServiceKeys.InputBackend) is not IInputBackend input)
        {
            return;
        }

        bool toggle = input.GetButton("<Keyboard>/m");
        if (toggle && !_prevToggle)
        {
            _runtime.Visible = !_runtime.Visible;
        }

        _prevToggle = toggle;
        if (!_runtime.Visible)
        {
            _lastWheel = input.GetMouseWheel();
            _prevLeftButton = input.GetButton("<Mouse>/LeftButton");
            return;
        }

        bool leftButton = input.GetButton("<Mouse>/LeftButton");
        if (leftButton && !_prevLeftButton)
        {
            HandleMinimapClick(input.GetMousePosition());
        }

        _prevLeftButton = leftButton;

        bool zoomIn = input.GetButton("<Keyboard>/pageUp") || input.GetButton("<Keyboard>/equals");
        bool zoomOut = input.GetButton("<Keyboard>/pageDown") || input.GetButton("<Keyboard>/minus");
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

        bool centerOnSelection = input.GetButton("<Keyboard>/c");
        if (centerOnSelection && !_prevCenterOnSelection)
        {
            _runtime.CenterOnSelected(_engine);
        }

        _prevCenterOnSelection = centerOnSelection;

        float wheel = input.GetMouseWheel();
        float wheelDelta = wheel - _lastWheel;
        _lastWheel = wheel;
        if (wheelDelta != 0f)
        {
            _runtime.ApplyWheelZoom(wheelDelta);
        }

        float panX = 0f;
        float panY = 0f;
        if (input.GetButton("<Keyboard>/leftArrow")) panX -= 1f;
        if (input.GetButton("<Keyboard>/rightArrow")) panX += 1f;
        if (input.GetButton("<Keyboard>/upArrow")) panY -= 1f;
        if (input.GetButton("<Keyboard>/downArrow")) panY += 1f;
        if (panX != 0f || panY != 0f)
        {
            _runtime.PanNormalized(panX * deltaTime * 0.9f, panY * deltaTime * 0.9f);
        }
    }

    private void HandleMinimapClick(Vector2 pointer)
    {
        if (_engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
        {
            return;
        }

        if (!_runtime.TryResolveWorldPointFromScreen(
            (int)view.Resolution.X,
            (int)view.Resolution.Y,
            pointer,
            out Vector2 worldCm))
        {
            return;
        }

        _engine.GlobalContext[MinimapControlServiceKeys.WorldClickRequest.Name] =
            new MinimapWorldClickRequest(string.Empty, worldCm.X, worldCm.Y);
    }

    private void SyncCameraView(IViewController view)
    {
        var camera = _engine.GameSession.Camera.State;
        var extent = CameraViewportUtil.ComputeViewportExtent(
            camera.DistanceCm,
            camera.FovYDeg,
            camera.Pitch,
            view.AspectRatio);
        _runtime.SetCameraView(
            camera.TargetCm.X,
            camera.TargetCm.Y,
            extent.widthCm,
            extent.heightCm);
    }
}
