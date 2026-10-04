using System;
using System.Globalization;
using System.Numerics;

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
    /// 有限 CSS 声明解析器。支持的属性与值形:
    /// width/height/font-size: 正长度(px 可省略,其他单位拒绝);
    /// opacity: 0..1;color/background-color: #RGB/#RGBA/#RRGGBB/#RRGGBBAA/rgb()/rgba();
    /// translate: 两个长度。
    /// 未知属性、重复属性、非法值、非正尺寸一律抛错——资产拼错必须当场失败。
    /// </summary>
    public static class WorldHudStyleCss
    {
        public static WorldHudStyle Parse(string css, string context)
        {
            if (string.IsNullOrWhiteSpace(css))
            {
                throw new InvalidOperationException($"{context}: 'css' must be a non-empty declaration string.");
            }

            var style = new WorldHudStyle();
            string? seenWidth = null;
            string? seenHeight = null;
            string? seenFontSize = null;
            string? seenOpacity = null;
            string? seenColor = null;
            string? seenBackgroundColor = null;
            string? seenTranslate = null;

            foreach (string rawDeclaration in css.Split(';'))
            {
                string declaration = rawDeclaration.Trim();
                if (declaration.Length == 0)
                {
                    continue;
                }

                int colon = declaration.IndexOf(':');
                if (colon <= 0 || colon == declaration.Length - 1)
                {
                    throw new InvalidOperationException($"{context}: css declaration '{declaration}' must be 'property: value'.");
                }

                string property = declaration[..colon].Trim();
                string value = declaration[(colon + 1)..].Trim();

                switch (property)
                {
                    case "width":
                        EnsureNotDuplicate(seenWidth, property, context);
                        seenWidth = property;
                        style.Width = ParsePositiveLength(value, property, context);
                        break;
                    case "height":
                        EnsureNotDuplicate(seenHeight, property, context);
                        seenHeight = property;
                        style.Height = ParsePositiveLength(value, property, context);
                        break;
                    case "font-size":
                        EnsureNotDuplicate(seenFontSize, property, context);
                        seenFontSize = property;
                        style.FontSize = ParsePositiveLength(value, property, context);
                        break;
                    case "opacity":
                        EnsureNotDuplicate(seenOpacity, property, context);
                        seenOpacity = property;
                        style.Opacity = ParseOpacity(value, context);
                        break;
                    case "color":
                        EnsureNotDuplicate(seenColor, property, context);
                        seenColor = property;
                        style.Color = ParseColor(value, property, context);
                        break;
                    case "background-color":
                        EnsureNotDuplicate(seenBackgroundColor, property, context);
                        seenBackgroundColor = property;
                        style.BackgroundColor = ParseColor(value, property, context);
                        break;
                    case "translate":
                        EnsureNotDuplicate(seenTranslate, property, context);
                        seenTranslate = property;
                        style.Translate = ParseTranslate(value, context);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{context}: css property '{property}' is not supported; supported: width, height, font-size, opacity, color, background-color, translate.");
                }
            }

            if (style.IsEmpty)
            {
                throw new InvalidOperationException($"{context}: css declares no supported property.");
            }

            return style;
        }

        private static void EnsureNotDuplicate(string? seen, string property, string context)
        {
            if (seen != null)
            {
                throw new InvalidOperationException($"{context}: css property '{property}' declared twice.");
            }
        }

        private static float ParsePositiveLength(string value, string property, string context)
        {
            string number = value;
            if (number.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                number = number[..^2].Trim();
            }
            else if (UnitSuffixPresent(number))
            {
                throw new InvalidOperationException($"{context}: css '{property}' only supports px lengths, got '{value}'.");
            }

            if (!TryParseFloat(number, out float length) || length <= 0f || !float.IsFinite(length))
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires a positive px length, got '{value}'.");
            }

            return length;
        }

        private static float ParseSignedLength(string value, string context, string property)
        {
            string number = value;
            if (number.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                number = number[..^2].Trim();
            }
            else if (UnitSuffixPresent(number))
            {
                throw new InvalidOperationException($"{context}: css '{property}' only supports px lengths, got '{value}'.");
            }

            if (!TryParseFloat(number, out float length) || !float.IsFinite(length))
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires a px length, got '{value}'.");
            }

            return length;
        }

        private static bool UnitSuffixPresent(string number)
        {
            return number.Length > 0 && (char.IsAsciiLetter(number[^1]) || number[^1] == '%');
        }

        private static float ParseOpacity(string value, string context)
        {
            if (!TryParseFloat(value, out float opacity) || opacity < 0f || opacity > 1f)
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
                ParseSignedLength(parts[0], context, "translate"),
                ParseSignedLength(parts[1], context, "translate"));
        }

        private static Vector4 ParseColor(string value, string property, string context)
        {
            if (value.StartsWith('#'))
            {
                return ParseHexColor(value, property, context);
            }

            if (value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            {
                return ParseRgbColor(value, property, context);
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
