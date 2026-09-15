using System;

namespace Ludots.UI.Browser;

public readonly struct BrowserHitMaskColor : IEquatable<BrowserHitMaskColor>
{
	public static BrowserHitMaskColor Default { get; } = new(0, 255, 1);

	public BrowserHitMaskColor(byte r, byte g, byte b)
	{
		R = r;
		G = g;
		B = b;
	}

	public byte R { get; }

	public byte G { get; }

	public byte B { get; }

	public bool MatchesPremultiplied(byte r, byte g, byte b, byte a)
	{
		return a > 0 && r == R && g == G && b == B;
	}

	public bool Equals(BrowserHitMaskColor other)
	{
		return R == other.R && G == other.G && B == other.B;
	}

	public override bool Equals(object? obj)
	{
		return obj is BrowserHitMaskColor other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(R, G, B);
	}

	public static bool operator ==(BrowserHitMaskColor left, BrowserHitMaskColor right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(BrowserHitMaskColor left, BrowserHitMaskColor right)
	{
		return !left.Equals(right);
	}
}
