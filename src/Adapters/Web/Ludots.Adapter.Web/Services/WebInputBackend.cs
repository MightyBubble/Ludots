using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Input.Runtime;
using Ludots.UI.Input;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebInputBackend : IInputBackend, IFrameSynchronizedInputBackend
    {
        private readonly object _lock = new();
        private readonly Queue<PointerEvent> _pointerEvents = new();
        private readonly Queue<QueuedFrameState> _frameTransitions = new();
        private WireInputState _state;
        private WireInputState _frameState;
        private bool _hasPointerSample;
        private bool _frameStateInitialized;
        private bool _hasQueuedTransition;
        private QueuedFrameState _lastQueuedTransition;
        private float _pendingMouseWheel;

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
                bool keyBitsChanged = _state.KeyBits != state.KeyBits;
                _state = state;
                _state.MouseWheel = 0f;
                _pendingMouseWheel += state.MouseWheel;
                _hasPointerSample = true;

                if (keyBitsChanged)
                {
                    EnqueueFrameTransition();
                }
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
            int buttonMask = BinaryPrimitives.ReadInt32LittleEndian(msg.Slice(InputProtocol.PointerButtonMaskOffset, 4));
            float x = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerXOffset, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerYOffset, 4));
            float deltaX = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerDeltaXOffset, 4));
            float deltaY = BinaryPrimitives.ReadSingleLittleEndian(msg.Slice(InputProtocol.PointerDeltaYOffset, 4));

            lock (_lock)
            {
                _state.ButtonMask = buttonMask;
                _state.MouseX = x;
                _state.MouseY = y;
                _hasPointerSample = true;

                if (action is PointerAction.Down or PointerAction.Up or PointerAction.Cancel)
                {
                    EnqueueFrameTransition();
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

        public void AdvanceFrameInput()
        {
            lock (_lock)
            {
                if (_frameTransitions.Count > 0)
                {
                    var transition = _frameTransitions.Dequeue();
                    _frameState.ButtonMask = transition.ButtonMask;
                    _frameState.KeyBits = transition.KeyBits;
                    _frameState.MouseX = transition.MouseX;
                    _frameState.MouseY = transition.MouseY;
                    if (_frameTransitions.Count == 0)
                    {
                        _hasQueuedTransition = false;
                    }
                }
                else
                {
                    _frameState.ButtonMask = _state.ButtonMask;
                    _frameState.KeyBits = _state.KeyBits;
                    _frameState.MouseX = _state.MouseX;
                    _frameState.MouseY = _state.MouseY;
                }

                _frameState.MouseWheel = _pendingMouseWheel;
                _pendingMouseWheel = 0f;
                _frameStateInitialized = true;
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
                if (!_frameStateInitialized)
                {
                    _frameState.MouseX = _state.MouseX;
                    _frameState.MouseY = _state.MouseY;
                }
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
                return SnapshotState().MouseWheel;
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
                    return (SnapshotState().KeyBits & (1UL << bitIndex)) != 0;
                }

                return false;
            }

            if (devicePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
            {
                string btnName = devicePath.Substring(8).ToUpperInvariant();
                WireInputState state = SnapshotState();
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
            WireInputState state = SnapshotState();
            return new Vector2(state.MouseX, state.MouseY);
        }

        public float GetMouseWheel() => SnapshotState().MouseWheel;

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;

        private void EnqueueFrameTransition()
        {
            var transition = new QueuedFrameState
            {
                ButtonMask = _state.ButtonMask,
                KeyBits = _state.KeyBits,
                MouseX = _state.MouseX,
                MouseY = _state.MouseY,
            };

            if (_hasQueuedTransition &&
                _lastQueuedTransition.ButtonMask == transition.ButtonMask &&
                _lastQueuedTransition.KeyBits == transition.KeyBits &&
                Math.Abs(_lastQueuedTransition.MouseX - transition.MouseX) < 0.001f &&
                Math.Abs(_lastQueuedTransition.MouseY - transition.MouseY) < 0.001f)
            {
                return;
            }

            _frameTransitions.Enqueue(transition);
            _lastQueuedTransition = transition;
            _hasQueuedTransition = true;
        }

        private WireInputState SnapshotState()
        {
            lock (_lock)
            {
                if (!_frameStateInitialized)
                {
                    return new WireInputState
                    {
                        ButtonMask = _state.ButtonMask,
                        MouseX = _state.MouseX,
                        MouseY = _state.MouseY,
                        MouseWheel = _pendingMouseWheel,
                        KeyBits = _state.KeyBits,
                    };
                }

                return _frameState;
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

        private struct QueuedFrameState
        {
            public int ButtonMask;
            public ulong KeyBits;
            public float MouseX;
            public float MouseY;
        }
    }
}
