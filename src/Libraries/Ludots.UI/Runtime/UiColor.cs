using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Ludots.UI.Runtime;

/// <summary>
/// Platform-agnostic RGBA color. Decouples the UI model layer from any
/// specific rendering backend (SkiaSharp, etc.).
/// </summary>
public readonly struct UiColor : IEquatable<UiColor>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    public byte Red => R;
    public byte Green => G;
    public byte Blue => B;
    public byte Alpha => A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UiColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static readonly UiColor Transparent = new(0, 0, 0, 0);
    public static readonly UiColor White = new(255, 255, 255, 255);
    public static readonly UiColor Black = new(0, 0, 0, 255);
    public static readonly UiColor PresetRed = new(255, 0, 0);
    public static readonly UiColor PresetGreen = new(0, 128, 0);
    public static readonly UiColor PresetBlue = new(0, 0, 255);
    public static readonly UiColor Cyan = new(0, 255, 255);
    public static readonly UiColor Gold = new(255, 215, 0);
    public static readonly UiColor LightGray = new(211, 211, 211);
    public static readonly UiColor DimGray = new(105, 105, 105);
    public static readonly UiColor DarkSlateGray = new(47, 79, 79);

    public UiColor WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>
    /// Parse hex color strings: "#RRGGBB", "#AARRGGBB", "RRGGBB", "AARRGGBB".
    /// Matches SkiaSharp's SKColor.TryParse format (alpha-first in 8-char hex).
    /// </summary>
    public static bool TryParse(string? value, out UiColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span.Slice(1);

        if (span.Length == 6)
        {
            if (TryParseHexByte(span.Slice(0, 2), out byte r) &&
                TryParseHexByte(span.Slice(2, 2), out byte g) &&
                TryParseHexByte(span.Slice(4, 2), out byte b))
            {
                color = new UiColor(r, g, b, 255);
                return true;
            }
        }
        else if (span.Length == 8)
        {
            if (TryParseHexByte(span.Slice(0, 2), out byte a) &&
                TryParseHexByte(span.Slice(2, 2), out byte r) &&
                TryParseHexByte(span.Slice(4, 2), out byte g) &&
                TryParseHexByte(span.Slice(6, 2), out byte b))
            {
                color = new UiColor(r, g, b, a);
                return true;
            }
        }
        else if (span.Length == 3)
        {
            if (TryParseHexNibble(span[0], out byte rn) &&
                TryParseHexNibble(span[1], out byte gn) &&
                TryParseHexNibble(span[2], out byte bn))
            {
                byte r = (byte)(rn << 4 | rn);
                byte g = (byte)(gn << 4 | gn);
                byte b = (byte)(bn << 4 | bn);
                color = new UiColor(r, g, b, 255);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> hex, out byte result)
    {
        return byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseHexNibble(char c, out byte result)
    {
        result = 0;
        if (c >= '0' && c <= '9') { result = (byte)(c - '0'); return true; }
        if (c >= 'a' && c <= 'f') { result = (byte)(c - 'a' + 10); return true; }
        if (c >= 'A' && c <= 'F') { result = (byte)(c - 'A' + 10); return true; }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(UiColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is UiColor c && Equals(c);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UiColor left, UiColor right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UiColor left, UiColor right) => !left.Equals(right);

    public override string ToString() =>
        A == 255
            ? $"#{R:X2}{G:X2}{B:X2}"
            : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
