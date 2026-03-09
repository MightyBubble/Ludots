using System.Text;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public static class UiFontRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> RegisteredFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SKTypeface> CachedTypefaces = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterFile(string familyName, string fontPath)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            throw new ArgumentException("Font family name is required.", nameof(familyName));
        }

        if (string.IsNullOrWhiteSpace(fontPath))
        {
            throw new ArgumentException("Font path is required.", nameof(fontPath));
        }

        lock (Sync)
        {
            RegisteredFiles[familyName.Trim()] = fontPath.Trim();

            string cachePrefix = familyName.Trim() + "|";
            foreach (string cacheKey in CachedTypefaces.Keys.Where(key => key.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                CachedTypefaces.Remove(cacheKey);
            }
        }
    }

    public static SKTypeface ResolveTypeface(string? familyList, bool bold)
    {
        string cacheKey = $"{familyList ?? string.Empty}|{bold}";

        lock (Sync)
        {
            if (CachedTypefaces.TryGetValue(cacheKey, out SKTypeface? cached))
            {
                return cached;
            }

            SKTypeface created = CreateTypeface(familyList, bold);
            CachedTypefaces[cacheKey] = created;
            return created;
        }
    }

    public static SKTypeface ResolveTypefaceForTextElement(string? familyList, bool bold, string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return ResolveTypeface(familyList, bold);
        }

        string cacheKey = $"glyph|{familyList ?? string.Empty}|{bold}|{textElement}";
        lock (Sync)
        {
            if (CachedTypefaces.TryGetValue(cacheKey, out SKTypeface? cached))
            {
                return cached;
            }

            SKTypeface resolved = CreateTypefaceForTextElement(familyList, bold, textElement);
            CachedTypefaces[cacheKey] = resolved;
            return resolved;
        }
    }

    public static bool SameTypeface(SKTypeface left, SKTypeface right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return ReferenceEquals(left, right)
            || string.Equals(left.FamilyName, right.FamilyName, StringComparison.OrdinalIgnoreCase);
    }

    private static SKTypeface CreateTypeface(string? familyList, bool bold)
    {
        foreach (string familyName in ParseFamilyList(familyList))
        {
            SKTypeface familyTypeface = ResolveSingleFamilyTypeface(familyName, bold);
            if (familyTypeface != SKTypeface.Default)
            {
                return familyTypeface;
            }
        }

        return ResolveDefaultTypeface(bold);
    }

    private static SKTypeface CreateTypefaceForTextElement(string? familyList, bool bold, string textElement)
    {
        SKTypeface baseTypeface = ResolveTypeface(familyList, bold);
        if (ContainsGlyphs(baseTypeface, textElement))
        {
            return baseTypeface;
        }

        foreach (string familyName in ParseFamilyList(familyList))
        {
            SKTypeface candidate = ResolveSingleFamilyTypeface(familyName, bold);
            if (ContainsGlyphs(candidate, textElement))
            {
                return candidate;
            }
        }

        if (TryGetFirstCodePoint(textElement, out int codePoint))
        {
            try
            {
                SKTypeface? matched = SKFontManager.Default.MatchCharacter(codePoint);
                if (matched != null)
                {
                    string? familyName = matched.FamilyName;
                    if (!string.IsNullOrWhiteSpace(familyName))
                    {
                        SKTypeface fallbackTypeface = ResolveSingleFamilyTypeface(familyName, bold);
                        if (ContainsGlyphs(fallbackTypeface, textElement))
                        {
                            return fallbackTypeface;
                        }
                    }

                    if (ContainsGlyphs(matched, textElement))
                    {
                        return matched;
                    }
                }
            }
            catch
            {
            }
        }

        return baseTypeface;
    }

    private static SKTypeface ResolveSingleFamilyTypeface(string familyName, bool bold)
    {
        string normalizedFamily = familyName.Trim();
        string cacheKey = $"family|{normalizedFamily}|{bold}";
        if (CachedTypefaces.TryGetValue(cacheKey, out SKTypeface? cached))
        {
            return cached;
        }

        SKTypeface resolved = CreateSingleFamilyTypeface(normalizedFamily, bold);
        CachedTypefaces[cacheKey] = resolved;
        return resolved;
    }

    private static SKTypeface CreateSingleFamilyTypeface(string familyName, bool bold)
    {
        SKFontStyle fontStyle = bold ? SKFontStyle.Bold : SKFontStyle.Normal;

        if (RegisteredFiles.TryGetValue(familyName, out string? fontPath))
        {
            try
            {
                return SKTypeface.FromFile(fontPath);
            }
            catch
            {
            }
        }

        string? mappedFamily = MapGenericFamily(familyName);
        try
        {
            return SKTypeface.FromFamilyName(mappedFamily, fontStyle) ?? SKTypeface.Default;
        }
        catch
        {
            return SKTypeface.Default;
        }
    }

    private static SKTypeface ResolveDefaultTypeface(bool bold)
    {
        string cacheKey = $"default|{bold}";
        if (CachedTypefaces.TryGetValue(cacheKey, out SKTypeface? cached))
        {
            return cached;
        }

        SKFontStyle fontStyle = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
        SKTypeface resolved = SKTypeface.FromFamilyName(null, fontStyle) ?? SKTypeface.Default;
        CachedTypefaces[cacheKey] = resolved;
        return resolved;
    }

    private static IEnumerable<string> ParseFamilyList(string? familyList)
    {
        if (string.IsNullOrWhiteSpace(familyList))
        {
            yield return "system-ui";
            yield break;
        }

        string[] parts = familyList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            string normalized = part.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static string? MapGenericFamily(string familyName)
    {
        return familyName.ToLowerInvariant() switch
        {
            "system-ui" or "sans-serif" => null,
            "serif" => "Times New Roman",
            "monospace" => "Consolas",
            _ => familyName
        };
    }

    private static bool ContainsGlyphs(SKTypeface typeface, string text)
    {
        try
        {
            return typeface.ContainsGlyphs(text);
        }
        catch
        {
            using SKFont font = new(typeface, 12f);
            return font.ContainsGlyphs(text);
        }
    }

    private static bool TryGetFirstCodePoint(string textElement, out int codePoint)
    {
        codePoint = 0;
        if (string.IsNullOrEmpty(textElement))
        {
            return false;
        }

        foreach (Rune rune in textElement.EnumerateRunes())
        {
            codePoint = rune.Value;
            return true;
        }

        return false;
    }
}
