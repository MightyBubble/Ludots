using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    /// <summary>
    /// Acceptance camera-mode switching from the fixture's own CameraAcceptance.Controls context:
    /// Rts / Tps / FollowClose / FollowWide presses replace the local virtual camera stack with
    /// the corresponding definition. Follow target and collection come from the definitions; the
    /// collection owner is the sole possessed rep. Runs in LocalInput so replicated clients
    /// (which execute only that group) keep the mode keys alive.
    /// </summary>
    public sealed class CameraAcceptanceModeSystem : ISystem<float>
    {
        private readonly GameEngine _engine;

        public CameraAcceptanceModeSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!CameraAcceptanceIds.IsAcceptanceMap(_engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return;
            }

            string? cameraId = null;
            if (input.PressedThisFrame(CameraAcceptanceIds.RtsModeActionId))
            {
                cameraId = CameraAcceptanceIds.RtsCameraId;
            }
            else if (input.PressedThisFrame(CameraAcceptanceIds.TpsModeActionId))
            {
                cameraId = CameraAcceptanceIds.TpsCameraId;
            }
            else if (input.PressedThisFrame(CameraAcceptanceIds.FollowCloseModeActionId))
            {
                cameraId = CameraAcceptanceIds.FollowCloseCameraId;
            }
            else if (input.PressedThisFrame(CameraAcceptanceIds.FollowWideModeActionId))
            {
                cameraId = CameraAcceptanceIds.FollowWideCameraId;
            }

            if (cameraId != null)
            {
                Runtime.CameraAcceptanceCameras.SwitchToProfile(_engine, cameraId);
            }
        }
    }
}
