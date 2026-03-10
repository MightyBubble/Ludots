using Arch.System;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using System.Numerics;
using CameraAcceptanceMod.Input;
using VirtualCameraShotsMod;

namespace CameraAcceptanceMod.Systems
{
    public sealed class CameraAcceptanceSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private PlayerInputHandler? _input;
        private bool _inputContextPushed;

        public CameraAcceptanceSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }

        public void BeforeUpdate(in float t) { }

        public void Update(in float t)
        {
            if (_input == null &&
                _engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                EnsureInputSchema(input);
                _input = input;
                _input.PushContext(CameraAcceptanceInputContexts.Acceptance);
                _inputContextPushed = true;
            }

            if (_input == null)
            {
                return;
            }

            if (_input.PressedThisFrame(CameraAcceptanceInputActions.ClearShot))
            {
                _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Clear = true
                });
            }

            if (_input.PressedThisFrame(CameraAcceptanceInputActions.ReplayShot))
            {
                _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Id = VirtualCameraShotIds.IntroFocus
                });
            }

            RenderOverlay();
        }

        public void AfterUpdate(in float t) { }

        public void Dispose()
        {
            if (_input != null && _inputContextPushed)
            {
                _input.PopContext(CameraAcceptanceInputContexts.Acceptance);
                _inputContextPushed = false;
            }
        }

        private static void EnsureInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(CameraAcceptanceInputContexts.Acceptance))
            {
                throw new System.InvalidOperationException($"Missing input context: {CameraAcceptanceInputContexts.Acceptance}");
            }

            if (!input.HasAction(CameraAcceptanceInputActions.ClearShot))
            {
                throw new System.InvalidOperationException($"Missing input action: {CameraAcceptanceInputActions.ClearShot}");
            }

            if (!input.HasAction(CameraAcceptanceInputActions.ReplayShot))
            {
                throw new System.InvalidOperationException($"Missing input action: {CameraAcceptanceInputActions.ReplayShot}");
            }
        }

        private void RenderOverlay()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) ||
                overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            var brain = _engine.GameSession.Camera.VirtualCameraBrain;
            bool hasAuthority = brain != null && brain.HasActiveCamera;
            string activeVirtualCamera = hasAuthority ? brain!.ActiveCameraId : "none";
            string mode = _engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeObj) && modeObj is string modeId
                ? modeId
                : "map-default";

            int x = 16;
            int y = 16;
            int w = 500;
            int h = 112;
            var bg = new Vector4(0.04f, 0.07f, 0.1f, 0.78f);
            var border = new Vector4(0.55f, 0.85f, 1f, 0.45f);
            var title = new Vector4(0.92f, 0.97f, 1f, 1f);
            var text = new Vector4(0.82f, 0.9f, 0.96f, 1f);
            var hint = new Vector4(1f, 0.9f, 0.55f, 1f);

            overlay.AddRect(x, y, w, h, bg, border);
            overlay.AddText(x + 10, y + 8, "Camera Acceptance", 16, title);
            overlay.AddText(x + 10, y + 32, $"Authority: {activeVirtualCamera} | ViewMode: {mode}", 14, text);
            overlay.AddText(x + 10, y + 52, "Map default stays active under higher-priority shots.", 14, text);
            overlay.AddText(x + 10, y + 74, "F1/F2/F3: switch Tactical / Follow / Inspect", 13, hint);
            overlay.AddText(x + 10, y + 92, "Enter: clear shot | R: replay intro shot", 13, hint);
        }
    }
}
