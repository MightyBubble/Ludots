using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CapabilityStandardVirtualCameraShowcaseMod.Runtime;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

internal sealed class CapabilityStandardVirtualCameraDebugPanelSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardVirtualCameraShowcaseConfig _config;
    private readonly CameraDebugPanelConfig _panel;
    private PlayerInputHandler? _input;
    private bool _visible;
    private bool _inputValidated;
    private int _selectedAdjustmentIndex;
    private string _lastAdjustment = "ready";

    public CapabilityStandardVirtualCameraDebugPanelSystem(
        GameEngine engine,
        CapabilityStandardVirtualCameraShowcaseConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _panel = _config.DebugPanel;
        _visible = _panel.InitialVisible;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_panel.Enabled || !CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureInput();
        HandleControls();

        if (_visible)
        {
            RenderPanel();
        }
    }

    private void EnsureInput()
    {
        if (_input == null)
        {
            _input = _engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("Capability standard virtual camera debug panel requires InputHandler.");
        }

        if (_inputValidated)
        {
            return;
        }

        RequireAction(_panel.ToggleActionId);
        RequireAction(_panel.NextActionId);
        RequireAction(_panel.PreviousActionId);
        RequireAction(_panel.IncreaseActionId);
        RequireAction(_panel.DecreaseActionId);
        _inputValidated = true;
    }

    private void RequireAction(string actionId)
    {
        if (!_input!.HasAction(actionId))
        {
            throw new InvalidOperationException(
                $"Capability standard virtual camera debug panel requires input action '{actionId}'.");
        }
    }

    private void HandleControls()
    {
        if (_input!.PressedThisFrame(_panel.ToggleActionId))
        {
            _visible = !_visible;
        }

        if (!_visible || _panel.Adjustments.Length == 0)
        {
            return;
        }

        if (_input.PressedThisFrame(_panel.NextActionId))
        {
            _selectedAdjustmentIndex = (_selectedAdjustmentIndex + 1) % _panel.Adjustments.Length;
        }

        if (_input.PressedThisFrame(_panel.PreviousActionId))
        {
            _selectedAdjustmentIndex = _selectedAdjustmentIndex <= 0
                ? _panel.Adjustments.Length - 1
                : _selectedAdjustmentIndex - 1;
        }

        if (_input.PressedThisFrame(_panel.IncreaseActionId))
        {
            ApplyAdjustment(+1f);
        }

        if (_input.PressedThisFrame(_panel.DecreaseActionId))
        {
            ApplyAdjustment(-1f);
        }
    }

    private void ApplyAdjustment(float direction)
    {
        VirtualCameraBrain brain = _engine.GameSession.Camera.VirtualCameraBrain
            ?? throw new InvalidOperationException("Capability standard virtual camera debug panel requires VirtualCameraBrain.");
        if (!brain.HasActiveCamera)
        {
            throw new InvalidOperationException("Capability standard virtual camera debug panel requires an active virtual camera.");
        }

        CameraDebugPanelAdjustmentConfig adjustment = _panel.Adjustments[_selectedAdjustmentIndex];
        CameraState state = _engine.GameSession.Camera.State;
        float current = ReadField(state, adjustment.ResolvedField);
        float next = Math.Clamp(current + (adjustment.Step * direction), adjustment.Min, adjustment.Max);

        var request = new CameraPoseRequest { VirtualCameraId = brain.ActiveCameraId };
        WriteField(request, adjustment.ResolvedField, next);
        _engine.GameSession.Camera.ApplyPose(request);
        _lastAdjustment = $"{adjustment.DisplayName} {current:0.##} -> {next:0.##}";
    }

    private void RenderPanel()
    {
        ScreenOverlayBuffer overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("Capability standard virtual camera debug panel requires ScreenOverlayBuffer.");

        VirtualCameraBrain brain = _engine.GameSession.Camera.VirtualCameraBrain
            ?? throw new InvalidOperationException("Capability standard virtual camera debug panel requires VirtualCameraBrain.");

        CameraState state = _engine.GameSession.Camera.State;
        VirtualCameraDefinition? definition = brain.ActiveDefinition;
        string activeMode = _engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out object? modeObj) &&
                            modeObj is string modeId
            ? modeId
            : "(none)";

        MouseCaptureRequest capture = _engine.GetService(CoreServiceKeys.MouseCaptureRequest) ?? MouseCaptureRequest.None;
        bool uiCaptured = _engine.TryGetService(CoreServiceKeys.UiCaptured, out bool captured) && captured;
        int impulseCount = _engine.TryGetService(CoreServiceKeys.CameraImpulseRuntime, out CameraImpulseRuntime impulseRuntime)
            ? impulseRuntime.ActiveCount
            : 0;
        string commandSource = TryResolveCommandSource(out Entity commandSourceEntity)
            ? DescribeEntity(commandSourceEntity)
            : "(missing)";

        int lineCount = 13 + _panel.Adjustments.Length;
        int height = 18 + (lineCount * _panel.LineHeight);
        var bg = new Vector4(0.02f, 0.025f, 0.03f, 0.84f);
        var border = new Vector4(0.18f, 0.7f, 0.78f, 0.85f);
        var title = new Vector4(0.82f, 0.98f, 1f, 1f);
        var text = new Vector4(0.86f, 0.9f, 0.92f, 1f);
        var muted = new Vector4(0.64f, 0.72f, 0.76f, 1f);
        var selected = new Vector4(1f, 0.9f, 0.54f, 1f);

        int x = _panel.X;
        int y = _panel.Y;
        overlay.AddRect(x, y, _panel.Width, height, bg, border);
        int lineY = y + 8;
        AddLine(overlay, x + 10, ref lineY, "Virtual Camera Playground", _panel.FontSize + 2, _panel.LineHeight, title);
        AddLine(overlay, x + 10, ref lineY, $"Mode: {Shorten(activeMode)}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Camera: {Shorten(brain.ActiveCameraId)}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Rig: {definition?.RigKind.ToString() ?? "(none)"}  Follow: {definition?.FollowTargetKind.ToString() ?? "(none)"}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Mouse: {(capture.Capture ? "locked" : "free")}  Hidden: {capture.HideCursor}  UI: {uiCaptured}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Command source: {commandSource}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Yaw {state.Yaw:0.##}  Pitch {state.Pitch:0.##}  Dist {state.DistanceCm:0.##}  FOV {state.FovYDeg:0.##}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"TPS pivot {state.RigPivotOffsetCm.X:0}/{state.RigPivotOffsetCm.Y:0}/{state.RigPivotOffsetCm.Z:0}  socket {state.RigCameraOffsetCm.X:0}/{state.RigCameraOffsetCm.Y:0}/{state.RigCameraOffsetCm.Z:0}", _panel.FontSize, _panel.LineHeight, muted);
        AddLine(overlay, x + 10, ref lineY, $"Collision {state.CameraCollisionCorrectionCm:0.##}cm  Impulse active {impulseCount}", _panel.FontSize, _panel.LineHeight, text);
        AddLine(overlay, x + 10, ref lineY, $"Last: {_lastAdjustment}", _panel.FontSize, _panel.LineHeight, muted);
        AddLine(overlay, x + 10, ref lineY, "F5 Orbit  F6 Heightmap  F7 TPS  F8 FPS", _panel.FontSize, _panel.LineHeight, muted);
        AddLine(overlay, x + 10, ref lineY, "F9 Panel  PageUp/PageDown Select  -/= Adjust  R Shake", _panel.FontSize, _panel.LineHeight, muted);

        for (int i = 0; i < _panel.Adjustments.Length; i++)
        {
            CameraDebugPanelAdjustmentConfig adjustment = _panel.Adjustments[i];
            float value = ReadField(state, adjustment.ResolvedField);
            string prefix = i == _selectedAdjustmentIndex ? ">" : " ";
            Vector4 color = i == _selectedAdjustmentIndex ? selected : text;
            AddLine(
                overlay,
                x + 10,
                ref lineY,
                $"{prefix} {adjustment.DisplayName}: {value:0.##}  step {adjustment.Step:0.##}",
                _panel.FontSize,
                _panel.LineHeight,
                color);
        }
    }

    private bool TryResolveCommandSource(out Entity entity)
    {
        entity = Entity.Null;
        return _engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity owner) &&
               _engine.World.IsAlive(owner) &&
               _engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore store) &&
               EntityCollectionContextRuntime.TryGetPrimary(
                   _engine.World,
                   store,
                   owner,
                   EntityCollectionKeys.CommandSource,
                   out entity) &&
               _engine.World.IsAlive(entity);
    }

    private string DescribeEntity(Entity entity)
    {
        if (_engine.World.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value))
        {
            return $"{name.Value}#{entity.Id}";
        }

        return $"Entity#{entity.Id}";
    }

    private static void AddLine(
        ScreenOverlayBuffer overlay,
        int x,
        ref int y,
        string text,
        int fontSize,
        int lineHeight,
        Vector4 color)
    {
        overlay.AddText(x, y, text, fontSize, color);
        y += Math.Max(1, lineHeight);
    }

    private static float ReadField(CameraState state, CameraDebugPanelField field)
    {
        return field switch
        {
            CameraDebugPanelField.Yaw => state.Yaw,
            CameraDebugPanelField.Pitch => state.Pitch,
            CameraDebugPanelField.DistanceCm => state.DistanceCm,
            CameraDebugPanelField.FovYDeg => state.FovYDeg,
            _ => throw new InvalidOperationException($"Unsupported camera debug field '{field}'.")
        };
    }

    private static void WriteField(CameraPoseRequest request, CameraDebugPanelField field, float value)
    {
        switch (field)
        {
            case CameraDebugPanelField.Yaw:
                request.Yaw = value;
                break;
            case CameraDebugPanelField.Pitch:
                request.Pitch = value;
                break;
            case CameraDebugPanelField.DistanceCm:
                request.DistanceCm = value;
                break;
            case CameraDebugPanelField.FovYDeg:
                request.FovYDeg = value;
                break;
            default:
                throw new InvalidOperationException($"Unsupported camera debug field '{field}'.");
        }
    }

    private static string Shorten(string value)
    {
        const string prefix = "CapabilityStandard.VirtualCamera.";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value.Substring(prefix.Length)
            : value;
    }
}
