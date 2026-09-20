using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkiaSharp;

using Ludots.UI.Runtime;

namespace Ludots.UI.Skia;

public static class UiFontRegistry
{
	private static readonly object Sync = new object();

	private static readonly Dictionary<string, string> RegisteredFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, SKTypeface> CachedTypefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

	private static readonly string[] GlyphFallbackFamilies =
	{
		"WenQuanYi Micro Hei",
		"Noto Sans CJK SC",
		"Noto Sans SC",
		"Source Han Sans SC",
		"Droid Sans Fallback",
		"Segoe UI",
		"Arial Unicode MS"
	};

	public static void RegisterFile(string familyName, string fontPath)
	{
		if (string.IsNullOrWhiteSpace(familyName))
		{
			throw new ArgumentException("Font family name is required.", "familyName");
		}
		if (string.IsNullOrWhiteSpace(fontPath))
		{
			throw new ArgumentException("Font path is required.", "fontPath");
		}
		lock (Sync)
		{
			RegisteredFiles[familyName.Trim()] = fontPath.Trim();
			string cachePrefix = familyName.Trim() + "|";
			string[] array = CachedTypefaces.Keys.Where((string text) => text.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
			foreach (string key in array)
			{
				CachedTypefaces.Remove(key);
			}
		}
	}

	public static SKTypeface ResolveTypeface(string? familyList, bool bold, bool italic = false)
	{
		string key = $"{familyList ?? string.Empty}|{bold}|{italic}";
		lock (Sync)
		{
			if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
			{
				return value;
			}
			SKTypeface sKTypeface = CreateTypeface(familyList, bold, italic);
			CachedTypefaces[key] = sKTypeface;
			return sKTypeface;
		}
	}

	public static SKTypeface ResolveTypefaceForTextElement(string? familyList, bool bold, string textElement, bool italic = false)
	{
		if (string.IsNullOrEmpty(textElement))
		{
			return ResolveTypeface(familyList, bold, italic);
		}
		string key = $"glyph|{familyList ?? string.Empty}|{bold}|{italic}|{textElement}";
		lock (Sync)
		{
			if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
			{
				return value;
			}
			SKTypeface sKTypeface = CreateTypefaceForTextElement(familyList, bold, textElement, italic);
			CachedTypefaces[key] = sKTypeface;
			return sKTypeface;
		}
	}

	public static bool SameTypeface(SKTypeface left, SKTypeface right)
	{
		ArgumentNullException.ThrowIfNull(left, "left");
		ArgumentNullException.ThrowIfNull(right, "right");
		return left == right || string.Equals(left.FamilyName, right.FamilyName, StringComparison.OrdinalIgnoreCase);
	}

	private static SKTypeface CreateTypeface(string? familyList, bool bold, bool italic)
	{
		foreach (string item in ParseFamilyList(familyList))
		{
			SKTypeface sKTypeface = ResolveSingleFamilyTypeface(item, bold, italic);
			if (!IsUnresolvedFallback(sKTypeface, item))
			{
				return sKTypeface;
			}
		}
		return ResolveDefaultTypeface(bold, italic);
	}

	private static SKTypeface CreateTypefaceForTextElement(string? familyList, bool bold, string textElement, bool italic)
	{
		SKTypeface preferred = ResolveTypeface(familyList, bold, italic);
		if (ContainsGlyphs(preferred, textElement))
		{
			return preferred;
		}
		foreach (string item in ParseFamilyList(familyList))
		{
			SKTypeface candidate = ResolveSingleFamilyTypeface(item, bold, italic);
			if (!IsUnresolvedFallback(candidate, item) && ContainsGlyphs(candidate, textElement))
			{
				return candidate;
			}
		}
		if (TryGetFirstCodePoint(textElement, out int codePoint))
		{
			SKTypeface matched = SKFontManager.Default.MatchCharacter(codePoint);
			if (matched != null && ContainsGlyphs(matched, textElement))
			{
				string familyName = matched.FamilyName;
				if (!string.IsNullOrWhiteSpace(familyName))
				{
					SKTypeface named = ResolveSingleFamilyTypeface(familyName, bold, italic);
					if (ContainsGlyphs(named, textElement))
					{
						return named;
					}
				}
				return matched;
			}
		}
		foreach (string fallbackFamily in GlyphFallbackFamilies)
		{
			SKTypeface fallback = ResolveSingleFamilyTypeface(fallbackFamily, bold, italic);
			if (!IsUnresolvedFallback(fallback, fallbackFamily) && ContainsGlyphs(fallback, textElement))
			{
				return fallback;
			}
		}
		throw new InvalidOperationException(
			$"No installed typeface contains glyphs for text element '{textElement}'. Register a covering font via UiFontRegistry.RegisterFile or install a CJK-capable family such as WenQuanYi Micro Hei.");
	}

	private static SKTypeface ResolveSingleFamilyTypeface(string familyName, bool bold, bool italic)
	{
		string text = familyName.Trim();
		string key = $"family|{text}|{bold}|{italic}";
		if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
		{
			return value;
		}
		SKTypeface sKTypeface = CreateSingleFamilyTypeface(text, bold, italic);
		CachedTypefaces[key] = sKTypeface;
		return sKTypeface;
	}

	private static SKFontStyle ResolveFontStyle(bool bold, bool italic)
	{
		if (bold && italic)
		{
			return SKFontStyle.BoldItalic;
		}

		if (bold)
		{
			return SKFontStyle.Bold;
		}

		if (italic)
		{
			return SKFontStyle.Italic;
		}

		return SKFontStyle.Normal;
	}

	private static SKTypeface CreateSingleFamilyTypeface(string familyName, bool bold, bool italic)
	{
		SKFontStyle style = ResolveFontStyle(bold, italic);
		if (RegisteredFiles.TryGetValue(familyName, out string value))
		{
			SKTypeface fromFile = SKTypeface.FromFile(value);
			if (fromFile == null)
			{
				throw new InvalidOperationException($"UiFontRegistry.RegisterFile typeface failed to load from '{value}'.");
			}
			return fromFile;
		}
		string mappedFamily = MapGenericFamily(familyName);
		SKTypeface fromFamily = SKTypeface.FromFamilyName(mappedFamily, style) ?? SKTypeface.Default;
		if (mappedFamily == null)
		{
			return fromFamily;
		}
		if (!string.Equals(fromFamily.FamilyName, mappedFamily, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(fromFamily.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
		{
			return SKTypeface.Default;
		}
		return fromFamily;
	}

	private static bool IsUnresolvedFallback(SKTypeface typeface, string requestedFamily)
	{
		if (typeface == SKTypeface.Default)
		{
			return true;
		}
		string mappedFamily = MapGenericFamily(requestedFamily);
		if (mappedFamily == null)
		{
			return false;
		}
		return !string.Equals(typeface.FamilyName, mappedFamily, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(typeface.FamilyName, requestedFamily, StringComparison.OrdinalIgnoreCase);
	}

	private static SKTypeface ResolveDefaultTypeface(bool bold, bool italic = false)
	{
		string key = $"default|{bold}|{italic}";
		if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
		{
			return value;
		}
		SKFontStyle style = ResolveFontStyle(bold, italic);
		SKTypeface sKTypeface = SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
		CachedTypefaces[key] = sKTypeface;
		return sKTypeface;
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
		string text = familyName.ToLowerInvariant();
		return text switch
		{
			"system-ui" or "sans-serif" => null,
			"serif" => "Times New Roman",
			"monospace" => "Consolas",
			_ => familyName,
		};
	}

	private static bool ContainsGlyphs(SKTypeface typeface, string text)
	{
		return typeface.ContainsGlyphs(text);
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
