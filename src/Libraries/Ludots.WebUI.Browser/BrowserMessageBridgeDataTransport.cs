using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.Browser;

public sealed class BrowserMessageBridgeDataTransport : IWebUiDataTransport
{
	public const string ControlChannel = "ludots.dataplane.control";
	public const string BinaryChunkChannel = "ludots.dataplane.binaryChunk";

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
	private readonly IBrowserMessageBridge _bridge;
	private readonly WebUiTransportCapabilities _capabilities;
	private readonly int _chunkSize;
	private bool _disposed;

	public BrowserMessageBridgeDataTransport(
		IBrowserMessageBridge bridge,
		WebUiTransportCapabilities? capabilities = null,
		int chunkSize = 64 * 1024)
	{
		_bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
		_capabilities = capabilities ?? WebUiTransportCapabilities.StringBridge();
		_chunkSize = Math.Max(1, chunkSize);
		_bridge.MessageReceived += OnMessageReceived;
	}

	public WebUiTransportCapabilities Capabilities => _capabilities;

	public event EventHandler<WebUiInboundPacket>? PacketReceived;

	public async ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		if (packet.ContentType == WebUiDataPlaneProtocol.BinaryContentType && !_capabilities.SupportsBinary)
		{
			await SendBinaryChunksAsync(packet, cancellationToken).ConfigureAwait(false);
			return;
		}

		await PostMessageOrMarkDisposedAsync(new BrowserScriptMessage(
			ControlChannel,
			JsonSerializer.Serialize(CreateWirePacket(packet), Options)), cancellationToken).ConfigureAwait(false);
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		_bridge.MessageReceived -= OnMessageReceived;
		return ValueTask.CompletedTask;
	}

	private async ValueTask SendBinaryChunksAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken)
	{
		byte[] bytes = packet.Payload.ToArray();
		string packetId = $"{packet.SessionId}:{packet.Topic}:{packet.RequestId}:{packet.Kind}";
		int totalChunks = Math.Max(1, (bytes.Length + _chunkSize - 1) / _chunkSize);
		for (int index = 0; index < totalChunks; index++)
		{
			int offset = index * _chunkSize;
			int length = Math.Min(_chunkSize, bytes.Length - offset);
			string data = Convert.ToBase64String(bytes, offset, length);
			var chunkPayload = new
			{
				packetId,
				packet.SessionId,
				packet.RequestId,
				packet.Topic,
				packet.Kind,
				packet.Delivery,
				packet.ContentType,
				packet.ClientSeq,
				index,
				totalChunks,
				byteOffset = offset,
				byteLength = length,
				totalByteLength = bytes.Length,
				encoding = "base64",
				data
			};
			await PostMessageOrMarkDisposedAsync(new BrowserScriptMessage(
				BinaryChunkChannel,
				JsonSerializer.Serialize(chunkPayload, Options)), cancellationToken).ConfigureAwait(false);
		}
	}

	private async ValueTask PostMessageOrMarkDisposedAsync(
		BrowserScriptMessage message,
		CancellationToken cancellationToken)
	{
		try
		{
			await _bridge.PostMessageAsync(message, cancellationToken).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			MarkDisposed();
			throw new ObjectDisposedException(
				nameof(BrowserMessageBridgeDataTransport),
				"The browser message bridge was disposed while DataPlane was sending.");
		}
	}

	private void OnMessageReceived(object? sender, BrowserScriptMessage message)
	{
		if (_disposed)
		{
			return;
		}

		if (!TryNormalizeIncomingPayload(message, out string payload))
		{
			return;
		}

		if (!WebUiDataPlaneProtocol.TryParseControlEnvelope(
			System.Text.Encoding.UTF8.GetBytes(payload),
			out WebUiControlEnvelope envelope,
			out _))
		{
			return;
		}

		var packet = new WebUiInboundPacket(
			envelope.SessionId,
			envelope.Topic,
			WebUiPacketKind.Control,
			WebUiDeliverySemantics.ReliableOrdered,
			WebUiDataPlaneProtocol.SerializeControlEnvelope(envelope),
			WebUiDataPlaneProtocol.ControlContentType,
			envelope.RequestId);
		PacketReceived?.Invoke(this, packet);
	}

	private static bool TryNormalizeIncomingPayload(BrowserScriptMessage message, out string payload)
	{
		payload = string.Empty;
		if (message.Channel == ControlChannel)
		{
			payload = message.Payload;
			return true;
		}

		if (message.Channel != "cefsharp")
		{
			return false;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(message.Payload);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			if (root.TryGetProperty("schemaVersion", out JsonElement schemaVersion))
			{
				payload = root.GetRawText();
				return schemaVersion.ValueKind == JsonValueKind.Number;
			}

			if (root.TryGetProperty("channel", out JsonElement channel) &&
				string.Equals(channel.GetString(), ControlChannel, StringComparison.Ordinal) &&
				root.TryGetProperty("payload", out JsonElement nestedPayload))
			{
				payload = nestedPayload.ValueKind == JsonValueKind.String
					? nestedPayload.GetString() ?? string.Empty
					: nestedPayload.GetRawText();
				return !string.IsNullOrWhiteSpace(payload);
			}
		}

		return false;
	}

	private static object CreateWirePacket(WebUiOutboundPacket packet)
	{
		return new
		{
			schemaVersion = WebUiDataPlaneProtocol.CurrentSchemaVersion,
			packet.SessionId,
			packet.RequestId,
			kind = packet.Kind.ToString(),
			packet.Topic,
			delivery = packet.Delivery.ToString(),
			packet.ContentType,
			packet.ClientSeq,
			payload = IsJsonContent(packet.ContentType)
				? TryParseJsonPayload(packet.Payload)
				: Convert.ToBase64String(packet.Payload.Span)
		};
	}

	private static bool IsJsonContent(string? contentType)
	{
		return !string.IsNullOrWhiteSpace(contentType) &&
			contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
	}

	private static object TryParseJsonPayload(ReadOnlyMemory<byte> payload)
	{
		try
		{
			return JsonSerializer.Deserialize<JsonElement>(payload.Span, Options);
		}
		catch (JsonException)
		{
			return WebUiDataPlaneProtocol.PayloadToString(payload);
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(BrowserMessageBridgeDataTransport));
		}
	}

	private void MarkDisposed()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_bridge.MessageReceived -= OnMessageReceived;
	}
}
