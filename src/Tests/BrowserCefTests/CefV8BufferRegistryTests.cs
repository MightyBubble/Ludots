using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefV8BufferRegistryTests
{
	private const uint Magic = 0x3856444c;
	private const int Version = 2;
	private const int HeaderBytes = 32;
	private const int BufferRecordBytes = 544;
	private const int LiveRegionRecordBytes = 272;
	private const int BufferIdBytes = 256;
	private const int MemoryMapNameBytes = 256;

	[Test]
	public void Write_StoresProviderPrivateLiveRegionsBesideBufferRecords()
	{
		using var registry = new CefV8BufferRegistry();

		registry.Write(new[]
		{
			new BrowserSharedBufferNativeRegion(
				"buffer-a",
				"Ludots_WebUI_buffer_a",
				4096,
				64,
				new[]
				{
					new BrowserSharedBufferLiveRegion(64, 128, 1),
					new BrowserSharedBufferLiveRegion(192, 128, 2)
				})
		});

		using MemoryMappedFile mapping = MemoryMappedFile.OpenExisting(
			registry.MemoryMapName,
			MemoryMappedFileRights.Read);
		using MemoryMappedViewAccessor accessor = mapping.CreateViewAccessor(
			0,
			HeaderBytes + BufferRecordBytes + (LiveRegionRecordBytes * 2),
			MemoryMappedFileAccess.Read);
		byte[] bytes = new byte[HeaderBytes + BufferRecordBytes + (LiveRegionRecordBytes * 2)];
		accessor.ReadArray(0, bytes, 0, bytes.Length);

		Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(Magic));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(Version));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), Is.EqualTo(1));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), Is.EqualTo(2));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)), Is.EqualTo(HeaderBytes));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)), Is.EqualTo(HeaderBytes + BufferRecordBytes));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)), Is.EqualTo(BufferRecordBytes));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)), Is.EqualTo(LiveRegionRecordBytes));

		Assert.That(ReadUtf8(bytes.AsSpan(HeaderBytes, BufferIdBytes)), Is.EqualTo("buffer-a"));
		Assert.That(ReadUtf8(bytes.AsSpan(HeaderBytes + BufferIdBytes, MemoryMapNameBytes)), Is.EqualTo("Ludots_WebUI_buffer_a"));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(HeaderBytes + BufferIdBytes + MemoryMapNameBytes)), Is.EqualTo(4096));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(HeaderBytes + BufferIdBytes + MemoryMapNameBytes + sizeof(int))), Is.EqualTo(64));

		int firstRegionOffset = HeaderBytes + BufferRecordBytes;
		Assert.That(ReadUtf8(bytes.AsSpan(firstRegionOffset, BufferIdBytes)), Is.EqualTo("buffer-a"));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(firstRegionOffset + BufferIdBytes)), Is.EqualTo(64));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(firstRegionOffset + BufferIdBytes + sizeof(int))), Is.EqualTo(128));
		Assert.That(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(firstRegionOffset + BufferIdBytes + (sizeof(int) * 2))), Is.EqualTo(1));

		int secondRegionOffset = firstRegionOffset + LiveRegionRecordBytes;
		Assert.That(ReadUtf8(bytes.AsSpan(secondRegionOffset, BufferIdBytes)), Is.EqualTo("buffer-a"));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(secondRegionOffset + BufferIdBytes)), Is.EqualTo(192));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(secondRegionOffset + BufferIdBytes + sizeof(int))), Is.EqualTo(128));
		Assert.That(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(secondRegionOffset + BufferIdBytes + (sizeof(int) * 2))), Is.EqualTo(2));
	}

	private static string ReadUtf8(ReadOnlySpan<byte> bytes)
	{
		int length = bytes.IndexOf((byte)0);
		if (length < 0)
		{
			length = bytes.Length;
		}

		return System.Text.Encoding.UTF8.GetString(bytes[..length]);
	}
}
