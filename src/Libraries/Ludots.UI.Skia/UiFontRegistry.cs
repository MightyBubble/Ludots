using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using SkiaSharp;

using Ludots.UI.Runtime;

namespace Ludots.UI.Skia;

public static class UiFontRegistry
{
	private static readonly object Sync = new object();

	private static readonly Dictionary<string, string> RegisteredFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, SKTypeface> CachedTypefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

	// 每量一段文字都要查一次，读路径不上锁；写入与清空仍在 Sync 内。
	private static readonly ConcurrentDictionary<StyleKey, SKTypeface> StyleTypefaces = new ConcurrentDictionary<StyleKey, SKTypeface>(StyleKeyComparer.Instance);

	// 预留容量：新字符第一次出现时只填表不扩容，界面稳态下量字不分配。
	private static readonly Dictionary<RuneKey, SKTypeface> RuneTypefaces = new Dictionary<RuneKey, SKTypeface>(2048, RuneKeyComparer.Instance);

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

	private static int _generation;

	/// <summary>每次注册字体文件加一；按字缓存字体选择的调用方据此作废缓存。</summary>
	public static int Generation => Volatile.Read(ref _generation);

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
			StyleTypefaces.Clear();
			RuneTypefaces.Clear();
			Interlocked.Increment(ref _generation);
		}
	}

	public static SKTypeface ResolveTypeface(string? familyList, bool bold, bool italic = false)
	{
		var styleKey = new StyleKey(familyList ?? string.Empty, bold, italic);
		if (StyleTypefaces.TryGetValue(styleKey, out SKTypeface? cached))
		{
			return cached;
		}
		lock (Sync)
		{
			string key = $"{familyList ?? string.Empty}|{bold}|{italic}";
			if (!CachedTypefaces.TryGetValue(key, out SKTypeface? value))
			{
				value = CreateTypeface(familyList, bold, italic);
				CachedTypefaces[key] = value;
			}
			StyleTypefaces[styleKey] = value;
			return value;
		}
	}

	/// <summary>
	/// 按码位选字体：首选字体有这个字就用它，没有再按 <see cref="ResolveTypefaceForTextElement"/> 的顺序找回退字体。
	/// 结果按（首选字体，码位）缓存，命中时不分配。
	/// </summary>
	public static SKTypeface ResolveTypefaceForCodePoint(string? familyList, bool bold, bool italic, SKTypeface preferred, int codePoint)
	{
		ArgumentNullException.ThrowIfNull(preferred, nameof(preferred));
		var runeKey = new RuneKey(preferred, codePoint);
		lock (Sync)
		{
			if (RuneTypefaces.TryGetValue(runeKey, out SKTypeface? cached))
			{
				return cached;
			}
		}

		SKTypeface resolved = preferred.ContainsGlyph(codePoint)
			? preferred
			: ResolveTypefaceForTextElement(familyList, bold, char.ConvertFromUtf32(codePoint), italic);
		lock (Sync)
		{
			RuneTypefaces[runeKey] = resolved;
		}
		return resolved;
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

	private readonly record struct StyleKey(string Family, bool Bold, bool Italic);

	private sealed class StyleKeyComparer : IEqualityComparer<StyleKey>
	{
		public static readonly StyleKeyComparer Instance = new StyleKeyComparer();

		public bool Equals(StyleKey x, StyleKey y) =>
			x.Bold == y.Bold && x.Italic == y.Italic && string.Equals(x.Family, y.Family, StringComparison.OrdinalIgnoreCase);

		public int GetHashCode(StyleKey key) =>
			HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Family), key.Bold, key.Italic);
	}

	private readonly record struct RuneKey(SKTypeface Preferred, int CodePoint);

	private sealed class RuneKeyComparer : IEqualityComparer<RuneKey>
	{
		public static readonly RuneKeyComparer Instance = new RuneKeyComparer();

		public bool Equals(RuneKey x, RuneKey y) => x.CodePoint == y.CodePoint && ReferenceEquals(x.Preferred, y.Preferred);

		public int GetHashCode(RuneKey key) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Preferred), key.CodePoint);
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
