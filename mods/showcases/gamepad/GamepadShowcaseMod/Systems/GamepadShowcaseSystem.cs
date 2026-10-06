using System;
using System.Numerics;
using Arch.System;
using GamepadShowcaseMod.Input;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace GamepadShowcaseMod.Systems
{
    /// <summary>
    /// Live gamepad visualizer + toy: sticks, triggers and buttons render as
    /// widgets every frame; A fires an expanding pulse ring, B cycles the
    /// accent color, Start/F6 hides the panel. Shared Default_Gameplay actions
    /// (Move / Look / Zoom) are displayed next to the raw widgets so players
    /// can see both the device state and the processed pipeline value.
    /// </summary>
    public sealed class GamepadShowcaseSystem : ISystem<float>
    {
        private const float PulseDurationSeconds = 0.6f;
        private const int StickBoxSize = 92;
        private const int StickDotSize = 12;

        private static readonly Vector4 PanelBg = new(0f, 0f, 0f, 0.62f);
        private static readonly Vector4 PanelBorder = new(0.6f, 0.75f, 1f, 0.5f);
        private static readonly Vector4 TextDim = new(0.62f, 0.72f, 0.85f, 1f);
        private static readonly Vector4 TextBright = new(0.92f, 0.96f, 1f, 1f);
        private static readonly Vector4[] Accents =
        {
            new(0.30f, 0.90f, 1.00f, 1f),
            new(0.35f, 1.00f, 0.55f, 1f),
            new(1.00f, 0.90f, 0.30f, 1f),
            new(1.00f, 0.45f, 0.90f, 1f),
        };

        private readonly GameEngine _engine;
        private PlayerInputHandler? _input;
        private bool _hudVisible = true;
        private int _accentIndex;
        private float _pulseElapsed = float.MaxValue;
        private int _pulseCount;

        public GamepadShowcaseSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }

        public void Update(in float t)
        {
            ResolveInput();
            if (_input == null) return;

            if (_input.PressedThisFrame(GamepadShowcaseInputActions.ToggleHud))
            {
                _hudVisible = !_hudVisible;
            }

            if (_input.PressedThisFrame(GamepadShowcaseInputActions.CycleColor))
            {
                _accentIndex = (_accentIndex + 1) % Accents.Length;
            }

            if (_input.PressedThisFrame(GamepadShowcaseInputActions.Pulse))
            {
                _pulseElapsed = 0f;
                _pulseCount++;
            }

            if (!_hudVisible) return;

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var obj) ||
                obj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            Render(overlay, t);
        }

        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        private void ResolveInput()
        {
            if (_input != null) return;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                _input = input;
            }
        }

        private void Render(ScreenOverlayBuffer overlay, float dt)
        {
            Vector4 accent = Accents[_accentIndex];
            int width = 560;
            int x = 16;
            int y = ResolveViewportHeight() - 268;

            overlay.AddRect(x, y, width, 252, PanelBg, PanelBorder);
            overlay.AddText(x + 12, y + 10, "[GAMEPAD SHOWCASE]", 16, accent);
            overlay.AddText(x + 232, y + 12, "Start/F6 hide | A pulse | B color", 13, TextDim);

            Vector2 leftStick = _input!.ReadAction<Vector2>("Move");
            Vector2 rightStick = _input.ReadAction<Vector2>("Look");
            float leftTrigger = _input.ReadAction<float>(GamepadShowcaseInputActions.LeftTrigger);
            float rightTrigger = _input.ReadAction<float>(GamepadShowcaseInputActions.RightTrigger);

            DrawStickWidget(overlay, x + 24, y + 48, "L-stick (Move)", leftStick, accent);
            DrawStickWidget(overlay, x + 168, y + 48, "R-stick (Look)", rightStick, accent);

            DrawTriggerBar(overlay, x + 312, y + 48, "LT", leftTrigger, accent);
            DrawTriggerBar(overlay, x + 312, y + 90, "RT", rightTrigger, accent);

            float zoom = _input.ReadAction<float>("Zoom");
            overlay.AddText(x + 312, y + 132, $"Zoom (RT-LT): {zoom,5:F2}", 13, TextBright);

            DrawButtonGrid(overlay, x + 24, y + 158, accent);

            StepPulse(overlay, dt, accent);

            overlay.AddText(x + 312, y + 158, $"Pulses: {_pulseCount}", 13, TextBright);
            overlay.AddText(x + 312, y + 178, "Camera: L-stick pan / R-stick look", 12, TextDim);
            overlay.AddText(x + 312, y + 196, "         LT/RT zoom / dpad rotate", 12, TextDim);
            overlay.AddText(x + 312, y + 214, "         LB queue / RB precise", 12, TextDim);
        }

        private void DrawStickWidget(ScreenOverlayBuffer overlay, int x, int y, string label, Vector2 value, Vector4 accent)
        {
            overlay.AddText(x, y - 4, label, 12, TextDim);
            int boxY = y + 12;
            overlay.AddRect(x, boxY, StickBoxSize, StickBoxSize, new Vector4(0.08f, 0.10f, 0.14f, 0.8f), new Vector4(0.5f, 0.6f, 0.8f, 0.4f));

            int cx = x + StickBoxSize / 2;
            int cy = boxY + StickBoxSize / 2;
            int half = StickBoxSize / 2 - StickDotSize / 2 - 2;
            overlay.AddLine(cx, boxY + 4, cx, boxY + StickBoxSize - 4, 1, new Vector4(0.4f, 0.5f, 0.65f, 0.5f));
            overlay.AddLine(x + 4, cy, x + StickBoxSize - 4, cy, 1, new Vector4(0.4f, 0.5f, 0.65f, 0.5f));

            int dotX = cx + (int)(Math.Clamp(value.X, -1f, 1f) * half) - StickDotSize / 2;
            int dotY = cy - (int)(Math.Clamp(value.Y, -1f, 1f) * half) - StickDotSize / 2;
            overlay.AddRect(dotX, dotY, StickDotSize, StickDotSize, accent, accent);

            overlay.AddText(x, boxY + StickBoxSize + 6, $"({value.X,5:F2}, {value.Y,5:F2})", 12, TextBright);
        }

        private void DrawTriggerBar(ScreenOverlayBuffer overlay, int x, int y, string label, float value, Vector4 accent)
        {
            overlay.AddText(x, y, label, 12, TextDim);
            int barX = x + 26;
            int barWidth = 200;
            int barHeight = 14;
            overlay.AddRect(barX, y - 1, barWidth, barHeight, new Vector4(0.08f, 0.10f, 0.14f, 0.8f), new Vector4(0.5f, 0.6f, 0.8f, 0.4f));
            int fillWidth = (int)(Math.Clamp(value, 0f, 1f) * (barWidth - 4));
            if (fillWidth > 0)
            {
                overlay.AddRect(barX + 2, y + 1, fillWidth, barHeight - 4, accent, accent);
            }

            overlay.AddText(barX + barWidth + 8, y, $"{value,4:F2}", 12, TextBright);
        }

        private void DrawButtonGrid(ScreenOverlayBuffer overlay, int x, int y, Vector4 accent)
        {
            // Face diamond:      Y
            //                  X   B
            //                    A(pulse)
            Button("A", x + 76, y + 46, _input!.IsDown(GamepadShowcaseInputActions.Pulse));
            Button("B", x + 116, y + 26, _input.IsDown(GamepadShowcaseInputActions.CycleColor));
            Button("X", x + 36, y + 26, _input.IsDown(GamepadShowcaseInputActions.West));
            Button("Y", x + 76, y + 6, _input.IsDown(GamepadShowcaseInputActions.North));

            // Dpad cross
            Button("^", x + 6, y + 6, _input.IsDown(GamepadShowcaseInputActions.DpadUp));
            Button("v", x + 6, y + 46, _input.IsDown(GamepadShowcaseInputActions.DpadDown));
            Button("<", x - 14, y + 26, _input.IsDown(GamepadShowcaseInputActions.DpadLeft));
            Button(">", x + 26, y + 26, _input.IsDown(GamepadShowcaseInputActions.DpadRight));

            // Shoulders / stick press / select as a labeled row
            overlay.AddText(x + 160, y + 6, RowMarker("LB", _input.IsDown(GamepadShowcaseInputActions.LeftShoulder)), 13, MarkerColor(_input.IsDown(GamepadShowcaseInputActions.LeftShoulder), accent));
            overlay.AddText(x + 160, y + 26, RowMarker("RB", _input.IsDown(GamepadShowcaseInputActions.RightShoulder)), 13, MarkerColor(_input.IsDown(GamepadShowcaseInputActions.RightShoulder), accent));
            overlay.AddText(x + 160, y + 46, RowMarker("L3", _input.IsDown(GamepadShowcaseInputActions.LeftStickPress)), 13, MarkerColor(_input.IsDown(GamepadShowcaseInputActions.LeftStickPress), accent));
            overlay.AddText(x + 160, y + 66, RowMarker("R3", _input.IsDown(GamepadShowcaseInputActions.RightStickPress)), 13, MarkerColor(_input.IsDown(GamepadShowcaseInputActions.RightStickPress), accent));
            overlay.AddText(x + 220, y + 6, RowMarker("Sel", _input.IsDown(GamepadShowcaseInputActions.Select)), 13, MarkerColor(_input.IsDown(GamepadShowcaseInputActions.Select), accent));

            void Button(string label, int bx, int by, bool down)
            {
                var fill = down ? accent : new Vector4(0.12f, 0.15f, 0.2f, 0.85f);
                overlay.AddRect(bx, by, 26, 26, fill, down ? accent : new Vector4(0.5f, 0.6f, 0.8f, 0.45f));
                overlay.AddText(bx + 8, by + 6, label, 12, down ? new Vector4(0f, 0f, 0f, 1f) : TextDim);
            }
        }

        private static string RowMarker(string label, bool down) => down ? $"[{label}]*" : $"[{label}] ";

        private static Vector4 MarkerColor(bool down, Vector4 accent) => down ? accent : TextDim;

        private void StepPulse(ScreenOverlayBuffer overlay, float dt, Vector4 accent)
        {
            if (_pulseElapsed >= PulseDurationSeconds)
            {
                return;
            }

            _pulseElapsed += dt;
            float k = Math.Clamp(_pulseElapsed / PulseDurationSeconds, 0f, 1f);
            float radius = 24f + k * 260f;
            var color = new Vector4(accent.X, accent.Y, accent.Z, 1f - k);

            (float cx, float cy) = ResolveViewportCenter();
            const int segments = 24;
            float prevX = cx + MathF.Cos(0f) * radius;
            float prevY = cy + MathF.Sin(0f) * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * (MathF.PI * 2f / segments);
                float px = cx + MathF.Cos(angle) * radius;
                float py = cy + MathF.Sin(angle) * radius;
                overlay.AddLine((int)prevX, (int)prevY, (int)px, (int)py, 3, color);
                prevX = px;
                prevY = py;
            }
        }

        private (float, float) ResolveViewportCenter()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.ViewController.Name, out var viewObj) &&
                viewObj is IViewController view)
            {
                return (view.Resolution.X * 0.5f, view.Resolution.Y * 0.5f);
            }

            return (640f, 360f);
        }

        private int ResolveViewportHeight()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.ViewController.Name, out var viewObj) &&
                viewObj is IViewController view)
            {
                return Math.Max(320, (int)view.Resolution.Y);
            }

            return 720;
        }
    }
}
