using System;
using System.Numerics;
using Godot;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Client.Godot.Rendering
{
    /// <summary>
    /// Control that draws ScreenHudBatchBuffer (Bar, Text) in screen space.
    /// Add to a CanvasLayer; call SetEngine to provide the engine reference.
    /// </summary>
    public partial class GodotScreenHudDrawer : Control
    {
        private Func<GameEngine?>? _getEngine;

        public void SetEngine(Func<GameEngine?> getEngine)
        {
            _getEngine = getEngine;
        }

        public override void _Process(double delta)
        {
            QueueRedraw();
        }

        public override void _Draw()
        {
            var engine = _getEngine?.Invoke();
            if (engine == null) return;

            if (!engine.GlobalContext.TryGetValue(ContextKeys.PresentationScreenHudBuffer, out var hudObj) ||
                hudObj is not ScreenHudBatchBuffer hud)
                return;

            engine.GlobalContext.TryGetValue(ContextKeys.PresentationWorldHudStrings, out var strObj);
            var strings = strObj as WorldHudStringTable;

            var span = hud.GetSpan();
            var font = ThemeDB.FallbackFont;
            var fontSize = ThemeDB.FallbackFontSize;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                int ix = (int)item.ScreenX;
                int iy = (int)item.ScreenY;
                int iw = (int)item.Width;
                int ih = (int)item.Height;

                if (item.Kind == WorldHudItemKind.Bar)
                {
                    var bg = ToColor(item.Color0);
                    var fg = ToColor(item.Color1);

                    DrawRect(new Rect2(ix, iy, iw, ih), bg);
                    int fw = (int)(iw * item.Value0);
                    if (fw > 0)
                        DrawRect(new Rect2(ix, iy, fw, ih), fg);
                    DrawRect(new Rect2(ix, iy, iw, ih), new global::Godot.Color(0, 0, 0, 1), false, 1);
                    continue;
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    int fs = item.FontSize <= 0 ? fontSize : item.FontSize;
                    var col = ToColor(item.Color0);

                    string? text = null;
                    if (item.Id0 != 0 && strings != null)
                    {
                        text = strings.TryGet(item.Id0);
                    }
                    else
                    {
                        var mode = (WorldHudValueMode)item.Id1;
                        if (mode == WorldHudValueMode.AttributeCurrentOverBase)
                            text = $"{(int)item.Value0}/{(int)item.Value1}";
                        else if (mode == WorldHudValueMode.AttributeCurrent)
                            text = $"{(int)item.Value0}";
                        else if (mode == WorldHudValueMode.Constant)
                            text = $"{item.Value0}";
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        DrawString(font, new global::Godot.Vector2(ix, iy + fs), text, HorizontalAlignment.Left, -1, fs, col);
                    }
                }
            }
        }

        private static global::Godot.Color ToColor(System.Numerics.Vector4 c)
        {
            float r = Clamp01(c.X);
            float g = Clamp01(c.Y);
            float b = Clamp01(c.Z);
            float a = Clamp01(c.W);
            return new global::Godot.Color(r, g, b, a);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
