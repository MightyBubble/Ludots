using System.Numerics;
using Ludots.Core.Input.Runtime;
using Raylib_cs;

namespace Ludots.Client.Raylib.Input
{
    public class RaylibInputBackend : IInputBackend
    {
        private bool _imeEnabled = false;
        private string _charBuffer = "";

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY"))
            {
                return Raylib_cs.Raylib.GetMouseWheelMove();
            }
            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            if (_imeEnabled) return false; // Block keyboard inputs when IME is active

            var key = RaylibInputPathParser.ParseKeyboardKey(devicePath);
            if (key.HasValue)
            {
                return Raylib_cs.Raylib.IsKeyDown(key.Value);
            }

            var mouseBtn = RaylibInputPathParser.ParseMouseButton(devicePath);
            if (mouseBtn.HasValue)
            {
                return Raylib_cs.Raylib.IsMouseButtonDown(mouseBtn.Value);
            }

            return false;
        }

        public Vector2 GetMousePosition()
        {
            return Raylib_cs.Raylib.GetMousePosition();
        }

        public float GetMouseWheel()
        {
            return Raylib_cs.Raylib.GetMouseWheelMove();
        }

        public void EnableIME(bool enable)
        {
            _imeEnabled = enable;
            // Raylib doesn't have explicit OS IME window control in base API
            // but we can start collecting chars.
        }

        public void SetIMECandidatePosition(int x, int y)
        {
            // Not supported in vanilla Raylib
        }

        public string GetCharBuffer()
        {
            // Consume chars from Raylib queue
            // Note: In a real loop we might accumulate this frame's chars
            // Raylib.GetCharPressed() returns one char at a time.
            string chars = "";
            int key = Raylib_cs.Raylib.GetCharPressed();
            while (key > 0)
            {
                if (key >= 32)
                {
                    chars += (char)key;
                }
                key = Raylib_cs.Raylib.GetCharPressed();
            }
            return chars;
        }
    }
}
