using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Runtime
{
    public enum SyntheticPointerButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    /// <summary>
    /// Engine-neutral virtual input device: the window-level counterpart to
    /// <see cref="PlayerInputHandler.InjectAction"/> (which is the semantic
    /// action level). Automation drivers enqueue window-space events; the host
    /// adapter consults this device alongside real hardware at its polling
    /// points, so synthetic input flows through the same UI hit-test / capture /
    /// binding / context pipeline as physical input.
    ///
    /// Write calls (MovePointer/PointerDown/...) queue events; the host loop
    /// calls <see cref="AdvanceFrame"/> once per frame before any input
    /// collection, which applies the queue and produces the per-frame edge
    /// sets (pressed/released, wheel, chars). All access is game-thread only.
    /// </summary>
    public sealed class SyntheticInputDevice
    {
        private enum EventKind { PointerMove, PointerButton, Wheel, Key, Text, ReleaseAll }

        private struct PendingEvent
        {
            public EventKind Kind;
            public float X;
            public float Y;
            public SyntheticPointerButton Button;
            public bool Down;
            public string? Text;
        }

        private readonly List<PendingEvent> _pending = new();
        private readonly HashSet<SyntheticPointerButton> _buttonsDown = new();
        private readonly HashSet<string> _keysDown = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<SyntheticPointerButton> _pressedThisFrame = new();
        private readonly HashSet<SyntheticPointerButton> _releasedThisFrame = new();
        private readonly HashSet<string> _keysPressedThisFrame = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keysReleasedThisFrame = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<char> _charsThisFrame = new();

        private Vector2 _pointerPosition;
        private float _wheelThisFrame;

        public bool HasPointerOverride { get; private set; }
        public Vector2 PointerPosition => _pointerPosition;
        public float WheelDeltaThisFrame => _wheelThisFrame;
        public IReadOnlyList<char> CharsThisFrame => _charsThisFrame;

        public int PendingEventCount => _pending.Count;

        // ---- write side (agent tools) ----

        /// <summary>Move the virtual pointer; implicitly engages the pointer override.</summary>
        public void MovePointer(float x, float y) =>
            _pending.Add(new PendingEvent { Kind = EventKind.PointerMove, X = x, Y = y });

        public void PointerDown(SyntheticPointerButton button) =>
            _pending.Add(new PendingEvent { Kind = EventKind.PointerButton, Button = button, Down = true });

        public void PointerUp(SyntheticPointerButton button) =>
            _pending.Add(new PendingEvent { Kind = EventKind.PointerButton, Button = button, Down = false });

        /// <summary>Full down+up within the same frame.</summary>
        public void Click(SyntheticPointerButton button)
        {
            PointerDown(button);
            PointerUp(button);
        }

        /// <summary>Disengage the pointer override (real hardware position wins again).</summary>
        public void ClearPointerOverride() => _pending.Add(new PendingEvent { Kind = EventKind.PointerMove, X = -1f, Y = -1f, Text = "clear" });

        public void Scroll(float deltaY) =>
            _pending.Add(new PendingEvent { Kind = EventKind.Wheel, Y = deltaY });

        /// <summary>Key names are engine-neutral ("A", "F5", "Space", "PageUp"); matching is case/underscore-insensitive.</summary>
        public void KeyDown(string key) =>
            _pending.Add(new PendingEvent { Kind = EventKind.Key, Text = NormalizeKey(key), Down = true });

        public void KeyUp(string key) =>
            _pending.Add(new PendingEvent { Kind = EventKind.Key, Text = NormalizeKey(key), Down = false });

        public void PressKey(string key)
        {
            KeyDown(key);
            KeyUp(key);
        }

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _pending.Add(new PendingEvent { Kind = EventKind.Text, Text = text });
        }

        /// <summary>Release every held button/key and disengage the pointer override.</summary>
        public void ReleaseAll() => _pending.Add(new PendingEvent { Kind = EventKind.ReleaseAll });

        // ---- frame boundary (host loop) ----

        public void AdvanceFrame()
        {
            _pressedThisFrame.Clear();
            _releasedThisFrame.Clear();
            _keysPressedThisFrame.Clear();
            _keysReleasedThisFrame.Clear();
            _charsThisFrame.Clear();
            _wheelThisFrame = 0f;

            foreach (PendingEvent e in _pending)
            {
                switch (e.Kind)
                {
                    case EventKind.PointerMove:
                        if (e.Text == "clear")
                        {
                            HasPointerOverride = false;
                        }
                        else
                        {
                            _pointerPosition = new Vector2(e.X, e.Y);
                            HasPointerOverride = true;
                        }
                        break;
                    case EventKind.PointerButton:
                        if (e.Down)
                        {
                            if (_buttonsDown.Add(e.Button)) _pressedThisFrame.Add(e.Button);
                        }
                        else if (_buttonsDown.Remove(e.Button) || _pressedThisFrame.Contains(e.Button))
                        {
                            _releasedThisFrame.Add(e.Button);
                        }
                        break;
                    case EventKind.Wheel:
                        _wheelThisFrame += e.Y;
                        break;
                    case EventKind.Key:
                        if (e.Down)
                        {
                            if (_keysDown.Add(e.Text!)) _keysPressedThisFrame.Add(e.Text!);
                        }
                        else if (_keysDown.Remove(e.Text!) || _keysPressedThisFrame.Contains(e.Text!))
                        {
                            _keysReleasedThisFrame.Add(e.Text!);
                        }
                        break;
                    case EventKind.Text:
                        _charsThisFrame.AddRange(e.Text!);
                        break;
                    case EventKind.ReleaseAll:
                        foreach (SyntheticPointerButton b in _buttonsDown) _releasedThisFrame.Add(b);
                        foreach (string k in _keysDown) _keysReleasedThisFrame.Add(k);
                        _buttonsDown.Clear();
                        _keysDown.Clear();
                        HasPointerOverride = false;
                        break;
                }
            }

            _pending.Clear();
        }

        // ---- read side (host adapters) ----

        public bool IsButtonDown(SyntheticPointerButton button) => _buttonsDown.Contains(button);
        public bool WasButtonPressedThisFrame(SyntheticPointerButton button) => _pressedThisFrame.Contains(button);
        public bool WasButtonReleasedThisFrame(SyntheticPointerButton button) => _releasedThisFrame.Contains(button);

        public bool IsKeyDown(string key) => _keysDown.Contains(key);
        public bool WasKeyPressedThisFrame(string key) => _keysPressedThisFrame.Contains(key);
        public bool WasKeyReleasedThisFrame(string key) => _keysReleasedThisFrame.Contains(key);

        /// <summary>Snapshot iteration is safe against mutation during UI event dispatch.</summary>
        public IReadOnlyList<string> KeysDownSnapshotPressedThisFrame() => new List<string>(_keysPressedThisFrame);
        public IReadOnlyList<string> KeysReleasedThisFrameSnapshot() => new List<string>(_keysReleasedThisFrame);

        public IReadOnlyCollection<string> KeysDown => _keysDown;
        public IReadOnlyCollection<SyntheticPointerButton> ButtonsDown => _buttonsDown;

        /// <summary>
        /// Expose this device as an <see cref="IInputDeviceWatcher"/> so AgentBridge mock
        /// input is enumerable through the same device contract as host hardware.
        /// </summary>
        public IInputDeviceWatcher WatchAsDeviceWatcher() => new SyntheticDeviceWatcher();

        private sealed class SyntheticDeviceWatcher : IInputDeviceWatcher
        {
            // The synthetic device fakes keys and a pointer with wheel; it has no touch surface.
            // It lives for the whole process, so the device set is constant and never fires changes.
            private static readonly InputDeviceDescriptor[] Devices =
            {
                new("synthetic-keyboard", InputDeviceKind.Keyboard, "AgentBridge Synthetic Keyboard", -1),
                new("synthetic-mouse", InputDeviceKind.Mouse, "AgentBridge Synthetic Mouse", -1),
            };

            public event Action<InputDeviceChangeEvent>? DeviceChanged
            {
                add { }
                remove { }
            }

            public IReadOnlyList<InputDeviceDescriptor> GetConnectedDevices() => Devices;
        }

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key name cannot be null or whitespace.", nameof(key));
            }

            return key.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
