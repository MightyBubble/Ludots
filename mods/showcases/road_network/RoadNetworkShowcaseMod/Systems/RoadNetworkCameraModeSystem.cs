using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    /// <summary>
    /// Road-map camera-mode keys: F1 selects the road tactical camera, F3 the road strategic
    /// camera. Scoped by the runtime's road-map guard plus the mod's own input context; runs in
    /// LocalInput so replicated clients keep the keys alive.
    /// </summary>
    internal sealed class RoadNetworkCameraModeSystem : ISystem<float>
    {
        private const string TacticalSwitchAction = "CameraModeTactical";
        private const string StrategicSwitchAction = "CameraModeInspect";

        internal const string RoadTacticalCameraId = "RoadNetwork.Camera.Tactical";
        internal const string RoadStrategicCameraId = "RoadNetwork.Camera.Strategic";

        private readonly Dictionary<string, object> _globals;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public RoadNetworkCameraModeSystem(Dictionary<string, object> globals, RoadNetworkShowcaseRuntime runtime)
        {
            _globals = globals;
            _runtime = runtime;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_runtime.IsActive ||
                !_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return;
            }

            string? cameraId = null;
            if (input.PressedThisFrame(TacticalSwitchAction))
            {
                cameraId = RoadTacticalCameraId;
            }
            else if (input.PressedThisFrame(StrategicSwitchAction))
            {
                cameraId = RoadStrategicCameraId;
            }

            if (cameraId != null)
            {
                _globals[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
                {
                    Id = cameraId,
                    ResetRuntimeState = true,
                    ReplaceActiveStack = true
                };
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
