using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public static class UiFontRegistry
{
	private static readonly object Sync = new object();

	private static readonly Dictionary<string, string> RegisteredFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, SKTypeface> CachedTypefaces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

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

	public static SKTypeface ResolveTypeface(string? familyList, bool bold)
	{
		string key = $"{familyList ?? string.Empty}|{bold}";
		lock (Sync)
		{
			if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
			{
				return value;
			}
			SKTypeface sKTypeface = CreateTypeface(familyList, bold);
			CachedTypefaces[key] = sKTypeface;
			return sKTypeface;
		}
	}

	public static SKTypeface ResolveTypefaceForTextElement(string? familyList, bool bold, string textElement)
	{
		if (string.IsNullOrEmpty(textElement))
		{
			return ResolveTypeface(familyList, bold);
		}
		string key = $"glyph|{familyList ?? string.Empty}|{bold}|{textElement}";
		lock (Sync)
		{
			if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
			{
				return value;
			}
			SKTypeface sKTypeface = CreateTypefaceForTextElement(familyList, bold, textElement);
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

	private static SKTypeface CreateTypeface(string? familyList, bool bold)
	{
		foreach (string item in ParseFamilyList(familyList))
		{
			SKTypeface sKTypeface = ResolveSingleFamilyTypeface(item, bold);
			if (sKTypeface != SKTypeface.Default)
			{
				return sKTypeface;
			}
		}
		return ResolveDefaultTypeface(bold);
	}

	private static SKTypeface CreateTypefaceForTextElement(string? familyList, bool bold, string textElement)
	{
		SKTypeface sKTypeface = ResolveTypeface(familyList, bold);
		if (ContainsGlyphs(sKTypeface, textElement))
		{
			return sKTypeface;
		}
		foreach (string item in ParseFamilyList(familyList))
		{
			SKTypeface sKTypeface2 = ResolveSingleFamilyTypeface(item, bold);
			if (ContainsGlyphs(sKTypeface2, textElement))
			{
				return sKTypeface2;
			}
		}
		if (TryGetFirstCodePoint(textElement, out var codePoint))
		{
			try
			{
				SKTypeface sKTypeface3 = SKFontManager.Default.MatchCharacter(codePoint);
				if (sKTypeface3 != null)
				{
					string familyName = sKTypeface3.FamilyName;
					if (!string.IsNullOrWhiteSpace(familyName))
					{
						SKTypeface sKTypeface4 = ResolveSingleFamilyTypeface(familyName, bold);
						if (ContainsGlyphs(sKTypeface4, textElement))
						{
							return sKTypeface4;
						}
					}
					if (ContainsGlyphs(sKTypeface3, textElement))
					{
						return sKTypeface3;
					}
				}
			}
			catch
			{
			}
		}
		return sKTypeface;
	}

	private static SKTypeface ResolveSingleFamilyTypeface(string familyName, bool bold)
	{
		string text = familyName.Trim();
		string key = $"family|{text}|{bold}";
		if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
		{
			return value;
		}
		SKTypeface sKTypeface = CreateSingleFamilyTypeface(text, bold);
		CachedTypefaces[key] = sKTypeface;
		return sKTypeface;
	}

	private static SKTypeface CreateSingleFamilyTypeface(string familyName, bool bold)
	{
		SKFontStyle style = (bold ? SKFontStyle.Bold : SKFontStyle.Normal);
		if (RegisteredFiles.TryGetValue(familyName, out string value))
		{
			try
			{
				return SKTypeface.FromFile(value);
			}
			catch
			{
			}
		}
		string familyName2 = MapGenericFamily(familyName);
		try
		{
			return SKTypeface.FromFamilyName(familyName2, style) ?? SKTypeface.Default;
		}
		catch
		{
			return SKTypeface.Default;
		}
	}

	private static SKTypeface ResolveDefaultTypeface(bool bold)
	{
		string key = $"default|{bold}";
		if (CachedTypefaces.TryGetValue(key, out SKTypeface value))
		{
			return value;
		}
		SKFontStyle style = (bold ? SKFontStyle.Bold : SKFontStyle.Normal);
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
		string[] array = parts;
		foreach (string part in array)
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
		if (1 == 0)
		{
		}
		string result;
		switch (text)
		{
		case "system-ui":
		case "sans-serif":
			result = null;
			break;
		case "serif":
			result = "Times New Roman";
			break;
		case "monospace":
			result = "Consolas";
			break;
		default:
			result = familyName;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool ContainsGlyphs(SKTypeface typeface, string text)
	{
		try
		{
			return typeface.ContainsGlyphs(text);
		}
		catch
		{
			using SKFont sKFont = new SKFont(typeface);
			return sKFont.ContainsGlyphs(text);
		}
	}

	private static bool TryGetFirstCodePoint(string textElement, out int codePoint)
	{
		codePoint = 0;
		if (string.IsNullOrEmpty(textElement))
		{
			return false;
		}
		using (StringRuneEnumerator stringRuneEnumerator = textElement.EnumerateRunes().GetEnumerator())
		{
			if (stringRuneEnumerator.MoveNext())
			{
				codePoint = stringRuneEnumerator.Current.Value;
				return true;
			}
		}
		return false;
	}
}
