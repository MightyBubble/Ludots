using System;

namespace Ludots.UI.Browser;

public static class BrowserHitMaskComposite
{
	public static void ApplyToPremultipliedBuffer(
		Span<byte> pixels,
		int rowBytes,
		int width,
		int height,
		BrowserPixelFormat format,
		BrowserHitMaskColor mask)
	{
		bool bgra = format switch
		{
			BrowserPixelFormat.Bgra8888Premultiplied => true,
			BrowserPixelFormat.Rgba8888Premultiplied => false,
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported browser pixel format.")
		};

		if (width < 0 || height < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Hit-mask buffer dimensions must be non-negative.");
		}

		int minRowBytes = checked(width * BrowserFrameBuffer.BytesPerPixel);
		if (rowBytes < minRowBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(rowBytes), "Hit-mask row bytes are smaller than the width.");
		}

		int required = checked(rowBytes * height);
		if (pixels.Length < required)
		{
			throw new ArgumentException("Hit-mask pixel buffer is smaller than rowBytes * height.", nameof(pixels));
		}

		for (int y = 0; y < height; y++)
		{
			int row = y * rowBytes;
			for (int x = 0; x < width; x++)
			{
				int offset = row + (x * BrowserFrameBuffer.BytesPerPixel);
				ClearVisualIfMask(pixels.Slice(offset, BrowserFrameBuffer.BytesPerPixel), mask, bgra);
			}
		}
	}

	public static void ApplyToBgraBuffer(
		Span<byte> pixels,
		int rowBytes,
		int width,
		int height,
		BrowserHitMaskColor mask)
	{
		ApplyToPremultipliedBuffer(
			pixels,
			rowBytes,
			width,
			height,
			BrowserPixelFormat.Bgra8888Premultiplied,
			mask);
	}

	public static void WriteVisualBgra(
		Span<byte> destination,
		byte b,
		byte g,
		byte r,
		byte a,
		BrowserHitMaskColor? mask)
	{
		WriteVisual(destination, b, g, r, a, mask, bgra: true);
	}

	public static void WriteVisualRgba(
		Span<byte> destination,
		byte r,
		byte g,
		byte b,
		byte a,
		BrowserHitMaskColor? mask)
	{
		WriteVisual(destination, b, g, r, a, mask, bgra: false);
	}

	private static void WriteVisual(
		Span<byte> destination,
		byte b,
		byte g,
		byte r,
		byte a,
		BrowserHitMaskColor? mask,
		bool bgra)
	{
		if (destination.Length < BrowserFrameBuffer.BytesPerPixel)
		{
			throw new ArgumentException("Pixel destination must hold one BGRA/RGBA pixel.", nameof(destination));
		}

		if (mask is { } hitMask && hitMask.MatchesPremultiplied(r, g, b, a))
		{
			destination[0] = 0;
			destination[1] = 0;
			destination[2] = 0;
			destination[3] = 0;
			return;
		}

		if (bgra)
		{
			destination[0] = b;
			destination[1] = g;
			destination[2] = r;
			destination[3] = a;
			return;
		}

		destination[0] = r;
		destination[1] = g;
		destination[2] = b;
		destination[3] = a;
	}

	private static void ClearVisualIfMask(Span<byte> pixel, BrowserHitMaskColor mask, bool bgra)
	{
		byte b;
		byte g;
		byte r;
		byte a;
		if (bgra)
		{
			b = pixel[0];
			g = pixel[1];
			r = pixel[2];
			a = pixel[3];
		}
		else
		{
			r = pixel[0];
			g = pixel[1];
			b = pixel[2];
			a = pixel[3];
		}

		if (mask.MatchesPremultiplied(r, g, b, a))
		{
			pixel[0] = 0;
			pixel[1] = 0;
			pixel[2] = 0;
			pixel[3] = 0;
		}
	}
}
