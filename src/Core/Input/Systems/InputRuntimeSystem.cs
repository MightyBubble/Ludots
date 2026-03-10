using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Gameplay;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
 
namespace Ludots.Core.Input.Systems
{
    public sealed class InputRuntimeSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
        private readonly AuthoritativeInputAccumulator? _authoritativeInput;
 
        public InputRuntimeSystem(Dictionary<string, object> globals, AuthoritativeInputAccumulator? authoritativeInput = null)
        {
            _globals = globals;
            _authoritativeInput = authoritativeInput;
        }
 
        public void Initialize()
        {
        }
 
        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var handlerObj) || handlerObj is not PlayerInputHandler input)
            {
                return;
            }
 
            bool uiCaptured = _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var capturedObj) && capturedObj is bool b && b;
            input.InputBlocked = uiCaptured;
            input.Update();
            _authoritativeInput?.CaptureVisualFrame(input);

            if (_globals.TryGetValue(CoreServiceKeys.GameSession.Name, out var sessionObj) && sessionObj is GameSession session)
            {
                session.Camera.CaptureVisualInput();
            }
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
