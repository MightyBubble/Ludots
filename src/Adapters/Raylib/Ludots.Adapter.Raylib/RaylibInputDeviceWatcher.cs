using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ludots.Platform.Abstractions;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// Raylib-backed device discovery. Keyboard and mouse are window-inherent and therefore
    /// always connected; gamepads are diffed per Poll() and surface as hot-plug events.
    /// Poll must run on the game thread, after InitWindow.
    /// </summary>
    internal sealed class RaylibInputDeviceWatcher : IInputDeviceWatcher
    {
        private const int MaxGamepads = 4;

        private static readonly InputDeviceDescriptor Keyboard =
            new("keyboard-primary", InputDeviceKind.Keyboard, "Keyboard", -1);

        private static readonly InputDeviceDescriptor Mouse =
            new("mouse-primary", InputDeviceKind.Mouse, "Mouse", -1);

        private Dictionary<string, InputDeviceDescriptor> _devices = new(StringComparer.Ordinal);
        private Dictionary<string, InputDeviceDescriptor> _next = new(StringComparer.Ordinal);
        private readonly List<InputDeviceDescriptor> _snapshot = new();

        public event Action<InputDeviceChangeEvent>? DeviceChanged;

        public IReadOnlyList<InputDeviceDescriptor> GetConnectedDevices() => _snapshot;

        public void Poll()
        {
            _next.Clear();
            _next[Keyboard.DeviceId] = Keyboard;
            _next[Mouse.DeviceId] = Mouse;
            for (int i = 0; i < MaxGamepads; i++)
            {
                if (!Rl.IsGamepadAvailable(i))
                {
                    continue;
                }

                string deviceId = $"gamepad-{i}";
                _next[deviceId] = new InputDeviceDescriptor(
                    deviceId,
                    InputDeviceKind.Gamepad,
                    ReadGamepadName(i, deviceId),
                    -1);
            }

            foreach (KeyValuePair<string, InputDeviceDescriptor> entry in _next)
            {
                if (!_devices.ContainsKey(entry.Key))
                {
                    DeviceChanged?.Invoke(new InputDeviceChangeEvent(entry.Value, InputDeviceChangeKind.Connected));
                }
            }

            foreach (KeyValuePair<string, InputDeviceDescriptor> entry in _devices)
            {
                if (!_next.ContainsKey(entry.Key))
                {
                    DeviceChanged?.Invoke(new InputDeviceChangeEvent(entry.Value, InputDeviceChangeKind.Disconnected));
                }
            }

            (_devices, _next) = (_next, _devices);
            _snapshot.Clear();
            _snapshot.AddRange(_devices.Values);
        }

        private static string ReadGamepadName(int index, string fallback)
        {
            IntPtr namePtr = Rl.GetGamepadName(index);
            string? name = namePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(namePtr) : null;
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
    }
}
