using System.Text;
using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.Browser;

public sealed class BrowserSharedMemoryDataTransport : IWebUiDataTransport
{
	public const string SharedBufferChannel = BrowserDataPlaneMessageChannels.SharedBuffer;

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
	private readonly IBrowserMessageBridge _bridge;
	private readonly BrowserSharedMemoryBufferStore _store;
	private readonly WebUiTransportCapabilities _capabilities;
	private bool _disposed;

	public BrowserSharedMemoryDataTransport(
		IBrowserMessageBridge bridge,
		BrowserSharedMemoryBufferStore store,
		IEnumerable<BrowserSharedMemoryTopicBuffer> buffers)
	{
		_bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		ArgumentNullException.ThrowIfNull(buffers);
		foreach (BrowserSharedMemoryTopicBuffer buffer in buffers)
		{
			_store.AddBuffer(buffer);
		}

		WebUiSharedBufferDescriptor[] descriptors = _store.Descriptors;
		if (descriptors.Length == 0)
		{
			throw new ArgumentException("At least one shared-memory buffer is required.", nameof(buffers));
		}

		_capabilities = WebUiTransportCapabilities.SharedMemory(
			maxPacketBytes: descriptors.Max(static descriptor => descriptor.CapacityBytes),
			chunkSize: descriptors.Max(static descriptor => descriptor.CapacityBytes),
			sharedBuffers: descriptors);
		_bridge.MessageReceived += OnMessageReceived;
	}

	public WebUiTransportCapabilities Capabilities => _capabilities;

	public event EventHandler<WebUiInboundPacket>? PacketReceived;

	public async ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		if (packet.ContentType == WebUiDataPlaneProtocol.BinaryContentType)
		{
			if (!_store.HasTopic(packet.Topic))
			{
				throw new InvalidOperationException(
					$"Topic '{packet.Topic}' does not have a shared-memory buffer.");
			}

			await SendSharedBufferDescriptorAsync(packet, cancellationToken).ConfigureAwait(false);
			return;
		}

		await PostMessageOrMarkDisposedAsync(new BrowserScriptMessage(
			BrowserMessageBridgeDataTransport.ControlChannel,
			JsonSerializer.Serialize(CreateWirePacket(packet), Options)), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_bridge.MessageReceived -= OnMessageReceived;
		await _store.DisposeAsync().ConfigureAwait(false);
	}

	private async ValueTask SendSharedBufferDescriptorAsync(
		WebUiOutboundPacket packet,
		CancellationToken cancellationToken)
	{
		WebUiSharedBufferWriteResult result = _store.WriteLatestWins(
			packet.Topic,
			packet.Payload.Span,
			packet.ClientSeq);
		if (!result.Accepted)
		{
			if (packet.Delivery == WebUiDeliverySemantics.ReliableOrdered)
			{
				await PostMessageOrMarkDisposedAsync(new BrowserScriptMessage(
					BrowserMessageBridgeDataTransport.ControlChannel,
					JsonSerializer.Serialize(CreateWirePacket(WebUiDataPlaneProtocol.CreateControlResponse(
						packet.SessionId,
						packet.RequestId,
						"error",
						packet.Topic,
						new { error = result.Error },
						WebUiPacketKind.CommandError)), Options)), cancellationToken).ConfigureAwait(false);
			}

			return;
		}

		var wirePacket = new
		{
			schemaVersion = WebUiDataPlaneProtocol.CurrentSchemaVersion,
			packet.SessionId,
			packet.RequestId,
			kind = packet.Kind.ToString(),
			packet.Topic,
			delivery = packet.Delivery.ToString(),
			packet.ContentType,
			packet.ClientSeq,
			payload = new
			{
				sharedBuffer = result.Descriptor
			}
		};
		await PostMessageOrMarkDisposedAsync(new BrowserScriptMessage(
			SharedBufferChannel,
			JsonSerializer.Serialize(wirePacket, Options)), cancellationToken).ConfigureAwait(false);
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
				nameof(BrowserSharedMemoryDataTransport),
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
			Encoding.UTF8.GetBytes(payload),
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
		if (message.Channel == BrowserMessageBridgeDataTransport.ControlChannel)
		{
			payload = message.Payload;
			return true;
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
			throw new ObjectDisposedException(nameof(BrowserSharedMemoryDataTransport));
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
