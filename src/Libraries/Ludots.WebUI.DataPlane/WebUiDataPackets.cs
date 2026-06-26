using System.Text;
using System.Text.Json;

namespace Ludots.WebUI.DataPlane;

public enum WebUiPacketKind : byte
{
	Control = 1,
	Snapshot = 2,
	Delta = 3,
	Command = 4,
	CommandAck = 5,
	CommandError = 6,
	Diagnostics = 7,
	BinaryChunk = 8,
}

public enum WebUiDeliverySemantics : byte
{
	ReliableOrdered = 1,
	LatestWins = 2,
}

public sealed record WebUiTransportCapabilities(
	bool SupportsBinary,
	bool SupportsSharedMemory,
	bool SupportsReliableOrdered,
	bool SupportsLatestWins,
	int MaxPacketBytes)
{
	public string ModeName { get; init; } = SupportsSharedMemory
		? "shared-memory"
		: SupportsBinary
			? "binary"
			: "message";

	public bool SupportsControlMessages { get; init; } = true;

	public bool SupportsBase64Chunks { get; init; } = !SupportsBinary;

	public bool SupportsChunking { get; init; } = !SupportsBinary;

	public int ChunkSize { get; init; } = MaxPacketBytes;

	public int ExpectedManagedCopiesPerPayload { get; init; } = SupportsSharedMemory ? 0 : SupportsBinary ? 1 : 2;

	public string[] DeliverySemantics { get; init; } =
		BuildDeliverySemantics(SupportsReliableOrdered, SupportsLatestWins);

	public WebUiSharedBufferDescriptor[] SharedBuffers { get; init; } = Array.Empty<WebUiSharedBufferDescriptor>();

	public static WebUiTransportCapabilities StringBridge(int maxPacketBytes = 1024 * 1024)
	{
		return MessageBridge(maxPacketBytes);
	}

	public static WebUiTransportCapabilities MessageBridge(
		int maxPacketBytes = 1024 * 1024,
		int chunkSize = 64 * 1024,
		int expectedManagedCopiesPerPayload = 2)
	{
		return new WebUiTransportCapabilities(
			SupportsBinary: false,
			SupportsSharedMemory: false,
			SupportsReliableOrdered: true,
			SupportsLatestWins: true,
			MaxPacketBytes: Math.Max(1, maxPacketBytes))
		{
			ModeName = "message",
			SupportsBase64Chunks = true,
			SupportsChunking = true,
			ChunkSize = Math.Max(1, chunkSize),
			ExpectedManagedCopiesPerPayload = Math.Max(1, expectedManagedCopiesPerPayload)
		};
	}

	public static WebUiTransportCapabilities BinaryBridge(
		int maxPacketBytes = 16 * 1024 * 1024,
		int expectedManagedCopiesPerPayload = 1)
	{
		return new WebUiTransportCapabilities(
			SupportsBinary: true,
			SupportsSharedMemory: false,
			SupportsReliableOrdered: true,
			SupportsLatestWins: true,
			MaxPacketBytes: Math.Max(1, maxPacketBytes))
		{
			ModeName = "binary",
			SupportsBase64Chunks = false,
			SupportsChunking = true,
			ChunkSize = Math.Max(1, maxPacketBytes),
			ExpectedManagedCopiesPerPayload = Math.Max(0, expectedManagedCopiesPerPayload)
		};
	}

	public static WebUiTransportCapabilities SharedMemory(
		int maxPacketBytes = 64 * 1024 * 1024,
		int chunkSize = 64 * 1024 * 1024,
		IReadOnlyCollection<WebUiSharedBufferDescriptor>? sharedBuffers = null)
	{
		return new WebUiTransportCapabilities(
			SupportsBinary: true,
			SupportsSharedMemory: true,
			SupportsReliableOrdered: true,
			SupportsLatestWins: true,
			MaxPacketBytes: Math.Max(1, maxPacketBytes))
		{
			ModeName = "shared-memory",
			SupportsBase64Chunks = false,
			SupportsChunking = false,
			ChunkSize = Math.Max(1, chunkSize),
			ExpectedManagedCopiesPerPayload = 0,
			SharedBuffers = sharedBuffers?.ToArray() ?? Array.Empty<WebUiSharedBufferDescriptor>()
		};
	}

	public static WebUiTransportCapabilities MockPreview(int maxPacketBytes = 1024 * 1024)
	{
		return MessageBridge(maxPacketBytes) with
		{
			ModeName = "mock",
			ExpectedManagedCopiesPerPayload = 2
		};
	}

	public bool Satisfies(string capabilityName)
	{
		if (string.IsNullOrWhiteSpace(capabilityName))
		{
			return true;
		}

		return NormalizeCapabilityName(capabilityName) switch
		{
			"message" => string.Equals(ModeName, "message", StringComparison.Ordinal) ||
				string.Equals(ModeName, "mock", StringComparison.Ordinal),
			"mock" => string.Equals(ModeName, "mock", StringComparison.Ordinal),
			"control" or "control-message" or "control-messages" => SupportsControlMessages,
			"binary" or "binary-native" => SupportsBinary,
			"binary-base64" or "base64" or "base64-chunks" => SupportsBase64Chunks,
			"chunking" => SupportsChunking,
			"shared-memory" or "sharedmemory" => SupportsSharedMemory,
			"shared-buffer" or "shared-buffer-descriptor" or "shared-buffer-descriptors" => SupportsSharedMemory && SharedBuffers.Length > 0,
			"reliable-ordered" => SupportsReliableOrdered,
			"latest-wins" => SupportsLatestWins,
			_ => false
		};
	}

	private static string[] BuildDeliverySemantics(bool supportsReliableOrdered, bool supportsLatestWins)
	{
		var values = new List<string>(2);
		if (supportsReliableOrdered)
		{
			values.Add(nameof(WebUiDeliverySemantics.ReliableOrdered));
		}

		if (supportsLatestWins)
		{
			values.Add(nameof(WebUiDeliverySemantics.LatestWins));
		}

		return values.ToArray();
	}

	private static string NormalizeCapabilityName(string value)
	{
		return value.Trim().Replace('.', '-').Replace('_', '-').ToLowerInvariant();
	}
}

public sealed record WebUiInboundPacket(
	string SessionId,
	string Topic,
	WebUiPacketKind Kind,
	WebUiDeliverySemantics Delivery,
	ReadOnlyMemory<byte> Payload,
	string? ContentType = null,
	long RequestId = 0,
	long ClientSeq = 0);

public sealed record WebUiOutboundPacket(
	string SessionId,
	string Topic,
	WebUiPacketKind Kind,
	WebUiDeliverySemantics Delivery,
	ReadOnlyMemory<byte> Payload,
	string? ContentType = null,
	long RequestId = 0,
	long ClientSeq = 0);

public interface IWebUiDataTransport : IAsyncDisposable
{
	WebUiTransportCapabilities Capabilities { get; }

	event EventHandler<WebUiInboundPacket>? PacketReceived;

	ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default);
}

public sealed record WebUiControlEnvelope(
	int SchemaVersion,
	string SessionId,
	long RequestId,
	string Kind,
	string Topic,
	JsonElement Payload);

public static class WebUiDataPlaneProtocol
{
	public const int CurrentSchemaVersion = 1;
	public const string ControlContentType = "application/json+ludots-dataplane-control";
	public const string BinaryContentType = "application/octet-stream";

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

	public static byte[] SerializeControlEnvelope(WebUiControlEnvelope envelope)
	{
		if (envelope.SchemaVersion != CurrentSchemaVersion)
		{
			throw new InvalidOperationException($"Unsupported WebUI DataPlane schema version {envelope.SchemaVersion}.");
		}

		return JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
	}

	public static bool TryParseControlEnvelope(ReadOnlySpan<byte> payload, out WebUiControlEnvelope envelope, out string error)
	{
		envelope = default!;
		error = string.Empty;

		try
		{
			WebUiControlEnvelope? parsed = JsonSerializer.Deserialize<WebUiControlEnvelope>(payload, Options);
			if (parsed == null)
			{
				error = "Control envelope is empty.";
				return false;
			}

			if (parsed.SchemaVersion != CurrentSchemaVersion)
			{
				error = $"Unsupported WebUI DataPlane schema version {parsed.SchemaVersion}.";
				return false;
			}

			envelope = parsed;
			return true;
		}
		catch (JsonException ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public static WebUiInboundPacket CreateControlPacket(
		string sessionId,
		long requestId,
		string kind,
		string topic,
		object payload)
	{
		byte[] bytes = SerializeControlEnvelope(CreateControlEnvelope(sessionId, requestId, kind, topic, payload));
		return new WebUiInboundPacket(
			sessionId,
			topic,
			WebUiPacketKind.Control,
			WebUiDeliverySemantics.ReliableOrdered,
			bytes,
			ControlContentType,
			requestId);
	}

	public static WebUiOutboundPacket CreateControlResponse(
		string sessionId,
		long requestId,
		string kind,
		string topic,
		object payload,
		WebUiPacketKind packetKind = WebUiPacketKind.Control)
	{
		byte[] bytes = SerializeControlEnvelope(CreateControlEnvelope(sessionId, requestId, kind, topic, payload));
		return new WebUiOutboundPacket(
			sessionId,
			topic,
			packetKind,
			WebUiDeliverySemantics.ReliableOrdered,
			bytes,
			ControlContentType,
			requestId);
	}

	public static WebUiControlEnvelope CreateControlEnvelope(
		string sessionId,
		long requestId,
		string kind,
		string topic,
		object payload)
	{
		if (string.IsNullOrWhiteSpace(sessionId))
		{
			throw new ArgumentException("Session id is required.", nameof(sessionId));
		}

		if (string.IsNullOrWhiteSpace(kind))
		{
			throw new ArgumentException("Control kind is required.", nameof(kind));
		}

		JsonElement payloadElement = JsonSerializer.SerializeToElement(payload, Options);
		return new WebUiControlEnvelope(
			CurrentSchemaVersion,
			sessionId.Trim(),
			requestId,
			kind.Trim(),
			topic?.Trim() ?? string.Empty,
			payloadElement);
	}

	public static string PayloadToString(ReadOnlyMemory<byte> payload)
	{
		return Encoding.UTF8.GetString(payload.Span);
	}
}
