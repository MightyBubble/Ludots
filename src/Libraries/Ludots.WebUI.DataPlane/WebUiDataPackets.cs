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
	public static WebUiTransportCapabilities StringBridge(int maxPacketBytes = 1024 * 1024)
	{
		return new WebUiTransportCapabilities(
			SupportsBinary: false,
			SupportsSharedMemory: false,
			SupportsReliableOrdered: true,
			SupportsLatestWins: true,
			MaxPacketBytes: Math.Max(1, maxPacketBytes));
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
