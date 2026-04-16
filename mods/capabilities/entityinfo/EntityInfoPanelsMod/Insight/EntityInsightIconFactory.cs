using System;
using System.Collections.Generic;

namespace EntityInfoPanelsMod.Insight;

public sealed class EntityInsightIconFactory
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string Build(string glyph, string accentColorHex, string surfaceColorHex, bool emphatic = false)
    {
        string normalizedGlyph = NormalizeGlyphRequired(glyph);
        string normalizedAccent = NormalizeHexRequired(accentColorHex, nameof(accentColorHex));
        string normalizedSurface = NormalizeHexRequired(surfaceColorHex, nameof(surfaceColorHex));
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

    private static string NormalizeGlyphRequired(string? glyph)
    {
        string normalized = (glyph ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Entity insight glyph must not be empty.");
        }

        return normalized;
    }

    private static string NormalizeHexRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Entity insight color '{parameterName}' must not be empty.");
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
            throw new InvalidOperationException($"Entity insight color '{parameterName}' must be a 3- or 6-digit hex value.");
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
                throw new InvalidOperationException($"Entity insight color '{parameterName}' contains non-hex characters.");
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
