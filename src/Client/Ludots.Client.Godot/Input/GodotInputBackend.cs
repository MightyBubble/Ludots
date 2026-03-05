using System.Numerics;
using Godot;
using Ludots.Core.Input.Runtime;

// IInputBackend uses System.Numerics.Vector2

namespace Ludots.Client.Godot.Input
{
    /// <summary>
    /// Godot implementation of IInputBackend. Must be added to scene tree to receive _Input for wheel/chars.
    /// </summary>
    public partial class GodotInputBackend : Node, IInputBackend
    {
        private bool _imeEnabled;
        private string _charBuffer = "";
        private float _accumulatedWheel;

        public override void _Input(InputEvent e)
        {
            if (e is InputEventMouseButton mb)
            {
                if (mb.ButtonIndex == global::Godot.MouseButton.WheelUp) _accumulatedWheel += 1f;
                if (mb.ButtonIndex == global::Godot.MouseButton.WheelDown) _accumulatedWheel -= 1f;
            }

            if (e is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode != global::Godot.Key.None)
            {
                var unicode = keyEvent.Unicode;
                if (unicode > 0 && unicode >= 32)
                {
                    _charBuffer += (char)unicode;
                }
            }
        }

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY", StringComparison.OrdinalIgnoreCase))
            {
                return GetMouseWheel();
            }
            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            if (_imeEnabled) return false;

            var key = GodotInputPathParser.ParseKeyboardKey(devicePath);
            if (key.HasValue)
            {
                return global::Godot.Input.IsKeyPressed(key.Value);
            }

            var mouseBtn = GodotInputPathParser.ParseMouseButton(devicePath);
            if (mouseBtn.HasValue)
            {
                return global::Godot.Input.IsMouseButtonPressed(mouseBtn.Value);
            }

            return false;
        }

        public System.Numerics.Vector2 GetMousePosition()
        {
            var vp = GetViewport();
            var v = vp != null ? vp.GetMousePosition() : global::Godot.DisplayServer.MouseGetPosition();
            return new System.Numerics.Vector2((float)v.X, (float)v.Y);
        }

        public float GetMouseWheel()
        {
            var v = _accumulatedWheel;
            _accumulatedWheel = 0f;
            return v;
        }

        public void EnableIME(bool enable)
        {
            _imeEnabled = enable;
        }

        public void SetIMECandidatePosition(int x, int y)
        {
            // Godot IME handled by engine
        }

        public string GetCharBuffer()
        {
            var s = _charBuffer;
            _charBuffer = "";
            return s;
        }
    }
}
