using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Automation
{
    public sealed class InputAutomationPlayer
    {
        private readonly List<InputAutomationCommand> _commands;
        private readonly List<InputAutomationFrameEvent> _frameEvents = new(16);
        private readonly Dictionary<string, bool> _keys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<InputAutomationPointerButton, bool> _pointerButtons = new();
        private Vector2 _pointerPosition;
        private bool _hasPointerPosition;
        private float _mouseWheel;
        private string _charBuffer = string.Empty;
        private int _currentFrame = -1;

        public InputAutomationPlayer(IEnumerable<InputAutomationCommand> commands)
        {
            ArgumentNullException.ThrowIfNull(commands);
            _commands = new List<InputAutomationCommand>(commands);
            _commands.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        }

        public bool UsesExternalFrameClock { get; set; }

        public int CurrentFrame => _currentFrame;

        public bool HasPointerPosition => _hasPointerPosition;

        public Vector2 PointerPosition => _pointerPosition;

        public float MouseWheel => _mouseWheel;

        public IReadOnlyList<InputAutomationFrameEvent> FrameEvents => _frameEvents;

        public void SetFrame(int frame)
        {
            if (frame < 0)
            {
                Reset();
                return;
            }

            if (frame <= _currentFrame)
            {
                Reset();
            }

            while (_currentFrame < frame)
            {
                AdvanceFrame();
            }
        }

        public void AdvanceFrame()
        {
            _currentFrame++;
            _mouseWheel = 0f;
            _charBuffer = string.Empty;
            _frameEvents.Clear();

            ApplyContinuousCommands(_currentFrame);
            for (int i = 0; i < _commands.Count; i++)
            {
                var command = _commands[i];
                if (command.Frame == _currentFrame)
                {
                    ApplyStartCommand(command);
                }
                else if (GetEndFrame(command) == _currentFrame)
                {
                    ApplyEndCommand(command);
                }
            }
        }

        public bool TryGetButton(string devicePath, out bool isDown)
        {
            string normalized = NormalizeDevicePath(devicePath);
            if (normalized.Length == 0)
            {
                isDown = false;
                return false;
            }

            if (TryResolvePointerButton(normalized, out InputAutomationPointerButton pointerButton))
            {
                isDown = _pointerButtons.TryGetValue(pointerButton, out bool pointerDown) && pointerDown;
                return true;
            }

            if (normalized.StartsWith("keyboard/", StringComparison.OrdinalIgnoreCase))
            {
                string key = normalized["keyboard/".Length..];
                isDown = _keys.TryGetValue(NormalizeKey(key), out bool keyDown) && keyDown;
                return true;
            }

            isDown = false;
            return false;
        }

        public string ConsumeCharBuffer()
        {
            string value = _charBuffer;
            _charBuffer = string.Empty;
            return value;
        }

        public void Reset()
        {
            _currentFrame = -1;
            _frameEvents.Clear();
            _keys.Clear();
            _pointerButtons.Clear();
            _pointerPosition = default;
            _hasPointerPosition = false;
            _mouseWheel = 0f;
            _charBuffer = string.Empty;
        }

        private void ApplyContinuousCommands(int frame)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                var command = _commands[i];
                int duration = Math.Max(0, command.DurationFrames);
                if (duration <= 0 || frame < command.Frame || frame > command.Frame + duration)
                {
                    continue;
                }

                switch (command.Kind)
                {
                    case InputAutomationCommandKind.PointerMove:
                    case InputAutomationCommandKind.PointerDrag:
                        SetPointer(Interpolate(command, frame));
                        AddPointerEvent(InputAutomationFrameEventKind.PointerMove, command.Button, Vector2.Zero, command.Modifiers);
                        break;
                    case InputAutomationCommandKind.PointerClick:
                        SetPointer(new Vector2(command.X, command.Y));
                        break;
                }

                if (command.Kind is InputAutomationCommandKind.PointerDrag or InputAutomationCommandKind.PointerClick &&
                    frame > command.Frame &&
                    frame < command.Frame + duration)
                {
                    _pointerButtons[command.Button] = true;
                }
            }
        }

        private void ApplyStartCommand(InputAutomationCommand command)
        {
            switch (command.Kind)
            {
                case InputAutomationCommandKind.PointerMove:
                    if (command.DurationFrames <= 0)
                    {
                        SetPointer(new Vector2(command.X, command.Y));
                        AddPointerEvent(InputAutomationFrameEventKind.PointerMove, null, Vector2.Zero, command.Modifiers);
                    }
                    break;
                case InputAutomationCommandKind.PointerDown:
                    SetPointer(new Vector2(command.X, command.Y));
                    _pointerButtons[command.Button] = true;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerDown, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.PointerUp:
                    SetPointer(new Vector2(command.X, command.Y));
                    _pointerButtons[command.Button] = false;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerUp, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.PointerClick:
                    SetPointer(new Vector2(command.X, command.Y));
                    AddPointerEvent(InputAutomationFrameEventKind.PointerMove, null, Vector2.Zero, command.Modifiers);
                    _pointerButtons[command.Button] = true;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerDown, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.PointerDrag:
                    SetPointer(new Vector2(command.X, command.Y));
                    AddPointerEvent(InputAutomationFrameEventKind.PointerMove, null, Vector2.Zero, command.Modifiers);
                    _pointerButtons[command.Button] = true;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerDown, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.PointerScroll:
                    SetPointer(new Vector2(command.X, command.Y));
                    _mouseWheel += command.DeltaY;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerScroll, null, new Vector2(command.DeltaX, command.DeltaY), command.Modifiers);
                    break;
                case InputAutomationCommandKind.KeyDown:
                    SetKey(command, true);
                    AddKeyboardEvent(InputAutomationFrameEventKind.KeyDown, command);
                    break;
                case InputAutomationCommandKind.KeyUp:
                    SetKey(command, false);
                    AddKeyboardEvent(InputAutomationFrameEventKind.KeyUp, command);
                    break;
                case InputAutomationCommandKind.KeyStroke:
                    SetKey(command, true);
                    AddKeyboardEvent(InputAutomationFrameEventKind.KeyDown, command);
                    break;
                case InputAutomationCommandKind.Text:
                    _charBuffer += command.Text;
                    AddTextEvents(command.Text, command.Modifiers);
                    break;
            }
        }

        private void ApplyEndCommand(InputAutomationCommand command)
        {
            switch (command.Kind)
            {
                case InputAutomationCommandKind.PointerClick:
                    SetPointer(new Vector2(command.X, command.Y));
                    _pointerButtons[command.Button] = false;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerUp, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.PointerDrag:
                    SetPointer(new Vector2(command.EndX, command.EndY));
                    _pointerButtons[command.Button] = false;
                    AddPointerEvent(InputAutomationFrameEventKind.PointerUp, command.Button, Vector2.Zero, command.Modifiers);
                    break;
                case InputAutomationCommandKind.KeyStroke:
                    SetKey(command, false);
                    AddKeyboardEvent(InputAutomationFrameEventKind.KeyUp, command);
                    break;
            }
        }

        private void SetPointer(Vector2 position)
        {
            _pointerPosition = position;
            _hasPointerPosition = true;
        }

        private void SetKey(InputAutomationCommand command, bool isDown)
        {
            string key = ResolveKey(command);
            if (key.Length > 0)
            {
                _keys[NormalizeKey(key)] = isDown;
            }
        }

        private void AddPointerEvent(InputAutomationFrameEventKind kind, InputAutomationPointerButton? button, Vector2 delta, int modifiers)
        {
            _frameEvents.Add(new InputAutomationFrameEvent(
                kind,
                _pointerPosition,
                button,
                delta,
                string.Empty,
                string.Empty,
                modifiers));
        }

        private void AddKeyboardEvent(InputAutomationFrameEventKind kind, InputAutomationCommand command)
        {
            string key = ResolveKey(command);
            _frameEvents.Add(new InputAutomationFrameEvent(
                kind,
                default,
                null,
                default,
                key,
                string.Empty,
                command.Modifiers));
        }

        private void AddTextEvents(string text, int modifiers)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                string character = text[i].ToString();
                _frameEvents.Add(new InputAutomationFrameEvent(
                    InputAutomationFrameEventKind.Character,
                    default,
                    null,
                    default,
                    character,
                    character,
                    modifiers));
            }
        }

        private static Vector2 Interpolate(InputAutomationCommand command, int frame)
        {
            int duration = Math.Max(1, command.DurationFrames);
            float progress = Math.Clamp((frame - command.Frame) / (float)duration, 0f, 1f);
            return new Vector2(
                command.X + ((command.EndX - command.X) * progress),
                command.Y + ((command.EndY - command.Y) * progress));
        }

        private static int GetEndFrame(InputAutomationCommand command)
        {
            return command.Kind switch
            {
                InputAutomationCommandKind.PointerClick => command.Frame + Math.Max(1, command.DurationFrames),
                InputAutomationCommandKind.PointerDrag => command.Frame + Math.Max(1, command.DurationFrames),
                InputAutomationCommandKind.KeyStroke => command.Frame + Math.Max(1, command.DurationFrames),
                _ => -1
            };
        }

        private static string ResolveKey(InputAutomationCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.Key))
            {
                return command.Key.Trim();
            }

            string normalized = NormalizeDevicePath(command.DevicePath);
            return normalized.StartsWith("keyboard/", StringComparison.OrdinalIgnoreCase)
                ? normalized["keyboard/".Length..]
                : string.Empty;
        }

        private static string NormalizeDevicePath(string? devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return string.Empty;
            }

            return devicePath.Trim()
                .Replace("<Keyboard>/", "keyboard/", StringComparison.OrdinalIgnoreCase)
                .Replace("<Mouse>/", "mouse/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string key)
        {
            string normalized = key.Trim();
            if (normalized.StartsWith("Arrow", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["Arrow".Length..];
            }

            return normalized.ToUpperInvariant();
        }

        private static bool TryResolvePointerButton(string normalizedPath, out InputAutomationPointerButton button)
        {
            string value = normalizedPath.StartsWith("mouse/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath["mouse/".Length..]
                : normalizedPath;
            value = value.Replace("Button", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (Enum.TryParse(value, ignoreCase: true, out button))
            {
                return true;
            }

            button = default;
            return false;
        }
    }
}
