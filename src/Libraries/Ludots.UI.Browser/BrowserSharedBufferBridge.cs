using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public readonly record struct BrowserSharedBufferReadRequest(
	string BufferId,
	int ByteOffset,
	int ByteLength,
	long Sequence);

public sealed class BrowserSharedBufferBridge
{
	private readonly object _sync = new();
	private readonly Dictionary<string, Func<BrowserSharedBufferReadRequest, byte[]>> _readers = new(StringComparer.Ordinal);

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
}
