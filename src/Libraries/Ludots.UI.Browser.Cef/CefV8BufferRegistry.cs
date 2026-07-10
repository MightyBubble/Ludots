using System;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefV8BufferRegistry : IDisposable
{
	public const string RegistryEnvironmentVariableName = "LUDOTS_CEF_V8_BUFFER_REGISTRY";
	private const int RegistryBytes = 64 * 1024;
	private const int HeaderBytes = 32;
	private const int BufferRecordBytes = 544;
	private const int LiveRegionRecordBytes = 272;
	private const int BufferIdBytes = 256;
	private const int MemoryMapNameBytes = 256;
	private const uint Magic = 0x3856444c; // LDV8
	private const int Version = 2;

	private readonly object _sync = new();
	private readonly string _memoryMapName = $"Ludots_CefV8BufferRegistry_{Guid.NewGuid():N}";
	private readonly MemoryMappedFile _mapping;
	private readonly MemoryMappedViewAccessor _accessor;
	private bool _disposed;

	public CefV8BufferRegistry()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Ludots CEF V8 buffer registry uses named memory-mapped files and is supported on Windows hosts.");
		}

		_mapping = MemoryMappedFile.CreateNew(
			_memoryMapName,
			RegistryBytes,
			MemoryMappedFileAccess.ReadWrite);
		_accessor = _mapping.CreateViewAccessor(0, RegistryBytes, MemoryMappedFileAccess.ReadWrite);
		Write(Array.Empty<BrowserSharedBufferNativeRegion>());
	}

	public string MemoryMapName => _memoryMapName;

	public void Write(BrowserSharedBufferNativeRegion[] regions)
	{
		ArgumentNullException.ThrowIfNull(regions);
		int liveRegionCount = regions.Sum(static region => region.LiveRegions?.Length ?? 0);
		int byteCount = HeaderBytes +
			(regions.Length * BufferRecordBytes) +
			(liveRegionCount * LiveRegionRecordBytes);
		if (byteCount > RegistryBytes)
		{
			throw new InvalidOperationException(
				$"Native V8 buffer registry payload is too large: {regions.Length} buffers and {liveRegionCount} live regions.");
		}

		lock (_sync)
		{
			ThrowIfDisposed();
			byte[] bytes = new byte[RegistryBytes];
			BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Version);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), regions.Length);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), liveRegionCount);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), HeaderBytes);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), HeaderBytes + (regions.Length * BufferRecordBytes));
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), BufferRecordBytes);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), LiveRegionRecordBytes);
			for (int i = 0; i < regions.Length; i++)
			{
				int offset = HeaderBytes + (i * BufferRecordBytes);
				BrowserSharedBufferNativeRegion region = regions[i];
				WriteUtf8(bytes.AsSpan(offset, BufferIdBytes), region.BufferId);
				WriteUtf8(bytes.AsSpan(offset + BufferIdBytes, MemoryMapNameBytes), region.MemoryMapName);
				BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + BufferIdBytes + MemoryMapNameBytes), region.CapacityBytes);
				BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + BufferIdBytes + MemoryMapNameBytes + sizeof(int)), region.HeaderBytes);
			}

			int liveRegionOffset = HeaderBytes + (regions.Length * BufferRecordBytes);
			for (int i = 0; i < regions.Length; i++)
			{
				BrowserSharedBufferNativeRegion region = regions[i];
				BrowserSharedBufferLiveRegion[] liveRegions = region.LiveRegions ?? Array.Empty<BrowserSharedBufferLiveRegion>();
				for (int j = 0; j < liveRegions.Length; j++)
				{
					BrowserSharedBufferLiveRegion liveRegion = liveRegions[j];
					WriteUtf8(bytes.AsSpan(liveRegionOffset, BufferIdBytes), region.BufferId);
					BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(liveRegionOffset + BufferIdBytes), liveRegion.ByteOffset);
					BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(liveRegionOffset + BufferIdBytes + sizeof(int)), liveRegion.ByteLength);
					BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(liveRegionOffset + BufferIdBytes + (sizeof(int) * 2)), liveRegion.Sequence);
					liveRegionOffset += LiveRegionRecordBytes;
				}
			}

			_accessor.WriteArray(0, bytes, 0, bytes.Length);
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		_accessor.Dispose();
		_mapping.Dispose();
	}

	private static void WriteUtf8(Span<byte> destination, string value)
	{
		destination.Clear();
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		int byteCount = Encoding.UTF8.GetByteCount(value);
		if (byteCount >= destination.Length)
		{
			throw new InvalidOperationException($"Native V8 buffer registry value is too long: {value}");
		}

		Encoding.UTF8.GetBytes(value, destination);
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CefV8BufferRegistry));
		}
	}
}
