using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ExCSS;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// 世界 HUD 的有限 CSS 样式(加载期解析,运行期只读)。
    /// 所有字段可空:未声明的属性沿用引擎默认值;声明即生效,不存在静默回退。
    /// translate 采用 CSS 语义(屏幕坐标 y 向下为正),投影后应用,与相机距离无关。
    /// </summary>
    public struct WorldHudStyle
    {
        public float? Width;
        public float? Height;
        public float? FontSize;
        public float? Opacity;
        public Vector4? Color;
        public Vector4? BackgroundColor;
        public Vector2? Translate;

        public bool IsEmpty =>
            !Width.HasValue && !Height.HasValue && !FontSize.HasValue && !Opacity.HasValue &&
            !Color.HasValue && !BackgroundColor.HasValue && !Translate.HasValue;
    }

    /// <summary>
    /// 有限 CSS 声明解析。解析本体复用仓库的 ExCSS(与 UI 车道同一轮子);
    /// 本类型只负责把声明映射为 HUD 样式并执行严格合同:
    /// 未知属性、重复声明、被解析器丢弃的残缺声明、非法值一律抛错——资产拼错必须当场失败。
    /// 支持的属性与值形:
    /// width/height/font-size: 正长度(px 可省略,其他单位拒绝);
    /// opacity: 0..1;color/background-color: #RGB/#RGBA/#RRGGBB/#RRGGBBAA/rgb()/rgba();
    /// translate: 两个长度。
    /// </summary>
    public static class WorldHudStyleCss
    {
        private const string Supported = "width, height, font-size, opacity, color, background-color, translate";

        public static WorldHudStyle Parse(string css, string context)
        {
            if (string.IsNullOrWhiteSpace(css))
            {
                throw new InvalidOperationException($"{context}: 'css' must be a non-empty declaration string.");
            }

            // 声明切分只按分号/冒号定界(本子集的值内不会出现分号),拒绝重复属性名;
            // 值的语法验证逐条交给 ExCSS:它认识的属性以其解析结果为准(色值会被规范化为
            // rgb()/rgba() 形),它没有属性模型而整条丢弃的名字(如 translate)由本类型映射器
            // 自行验证——无论哪条路径,非法值与未知属性都当场抛错。
            HashSet<string> declaredNames = new(StringComparer.OrdinalIgnoreCase);
            var style = new WorldHudStyle();
            foreach (string raw in css.Split(';'))
            {
                string declaration = raw.Trim();
                if (declaration.Length == 0)
                {
                    continue;
                }

                int colon = declaration.IndexOf(':');
                if (colon <= 0 || colon == declaration.Length - 1)
                {
                    throw new InvalidOperationException($"{context}: css declaration '{declaration}' must be 'property: value'.");
                }

                string name = declaration[..colon].Trim();
                if (!declaredNames.Add(name.ToLowerInvariant()))
                {
                    throw new InvalidOperationException($"{context}: css property '{name}' declared twice.");
                }

                string rawValue = declaration[(colon + 1)..].Trim();
                Stylesheet sheet = new StylesheetParser().Parse("*{" + declaration + "}");
                bool mapped = false;
                foreach (IStyleRule rule in sheet.StyleRules)
                {
                    foreach (Property property in rule.Style)
                    {
                        MapDeclaration(ref style, property.Name.ToLowerInvariant(), property.Value, context);
                        mapped = true;
                    }
                }

                if (!mapped)
                {
                    MapDeclaration(ref style, name, rawValue, context);
                }
            }

            if (style.IsEmpty)
            {
                throw new InvalidOperationException($"{context}: css declares no supported property.");
            }

            return style;
        }

        private static void MapDeclaration(ref WorldHudStyle style, string name, string value, string context)
        {
            switch (name)
            {
                case "width":
                    style.Width = ParsePositiveLength(value, "width", context);
                    break;
                case "height":
                    style.Height = ParsePositiveLength(value, "height", context);
                    break;
                case "font-size":
                    style.FontSize = ParsePositiveLength(value, "font-size", context);
                    break;
                case "opacity":
                    style.Opacity = ParseOpacity(value, context);
                    break;
                case "color":
                    style.Color = ParseColor(value, "color", context);
                    break;
                case "background-color":
                    style.BackgroundColor = ParseColor(value, "background-color", context);
                    break;
                case "translate":
                    style.Translate = ParseTranslate(value, context);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{context}: css property '{name}' is not supported; supported: {Supported}.");
            }
        }

        private static float ParsePositiveLength(string value, string property, string context)
        {
            float length = ParsePxLength(value, property, context);
            if (length <= 0f)
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires a positive px length, got '{value}'.");
            }

            return length;
        }

        private static float ParsePxLength(string value, string property, string context)
        {
            string number = value.Trim();
            if (number.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                number = number[..^2].Trim();
            }
            else if (number.Length > 0 && (char.IsAsciiLetter(number[^1]) || number[^1] == '%'))
            {
                throw new InvalidOperationException($"{context}: css '{property}' only supports px lengths, got '{value}'.");
            }

            if (!TryParseFloat(number, out float length) || !float.IsFinite(length))
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires a px length, got '{value}'.");
            }

            return length;
        }

        private static float ParseOpacity(string value, string context)
        {
            if (!TryParseFloat(value.Trim(), out float opacity) || opacity < 0f || opacity > 1f)
            {
                throw new InvalidOperationException($"{context}: css 'opacity' requires a number in [0,1], got '{value}'.");
            }

            return opacity;
        }

        private static Vector2 ParseTranslate(string value, string context)
        {
            string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"{context}: css 'translate' requires exactly two px lengths (x y), got '{value}'.");
            }

            return new Vector2(
                ParsePxLength(parts[0], "translate", context),
                ParsePxLength(parts[1], "translate", context));
        }

        private static Vector4 ParseColor(string value, string property, string context)
        {
            string trimmed = value.Trim();
            if (trimmed.StartsWith('#'))
            {
                return ParseHexColor(trimmed, property, context);
            }

            if (trimmed.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            {
                return ParseRgbColor(trimmed, property, context);
            }

            throw new InvalidOperationException(
                $"{context}: css '{property}' requires #RGB/#RGBA/#RRGGBB/#RRGGBBAA or rgb()/rgba(), got '{value}'.");
        }

        private static Vector4 ParseHexColor(string value, string property, string context)
        {
            string hex = value[1..];
            if (hex.Length is not (3 or 4 or 6 or 8) || !IsHex(hex))
            {
                throw new InvalidOperationException(
                    $"{context}: css '{property}' hex color must be #RGB/#RGBA/#RRGGBB/#RRGGBBAA, got '{value}'.");
            }

            int componentDigits = hex.Length is 3 or 4 ? 1 : 2;
            float scale = componentDigits == 1 ? 1f / 15f : 1f / 255f;
            float r = HexGroup(hex, 0, componentDigits) * scale;
            float g = HexGroup(hex, 1, componentDigits) * scale;
            float b = HexGroup(hex, 2, componentDigits) * scale;
            float a = hex.Length is 4 or 8 ? HexGroup(hex, 3, componentDigits) * scale : 1f;
            return new Vector4(r, g, b, a);
        }

        private static Vector4 ParseRgbColor(string value, string property, string context)
        {
            bool hasAlpha = value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
            string inner = value[value.IndexOf('(')..].Trim('(', ')', ' ');
            string[] parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != (hasAlpha ? 4 : 3))
            {
                throw new InvalidOperationException(
                    $"{context}: css '{property}' rgb() takes 3 components, rgba() takes 4, got '{value}'.");
            }

            if (!TryParseFloat(parts[0], out float r) || !TryParseFloat(parts[1], out float g) || !TryParseFloat(parts[2], out float b) ||
                r < 0f || r > 255f || g < 0f || g > 255f || b < 0f || b > 255f)
            {
                throw new InvalidOperationException(
                    $"{context}: css '{property}' rgb components must be numbers in [0,255], got '{value}'.");
            }

            float alpha = 1f;
            if (hasAlpha &&
                (!TryParseFloat(parts[3], out alpha) || alpha < 0f || alpha > 1f))
            {
                throw new InvalidOperationException(
                    $"{context}: css '{property}' rgba alpha must be in [0,1], got '{value}'.");
            }

            return new Vector4(r / 255f, g / 255f, b / 255f, alpha);
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsHex(string text)
        {
            foreach (char c in text)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static int HexGroup(string hex, int group, int componentDigits)
        {
            return Convert.ToInt32(hex.Substring(group * componentDigits, componentDigits), 16);
        }
    }
}
