using System.Numerics;
using Ludots.Core.Input.Runtime;
using Raylib_cs;
using Ludots.Raylib.Render;

namespace Ludots.Client.Raylib.Input
{
    /// <summary>
    /// Hardware input backend for Raylib. When a <see cref="SyntheticInputDevice"/>
    /// is attached, virtual window-level state is overlaid onto the physical
    /// state so injected input flows through the same binding pipeline.
    /// </summary>
    public class RaylibInputBackend : IInputBackend
    {
        private readonly SyntheticInputDevice? _synthetic;
        private bool _imeEnabled = false;
        private string _charBuffer = "";

        public RaylibInputBackend(SyntheticInputDevice? synthetic = null)
        {
            _synthetic = synthetic;
            DisableImeForGameWindow();
        }

        /// <summary>
        /// A game window has no text input; on Windows the active IME would otherwise swallow
        /// letter keys (WASD arrives as composition, never as WM_KEYDOWN) while SPACE passes
        /// through — exactly the asymmetric loss reproduced by Chinese-IME players. Disassociate
        /// the IME context from the raylib window so raw keys always reach the game.
        /// </summary>
        private static void DisableImeForGameWindow()
        {
            try
            {
                IntPtr handle = Raylib_cs.Raylib.GetWindowHandle();
                if (handle != IntPtr.Zero)
                {
                    ImmAssociateContext(handle, IntPtr.Zero);
                }
            }
            catch (DllNotFoundException)
            {
                // Non-Windows platforms have no IMM32; raw keys are not IME-filtered there.
            }
        }

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIme);

        public float GetAxis(string devicePath)
        {
            if (devicePath.StartsWith("<Mouse>/ScrollY"))
            {
                return Raylib_cs.Raylib.GetMouseWheelMove() + (_synthetic?.WheelDeltaThisFrame ?? 0f);
            }
            return 0f;
        }

        public bool GetButton(string devicePath)
        {
            var mouseBtn = RaylibInputPathParser.ParseMouseButton(devicePath);
            if (mouseBtn.HasValue && _synthetic != null &&
                _synthetic.IsButtonDown(ToSyntheticButton(mouseBtn.Value)))
            {
                return true;
            }

            if (_imeEnabled) return false; // Block keyboard inputs when IME is active

            var key = RaylibInputPathParser.ParseKeyboardKey(devicePath);
            if (key.HasValue)
            {
                if (_synthetic != null && _synthetic.IsKeyDown(ToSyntheticKeyName(key.Value)))
                {
                    return true;
                }

                return Raylib_cs.Raylib.IsKeyDown(key.Value);
            }

            if (mouseBtn.HasValue)
            {
                return Raylib_cs.Raylib.IsMouseButtonDown(mouseBtn.Value);
            }

            return false;
        }

        public Vector2 GetMousePosition()
        {
            if (_synthetic != null && _synthetic.HasPointerOverride)
            {
                return _synthetic.PointerPosition;
            }

            if (!Raylib_cs.Raylib.IsWindowFocused())
            {
                // Report an invalid pointer position while the game window is unfocused
                // so edge-pan and other viewport-bound interactions do not latch.
                return new Vector2(-1f, -1f);
            }

            return Raylib_cs.Raylib.GetMousePosition();
        }

        public float GetMouseWheel()
        {
            return Raylib_cs.Raylib.GetMouseWheelMove() + (_synthetic?.WheelDeltaThisFrame ?? 0f);
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

            if (_synthetic != null && _synthetic.CharsThisFrame.Count > 0)
            {
                chars += new string(System.Linq.Enumerable.ToArray(_synthetic.CharsThisFrame));
            }

            return chars;
        }

        public static SyntheticPointerButton ToSyntheticButton(MouseButton button) => button switch
        {
            MouseButton.MOUSE_LEFT_BUTTON => SyntheticPointerButton.Left,
            MouseButton.MOUSE_RIGHT_BUTTON => SyntheticPointerButton.Right,
            _ => SyntheticPointerButton.Middle,
        };

        internal static string ToSyntheticKeyName(KeyboardKey key)
        {
            // "KEY_PAGE_UP" -> "PAGEUP" (device names are case/underscore-insensitive)
            string raw = key.ToString();
            const string prefix = "KEY_";
            if (raw.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                raw = raw.Substring(prefix.Length);
            }

            return SyntheticInputDevice.NormalizeKey(raw);
        }
    }
}
