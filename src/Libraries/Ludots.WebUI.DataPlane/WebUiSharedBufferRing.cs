namespace Ludots.WebUI.DataPlane;

public sealed record WebUiSharedBufferDescriptor(
	string BufferId,
	string Topic,
	int SchemaId,
	string Layout,
	int CapacityBytes,
	int HeaderBytes,
	int ByteOffset,
	int ByteLength,
	long Sequence,
	long Tick,
	long DroppedPackets,
	long CoalescedPackets)
{
	public const string RingBufferLayout = "ring-buffer";
}

public readonly record struct WebUiSharedBufferWriteResult(
	bool Accepted,
	WebUiSharedBufferDescriptor Descriptor,
	string Error);

public sealed class WebUiSharedBufferRing
{
	private readonly object _sync = new();
	private readonly Memory<byte> _storage;
	private readonly string _bufferId;
	private readonly string _topic;
	private readonly int _schemaId;
	private int _writeOffset;
	private long _sequence;
	private long _droppedPackets;
	private long _coalescedPackets;

	public const int DefaultHeaderBytes = 64;

	public WebUiSharedBufferRing(
		string bufferId,
		string topic,
		int schemaId,
		Memory<byte> storage,
		int headerBytes = DefaultHeaderBytes)
	{
		if (string.IsNullOrWhiteSpace(bufferId))
		{
			throw new ArgumentException("Shared buffer id is required.", nameof(bufferId));
		}

		if (string.IsNullOrWhiteSpace(topic))
		{
			throw new ArgumentException("Shared buffer topic is required.", nameof(topic));
		}

		if (schemaId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(schemaId), "Schema id must be positive.");
		}

		if (headerBytes < 0 || headerBytes >= storage.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(headerBytes), "Header bytes must leave payload capacity.");
		}

		_bufferId = bufferId.Trim();
		_topic = topic.Trim();
		_schemaId = schemaId;
		_storage = storage;
		HeaderBytes = headerBytes;
		_writeOffset = headerBytes;
	}

	public int CapacityBytes => _storage.Length;

	public int HeaderBytes { get; }

	public WebUiSharedBufferWriteResult WriteLatestWins(ReadOnlySpan<byte> payload, long tick)
	{
		lock (_sync)
		{
			int dataCapacity = CapacityBytes - HeaderBytes;
			if (payload.Length > dataCapacity)
			{
				_droppedPackets++;
				return new WebUiSharedBufferWriteResult(
					Accepted: false,
					CreateDescriptor(byteOffset: HeaderBytes, byteLength: 0, tick),
					$"Payload size {payload.Length} exceeds shared buffer capacity {dataCapacity}.");
			}

			if (_writeOffset + payload.Length > CapacityBytes)
			{
				_writeOffset = HeaderBytes;
			}

			int byteOffset = _writeOffset;
			payload.CopyTo(_storage.Span.Slice(byteOffset, payload.Length));
			_writeOffset += payload.Length;
			_sequence++;
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
			return CreateDescriptor(HeaderBytes, 0, tick: 0);
		}
	}

	private WebUiSharedBufferDescriptor CreateDescriptor(int byteOffset, int byteLength, long tick)
	{
		return new WebUiSharedBufferDescriptor(
			_bufferId,
			_topic,
			_schemaId,
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
}
