using System;
using System.Runtime.InteropServices;
using Ludots.UI.Browser;
using SkiaSharp;

namespace Ludots.UI.Browser.Skia;

public sealed class SkiaBrowserFrameRenderer : IDisposable
{
	private static readonly SKSamplingOptions BrowserSampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

	private readonly SKPaint _paint = new SKPaint
	{
		IsAntialias = false
	};

	private BrowserFrame? _cachedFrame;
	private SKImage? _cachedImage;
	private bool _disposed;

	public void DrawFrame(SKCanvas canvas, SKRect destination, BrowserFrame frame)
	{
		ArgumentNullException.ThrowIfNull(canvas);
		ArgumentNullException.ThrowIfNull(frame);
		ThrowIfDisposed();
		if (destination.Width <= 0.01f || destination.Height <= 0.01f)
		{
			return;
		}

		SKImage image = GetImage(frame);
		canvas.DrawImage(image, destination, BrowserSampling, _paint);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		ReleaseCachedImage();
		_paint.Dispose();
		_disposed = true;
	}

	private SKImage GetImage(BrowserFrame frame)
	{
		if (ReferenceEquals(_cachedFrame, frame) && _cachedImage != null)
		{
			return _cachedImage;
		}

		ReleaseCachedImage();
		_cachedFrame = frame;
		_cachedImage = CreateImage(frame);
		return _cachedImage;
	}

	private static SKImage CreateImage(BrowserFrame frame)
	{
		SKImageInfo imageInfo = new SKImageInfo(
			frame.Viewport.Width,
			frame.Viewport.Height,
			ToColorType(frame.PixelFormat),
			SKAlphaType.Premul);

		if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> segment) || segment.Array == null)
		{
			return SKImage.FromPixelCopy(imageInfo, frame.Pixels.Span, frame.RowBytes);
		}

		GCHandle pin = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
		bool ownsPin = true;
		try
		{
			IntPtr pixels = IntPtr.Add(pin.AddrOfPinnedObject(), segment.Offset);
			using var pixmap = new SKPixmap(imageInfo, pixels, frame.RowBytes);
			SKImage image = SKImage.FromPixels(pixmap, ReleasePinnedPixels, pin);
			ownsPin = false;
			return image;
		}
		finally
		{
			if (ownsPin && pin.IsAllocated)
			{
				pin.Free();
			}
		}
	}

	private void ReleaseCachedImage()
	{
		_cachedImage?.Dispose();
		_cachedImage = null;
		_cachedFrame = null;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(SkiaBrowserFrameRenderer));
		}
	}

	private static void ReleasePinnedPixels(IntPtr pixels, object context)
	{
		if (context is GCHandle handle && handle.IsAllocated)
		{
			handle.Free();
		}
	}

	private static SKColorType ToColorType(BrowserPixelFormat format)
	{
		return format switch
		{
			BrowserPixelFormat.Bgra8888Premultiplied => SKColorType.Bgra8888,
			BrowserPixelFormat.Rgba8888Premultiplied => SKColorType.Rgba8888,
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported browser pixel format.")
		};
	}
}
