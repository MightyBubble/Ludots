using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Browser;

public readonly record struct BrowserSharedBufferReadRequest(
	string BufferId,
	int ByteOffset,
	int ByteLength,
	long Sequence);

public readonly record struct BrowserSharedBufferLiveRegion(
	int ByteOffset,
	int ByteLength,
	long Sequence);

public readonly record struct BrowserSharedBufferNativeRegion(
	string BufferId,
	string MemoryMapName,
	int CapacityBytes,
	int HeaderBytes,
	BrowserSharedBufferLiveRegion[] LiveRegions);

public sealed class BrowserSharedBufferBridge
{
	private readonly object _sync = new();
	private readonly Dictionary<string, Func<BrowserSharedBufferReadRequest, byte[]>> _readers = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BrowserSharedBufferNativeRegion> _nativeRegions = new(StringComparer.Ordinal);

	public event EventHandler? NativeRegionsChanged;

	public void RegisterReader(
		string bufferId,
		Func<BrowserSharedBufferReadRequest, byte[]> reader)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(bufferId));
		}

		ArgumentNullException.ThrowIfNull(reader);
		lock (_sync)
		{
			_readers[bufferId.Trim()] = reader;
		}
	}

	public bool UnregisterReader(string bufferId)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			return false;
		}

		lock (_sync)
		{
			return _readers.Remove(bufferId.Trim());
		}
	}

	public void RegisterNativeRegion(BrowserSharedBufferNativeRegion region)
	{
		BrowserSharedBufferNativeRegion normalizedRegion = NormalizeNativeRegion(region);
		string normalizedBufferId = region.BufferId.Trim();
		lock (_sync)
		{
			_nativeRegions[normalizedBufferId] = normalizedRegion;
		}

		NativeRegionsChanged?.Invoke(this, EventArgs.Empty);
	}

	public bool UnregisterNativeRegion(string bufferId)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			return false;
		}

		bool removed;
		lock (_sync)
		{
			removed = _nativeRegions.Remove(bufferId.Trim());
		}

		if (removed)
		{
			NativeRegionsChanged?.Invoke(this, EventArgs.Empty);
		}

		return removed;
	}

	public void UpdateNativeRegionLiveRegions(
		string bufferId,
		IReadOnlyCollection<BrowserSharedBufferLiveRegion> liveRegions)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(bufferId));
		}

		ArgumentNullException.ThrowIfNull(liveRegions);

		string normalizedBufferId = bufferId.Trim();
		lock (_sync)
		{
			if (!_nativeRegions.TryGetValue(normalizedBufferId, out BrowserSharedBufferNativeRegion region))
			{
				throw new InvalidOperationException($"Shared buffer '{normalizedBufferId}' is not registered for native access.");
			}

			_nativeRegions[normalizedBufferId] = region with
			{
				LiveRegions = NormalizeLiveRegions(region, liveRegions)
			};
		}

		NativeRegionsChanged?.Invoke(this, EventArgs.Empty);
	}

	public BrowserSharedBufferNativeRegion[] GetNativeRegionsSnapshot()
	{
		lock (_sync)
		{
			return _nativeRegions.Values.ToArray();
		}
	}

	public byte[] ReadSharedBuffer(string bufferId, int byteOffset, int byteLength, long sequence)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(bufferId));
		}

		if (byteOffset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(byteOffset), "Byte offset must be non-negative.");
		}

		if (byteLength < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length must be non-negative.");
		}

		string normalizedBufferId = bufferId.Trim();
		Func<BrowserSharedBufferReadRequest, byte[]> reader;
		lock (_sync)
		{
			if (!_readers.TryGetValue(normalizedBufferId, out reader!))
			{
				throw new InvalidOperationException($"Shared buffer '{normalizedBufferId}' is not registered.");
			}
		}

		return reader(new BrowserSharedBufferReadRequest(
			normalizedBufferId,
			byteOffset,
			byteLength,
			sequence));
	}

	private static BrowserSharedBufferNativeRegion NormalizeNativeRegion(BrowserSharedBufferNativeRegion region)
	{
		if (string.IsNullOrWhiteSpace(region.BufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(region));
		}

		if (string.IsNullOrWhiteSpace(region.MemoryMapName))
		{
			throw new ArgumentException("Shared buffer memory map name is required.", nameof(region));
		}

		if (region.CapacityBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(region), "Shared buffer capacity must be positive.");
		}

		if (region.HeaderBytes < 0 || region.HeaderBytes >= region.CapacityBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(region), "Shared buffer header bytes must leave payload capacity.");
		}

		return region with
		{
			BufferId = region.BufferId.Trim(),
			MemoryMapName = region.MemoryMapName.Trim(),
			LiveRegions = NormalizeLiveRegions(region, region.LiveRegions ?? Array.Empty<BrowserSharedBufferLiveRegion>())
		};
	}

	private static BrowserSharedBufferLiveRegion[] NormalizeLiveRegions(
		BrowserSharedBufferNativeRegion region,
		IEnumerable<BrowserSharedBufferLiveRegion> liveRegions)
	{
		var normalized = new List<BrowserSharedBufferLiveRegion>();
		foreach (BrowserSharedBufferLiveRegion liveRegion in liveRegions)
		{
			if (liveRegion.ByteOffset < region.HeaderBytes || liveRegion.ByteOffset > region.CapacityBytes)
			{
				throw new ArgumentOutOfRangeException(nameof(liveRegions), "Live region byte offset is outside the shared-buffer payload region.");
			}

			if (liveRegion.ByteLength < 0 ||
				(long)liveRegion.ByteOffset + liveRegion.ByteLength > region.CapacityBytes)
			{
				throw new ArgumentOutOfRangeException(nameof(liveRegions), "Live region byte range exceeds the shared-buffer capacity.");
			}

			if (liveRegion.Sequence <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(liveRegions), "Live region sequence must be positive.");
			}

			normalized.Add(liveRegion);
		}

		return normalized.ToArray();
	}
}
