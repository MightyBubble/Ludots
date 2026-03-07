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

    private static SKTypeface CreateTypeface(string? familyList, bool bold)
    {
        SKFontStyle fontStyle = bold ? SKFontStyle.Bold : SKFontStyle.Normal;

        foreach (string familyName in ParseFamilyList(familyList))
        {
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
                SKTypeface? familyTypeface = SKTypeface.FromFamilyName(mappedFamily, fontStyle);
                if (familyTypeface != null)
                {
                    return familyTypeface;
                }
            }
            catch
            {
            }
        }

        return SKTypeface.FromFamilyName(null, fontStyle) ?? SKTypeface.Default;
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
}
