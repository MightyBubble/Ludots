using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkCameraResetSystem : ISystem<float>
    {
        private const string ResetCameraAction = "ResetCamera";

        private readonly Dictionary<string, object> _globals;
        private readonly GameEngine _engine;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public RoadNetworkCameraResetSystem(
            Dictionary<string, object> globals,
            GameEngine engine,
            RoadNetworkShowcaseRuntime runtime)
        {
            _globals = globals;
            _engine = engine;
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
                inputObj is not IInputActionReader input ||
                !input.PressedThisFrame(ResetCameraAction))
            {
                return;
            }

            _runtime.TryResetCamera(_engine);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
