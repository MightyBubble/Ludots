using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
 
namespace Ludots.Core.Systems
{
    public sealed class InputRuntimeSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
 
        public InputRuntimeSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }
 
        public void Initialize()
        {
        }
 
        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var handlerObj) || handlerObj is not PlayerInputHandler input)
            {
                return;
            }
 
            bool uiCaptured = _globals.TryGetValue(ContextKeys.UiCaptured, out var capturedObj) && capturedObj is bool b && b;
            input.InputBlocked = uiCaptured;
            input.Update();
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
