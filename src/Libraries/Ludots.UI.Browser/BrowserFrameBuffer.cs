using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public sealed class BrowserFrameBuffer
{
	public const int BytesPerPixel = 4;

	private readonly object _sync = new object();
	private readonly byte[] _pixels;
	private readonly List<BrowserDirtyRect> _dirtyRects = new List<BrowserDirtyRect>();

	private long _sequence;

	public BrowserFrameBuffer(BrowserViewport viewport, BrowserPixelFormat pixelFormat)
	{
		ValidateViewport(viewport);
		Viewport = viewport;
		PixelFormat = pixelFormat;
		RowBytes = checked(viewport.Width * BytesPerPixel);
		_pixels = new byte[checked(RowBytes * viewport.Height)];
	}

	public BrowserViewport Viewport { get; }

	public BrowserPixelFormat PixelFormat { get; }

	public int RowBytes { get; }

	public long Sequence
	{
		get
		{
			lock (_sync)
			{
				return _sequence;
			}
		}
	}

	public void ApplyFullFrame(ReadOnlySpan<byte> source)
	{
		if (source.Length < _pixels.Length)
		{
			throw new ArgumentException("Source pixel buffer is smaller than the browser frame buffer.", nameof(source));
		}

		lock (_sync)
		{
			source.Slice(0, _pixels.Length).CopyTo(_pixels);
			_dirtyRects.Clear();
			_dirtyRects.Add(new BrowserDirtyRect(0, 0, Viewport.Width, Viewport.Height));
			_sequence++;
		}
	}

	public void ApplyDirtyFrame(ReadOnlySpan<byte> source, int sourceRowBytes, BrowserDirtyRect dirtyRect)
	{
		ValidateRect(dirtyRect);
		if (sourceRowBytes < RowBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceRowBytes), "Source row bytes are smaller than the viewport width.");
		}
		if (source.Length < checked(sourceRowBytes * Viewport.Height))
		{
			throw new ArgumentException("Source pixel buffer is smaller than sourceRowBytes * height.", nameof(source));
		}

		lock (_sync)
		{
			CopyRect(source, sourceRowBytes, dirtyRect);
			_dirtyRects.Clear();
			_dirtyRects.Add(dirtyRect);
			_sequence++;
		}
	}

	public unsafe void ApplyFullFrame(IntPtr sourceBuffer)
	{
		if (sourceBuffer == IntPtr.Zero)
		{
			throw new ArgumentNullException(nameof(sourceBuffer));
		}

		lock (_sync)
		{
			fixed (byte* target = _pixels)
			{
				Buffer.MemoryCopy((void*)sourceBuffer, target, _pixels.Length, _pixels.Length);
			}

			_dirtyRects.Clear();
			_dirtyRects.Add(new BrowserDirtyRect(0, 0, Viewport.Width, Viewport.Height));
			_sequence++;
		}
	}

	public unsafe void ApplyDirtyFrame(IntPtr sourceBuffer, int sourceRowBytes, BrowserDirtyRect dirtyRect)
	{
		ValidateRect(dirtyRect);
		if (sourceBuffer == IntPtr.Zero)
		{
			throw new ArgumentNullException(nameof(sourceBuffer));
		}
		if (sourceRowBytes < RowBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceRowBytes), "Source row bytes are smaller than the viewport width.");
		}

		lock (_sync)
		{
			CopyRect((byte*)sourceBuffer, sourceRowBytes, dirtyRect);
			_dirtyRects.Clear();
			_dirtyRects.Add(dirtyRect);
			_sequence++;
		}
	}

	public void ApplyDirtyFrame(ReadOnlySpan<byte> source, int sourceRowBytes, IReadOnlyList<BrowserDirtyRect> dirtyRects)
	{
		ArgumentNullException.ThrowIfNull(dirtyRects);
		if (sourceRowBytes < RowBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceRowBytes), "Source row bytes are smaller than the viewport width.");
		}
		if (source.Length < checked(sourceRowBytes * Viewport.Height))
		{
			throw new ArgumentException("Source pixel buffer is smaller than sourceRowBytes * height.", nameof(source));
		}
		if (dirtyRects.Count == 0)
		{
			return;
		}

		lock (_sync)
		{
			_dirtyRects.Clear();
			foreach (BrowserDirtyRect rect in dirtyRects)
			{
				ValidateRect(rect);
				CopyRect(source, sourceRowBytes, rect);
				_dirtyRects.Add(rect);
			}
			_sequence++;
		}
	}

	public BrowserFrame Snapshot()
	{
		lock (_sync)
		{
			return new BrowserFrame(
				Viewport,
				PixelFormat,
				(byte[])_pixels.Clone(),
				RowBytes,
				_dirtyRects.ToArray(),
				_sequence);
		}
	}

	public BrowserFrameReadyEventArgs CreateFrameReadyEventArgs()
	{
		lock (_sync)
		{
			return new BrowserFrameReadyEventArgs(
				Viewport,
				PixelFormat,
				_dirtyRects.ToArray(),
				_sequence);
		}
	}

	public void ReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
	{
		ArgumentNullException.ThrowIfNull(readFrame);
		lock (_sync)
		{
			var frame = new BrowserFrameAccess(
				Viewport,
				PixelFormat,
				_pixels,
				RowBytes,
				_dirtyRects,
				_sequence);
			readFrame(in frame, state);
		}
	}

	private void CopyRect(ReadOnlySpan<byte> source, int sourceRowBytes, BrowserDirtyRect rect)
	{
		int rowLength = checked(rect.Width * BytesPerPixel);
		for (int row = 0; row < rect.Height; row++)
		{
			int sourceOffset = checked(((rect.Y + row) * sourceRowBytes) + (rect.X * BytesPerPixel));
			int targetOffset = checked(((rect.Y + row) * RowBytes) + (rect.X * BytesPerPixel));
			source.Slice(sourceOffset, rowLength).CopyTo(_pixels.AsSpan(targetOffset, rowLength));
		}
	}

	private unsafe void CopyRect(byte* source, int sourceRowBytes, BrowserDirtyRect rect)
	{
		int rowLength = checked(rect.Width * BytesPerPixel);
		fixed (byte* targetBase = _pixels)
		{
			for (int row = 0; row < rect.Height; row++)
			{
				byte* sourceRow = source + checked(((rect.Y + row) * sourceRowBytes) + (rect.X * BytesPerPixel));
				byte* targetRow = targetBase + checked(((rect.Y + row) * RowBytes) + (rect.X * BytesPerPixel));
				Buffer.MemoryCopy(sourceRow, targetRow, rowLength, rowLength);
			}
		}
	}

	private void ValidateRect(BrowserDirtyRect rect)
	{
		if (rect.Width <= 0 || rect.Height <= 0 || rect.Right > Viewport.Width || rect.Bottom > Viewport.Height)
		{
			throw new ArgumentOutOfRangeException(nameof(rect), "Dirty rect must fit inside the browser viewport.");
		}
	}

	private static void ValidateViewport(BrowserViewport viewport)
	{
		if (viewport.Width <= 0 || viewport.Height <= 0 || viewport.DeviceScaleFactor <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(viewport), "Browser frame buffer viewport must be valid.");
		}
	}
}
