using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Input.Runtime;
using Ludots.UI.Input;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebInputBackend : IInputBackend
    {
        private readonly object _lock = new();
        private readonly Queue<PointerEvent> _pointerEvents = new();
        private WireInputState _state;
        private bool _hasPointerSample;

        public void ApplyStateMessage(ReadOnlySpan<byte> msg)
        {
            if (msg.Length < InputProtocol.InputStateMessageSize || msg[0] != InputProtocol.MsgTypeInputState)
            {
                return;
            }

            var state = new WireInputState
            {
                ButtonMask = BinaryPrimitives.ReadInt32LittleEndian(msg.Slice(InputProtocol.InputStateButtonMaskOffset, 4)),
                MouseX = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.InputStateMouseXOffset, 4)),
                MouseY = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.InputStateMouseYOffset, 4)),
                MouseWheel = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.InputStateMouseWheelOffset, 4)),
                KeyBits = BinaryPrimitives.ReadUInt64LittleEndian(msg.Slice(InputProtocol.InputStateKeyBitsOffset, 8)),
            };

            lock (_lock)
            {
                _state = state;
                _hasPointerSample = true;
            }
        }

        public void EnqueuePointerMessage(ReadOnlySpan<byte> msg)
        {
            if (msg.Length < InputProtocol.PointerEventMessageSize || msg[0] != InputProtocol.MsgTypePointerEvent)
            {
                return;
            }

            byte rawAction = msg[InputProtocol.PointerActionOffset];
            PointerAction action = Enum.IsDefined(typeof(PointerAction), (int)rawAction)
                ? (PointerAction)rawAction
                : PointerAction.Move;
            float x = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerXOffset, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerYOffset, 4));
            float deltaX = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerDeltaXOffset, 4));
            float deltaY = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerDeltaYOffset, 4));

            lock (_lock)
            {
                _state.MouseX = x;
                _state.MouseY = y;
                _hasPointerSample = true;
                if (action == PointerAction.Scroll)
                {
                    _state.MouseWheel = deltaY;
                }

                _pointerEvents.Enqueue(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = action,
                    X = x,
                    Y = y,
                    DeltaX = deltaX,
                    DeltaY = deltaY,
                });
            }
        }

        public void SyncNeutralViewport(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            lock (_lock)
            {
                if (_hasPointerSample)
                {
                    return;
                }

                _state.MouseX = width * 0.5f;
                _state.MouseY = height * 0.5f;
            }
        }

        public bool TryDequeuePointerEvent(out PointerEvent? pointerEvent)
        {
            lock (_lock)
            {
                if (_pointerEvents.Count == 0)
                {
                    pointerEvent = null;
                    return false;
                }

                pointerEvent = _pointerEvents.Dequeue();
                return true;
            }
        }

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY", StringComparison.OrdinalIgnoreCase))
            {
                return SnapshotState(peekOnly: false).MouseWheel;
            }

            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            if (devicePath.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
            {
                int bitIndex = KeyPathToBitIndex(devicePath.Substring(11));
                if (bitIndex >= 0)
                {
                    return (SnapshotState(peekOnly: true).KeyBits & (1UL << bitIndex)) != 0;
                }

                return false;
            }

            if (devicePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
            {
                string btnName = devicePath.Substring(8).ToUpperInvariant();
                WireInputState state = SnapshotState(peekOnly: true);
                return btnName switch
                {
                    "LEFTBUTTON" => (state.ButtonMask & InputProtocol.ButtonMaskLeft) != 0,
                    "RIGHTBUTTON" => (state.ButtonMask & InputProtocol.ButtonMaskRight) != 0,
                    "MIDDLEBUTTON" => (state.ButtonMask & InputProtocol.ButtonMaskMiddle) != 0,
                    _ => false,
                };
            }

            return false;
        }

        public Vector2 GetMousePosition()
        {
            WireInputState state = SnapshotState(peekOnly: true);
            return new Vector2(state.MouseX, state.MouseY);
        }

        public float GetMouseWheel() => SnapshotState(peekOnly: false).MouseWheel;

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;

        private WireInputState SnapshotState(bool peekOnly)
        {
            lock (_lock)
            {
                WireInputState snapshot = _state;
                if (!peekOnly)
                {
                    _state.MouseWheel = 0f;
                }

                return snapshot;
            }
        }

        private static int KeyPathToBitIndex(string keyName)
        {
            if (keyName.Length == 1)
            {
                char c = char.ToUpperInvariant(keyName[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    return c - 'A';
                }

                if (c >= '0' && c <= '9')
                {
                    return 26 + (c - '0');
                }
            }

            return keyName.ToUpperInvariant() switch
            {
                "SPACE" => 36,
                "LEFTSHIFT" => 37,
                "LEFTCONTROL" => 38,
                "LEFTALT" => 39,
                "ENTER" => 40,
                "ESCAPE" => 41,
                "TAB" => 42,
                "BACKSPACE" => 43,
                "DELETE" => 44,
                "UP" => 45,
                "DOWN" => 46,
                "LEFT" => 47,
                "RIGHT" => 48,
                "F1" => 49,
                "F2" => 50,
                "F3" => 51,
                "F4" => 52,
                "F5" => 53,
                _ => -1,
            };
        }

        private struct WireInputState
        {
            public int ButtonMask;
            public float MouseX;
            public float MouseY;
            public float MouseWheel;
            public ulong KeyBits;
        }
    }
}
