using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Attributes;
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
            bool uiWheelCaptured = _globals.TryGetValue(CoreServiceKeys.UiWheelCaptured.Name, out var wheelCapturedObj) && wheelCapturedObj is bool wb && wb;
            input.InputBlocked = uiCaptured;
            input.Update(dt);
            if (uiWheelCaptured)
            {
                SuppressCameraZoom(input);
            }
            RunFrameConsumers(input, dt);
            ApplyPointerCaptureSuppression(input);
            if (_authoritativeInput != null)
            {
                _authoritativeInput.CaptureVisualFrame(input);
                PreserveConfiguredActionValues(input);
                AuthoritativeGroundPointerHelper.Capture(_globals, input, _authoritativeInput);
            }

            if (_pointerButtons != null)
            {
                CapturePointerButtons(input);
            }

            if (_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatInputRuntime.Name, out var seatInputObj) &&
                seatInputObj is ClientLocalSeatInputRuntime seatInput)
            {
                seatInput.UpdateVisualFrame(dt);
            }
        }

        private void RunFrameConsumers(PlayerInputHandler input, float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputFrameConsumers.Name, out var consumersObj) ||
                consumersObj is not List<IInputFrameConsumer> consumers ||
                consumers.Count == 0 ||
                !_globals.TryGetValue(CoreServiceKeys.Engine.Name, out var engineObj) ||
                engineObj is not GameEngine engine)
            {
                return;
            }

            for (int i = 0; i < consumers.Count; i++)
            {
                consumers[i]?.Consume(engine, input, dt);
            }
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void CapturePointerButtons(PlayerInputHandler input)
        {
            var bindings = InteractionActionBindingsResolver.Require(_globals, nameof(InputRuntimeSystem));
            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            CapturePointerButton(input, bindings.ConfirmActionId, pointer);
            CapturePointerButton(input, bindings.CommandActionId, pointer);
            CapturePointerButton(input, bindings.CancelActionId, pointer);
        }

        private void PreserveConfiguredActionValues(PlayerInputHandler input)
        {
            if (_authoritativeInput == null ||
                !_globals.TryGetValue(CoreServiceKeys.InputActionAttributeBindingRegistry.Name, out object? registryObj) ||
                registryObj is not InputActionAttributeBindingRegistry registry)
            {
                return;
            }

            InputActionAttributeBindingEntry[] entries = registry.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                InputActionAttributeBindingEntry entry = entries[i];
                if (!entry.PreserveValueUntilSnapshot)
                {
                    continue;
                }

                Vector3 value = input.ReadAction<Vector3>(entry.ActionId);
                bool isDown = input.IsDown(entry.ActionId);
                bool pressed = input.PressedThisFrame(entry.ActionId);
                bool released = input.ReleasedThisFrame(entry.ActionId);
                if (!isDown && !pressed && value.LengthSquared() <= 0.000001f)
                {
                    continue;
                }

                _authoritativeInput.CaptureAction(
                    entry.ActionId,
                    value,
                    isDown,
                    pressed,
                    released,
                    preserveValueUntilSnapshot: true);
            }
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

        private void ApplyPointerCaptureSuppression(PlayerInputHandler input)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.PointerInputCaptured.Name, out var capturedObj) ||
                capturedObj is not bool captured ||
                !captured)
            {
                return;
            }

            var bindings = InteractionActionBindingsResolver.Require(_globals, nameof(InputRuntimeSystem));
            bool preserveCommandAction = ShouldPreserveCapturedCommand(bindings.CommandActionId);
            Suppress(input, bindings.ConfirmActionId);
            if (!preserveCommandAction)
            {
                Suppress(input, bindings.CommandActionId);
            }
            Suppress(input, bindings.CancelActionId);
            if (_globals is Dictionary<string, object> mutable)
            {
                mutable[CoreServiceKeys.PointerInputCaptured.Name] = false;
            }
        }

        private bool ShouldPreserveCapturedCommand(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                _globals.TryGetValue(CoreServiceKeys.AuthoritativeGroundPointerOverride.Name, out var overrideObj) &&
                overrideObj is AuthoritativeGroundPointerOverride pointerOverride &&
                pointerOverride.HasOverride &&
                string.Equals(pointerOverride.ActionId, actionId, System.StringComparison.Ordinal);
        }

        private void SuppressCameraZoom(PlayerInputHandler input)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputActionAttributeBindingRegistry.Name, out object? registryObj) ||
                registryObj is not InputActionAttributeBindingRegistry registry)
            {
                ClearUiWheelCaptured();
                return;
            }

            InputActionAttributeBindingEntry[] entries = registry.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].SuppressOnUiWheelCaptured)
                {
                    Suppress(input, entries[i].ActionId);
                }
            }

            ClearUiWheelCaptured();
        }

        private void ClearUiWheelCaptured()
        {
            if (_globals is Dictionary<string, object> mutable)
            {
                mutable[CoreServiceKeys.UiWheelCaptured.Name] = false;
            }
        }

        private void Suppress(PlayerInputHandler input, string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            input.SuppressActionThisFrame(actionId);
            _authoritativeInput?.SuppressActionThisTick(actionId);
            _pointerButtons?.SuppressActionThisTick(actionId);
        }
    }
}
