using System.IO.MemoryMappedFiles;
using Ludots.UI.Browser;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.Browser;

public sealed record BrowserSharedMemoryTopicBuffer(
	string Topic,
	string BufferId,
	int SchemaId,
	int CapacityBytes,
	int HeaderBytes = WebUiSharedBufferRing.DefaultHeaderBytes);

public sealed record BrowserSharedMemoryBufferInfo(
	string BufferId,
	string Topic,
	int SchemaId,
	string MemoryMapName,
	int CapacityBytes,
	int HeaderBytes);

public sealed class BrowserSharedMemoryBufferStore : IDisposable, IAsyncDisposable
{
	private readonly object _sync = new();
	private readonly BrowserSharedBufferBridge _sharedBuffers;
	private readonly Dictionary<string, SharedMemoryBufferSlot> _buffersById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SharedMemoryBufferSlot> _buffersByTopic = new(StringComparer.Ordinal);
	private bool _disposed;

	public BrowserSharedMemoryBufferStore(BrowserSharedBufferBridge sharedBuffers)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException(
				"Browser shared-memory DataPlane uses named memory-mapped files and is currently supported on Windows hosts.");
		}

		_sharedBuffers = sharedBuffers ?? throw new ArgumentNullException(nameof(sharedBuffers));
	}

	public WebUiSharedBufferDescriptor[] Descriptors
	{
		get
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				return _buffersById.Values.Select(static slot => slot.CreateDescriptor()).ToArray();
			}
		}
	}

	public void AddBuffer(BrowserSharedMemoryTopicBuffer definition)
	{
		ValidateDefinition(definition);
		lock (_sync)
		{
			ThrowIfDisposed();
			if (_buffersById.ContainsKey(definition.BufferId))
			{
				throw new InvalidOperationException($"Shared buffer '{definition.BufferId}' is already registered.");
			}

			if (_buffersByTopic.ContainsKey(definition.Topic))
			{
				throw new InvalidOperationException($"Topic '{definition.Topic}' already has a shared buffer.");
			}

			var slot = SharedMemoryBufferSlot.Create(definition);
			_buffersById.Add(slot.BufferId, slot);
			_buffersByTopic.Add(slot.Topic, slot);
			_sharedBuffers.RegisterReader(slot.BufferId, slot.Read);
		}
	}

	public bool HasTopic(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return false;
		}

		lock (_sync)
		{
			return !_disposed && _buffersByTopic.ContainsKey(topic.Trim());
		}
	}

	public WebUiSharedBufferWriteResult WriteLatestWins(
		string topic,
		ReadOnlySpan<byte> payload,
		long tick)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			throw new ArgumentException("Topic is required.", nameof(topic));
		}

		SharedMemoryBufferSlot slot;
		lock (_sync)
		{
			ThrowIfDisposed();
			if (!_buffersByTopic.TryGetValue(topic.Trim(), out slot!))
			{
				throw new InvalidOperationException($"Topic '{topic.Trim()}' does not have a shared-memory buffer.");
			}
		}

		return slot.WriteLatestWins(payload, tick);
	}

	public BrowserSharedMemoryBufferInfo GetBufferInfo(string bufferId)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(bufferId));
		}

		lock (_sync)
		{
			ThrowIfDisposed();
			if (!_buffersById.TryGetValue(bufferId.Trim(), out SharedMemoryBufferSlot? slot))
			{
				throw new InvalidOperationException($"Shared buffer '{bufferId.Trim()}' is not registered.");
			}

			return slot.Info;
		}
	}

	public void Dispose()
	{
		SharedMemoryBufferSlot[] slots;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			slots = _buffersById.Values.ToArray();
			_buffersById.Clear();
			_buffersByTopic.Clear();
		}

		foreach (SharedMemoryBufferSlot slot in slots)
		{
			_sharedBuffers.UnregisterReader(slot.BufferId);
			slot.Dispose();
		}
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private static void ValidateDefinition(BrowserSharedMemoryTopicBuffer definition)
	{
		if (string.IsNullOrWhiteSpace(definition.Topic))
		{
			throw new ArgumentException("Shared-memory topic is required.", nameof(definition));
		}

		if (string.IsNullOrWhiteSpace(definition.BufferId))
		{
			throw new ArgumentException("Shared-memory buffer id is required.", nameof(definition));
		}

		if (definition.SchemaId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(definition), "Schema id must be positive.");
		}

		if (definition.HeaderBytes < 0 || definition.HeaderBytes >= definition.CapacityBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(definition), "Header bytes must leave payload capacity.");
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(BrowserSharedMemoryBufferStore));
		}
	}

	private sealed class SharedMemoryBufferSlot : IDisposable
	{
		private readonly object _sync = new();
		private readonly MemoryMappedFile _mapping;
		private readonly MemoryMappedViewStream _stream;
		private readonly Dictionary<int, SharedMemoryWriteRegion> _regionsByOffset = new();
		private readonly List<int> _overlappingRegionOffsets = new();
		private int _writeOffset;
		private long _sequence;
		private long _droppedPackets;
		private long _coalescedPackets;
		private bool _disposed;

		private SharedMemoryBufferSlot(
			string bufferId,
			string topic,
			int schemaId,
			int capacityBytes,
			int headerBytes,
			string memoryMapName,
			MemoryMappedFile mapping,
			MemoryMappedViewStream stream)
		{
			BufferId = bufferId;
			Topic = topic;
			SchemaId = schemaId;
			CapacityBytes = capacityBytes;
			HeaderBytes = headerBytes;
			MemoryMapName = memoryMapName;
			_mapping = mapping;
			_stream = stream;
			_writeOffset = HeaderBytes;
		}

		public string BufferId { get; }

		public string Topic { get; }

		public int SchemaId { get; }

		public int CapacityBytes { get; }

		public int HeaderBytes { get; }

		public string MemoryMapName { get; }

		public BrowserSharedMemoryBufferInfo Info => new(
			BufferId,
			Topic,
			SchemaId,
			MemoryMapName,
			CapacityBytes,
			HeaderBytes);

		public static SharedMemoryBufferSlot Create(BrowserSharedMemoryTopicBuffer definition)
		{
			string bufferId = definition.BufferId.Trim();
			string topic = definition.Topic.Trim();
			string memoryMapName = CreateMemoryMapName(bufferId);
			MemoryMappedFile mapping = MemoryMappedFile.CreateNew(
				memoryMapName,
				definition.CapacityBytes,
				MemoryMappedFileAccess.ReadWrite);
			MemoryMappedViewStream stream = mapping.CreateViewStream(
				0,
				definition.CapacityBytes,
				MemoryMappedFileAccess.ReadWrite);
			return new SharedMemoryBufferSlot(
				bufferId,
				topic,
				definition.SchemaId,
				definition.CapacityBytes,
				definition.HeaderBytes,
				memoryMapName,
				mapping,
				stream);
		}

		public WebUiSharedBufferWriteResult WriteLatestWins(ReadOnlySpan<byte> payload, long tick)
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				int dataCapacity = CapacityBytes - HeaderBytes;
				if (payload.Length > dataCapacity)
				{
					_droppedPackets++;
					return new WebUiSharedBufferWriteResult(
						Accepted: false,
						CreateDescriptor(HeaderBytes, 0, tick),
						$"Payload size {payload.Length} exceeds shared buffer capacity {dataCapacity}.");
				}

				if (_writeOffset + payload.Length > CapacityBytes)
				{
					_writeOffset = HeaderBytes;
				}

				int byteOffset = _writeOffset;
				RemoveOverlappingRegions(byteOffset, payload.Length);
				_stream.Position = byteOffset;
				_stream.Write(payload);
				_writeOffset += payload.Length;
				_sequence++;
				_regionsByOffset[byteOffset] = new SharedMemoryWriteRegion(payload.Length, _sequence);
				if (_sequence > 1)
				{
					_coalescedPackets++;
				}

				return new WebUiSharedBufferWriteResult(
					Accepted: true,
					CreateDescriptor(byteOffset, payload.Length, tick),
					Error: string.Empty);
			}
		}

		public WebUiSharedBufferDescriptor CreateDescriptor()
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				return CreateDescriptor(HeaderBytes, 0, tick: 0);
			}
		}

		public byte[] Read(BrowserSharedBufferReadRequest request)
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				if (request.ByteOffset < HeaderBytes || request.ByteOffset > CapacityBytes)
				{
					throw new ArgumentOutOfRangeException(nameof(request), "Shared buffer byte offset is outside the payload region.");
				}

				if (request.ByteLength < 0 || request.ByteOffset + request.ByteLength > CapacityBytes)
				{
					throw new ArgumentOutOfRangeException(nameof(request), "Shared buffer byte range exceeds the buffer capacity.");
				}

				if (request.Sequence <= 0 || request.Sequence > _sequence)
				{
					throw new InvalidOperationException(
						$"Shared buffer '{BufferId}' sequence {request.Sequence} is not available.");
				}

				if (!_regionsByOffset.TryGetValue(request.ByteOffset, out SharedMemoryWriteRegion region) ||
					region.ByteLength != request.ByteLength ||
					region.Sequence != request.Sequence)
				{
					throw new InvalidOperationException(
						$"Shared buffer '{BufferId}' range is no longer available for sequence {request.Sequence}.");
				}

				byte[] bytes = new byte[request.ByteLength];
				_stream.Position = request.ByteOffset;
				_stream.ReadExactly(bytes);
				return bytes;
			}
		}

		private void RemoveOverlappingRegions(int byteOffset, int byteLength)
		{
			int endExclusive = byteOffset + byteLength;
			_overlappingRegionOffsets.Clear();
			foreach (KeyValuePair<int, SharedMemoryWriteRegion> region in _regionsByOffset)
			{
				int regionEndExclusive = region.Key + region.Value.ByteLength;
				if (byteOffset < regionEndExclusive && region.Key < endExclusive)
				{
					_overlappingRegionOffsets.Add(region.Key);
				}
			}

			for (int i = 0; i < _overlappingRegionOffsets.Count; i++)
			{
				_regionsByOffset.Remove(_overlappingRegionOffsets[i]);
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

			_stream.Dispose();
			_mapping.Dispose();
		}

		private WebUiSharedBufferDescriptor CreateDescriptor(int byteOffset, int byteLength, long tick)
		{
			return new WebUiSharedBufferDescriptor(
				BufferId,
				Topic,
				SchemaId,
				WebUiSharedBufferDescriptor.RingBufferLayout,
				CapacityBytes,
				HeaderBytes,
				byteOffset,
				byteLength,
				_sequence,
				tick,
				_droppedPackets,
				_coalescedPackets);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(BrowserSharedMemoryBufferStore));
			}
		}

		private static string CreateMemoryMapName(string bufferId)
		{
			string normalized = string.Concat(bufferId.Select(static character =>
				char.IsLetterOrDigit(character) ? character : '_'));
			return $"Ludots_WebUI_{normalized}_{Guid.NewGuid():N}";
		}

		private readonly record struct SharedMemoryWriteRegion(int ByteLength, long Sequence);
	}
}
