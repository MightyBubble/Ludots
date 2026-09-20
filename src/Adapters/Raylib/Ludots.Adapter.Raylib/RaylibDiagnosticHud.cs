using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// 轻量原生诊断 HUD（点阵字形、零资产依赖）：LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD 开关驱动的
    /// FPS/帧耗时/车道计数只读面板。绘制发生在覆盖层合成之后、EndDrawing 之前（#1325 自宿主拆出）。
    /// </summary>
    internal static class RaylibDiagnosticHud
    {
        public static void Draw(GameEngine engine, PresentationTimingDiagnostics? timing)
        {
            DrawLightweightDiagnosticHud(engine, timing);
        }

        private static void DrawLightweightDiagnosticHud(GameEngine engine, PresentationTimingDiagnostics? timing)
        {
            if (timing == null)
            {
                return;
            }

            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Ludots.Core.Gameplay.GAS.EffectRequestQueue? effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue);
            float frameMs = timing.LastWallFrameMs > 0.001f
                ? timing.LastWallFrameMs
                : (timing.WallFrameMs > 0.001f ? timing.WallFrameMs : timing.LastFrameMs);
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            string line1 = $"FPS {FormatFixed(fps, 4, 0)}  FRAME {FormatFixed(frameMs, 5, 1)}MS  TICK {FormatFixed(timing.LastTotalTickMs, 5, 1)}MS";
            string line2 = $"ISM {FormatFixed(timing.PrimitiveInstancesLastFrame, 6)}  FIELD {FormatFixed(timing.GlobalFieldTexturesLastFrame, 4)}/{FormatFixed(timing.GlobalFieldDirtyUploadsLastFrame, 4)}  3D {FormatFixed(timing.LastMode3DMs, 5, 1)}MS";
            string line3 = $"HUD {FormatFixed(timing.WorldHudProjectedLastFrame, 6)}/{FormatFixed(worldHud?.Count ?? 0, 6)}  BAR {FormatFixed(screenHud?.BarCount ?? 0, 6)}  TEXT {FormatFixed(screenHud?.TextCount ?? 0, 6)}";
            string line4 = $"SKIA {FormatFixed(timing.LastScreenOverlayPaintMs, 5, 1)}MS  EMIT {FormatFixed(timing.LastPresenterEmitMs, 5, 1)}MS  BEHAV {FormatFixed(timing.LastPresenterBehaviorMs, 5, 1)}MS";
            string line5 = $"FXQ {FormatFixed(effectRequests?.Count ?? 0, 6)}  OVF {FormatFixed(effectRequests?.OverflowCount ?? 0, 6)}  AVL {FormatFixed(effectRequests?.AvailableCapacity ?? 0, 6)}";

            const int x = 10;
            const int y = 10;
            const int fontSize = 20;
            const int lineHeight = 25;
            const int panelWidth = 720;
            const int panelHeight = 137;
            var background = new Color(0, 0, 0, 238);
            var border = new Color(80, 255, 150, 255);
            Rl.DrawRectangle(x - 8, y - 8, panelWidth, panelHeight, background);
            Rl.DrawRectangleLines(x - 8, y - 8, panelWidth, panelHeight, border);
            DrawDiagnosticText(line1, x, y, fontSize, new Color(215, 255, 220, 255));
            DrawDiagnosticText(line2, x, y + lineHeight, fontSize, new Color(220, 240, 255, 255));
            DrawDiagnosticText(line3, x, y + lineHeight * 2, fontSize, new Color(255, 245, 185, 255));
            DrawDiagnosticText(line4, x, y + lineHeight * 3, fontSize, new Color(245, 210, 255, 255));
            DrawDiagnosticText(line5, x, y + lineHeight * 4, fontSize, new Color(255, 215, 180, 255));
        }

        private static string FormatFixed(float value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(double value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(int value, int width)
        {
            string text = value.ToString();
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static void DrawDiagnosticText(string text, int x, int y, int fontSize, Color color)
        {
            _ = fontSize;
            DrawBitmapText(text, x + 2, y + 2, 2, new Color(0, 0, 0, 255));
            DrawBitmapText(text, x, y, 2, color);
        }

        private static void DrawBitmapText(string text, int x, int y, int scale, Color color)
        {
            int cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (c == ' ')
                {
                    cursor += 4 * scale;
                    continue;
                }

                ulong glyph = GetDiagnosticGlyph(c);
                for (int row = 0; row < 7; row++)
                {
                    int bits = (int)((glyph >> ((6 - row) * 5)) & 0b11111UL);
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0)
                        {
                            continue;
                        }

                        Rl.DrawRectangle(cursor + col * scale, y + row * scale, scale, scale, color);
                    }
                }

                cursor += 6 * scale;
            }
        }

        private static ulong PackDiagnosticGlyph(int r0, int r1, int r2, int r3, int r4, int r5, int r6)
        {
            return (((ulong)r0 & 0b11111UL) << 30) |
                   (((ulong)r1 & 0b11111UL) << 25) |
                   (((ulong)r2 & 0b11111UL) << 20) |
                   (((ulong)r3 & 0b11111UL) << 15) |
                   (((ulong)r4 & 0b11111UL) << 10) |
                   (((ulong)r5 & 0b11111UL) << 5) |
                   ((ulong)r6 & 0b11111UL);
        }

        private static ulong GetDiagnosticGlyph(char c)
        {
            return c switch
            {
                'A' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'B' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
                'C' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111),
                'D' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110),
                'E' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
                'F' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
                'G' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111),
                'H' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'I' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111),
                'K' => PackDiagnosticGlyph(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
                'L' => PackDiagnosticGlyph(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
                'M' => PackDiagnosticGlyph(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
                'N' => PackDiagnosticGlyph(0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001),
                'O' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'P' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
                'R' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
                'S' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
                'T' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                'U' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'V' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b01010, 0b00100),
                'W' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010),
                'X' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
                'Y' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
                '0' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
                '1' => PackDiagnosticGlyph(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                '2' => PackDiagnosticGlyph(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
                '3' => PackDiagnosticGlyph(0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110),
                '4' => PackDiagnosticGlyph(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
                '5' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110),
                '6' => PackDiagnosticGlyph(0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
                '7' => PackDiagnosticGlyph(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
                '8' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
                '9' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110),
                '.' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100),
                '/' => PackDiagnosticGlyph(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
                '-' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
                ':' => PackDiagnosticGlyph(0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000),
                '|' => PackDiagnosticGlyph(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                _ => PackDiagnosticGlyph(0b11111, 0b10001, 0b00001, 0b00110, 0b00100, 0b00000, 0b00100),
            };
        }
    }
}
