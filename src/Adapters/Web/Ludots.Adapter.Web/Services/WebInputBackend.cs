using System;
using System.Numerics;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Input.Runtime;

namespace Ludots.Adapter.Web.Services
{
    /// <summary>
    /// Thread-safe input state fed by WebSocket binary messages.
    /// The WebTransportLayer writes from a receive thread; GameEngine reads from the game thread.
    /// </summary>
    public sealed class WebInputBackend : IInputBackend
    {
        private readonly object _lock = new();
        private WireInputState _state;

        public void ApplyMessage(ReadOnlySpan<byte> msg)
        {
            if (msg.Length < InputProtocol.InputMessageSize || msg[0] != InputProtocol.MsgTypeInput)
                return;

            var s = new WireInputState
            {
                ButtonMask = BitConverter.ToInt32(msg.Slice(1, 4)),
                MouseX = BitConverter.ToSingle(msg.Slice(5, 4)),
                MouseY = BitConverter.ToSingle(msg.Slice(9, 4)),
                MouseWheel = BitConverter.ToSingle(msg.Slice(13, 4)),
                KeyBits = BitConverter.ToUInt64(msg.Slice(17, 8)),
            };

            lock (_lock)
            {
                _state = s;
            }
        }

        private WireInputState SnapshotState()
        {
            lock (_lock) return _state;
        }

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY", StringComparison.OrdinalIgnoreCase))
                return SnapshotState().MouseWheel;
            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            if (devicePath.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
            {
                int bitIndex = KeyPathToBitIndex(devicePath.Substring(11));
                if (bitIndex >= 0)
                    return (SnapshotState().KeyBits & (1UL << bitIndex)) != 0;
                return false;
            }

            if (devicePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
            {
                string btnName = devicePath.Substring(8).ToUpperInvariant();
                var state = SnapshotState();
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
            var s = SnapshotState();
            return new Vector2(s.MouseX, s.MouseY);
        }

        public float GetMouseWheel() => SnapshotState().MouseWheel;

        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        private static int KeyPathToBitIndex(string keyName)
        {
            if (keyName.Length == 1)
            {
                char c = char.ToUpperInvariant(keyName[0]);
                if (c >= 'A' && c <= 'Z') return c - 'A';       // bits 0-25
                if (c >= '0' && c <= '9') return 26 + (c - '0'); // bits 26-35
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
