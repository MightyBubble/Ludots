using SkiaSharp;
using Ludots.UI.Widgets;
using Ludots.UI.HtmlEngine.Core;
using Ludots.UI.Input;
using ExCSS;
using System.Globalization;
using System.Collections.Generic;

namespace Ludots.UI.HtmlEngine
{
    public struct RenderCmd
    {
        public bool IsPushLayer;
        public bool IsPopLayer;
        public float Opacity;
        
        public SKRect Rect;
        public SKColor Color;
        public bool HasBackground;
        
        public bool HasBorder;
        public SKColor BorderColor;
        public float BorderWidth;
        public float BorderRadius;
        
        public bool IsText;
        public string Text;
        public float FontSize;
        public SKColor TextColor;
        public SKFontStyleWeight FontWeight;
        public string TextAlign;
        
        // Metadata for HitTest
        public string Tag;
        public string Id;
        public string Class;
    }

    public class HtmlWidget : Widget
    {
        private CssBox _rootBox;
        private HtmlParserService _parser;
        private string _html;
        private string _css;
        
        private bool _isLayoutDirty = true;
        private float _lastLayoutWidth = -1;
        private float _lastLayoutHeight = -1;
        
        // DOD: Flattened Render Commands
        private List<RenderCmd> _renderCommands = new List<RenderCmd>();
        
        // Cached Skia Objects
        private SKPaint _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private SKPaint _strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private SKPaint _textPaint = new SKPaint { IsAntialias = true };
        // Font cache is tricky due to size/weight changes. We'll create font on fly or cache better later.

        public new bool IsDirty 
        {
            get => _isLayoutDirty || base.IsDirty;
            set { _isLayoutDirty = value; base.IsDirty = value; }
        }

        public string Html
        {
            get => _html;
            set { _html = value; Rebuild(); }
        }

        public string Css
        {
            get => _css;
            set { _css = value; Rebuild(); }
        }

        public HtmlWidget()
        {
            _parser = new HtmlParserService();
        }

        private void Rebuild()
        {
            if (string.IsNullOrEmpty(_html)) return;

            var doc = _parser.ParseHtml(_html);
            _rootBox = FlexBuilder.BuildTree(doc.Body);
            
            if (!string.IsNullOrEmpty(_css))
            {
                var stylesheet = _parser.ParseCss(_css);
                _parser.ApplyStyles(_rootBox, stylesheet);
            }
            
            FlexBuilder.ApplyFlexStyles(_rootBox);
            ApplyMeasureFunc(_rootBox);
            IsDirty = true;
        }

        private void ApplyMeasureFunc(CssBox box)
        {
            // If it's a leaf node and has text content, set measure func
            if (box.Children.Count == 0 && !string.IsNullOrWhiteSpace(box.Element.TextContent))
            {
                box.FlexNode.SetMeasureFunc((node, width, widthMode, height, heightMode) => 
                {
                    var text = box.Element.TextContent.Trim();
                    if (string.IsNullOrEmpty(text)) return new FlexLayoutSharp.Size(0, 0);

                    // Font Properties
                    float fontSize = 16;
                    if (TryGetFloat(box.ComputedStyle.FontSize, out float fs)) fontSize = fs;

                    SKFontStyleWeight weight = SKFontStyleWeight.Normal;
                    if (box.ComputedStyle.FontWeight == "bold") weight = SKFontStyleWeight.Bold;
                    else if (int.TryParse(box.ComputedStyle.FontWeight, out int wVal)) weight = (SKFontStyleWeight)wVal;

                    using var typeface = SKTypeface.FromFamilyName("Arial", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                    using var font = new SKFont(typeface, fontSize);
                    using var paint = new SKPaint(font) { IsAntialias = true };
                    
                    var bounds = new SKRect();
                    float measuredWidth = paint.MeasureText(text, ref bounds);
                    
                    // Use FontMetrics for line height
                    var metrics = font.Metrics;
                    float measuredHeight = metrics.Descent - metrics.Ascent; 
                    
                    // Return Size struct
                    return new FlexLayoutSharp.Size(measuredWidth, measuredHeight);
                });
            }
            
            foreach (var child in box.Children)
            {
                ApplyMeasureFunc(child);
            }
        }

        protected override bool OnPointerEvent(PointerEvent e, float localX, float localY)
        {
            if (_renderCommands.Count == 0) return false;

            if (e.Action == PointerAction.Down)
            {
                // Iterate backwards for Z-order (Top first)
                for (int i = _renderCommands.Count - 1; i >= 0; i--)
                {
                    var cmd = _renderCommands[i];
                    if (cmd.IsPushLayer || cmd.IsPopLayer) continue;
                    
                    // Simple AABB check
                    if (localX >= cmd.Rect.Left && localX <= cmd.Rect.Right &&
                        localY >= cmd.Rect.Top && localY <= cmd.Rect.Bottom)
                    {
                        if (!IsInteractive(cmd))
                        {
                            continue;
                        }

                        string text = cmd.IsText ? cmd.Text : "";
                        if (text.Length > 20) text = text.Substring(0, 20) + "...";
                        
                        Console.WriteLine($"[HtmlWidget] Clicked: <{cmd.Tag} id='{cmd.Id}' class='{cmd.Class}'> Text: '{text}'");
                        return true;
                    }
                }
            }
            return base.OnPointerEvent(e, localX, localY);
        }

        private static bool IsInteractive(in RenderCmd cmd)
        {
            string tag = cmd.Tag ?? "";
            if (tag == "button" || tag == "a" || tag == "input" || tag == "select" || tag == "textarea")
            {
                return true;
            }

            string id = cmd.Id ?? "";
            if (!string.IsNullOrWhiteSpace(id))
            {
                return true;
            }

            string cls = cmd.Class ?? "";
            if (cls.IndexOf("btn", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (cls.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (cls.IndexOf("click", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (cls.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        // Remove old HitTest
        // private CssBox HitTest(...) 

        public override void Render(SKCanvas canvas)
        {
            if (_rootBox == null) return;

            // Recalculate layout ONLY if size changed or dirty flag
            if (_isLayoutDirty || Math.Abs(_lastLayoutWidth - Width) > 0.01f || Math.Abs(_lastLayoutHeight - Height) > 0.01f)
            {
                _rootBox.FlexNode.CalculateLayout(Width, Height, FlexLayoutSharp.Direction.LTR);
                _lastLayoutWidth = Width;
                _lastLayoutHeight = Height;
                _isLayoutDirty = false;
                
                // DOD: Rebuild Render Commands (Local Coordinates)
                _renderCommands.Clear();
                CollectCommands(_rootBox, 0, 0);
            }

            canvas.Save();
            canvas.Translate(X, Y);

            // Execute Commands (No recursion)
            foreach (var cmd in _renderCommands)
            {
                if (cmd.IsPushLayer)
                {
                     canvas.SaveLayer(new SKPaint { Color = new SKColor(255, 255, 255, (byte)(cmd.Opacity * 255)) });
                     continue;
                }
                if (cmd.IsPopLayer)
                {
                    canvas.Restore();
                    continue;
                }
                
                if (cmd.IsText)
                {
                    DrawTextCmd(canvas, cmd);
                    continue;
                }
                
                // Box
                var rrect = new SKRoundRect(cmd.Rect, cmd.BorderRadius, cmd.BorderRadius);
                
                if (cmd.HasBackground)
                {
                    _fillPaint.Color = cmd.Color;
                    if (cmd.BorderRadius > 0) canvas.DrawRoundRect(rrect, _fillPaint);
                    else canvas.DrawRect(cmd.Rect, _fillPaint);
                }
                
                if (cmd.HasBorder)
                {
                    _strokePaint.Color = cmd.BorderColor;
                    _strokePaint.StrokeWidth = cmd.BorderWidth;
                    if (cmd.BorderRadius > 0) canvas.DrawRoundRect(rrect, _strokePaint);
                    else canvas.DrawRect(cmd.Rect, _strokePaint);
                }
            }
            
            canvas.Restore();

            IsDirty = false;
        }

        private void CollectCommands(CssBox box, float parentX, float parentY)
        {
            float x = parentX + box.FlexNode.LayoutGetLeft();
            float y = parentY + box.FlexNode.LayoutGetTop();
            float w = box.FlexNode.LayoutGetWidth();
            float h = box.FlexNode.LayoutGetHeight();
            
            // Opacity
            float opacity = 1.0f;
            if (TryGetFloat(box.ComputedStyle.Opacity, out float parsedOpacity)) opacity = parsedOpacity;
            
            if (opacity < 1.0f)
            {
                _renderCommands.Add(new RenderCmd { IsPushLayer = true, Opacity = opacity });
            }

            var rect = new SKRect(x, y, x + w, y + h);
            
            // Background & Border Properties
            float borderRadius = 0;
            if (TryGetFloat(box.ComputedStyle.BorderRadius, out float radiusVal)) borderRadius = radiusVal;
            
            var cmd = new RenderCmd 
            { 
                Rect = rect, 
                BorderRadius = borderRadius,
                Tag = box.Element.TagName,
                Id = box.Element.Id,
                Class = box.Element.ClassName
            };

            // Background
            var bgColStr = box.ComputedStyle.BackgroundColor;
            if (TryParseColor(bgColStr, out SKColor bgColor))
            {
                cmd.HasBackground = true;
                cmd.Color = bgColor;
            }

            // Border
            var borderColorStr = box.ComputedStyle.BorderColor;
            var borderWidthStr = box.ComputedStyle.BorderWidth;
            if (!string.IsNullOrEmpty(borderColorStr) && !string.IsNullOrEmpty(borderWidthStr) && 
                TryParseColor(borderColorStr, out SKColor borderColor) && TryGetFloat(borderWidthStr, out float borderWidth))
            {
                cmd.HasBorder = true;
                cmd.BorderColor = borderColor;
                cmd.BorderWidth = borderWidth;
            }
            
            _renderCommands.Add(cmd);

            // Text
            if (!string.IsNullOrEmpty(box.Element.TextContent) && box.Children.Count == 0)
            {
                 var text = box.Element.TextContent.Trim();
                 if (!string.IsNullOrEmpty(text))
                 {
                     var textCmd = new RenderCmd 
                     { 
                         IsText = true,
                         Text = text,
                         Rect = rect,
                         FontSize = 16,
                         TextColor = SKColors.Black,
                         FontWeight = SKFontStyleWeight.Normal,
                         TextAlign = box.ComputedStyle.TextAlign,
                         Tag = "TEXT",
                         Id = box.Element.Id,
                         Class = box.Element.ClassName
                     };
                     
                     if (TryGetFloat(box.ComputedStyle.FontSize, out float fs)) textCmd.FontSize = fs;
                     if (TryParseColor(box.ComputedStyle.Color, out SKColor col)) textCmd.TextColor = col;
                     if (box.ComputedStyle.FontWeight == "bold") textCmd.FontWeight = SKFontStyleWeight.Bold;
                     else if (int.TryParse(box.ComputedStyle.FontWeight, out int wVal)) textCmd.FontWeight = (SKFontStyleWeight)wVal;
                     
                     _renderCommands.Add(textCmd);
                 }
            }

            // Children
            foreach (var child in box.Children)
            {
                CollectCommands(child, x, y);
            }
            
            if (opacity < 1.0f)
            {
                _renderCommands.Add(new RenderCmd { IsPopLayer = true });
            }
        }
        
        private void DrawTextCmd(SKCanvas canvas, RenderCmd cmd)
        {
            using var typeface = SKTypeface.FromFamilyName("Arial", cmd.FontWeight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, cmd.FontSize);
            _textPaint.Color = cmd.TextColor;
            _textPaint.Typeface = typeface; // Needed? Paint uses Typeface? No, Font does.
            // Wait, DrawText takes Font and Paint.
            
            var bounds = new SKRect();
            _textPaint.MeasureText(cmd.Text, ref bounds); // This might need font...
            // Actually SKPaint.MeasureText uses the typeface on the paint?
            // Or we use font.MeasureText.
            // Let's use font.
            float width = font.MeasureText(cmd.Text, out bounds);

            float x = cmd.Rect.Left;
            float y = cmd.Rect.Top + cmd.FontSize; // Baseline approx

            if (cmd.TextAlign == "center")
            {
                x = cmd.Rect.Left + (cmd.Rect.Width - width) / 2;
            }
            else if (cmd.TextAlign == "right")
            {
                x = cmd.Rect.Left + cmd.Rect.Width - width;
            }
            else
            {
                x = cmd.Rect.Left + 5;
            }
            
            float h = cmd.Rect.Height;
            y = cmd.Rect.Top + (h + bounds.Height) / 2; // Center Vert

            canvas.DrawText(cmd.Text, x, y, font, _textPaint);
        }

        // Removed RenderBox
        // private void RenderBox(...)

        private bool TryGetFloat(string valStr, out float value)
        {
            value = 0;
            if (string.IsNullOrEmpty(valStr)) return false;
            if (valStr.EndsWith("px")) valStr = valStr.Substring(0, valStr.Length - 2);
            if (valStr.EndsWith("%")) return false; 
            return float.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private bool TryParseColor(string colorStr, out SKColor color)
        {
            color = SKColors.Transparent;
            if (string.IsNullOrEmpty(colorStr)) return false;
            
            // Simple mappings
            if (colorStr.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return true;
            if (colorStr.Equals("red", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Red; return true; }
            if (colorStr.Equals("blue", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Blue; return true; }
            if (colorStr.Equals("green", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Green; return true; }
            if (colorStr.Equals("white", StringComparison.OrdinalIgnoreCase)) { color = SKColors.White; return true; }
            if (colorStr.Equals("black", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Black; return true; }
            if (colorStr.Equals("gray", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Gray; return true; }
            if (colorStr.Equals("cyan", StringComparison.OrdinalIgnoreCase)) { color = SKColors.Cyan; return true; }
            
            // Hex
            if (colorStr.StartsWith("#"))
            {
                return SKColor.TryParse(colorStr, out color);
            }

            // Rgba
            if (colorStr.StartsWith("rgba"))
            {
                try 
                {
                    var parts = colorStr.Replace("rgba(", "").Replace(")", "").Split(',');
                    if (parts.Length == 4)
                    {
                        byte r = byte.Parse(parts[0].Trim());
                        byte g = byte.Parse(parts[1].Trim());
                        byte b = byte.Parse(parts[2].Trim());
                        // Alpha in CSS is 0-1, Skia is 0-255
                        float a = float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                        
                        // Fix: Check if alpha is 0-1 or 0-255
                        byte alphaByte;
                        if (a > 1.0f)
                        {
                            alphaByte = (byte)Math.Clamp(a, 0, 255);
                        }
                        else
                        {
                            alphaByte = (byte)(a * 255);
                        }
                        
                        color = new SKColor(r, g, b, alphaByte);
                        return true;
                    }
                }
                catch {}
            }
            
            return false;
        }
    }
}
