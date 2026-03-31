using System;
using System.Collections.Generic;

namespace EntityInfoPanelsMod.Insight;

public sealed class EntityInsightIconFactory
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string Build(string glyph, string accentColorHex, string surfaceColorHex, bool emphatic = false)
    {
        string normalizedGlyph = string.IsNullOrWhiteSpace(glyph) ? "?" : glyph.Trim();
        string normalizedAccent = NormalizeHex(accentColorHex, "#58B7FF");
        string normalizedSurface = NormalizeHex(surfaceColorHex, "#0F1721");
        string cacheKey = $"{normalizedGlyph}|{normalizedAccent}|{normalizedSurface}|{(emphatic ? 1 : 0)}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        string highlight = emphatic ? "#F4D77A" : normalizedAccent;
        string svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='84' height='84' viewBox='0 0 84 84'>" +
            $"<defs><linearGradient id='g' x1='0%' y1='0%' x2='100%' y2='100%'><stop offset='0%' stop-color='{normalizedSurface}'/><stop offset='100%' stop-color='#08111A'/></linearGradient></defs>" +
            $"<rect x='4' y='4' width='76' height='76' rx='22' fill='url(#g)' stroke='{highlight}' stroke-width='2.4'/>" +
            $"<rect x='12' y='12' width='60' height='60' rx='18' fill='{normalizedAccent}' opacity='0.92'/>" +
            $"<text x='42' y='48' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='{ResolveFontSize(normalizedGlyph)}' font-weight='800' fill='#F7FBFF'>{EscapeXml(normalizedGlyph)}</text>" +
            "</svg>";

        string uri = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
        _cache[cacheKey] = uri;
        return uri;
    }

    private static string ResolveFontSize(string glyph)
    {
        return glyph.Length switch
        {
            <= 1 => "28",
            2 => "23",
            _ => "18"
        };
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 3)
        {
            trimmed = string.Concat(
                trimmed[0], trimmed[0],
                trimmed[1], trimmed[1],
                trimmed[2], trimmed[2]);
        }

        if (trimmed.Length < 6)
        {
            return fallback;
        }

        string rgb = trimmed[..6];
        for (int i = 0; i < rgb.Length; i++)
        {
            char ch = rgb[i];
            bool isHex = (ch >= '0' && ch <= '9') ||
                         (ch >= 'A' && ch <= 'F') ||
                         (ch >= 'a' && ch <= 'f');
            if (!isHex)
            {
                return fallback;
            }
        }

        return "#" + rgb.ToUpperInvariant();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
