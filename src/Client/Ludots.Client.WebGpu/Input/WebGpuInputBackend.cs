using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Ludots.Client.WebGpu.Input
{
    public sealed class WebGpuInputBackend : IInputBackend
    {
        private readonly IWindow _window;
        private IInputContext? _input;
        private IKeyboard? _keyboard;
        private IMouse? _mouse;
        private readonly HashSet<Key> _pressedKeys = new();
        private readonly HashSet<MouseButton> _pressedMouseButtons = new();
        private bool _imeEnabled;
        private bool _windowFocused = true;
        private string _pendingChars = string.Empty;
        private float _wheelDelta;

        public WebGpuInputBackend(IWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void Attach()
        {
            if (_input != null)
            {
                return;
            }

            _input = _window.CreateInput();
            if (_input.Keyboards.Count == 0)
            {
                throw new InvalidOperationException("WebGPU input backend requires at least one keyboard device.");
            }

            if (_input.Mice.Count == 0)
            {
                throw new InvalidOperationException("WebGPU input backend requires at least one mouse device.");
            }

            _keyboard = _input.Keyboards[0];
            _mouse = _input.Mice[0];
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
            _keyboard.KeyChar += OnKeyChar;
            _keyboard.BeginInput();
            _mouse.MouseDown += OnMouseDown;
            _mouse.MouseUp += OnMouseUp;
            _mouse.Scroll += OnScroll;
            _window.FocusChanged += OnFocusChanged;
        }

        public void BeginFrame()
        {
            _wheelDelta = 0f;
            _pendingChars = string.Empty;
        }

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY", StringComparison.OrdinalIgnoreCase))
            {
                return _wheelDelta;
            }

            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            if (_imeEnabled)
            {
                return false;
            }

            if (_keyboard == null || _mouse == null)
            {
                throw new InvalidOperationException("WebGpuInputBackend.Attach() must be called before polling input.");
            }

            if (!_windowFocused)
            {
                return false;
            }

            Key? key = WebGpuInputPathParser.ParseKeyboardKey(devicePath);
            if (key.HasValue)
            {
                if (WebGpuWindowsInputState.TryGetKeyDown(key.Value, out bool windowsKeyDown))
                {
                    return windowsKeyDown;
                }

                return _pressedKeys.Contains(key.Value) || _keyboard.IsKeyPressed(key.Value);
            }

            MouseButton? mouseButton = WebGpuInputPathParser.ParseMouseButton(devicePath);
            if (mouseButton.HasValue)
            {
                if (WebGpuWindowsInputState.TryGetMouseButtonDown(mouseButton.Value, out bool windowsMouseDown))
                {
                    return windowsMouseDown;
                }

                return _pressedMouseButtons.Contains(mouseButton.Value) || _mouse.IsButtonPressed(mouseButton.Value);
            }

            return false;
        }

        public Vector2 GetMousePosition()
        {
            if (_mouse == null)
            {
                throw new InvalidOperationException("WebGpuInputBackend.Attach() must be called before polling input.");
            }

            if (!_windowFocused)
            {
                return new Vector2(-1f, -1f);
            }

            return _mouse.Position;
        }

        public float GetMouseWheel()
        {
            return _wheelDelta;
        }

        public void EnableIME(bool enable)
        {
            _imeEnabled = enable;
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer()
        {
            string chars = _pendingChars;
            _pendingChars = string.Empty;
            return chars;
        }

        public void DisposeInput()
        {
            _window.FocusChanged -= OnFocusChanged;
            if (_keyboard != null)
            {
                _keyboard.KeyDown -= OnKeyDown;
                _keyboard.KeyUp -= OnKeyUp;
                _keyboard.KeyChar -= OnKeyChar;
                _keyboard.EndInput();
            }

            if (_mouse != null)
            {
                _mouse.MouseDown -= OnMouseDown;
                _mouse.MouseUp -= OnMouseUp;
                _mouse.Scroll -= OnScroll;
            }

            _input?.Dispose();
            _input = null;
            _keyboard = null;
            _mouse = null;
            _pressedKeys.Clear();
            _pressedMouseButtons.Clear();
        }

        private void OnFocusChanged(bool focused)
        {
            _windowFocused = focused;
            if (!focused)
            {
                _pressedKeys.Clear();
                _pressedMouseButtons.Clear();
            }
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            _pressedKeys.Add(key);
        }

        private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
        {
            _pressedKeys.Remove(key);
        }

        private void OnKeyChar(IKeyboard keyboard, char c)
        {
            if (c >= 32)
            {
                _pendingChars += c;
            }
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            _wheelDelta += wheel.Y;
        }

        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            _pressedMouseButtons.Add(button);
        }

        private void OnMouseUp(IMouse mouse, MouseButton button)
        {
            _pressedMouseButtons.Remove(button);
        }
    }
}
