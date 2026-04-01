using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.Gameplay;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
 
namespace Ludots.Core.Input.Systems
{
    public sealed class InputRuntimeSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
        private readonly AuthoritativeInputAccumulator? _authoritativeInput;
        private readonly AuthoritativePointerButtonAccumulator? _pointerButtons;
 
        public InputRuntimeSystem(
            Dictionary<string, object> globals,
            AuthoritativeInputAccumulator? authoritativeInput = null,
            AuthoritativePointerButtonAccumulator? pointerButtons = null)
        {
            _globals = globals;
            _authoritativeInput = authoritativeInput;
            _pointerButtons = pointerButtons;
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

            if (_globals.TryGetValue(CoreServiceKeys.InputBackend.Name, out var backendObj) &&
                backendObj is IFrameSynchronizedInputBackend synchronizedBackend)
            {
                synchronizedBackend.AdvanceFrameInput();
            }
 
            bool uiCaptured = _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var capturedObj) && capturedObj is bool b && b;
            input.InputBlocked = uiCaptured;
            input.Update();
            if (_authoritativeInput != null)
            {
                _authoritativeInput.CaptureVisualFrame(input);
                AuthoritativeGroundPointerHelper.Capture(_globals, input, _authoritativeInput);
            }

            if (_pointerButtons != null)
            {
                CapturePointerButtons(input);
            }

            if (_globals.TryGetValue(CoreServiceKeys.GameSession.Name, out var sessionObj) && sessionObj is GameSession session)
            {
                session.Camera.SetUserInputSuppressed(uiCaptured);
                session.Camera.CaptureVisualInput();
            }
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void CapturePointerButtons(PlayerInputHandler input)
        {
            var bindings = _globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj) &&
                           bindingsObj is InteractionActionBindings configuredBindings
                ? configuredBindings
                : new InteractionActionBindings();
            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            CapturePointerButton(input, bindings.ConfirmActionId, pointer);
            CapturePointerButton(input, bindings.CommandActionId, pointer);
            CapturePointerButton(input, bindings.CancelActionId, pointer);
        }

        private void CapturePointerButton(PlayerInputHandler input, string actionId, Vector2 pointer)
        {
            if (_pointerButtons == null || string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _pointerButtons.Capture(
                actionId,
                pointer,
                input.IsDown(actionId),
                input.PressedThisFrame(actionId),
                input.ReleasedThisFrame(actionId));
        }
    }
}
