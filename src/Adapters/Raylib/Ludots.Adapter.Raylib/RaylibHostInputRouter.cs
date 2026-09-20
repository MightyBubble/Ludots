using System;
using Ludots.Client.Raylib.Input;
using Ludots.Core.Input.Runtime;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// 宿主输入路由：真实鼠标/键盘与 synthetic 回放经同一输入合同进入 UI，输出捕获语义
    /// （UiCaptured/UiWheelCaptured）供宿主决定世界指针拦截。frameIndex 与诊断路径由宿主显式传入，
    /// 回放时序与真实输入共享同一基准（#1324 自 RaylibHostLoop 拆出，无 static 可变状态）。
    /// </summary>
    internal sealed class RaylibHostInputRouter
    {
        private bool _uiPointerCaptured;
        private PointerButton? _uiCapturedPointerButton;
        private bool _hasLastUiPointerMove;
        private float _lastUiPointerMoveX;
        private float _lastUiPointerMoveY;


        internal UiInputFrameResult UpdateInput(UIRoot uiRoot, SyntheticUiPlayback syntheticUiPlayback, int frameIndex, string? diagnosticPath, SyntheticInputDevice? syntheticInput)
        {
            if (syntheticUiPlayback.Enabled &&
                HandleSyntheticUiPlayback(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath) is { Handled: true } syntheticResult)
            {
                ForwardKeyboardInput(uiRoot, syntheticInput);
                return syntheticResult;
            }

            var mousePos = syntheticInput is { HasPointerOverride: true } ? syntheticInput.PointerPosition : Rl.GetMousePosition();
            bool windowFocused = Rl.IsWindowFocused() || syntheticInput is { HasPointerOverride: true };
            float mouseWheel = Rl.GetMouseWheelMove() + (syntheticInput?.WheelDeltaThisFrame ?? 0f);
            UiNode? hitNode = _uiPointerCaptured ? null : uiRoot.Scene?.HitTest(mousePos.X, mousePos.Y);
            bool hitInteractiveUi = !_uiPointerCaptured && IsInteractiveUiNode(hitNode);
            bool uiWheelCaptured = false;
            bool uiInputHandled = false;

            if (_uiPointerCaptured)
            {
                bool capturedButtonDown = _uiCapturedPointerButton.HasValue &&
                    (Rl.IsMouseButtonDown(ToMouseButton(_uiCapturedPointerButton.Value)) ||
                     (syntheticInput?.IsButtonDown(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false));
                bool capturedButtonReleased = _uiCapturedPointerButton.HasValue &&
                    (Rl.IsMouseButtonReleased(ToMouseButton(_uiCapturedPointerButton.Value)) ||
                     (syntheticInput?.WasButtonReleasedThisFrame(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false));

                if (!windowFocused || (!_uiCapturedPointerButton.HasValue && !capturedButtonDown && !capturedButtonReleased) || capturedButtonReleased)
                {
                    if (windowFocused && _uiCapturedPointerButton.HasValue && capturedButtonReleased)
                    {
                        uiInputHandled |= uiRoot.HandleInput(new PointerEvent
                        {
                            DeviceType = InputDeviceType.Mouse,
                            PointerId = 0,
                            Action = PointerAction.Up,
                            Button = _uiCapturedPointerButton.Value,
                            X = mousePos.X,
                            Y = mousePos.Y
                        });
                    }

                    _uiPointerCaptured = false;
                    _uiCapturedPointerButton = null;
                    ResetUiPointerMoveCache();
                }
            }

            if ((_uiPointerCaptured || hitInteractiveUi) && ShouldForwardUiPointerMove(mousePos.X, mousePos.Y))
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = _uiCapturedPointerButton,
                    X = mousePos.X,
                    Y = mousePos.Y
                });
            }

            if ((_uiPointerCaptured || hitInteractiveUi) && Math.Abs(mouseWheel) > float.Epsilon)
            {
                uiWheelCaptured = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Scroll,
                    X = mousePos.X,
                    Y = mousePos.Y,
                    DeltaX = 0f,
                    DeltaY = -mouseWheel * 120f
                });
            }

            bool shouldRouteMouseDownToUi = hitInteractiveUi || uiRoot.HasFocusedCanvas || _uiPointerCaptured;
            foreach (MouseButton mouseButton in MouseButtonsInPriorityOrder)
            {
                bool syntheticPressed = syntheticInput?.WasButtonPressedThisFrame(RaylibInputBackend.ToSyntheticButton(mouseButton)) ?? false;
                if (!Rl.IsMouseButtonPressed(mouseButton) && !syntheticPressed)
                {
                    continue;
                }

                PointerButton pointerButton = ToPointerButton(mouseButton);
                if (!shouldRouteMouseDownToUi)
                {
                    continue;
                }

                bool handled = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Down,
                    Button = pointerButton,
                    X = mousePos.X,
                    Y = mousePos.Y
                });

                uiInputHandled |= handled;
                if (handled)
                {
                    _uiPointerCaptured = true;
                    _uiCapturedPointerButton = pointerButton;
                    ResetUiPointerMoveCache();
                }
            }

            // Same-frame synthetic releases (e.g. Click) arrive after the capture
            // check above; without this the UI capture would latch forever.
            if (_uiPointerCaptured && _uiCapturedPointerButton.HasValue &&
                (syntheticInput?.WasButtonReleasedThisFrame(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false))
            {
                uiInputHandled |= uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Up,
                    Button = _uiCapturedPointerButton.Value,
                    X = mousePos.X,
                    Y = mousePos.Y
                });
                _uiPointerCaptured = false;
                _uiCapturedPointerButton = null;
                ResetUiPointerMoveCache();
            }

            ForwardKeyboardInput(uiRoot, syntheticInput);
            return new UiInputFrameResult(Handled: uiInputHandled, PointerCaptured: _uiPointerCaptured, WheelCaptured: uiWheelCaptured);
        }


        private static void ForwardKeyboardInput(UIRoot uiRoot, SyntheticInputDevice? syntheticInput = null)
        {
            if (!uiRoot.HasFocusedCanvas)
            {
                DrainCharQueue();
                return;
            }

            int modifiers = ReadBrowserInputModifiers();
            foreach (KeyboardKey key in BrowserForwardedKeys)
            {
                if (Rl.IsKeyPressed(key))
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Down,
                        Key = MapKeyboardKey(key),
                        Code = key.ToString(),
                        Modifiers = modifiers
                    });
                }

                if (Rl.IsKeyReleased(key))
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Up,
                        Key = MapKeyboardKey(key),
                        Code = key.ToString(),
                        Modifiers = modifiers
                    });
                }
            }

            if (syntheticInput != null)
            {
                foreach (string key in syntheticInput.KeysDownSnapshotPressedThisFrame())
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Down,
                        Key = key,
                        Code = key,
                        Modifiers = modifiers
                    });
                }

                foreach (string key in syntheticInput.KeysReleasedThisFrameSnapshot())
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Up,
                        Key = key,
                        Code = key,
                        Modifiers = modifiers
                    });
                }
            }

            while (true)
            {
                int codePoint = Rl.GetCharPressed();
                if (codePoint == 0)
                {
                    break;
                }

                string text = char.ConvertFromUtf32(codePoint);
                uiRoot.HandleInput(new KeyboardEvent
                {
                    DeviceType = InputDeviceType.Keyboard,
                    Action = KeyboardAction.Character,
                    Key = text,
                    Text = text,
                    Modifiers = modifiers
                });
            }

            if (syntheticInput != null)
            {
                foreach (char c in syntheticInput.CharsThisFrame)
                {
                    string text = c.ToString();
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Character,
                        Key = text,
                        Text = text,
                        Modifiers = modifiers
                    });
                }
            }
        }


        private UiInputFrameResult HandleSyntheticUiPlayback(UIRoot uiRoot, SyntheticUiPlayback playback, int frameIndex, string? diagnosticPath)
        {
            if (frameIndex < playback.StartFrame)
            {
                return default;
            }

            if (frameIndex == playback.StartFrame)
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    X = playback.StartX,
                    Y = playback.StartY
                });
                _uiPointerCaptured = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Down,
                    Button = PointerButton.Left,
                    X = playback.StartX,
                    Y = playback.StartY
                });
                RaylibAdapterEnv.AppendDiagnostic(diagnosticPath, $"synthetic-ui down frame={frameIndex} x={playback.StartX:F1} y={playback.StartY:F1} captured={_uiPointerCaptured}");
                return new UiInputFrameResult(Handled: true, PointerCaptured: _uiPointerCaptured, WheelCaptured: false);
            }

            if (frameIndex > playback.StartFrame && frameIndex < playback.EndFrame)
            {
                (float x, float y) = InterpolateSyntheticPointer(playback, frameIndex);
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = PointerButton.Left,
                    X = x,
                    Y = y
                });

                bool uiWheelCaptured = false;
                if (playback.ScrollFrame == frameIndex && Math.Abs(playback.ScrollDeltaY) > float.Epsilon)
                {
                    uiWheelCaptured = uiRoot.HandleInput(new PointerEvent
                    {
                        DeviceType = InputDeviceType.Mouse,
                        PointerId = 0,
                        Action = PointerAction.Scroll,
                        X = x,
                        Y = y,
                        DeltaX = 0f,
                        DeltaY = playback.ScrollDeltaY
                    });
                    RaylibAdapterEnv.AppendDiagnostic(diagnosticPath, $"synthetic-ui scroll frame={frameIndex} x={x:F1} y={y:F1} deltaY={playback.ScrollDeltaY:F1}");
                }

                if (playback.KeyFrame == frameIndex)
                {
                    if (!string.IsNullOrWhiteSpace(playback.Key))
                    {
                        RaylibSyntheticKeyboardInput.SendKeyStroke(uiRoot, playback.Key);
                    }

                    if (!string.IsNullOrEmpty(playback.KeyText))
                    {
                        RaylibSyntheticKeyboardInput.SendTextInput(uiRoot, playback.KeyText);
                    }

                    RaylibAdapterEnv.AppendDiagnostic(diagnosticPath, $"synthetic-ui key frame={frameIndex} key={playback.Key} textLength={playback.KeyText.Length}");
                }

                return new UiInputFrameResult(Handled: true, PointerCaptured: _uiPointerCaptured, WheelCaptured: uiWheelCaptured);
            }

            if (frameIndex == playback.EndFrame)
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = PointerButton.Left,
                    X = playback.EndX,
                    Y = playback.EndY
                });
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Up,
                    Button = PointerButton.Left,
                    X = playback.EndX,
                    Y = playback.EndY
                });
                RaylibAdapterEnv.AppendDiagnostic(diagnosticPath, $"synthetic-ui up frame={frameIndex} x={playback.EndX:F1} y={playback.EndY:F1}");
                _uiPointerCaptured = false;
                return new UiInputFrameResult(Handled: true, PointerCaptured: false, WheelCaptured: false);
            }

            return default;
        }


        private static (float X, float Y) InterpolateSyntheticPointer(SyntheticUiPlayback playback, int frameIndex)
        {
            int moveFrames = Math.Max(1, playback.EndFrame - playback.StartFrame);
            float progress = Math.Clamp((frameIndex - playback.StartFrame) / (float)moveFrames, 0f, 1f);
            float x = playback.StartX + ((playback.EndX - playback.StartX) * progress);
            float y = playback.StartY + ((playback.EndY - playback.StartY) * progress);
            return (x, y);
        }


        private static bool IsInteractiveUiNode(UiNode? node)
        {
            for (UiNode? current = node; current != null; current = current.Parent)
            {
                if (current.ActionHandles.Count > 0)
                {
                    return true;
                }

                if (current.CanvasContent is Ludots.UI.Runtime.IUiCanvasInputSink)
                {
                    return true;
                }

                if (current.Style.Overflow == UiOverflow.Scroll)
                {
                    return true;
                }
            }

            return false;
        }


        private bool ShouldForwardUiPointerMove(float x, float y)
        {
            if (!_hasLastUiPointerMove ||
                Math.Abs(_lastUiPointerMoveX - x) > 0.01f ||
                Math.Abs(_lastUiPointerMoveY - y) > 0.01f)
            {
                _hasLastUiPointerMove = true;
                _lastUiPointerMoveX = x;
                _lastUiPointerMoveY = y;
                return true;
            }

            return false;
        }


        private void ResetUiPointerMoveCache()
        {
            _hasLastUiPointerMove = false;
            _lastUiPointerMoveX = 0f;
            _lastUiPointerMoveY = 0f;
        }


        private static MouseButton ToMouseButton(PointerButton button)
        {
            return button switch
            {
                PointerButton.Left => MouseButton.MOUSE_LEFT_BUTTON,
                PointerButton.Middle => MouseButton.MOUSE_MIDDLE_BUTTON,
                PointerButton.Right => MouseButton.MOUSE_RIGHT_BUTTON,
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported pointer button.")
            };
        }


        private static PointerButton ToPointerButton(MouseButton button)
        {
            return button switch
            {
                MouseButton.MOUSE_LEFT_BUTTON => PointerButton.Left,
                MouseButton.MOUSE_MIDDLE_BUTTON => PointerButton.Middle,
                MouseButton.MOUSE_RIGHT_BUTTON => PointerButton.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
            };
        }


        private static void DrainCharQueue()
        {
            while (Rl.GetCharPressed() != 0)
            {
            }
        }


        private static int ReadBrowserInputModifiers()
        {
            BrowserInputModifiers modifiers = BrowserInputModifiers.None;
            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT))
            {
                modifiers |= BrowserInputModifiers.Shift;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_CONTROL))
            {
                modifiers |= BrowserInputModifiers.Control;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_ALT) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_ALT))
            {
                modifiers |= BrowserInputModifiers.Alt;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_SUPER) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_SUPER))
            {
                modifiers |= BrowserInputModifiers.Meta;
            }

            return (int)modifiers;
        }


        private static string MapKeyboardKey(KeyboardKey key)
        {
            return key switch
            {
                KeyboardKey.KEY_ENTER => "Enter",
                KeyboardKey.KEY_TAB => "Tab",
                KeyboardKey.KEY_BACKSPACE => "Backspace",
                KeyboardKey.KEY_DELETE => "Delete",
                KeyboardKey.KEY_ESCAPE => "Escape",
                KeyboardKey.KEY_LEFT => "ArrowLeft",
                KeyboardKey.KEY_RIGHT => "ArrowRight",
                KeyboardKey.KEY_UP => "ArrowUp",
                KeyboardKey.KEY_DOWN => "ArrowDown",
                KeyboardKey.KEY_HOME => "Home",
                KeyboardKey.KEY_END => "End",
                KeyboardKey.KEY_PAGE_UP => "PageUp",
                KeyboardKey.KEY_PAGE_DOWN => "PageDown",
                KeyboardKey.KEY_SPACE => "Space",
                KeyboardKey.KEY_ZERO => "0",
                KeyboardKey.KEY_ONE => "1",
                KeyboardKey.KEY_TWO => "2",
                KeyboardKey.KEY_THREE => "3",
                KeyboardKey.KEY_FOUR => "4",
                KeyboardKey.KEY_FIVE => "5",
                KeyboardKey.KEY_SIX => "6",
                KeyboardKey.KEY_SEVEN => "7",
                KeyboardKey.KEY_EIGHT => "8",
                KeyboardKey.KEY_NINE => "9",
                >= KeyboardKey.KEY_A and <= KeyboardKey.KEY_Z => key.ToString()[4..],
                _ => key.ToString()
            };
        }



        internal static SyntheticUiPlayback ReadSyntheticUiPlayback()
        {
            bool enabled = RaylibAdapterEnv.ReadEnvBoolOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_PLAYBACK", defaultValue: false);
            int startFrame = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_FRAME", 180);
            int endFrame = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_FRAME", 260);
            if (endFrame <= startFrame)
            {
                endFrame = startFrame + 1;
            }

            return new SyntheticUiPlayback
            {
                Enabled = enabled,
                StartFrame = startFrame,
                EndFrame = endFrame,
                StartX = RaylibAdapterEnv.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_X", 190f),
                StartY = RaylibAdapterEnv.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_Y", 205f),
                EndX = RaylibAdapterEnv.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_X", 310f),
                EndY = RaylibAdapterEnv.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_Y", 270f),
                ScrollFrame = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_FRAME", -1),
                ScrollDeltaY = RaylibAdapterEnv.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_DELTA_Y", 0f),
                KeyFrame = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_FRAME", -1),
                Key = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY") ?? string.Empty,
                KeyText = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_TEXT") ?? string.Empty
            };
        }



        internal static bool ShouldCaptureWorldPointer(
            bool pointerCaptured,
            bool wheelCaptured,
            bool inputHandled)
        {
            return pointerCaptured || wheelCaptured || inputHandled;
        }


        private static readonly MouseButton[] MouseButtonsInPriorityOrder =
        {
            MouseButton.MOUSE_LEFT_BUTTON,
            MouseButton.MOUSE_RIGHT_BUTTON,
            MouseButton.MOUSE_MIDDLE_BUTTON
        };
        private static readonly KeyboardKey[] BrowserForwardedKeys =
        {
            KeyboardKey.KEY_ENTER,
            KeyboardKey.KEY_TAB,
            KeyboardKey.KEY_BACKSPACE,
            KeyboardKey.KEY_DELETE,
            KeyboardKey.KEY_ESCAPE,
            KeyboardKey.KEY_LEFT,
            KeyboardKey.KEY_RIGHT,
            KeyboardKey.KEY_UP,
            KeyboardKey.KEY_DOWN,
            KeyboardKey.KEY_HOME,
            KeyboardKey.KEY_END,
            KeyboardKey.KEY_PAGE_UP,
            KeyboardKey.KEY_PAGE_DOWN,
            KeyboardKey.KEY_SPACE,
            KeyboardKey.KEY_A,
            KeyboardKey.KEY_B,
            KeyboardKey.KEY_C,
            KeyboardKey.KEY_D,
            KeyboardKey.KEY_E,
            KeyboardKey.KEY_F,
            KeyboardKey.KEY_G,
            KeyboardKey.KEY_H,
            KeyboardKey.KEY_I,
            KeyboardKey.KEY_J,
            KeyboardKey.KEY_K,
            KeyboardKey.KEY_L,
            KeyboardKey.KEY_M,
            KeyboardKey.KEY_N,
            KeyboardKey.KEY_O,
            KeyboardKey.KEY_P,
            KeyboardKey.KEY_Q,
            KeyboardKey.KEY_R,
            KeyboardKey.KEY_S,
            KeyboardKey.KEY_T,
            KeyboardKey.KEY_U,
            KeyboardKey.KEY_V,
            KeyboardKey.KEY_W,
            KeyboardKey.KEY_X,
            KeyboardKey.KEY_Y,
            KeyboardKey.KEY_Z,
            KeyboardKey.KEY_ZERO,
            KeyboardKey.KEY_ONE,
            KeyboardKey.KEY_TWO,
            KeyboardKey.KEY_THREE,
            KeyboardKey.KEY_FOUR,
            KeyboardKey.KEY_FIVE,
            KeyboardKey.KEY_SIX,
            KeyboardKey.KEY_SEVEN,
            KeyboardKey.KEY_EIGHT,
            KeyboardKey.KEY_NINE
        };
    }


    internal sealed class SyntheticUiPlayback
    {
        public bool Enabled { get; init; }

        public int StartFrame { get; init; }

        public int EndFrame { get; init; }

        public float StartX { get; init; }

        public float StartY { get; init; }

        public float EndX { get; init; }

        public float EndY { get; init; }

        public int ScrollFrame { get; init; }

        public float ScrollDeltaY { get; init; }

        public int KeyFrame { get; init; }

        public string Key { get; init; } = string.Empty;

        public string KeyText { get; init; } = string.Empty;
    }

    internal readonly record struct UiInputFrameResult(bool Handled, bool PointerCaptured, bool WheelCaptured);
}
