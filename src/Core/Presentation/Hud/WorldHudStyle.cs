using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ExCSS;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>clip-path 异形底板预设(按条目矩形归一化;wire 占装饰块一个保留字节)。</summary>
    public enum HudClipShape : byte
    {
        None = 0,
        /// <summary>尖底盾(EL2 场匾):底边中点向下收尖。</summary>
        Shield = 1,
        /// <summary>菱形(ES2 旗)。</summary>
        Diamond = 2,
        /// <summary>燕尾旗(全战):底边中央内凹。</summary>
        Pennant = 3,
        /// <summary>平行四边形(ES2 侧签)。</summary>
        Parallelogram = 4,
        /// <summary>下尖水滴(Civ7)。</summary>
        PointedBottom = 5,
    }

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

        // 排版扩展(零值=关闭,默认渲染路径与未声明时逐像素一致):
        public float? CornerRadius;
        public float? BorderWidth;
        public float? Padding;
        /// <summary>边框颜色;未声明时边框沿用渲染端默认黑。W<=0 视为透明边框(即无边框)。</summary>
        public Vector4? BorderColor;
        /// <summary>血条前景(填充)渐变终点;仅 color 为双色 linear-gradient 时出现。</summary>
        public Vector4? FillGradientTo;
        /// <summary>背景渐变终点;background(-image) 为 linear-gradient 时出现。</summary>
        public Vector4? BackgroundGradientTo;
        public bool? Bold;
        public bool? Italic;
        /// <summary>阴影偏移/模糊(px)与颜色;W<=0 视为无阴影。文字=text-shadow,条=box-shadow。</summary>
        public Vector4? ShadowColor;
        public float? ShadowOffsetX;
        public float? ShadowOffsetY;
        public float? ShadowBlur;

        /// <summary>image_assets.json 登记的语义图片 id;仅血条条目消费(Id0 承载解析后的源串)。</summary>
        public string? ImageAssetId;
        /// <summary>clip-path 异形预设;见 HudClipShape。</summary>
        public HudClipShape ClipShape;

        public bool IsEmpty =>
            !Width.HasValue && !Height.HasValue && !FontSize.HasValue && !Opacity.HasValue &&
            !Color.HasValue && !BackgroundColor.HasValue && !Translate.HasValue &&
            !CornerRadius.HasValue && !BorderWidth.HasValue && !Padding.HasValue && !BorderColor.HasValue &&
            !FillGradientTo.HasValue && !BackgroundGradientTo.HasValue &&
            !Bold.HasValue && !Italic.HasValue &&
            !ShadowColor.HasValue && !ShadowOffsetX.HasValue && !ShadowOffsetY.HasValue && !ShadowBlur.HasValue &&
            ImageAssetId == null && ClipShape == 0;
    }

    /// <summary>
    /// 有限 CSS 声明解析。解析本体复用仓库的 ExCSS(与 UI 车道同一轮子);
    /// 本类型只负责把声明映射为 HUD 样式并执行严格合同:
    /// 未知属性、重复声明、被解析器丢弃的残缺声明、非法值一律抛错——资产拼错必须当场失败。
    /// 支持的属性与值形:
    /// width/height/font-size: 正长度(px 可省略,其他单位拒绝);
    /// opacity: 0..1;color/background-color: #RGB/#RGBA/#RRGGBB/#RRGGBBAA/rgb()/rgba()/标准色名
    /// 或双色 linear-gradient(to right, a, b)(渐变终点进 *GradientTo);
    /// translate: 两个长度;
    /// border: &lt;宽&gt;px solid &lt;色&gt;;border-width/border-color 单独声明亦可;
    /// border-radius/padding: 长度(padding 支持一至四值,取均匀内缩);
    /// font-weight: bold;font-style: italic;text-shadow/box-shadow: x y 模糊 颜色。
    /// </summary>
    public static class WorldHudStyleCss
    {
        private const string Supported =
            "width, height, font-size, opacity, color, background-color, background, background-image, " +
            "translate, border, border-width, border-color, border-radius, padding, " +
            "font-weight, font-style, text-shadow, box-shadow, image, clip-path";

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
                    if (value.TrimStart().StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseGradient(value, context, out Vector4 from, out Vector4 to);
                        style.Color = from;
                        style.FillGradientTo = to;
                    }
                    else
                    {
                        style.Color = ParseColor(value, "color", context);
                    }

                    break;
                case "background-color":
                    // background 简写会补一条 initial;显式的 initial/transparent 等于未声明。
                    if (!string.Equals(value.Trim(), "initial", StringComparison.OrdinalIgnoreCase))
                    {
                        style.BackgroundColor = ParseColor(value, "background-color", context);
                    }

                    break;
                case "background-attachment":
                case "background-origin":
                case "background-clip":
                case "background-repeat":
                case "background-position":
                case "background-size":
                    // background 简写会带出这些长手;HUD 渐变按"起点锚定、不重复"渲染,
                    // 只接受与该语义一致的默认值,其余显式声明一律拒绝。
                    if (!IsDefaultBackgroundLonghand(name, value.Trim()))
                    {
                        throw new InvalidOperationException(
                            $"{context}: css '{name}' only supports the default value (HUD gradients are start-anchored, non-repeating), got '{value}'.");
                    }

                    break;
                case "background":
                case "background-image":
                    if (value.TrimStart().StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseGradient(value, context, out Vector4 from, out Vector4 to);
                        style.BackgroundColor = from;
                        style.BackgroundGradientTo = to;
                    }
                    else
                    {
                        style.BackgroundColor = ParseColor(value, name, context);
                    }

                    break;
                case "translate":
                    style.Translate = ParseTranslate(value, context);
                    break;
                case "border-top-width":
                case "border-right-width":
                case "border-bottom-width":
                case "border-left-width":
                    SetUniformFloat(ref style.BorderWidth, ParsePositiveLength(value, name, context), name, context);
                    break;
                case "border-top-style":
                case "border-right-style":
                case "border-bottom-style":
                case "border-left-style":
                    if (!string.Equals(value.Trim(), "solid", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"{context}: css '{name}' only supports 'solid', got '{value}'.");
                    }

                    break;
                case "border-top-color":
                case "border-right-color":
                case "border-bottom-color":
                case "border-left-color":
                    SetUniformColor(ref style.BorderColor, ParseColor(value, name, context), name, context);
                    break;
                case "border-radius":
                    style.CornerRadius = ParsePositiveLength(value, "border-radius", context);
                    break;
                case "border-top-left-radius":
                case "border-top-right-radius":
                case "border-bottom-left-radius":
                case "border-bottom-right-radius":
                    // ExCSS 会把 border-radius 展开成四角长手,且每角是"横 纵"两段;两段与四角必须全部一致。
                    string[] cornerParts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (cornerParts.Length is < 1 or > 2)
                    {
                        throw new InvalidOperationException($"{context}: css '{name}' only supports one or two lengths, got '{value}'.");
                    }

                    float corner = ParsePositiveLength(cornerParts[0], name, context);
                    for (int i = 1; i < cornerParts.Length; i++)
                    {
                        float next = ParsePositiveLength(cornerParts[i], name, context);
                        if (MathF.Abs(next - corner) > 0.001f)
                        {
                            throw new InvalidOperationException($"{context}: css '{name}' horizontal/vertical radii must agree, got '{value}'.");
                        }
                    }

                    SetUniformFloat(ref style.CornerRadius, corner, name, context);
                    break;
                case "padding-top":
                case "padding-right":
                case "padding-bottom":
                case "padding-left":
                    SetUniformFloat(ref style.Padding, ParsePositiveLength(value, name, context), name, context);
                    break;
                case "font-weight":
                    style.Bold = value.Trim() switch
                    {
                        "bold" => true,
                        "normal" => false,
                        _ => ParseFontWeightNumber(value, context),
                    };
                    break;
                case "font-style":
                    style.Italic = value.Trim() switch
                    {
                        "italic" => true,
                        "normal" => false,
                        _ => throw new InvalidOperationException($"{context}: css 'font-style' only supports italic/normal, got '{value}'."),
                    };
                    break;
                case "text-shadow":
                case "box-shadow":
                    if (style.ShadowColor.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"{context}: css 'text-shadow' and 'box-shadow' are mutually exclusive within one style.");
                    }

                    ParseShadow(value, name, context, out Vector4 shadowColor, out float dx, out float dy, out float blur);
                    style.ShadowColor = shadowColor;
                    style.ShadowOffsetX = dx;
                    style.ShadowOffsetY = dy;
                    style.ShadowBlur = blur;
                    break;
                case "image":
                    style.ImageAssetId = ParseImageAssetId(value, context);
                    break;
                case "clip-path":
                    style.ClipShape = ParseClipShape(value, context);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{context}: css property '{name}' is not supported; supported: {Supported}.");
            }
        }

        private static string ParseImageAssetId(string value, string context)
        {
            string id = value.Trim().TrimEnd(';');
            if (id.Length == 0 ||
                id.StartsWith("url(", StringComparison.OrdinalIgnoreCase) ||
                id.Contains(' ') ||
                id.Contains('('))
            {
                throw new InvalidOperationException(
                    $"{context}: css 'image' requires a bare image asset id from Presentation/image_assets.json, got '{value}'.");
            }

            return id;
        }

        private static HudClipShape ParseClipShape(string value, string context)
        {
            switch (value.Trim())
            {
                case "shield": return HudClipShape.Shield;
                case "diamond": return HudClipShape.Diamond;
                case "pennant": return HudClipShape.Pennant;
                case "parallelogram": return HudClipShape.Parallelogram;
                case "pointed-bottom": return HudClipShape.PointedBottom;
                default:
                    throw new InvalidOperationException(
                        $"{context}: css 'clip-path' supports named presets shield/diamond/pennant/parallelogram/pointed-bottom, got '{value}'.");
            }
        }

        private static bool IsDefaultBackgroundLonghand(string name, string value)
        {
            return name switch
            {
                "background-attachment" => value is "scroll" or "initial",
                "background-origin" => value is "padding-box" or "initial",
                "background-clip" => value is "border-box" or "initial",
                "background-repeat" => value is "no-repeat" or "repeat" or "initial",
                "background-position" => value is "0% 0%" or "0 0" or "initial",
                "background-size" => value is "auto" or "none" or "initial",
                _ => false,
            };
        }

        private static bool ParseFontWeightNumber(string value, string context)
        {
            if (!TryParseFloat(value.Trim(), out float weight) || !float.IsFinite(weight))
            {
                throw new InvalidOperationException($"{context}: css 'font-weight' only supports bold/normal/100..900, got '{value}'.");
            }

            return weight >= 600f;
        }

        private static void SetUniformFloat(ref float? slot, float v, string name, string context)
        {
            if (slot.HasValue && MathF.Abs(slot.Value - v) > 0.001f)
            {
                throw new InvalidOperationException($"{context}: css '{name}' sides must agree; only uniform values are supported.");
            }

            slot = v;
        }

        private static void SetUniformColor(ref Vector4? slot, Vector4 v, string name, string context)
        {
            if (slot.HasValue && MathF.Abs(slot.Value.X - v.X) + MathF.Abs(slot.Value.Y - v.Y) +
                MathF.Abs(slot.Value.Z - v.Z) + MathF.Abs(slot.Value.W - v.W) > 0.001f)
            {
                throw new InvalidOperationException($"{context}: css '{name}' sides must agree; only uniform values are supported.");
            }

            slot = v;
        }

        private static void ParseGradient(string value, string context, out Vector4 from, out Vector4 to)
        {
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
            {
                throw new InvalidOperationException($"{context}: css gradient must be linear-gradient(a, b), got '{value}'.");
            }

            string inner = trimmed[..^1].Substring("linear-gradient(".Length);
            List<string> parts = SplitTopLevel(inner, ',');
            int index = 0;
            if (parts.Count > 0 && parts[0].Trim().StartsWith("to ", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(parts[0].Trim(), "to right", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{context}: css gradient direction only supports 'to right', got '{parts[0].Trim()}'.");
                }

                index = 1;
            }

            if (parts.Count - index != 2)
            {
                throw new InvalidOperationException($"{context}: css gradient requires exactly two colors, got '{value}'.");
            }

            from = ParseColor(parts[index].Trim(), "gradient", context);
            to = ParseColor(parts[index + 1].Trim(), "gradient", context);
        }

        private static void ParseShadow(string value, string property, string context, out Vector4 color, out float dx, out float dy, out float blur)
        {
            List<string> parts = SplitTopLevel(value, ' ');
            if (parts.Count is < 3 or > 4)
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires 'x y [blur] color', got '{value}'.");
            }

            color = Vector4.Zero;
            blur = 0f;
            int lengths = 0;
            float[] parsed = new float[3];
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i].Trim();
                if (part.StartsWith('#') ||
                    part.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    color = ParseColor(part, property, context);
                    continue;
                }

                if (lengths == 3)
                {
                    throw new InvalidOperationException($"{context}: css '{property}' requires 'x y [blur] color', got '{value}'.");
                }

                parsed[lengths++] = ParsePxLength(part, property, context);
            }

            if (lengths < 2)
            {
                throw new InvalidOperationException($"{context}: css '{property}' requires at least x y offsets, got '{value}'.");
            }

            dx = parsed[0];
            dy = parsed[1];
            blur = lengths == 3 ? parsed[2] : 0f;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            List<string> parts = new();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c is '(' or '[')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth--;
                }
                else if (c == separator && depth == 0)
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }

            parts.Add(text[start..]);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i].Length == 0)
                {
                    parts.RemoveAt(i);
                }
            }

            return parts;
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
