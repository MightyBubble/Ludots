using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceBlendClickSystem : ISystem<float>
    {
        private readonly GameEngine _engine;

        public CameraAcceptanceBlendClickSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, CameraAcceptanceIds.BlendMapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var capturedObj) &&
                capturedObj is bool captured &&
                captured)
            {
                return;
            }

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input ||
                !input.PressedThisFrame("Select"))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenRayProvider) is not IScreenRayProvider rayProvider)
            {
                throw new InvalidOperationException("ScreenRayProvider is required for blend acceptance.");
            }

            var pointer = input.ReadAction<Vector2>("PointerPos");
            var ray = rayProvider.GetRay(pointer);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm))
            {
                return;
            }

            _engine.GameSession.Camera.ActivateVirtualCamera(
                ResolveActiveBlendCameraId(),
                followTarget: new FixedPointFollowTarget(new Vector2(worldCm.X, worldCm.Y)),
                snapToFollowTargetWhenAvailable: true,
                resetRuntimeState: true);
        }

        private string ResolveActiveBlendCameraId()
        {
            return _engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }

        private sealed class FixedPointFollowTarget : ICameraFollowTarget
        {
            private readonly Vector2 _pointCm;

            public FixedPointFollowTarget(Vector2 pointCm)
            {
                _pointCm = pointCm;
            }

            public bool TryGetPosition(out Vector2 positionCm)
            {
                positionCm = _pointCm;
                return true;
            }
        }
    }
}
