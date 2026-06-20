using System.Text.Json;

namespace Ludots.WebUI.DataPlane;

public readonly record struct WebUiTopicContext(string SessionId, string Topic, long RequestId, JsonElement Parameters);

public interface IWebUiTopicProducer
{
	string Topic { get; }

	bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet);
}

public sealed class WebUiDataPlaneRuntime : IDisposable, IAsyncDisposable
{
	private readonly Dictionary<string, IWebUiTopicProducer> _topics = new(StringComparer.Ordinal);
	private readonly Dictionary<string, WebUiDataPlaneSession> _sessions = new(StringComparer.Ordinal);
	private readonly WebUiCommandRouter? _commandRouter;
	private bool _disposed;

	public WebUiDataPlaneRuntime(WebUiCommandRouter? commandRouter = null)
	{
		_commandRouter = commandRouter;
	}

	public int SessionCount => _sessions.Count;

	public void RegisterTopic(IWebUiTopicProducer producer)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(producer);
		if (string.IsNullOrWhiteSpace(producer.Topic))
		{
			throw new ArgumentException("Topic producer requires a topic.", nameof(producer));
		}

		_topics[producer.Topic] = producer;
	}

	public WebUiDataPlaneSession AttachSession(string sessionId, IWebUiDataTransport transport)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(sessionId))
		{
			throw new ArgumentException("Session id is required.", nameof(sessionId));
		}

		ArgumentNullException.ThrowIfNull(transport);
		sessionId = sessionId.Trim();
		if (_sessions.Remove(sessionId, out WebUiDataPlaneSession? existing))
		{
			existing.Dispose();
		}

		var session = new WebUiDataPlaneSession(sessionId, transport, HandlePacketAsync);
		_sessions[session.SessionId] = session;
		return session;
	}

	public bool DetachSession(string sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionId) ||
			!_sessions.Remove(sessionId.Trim(), out WebUiDataPlaneSession? session))
		{
			return false;
		}

		session.Dispose();
		return true;
	}

	public async ValueTask PublishAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		foreach (WebUiDataPlaneSession session in _sessions.Values)
		{
			if (!session.IsSubscribed(packet.Topic))
			{
				continue;
			}

			session.Enqueue(packet with { SessionId = session.SessionId });
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private async ValueTask HandlePacketAsync(
		WebUiDataPlaneSession session,
		WebUiInboundPacket packet,
		CancellationToken cancellationToken)
	{
		if (packet.Kind == WebUiPacketKind.Command)
		{
			await HandleCommandAsync(session, packet, cancellationToken).ConfigureAwait(false);
			return;
		}

		if (packet.Kind != WebUiPacketKind.Control)
		{
			return;
		}

		if (!WebUiDataPlaneProtocol.TryParseControlEnvelope(packet.Payload.Span, out WebUiControlEnvelope envelope, out string error))
		{
			session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
				session.SessionId,
				packet.RequestId,
				"error",
				packet.Topic,
				new { error }));
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		switch (envelope.Kind)
		{
			case "handshake":
				session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
					session.SessionId,
					envelope.RequestId,
					"handshakeAck",
					envelope.Topic,
					new
					{
						session.SessionId,
						capabilities = session.Transport.Capabilities,
						protocol = WebUiDataPlaneProtocol.CurrentSchemaVersion
					}));
				await session.FlushAsync(cancellationToken).ConfigureAwait(false);
				break;
			case "subscribe":
				await SubscribeAsync(session, envelope, cancellationToken).ConfigureAwait(false);
				break;
			case "unsubscribe":
				session.Unsubscribe(envelope.Topic);
				session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
					session.SessionId,
					envelope.RequestId,
					"unsubscribed",
					envelope.Topic,
					new { envelope.Topic }));
				await session.FlushAsync(cancellationToken).ConfigureAwait(false);
				break;
			case "command":
				WebUiInboundPacket commandPacket = new(
					session.SessionId,
					envelope.Topic,
					WebUiPacketKind.Command,
					WebUiDeliverySemantics.ReliableOrdered,
					JsonSerializer.SerializeToUtf8Bytes(envelope.Payload),
					"application/json",
					envelope.RequestId);
				await HandleCommandAsync(session, commandPacket, cancellationToken).ConfigureAwait(false);
				break;
		}
	}

	private async ValueTask SubscribeAsync(
		WebUiDataPlaneSession session,
		WebUiControlEnvelope envelope,
		CancellationToken cancellationToken)
	{
		if (!_topics.TryGetValue(envelope.Topic, out IWebUiTopicProducer? producer))
		{
			session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
				session.SessionId,
				envelope.RequestId,
				"error",
				envelope.Topic,
				new { error = $"Unknown topic '{envelope.Topic}'." }));
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		bool added = session.Subscribe(envelope.Topic);
		var context = new WebUiTopicContext(session.SessionId, envelope.Topic, envelope.RequestId, envelope.Payload);
		if (added && producer.TryCreateSnapshot(in context, out WebUiOutboundPacket snapshot))
		{
			session.Enqueue(snapshot with { SessionId = session.SessionId, RequestId = envelope.RequestId });
		}

		await session.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask HandleCommandAsync(
		WebUiDataPlaneSession session,
		WebUiInboundPacket packet,
		CancellationToken cancellationToken)
	{
		if (_commandRouter == null)
		{
			session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
				session.SessionId,
				packet.RequestId,
				"commandError",
				packet.Topic,
				new { packet.ClientSeq, code = "command_router_missing", message = "No WebUI command router is registered." },
				WebUiPacketKind.CommandError));
		}
		else
		{
			session.Enqueue(await _commandRouter.HandleAsync(packet, cancellationToken).ConfigureAwait(false));
		}

		await session.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (WebUiDataPlaneSession session in _sessions.Values)
		{
			await session.DisposeAsync().ConfigureAwait(false);
		}

		_sessions.Clear();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (WebUiDataPlaneSession session in _sessions.Values)
		{
			session.Dispose();
		}

		_sessions.Clear();
	}
}

public sealed class WebUiDataPlaneSession : IDisposable, IAsyncDisposable
{
	private readonly IWebUiDataTransport _transport;
	private readonly Func<WebUiDataPlaneSession, WebUiInboundPacket, CancellationToken, ValueTask> _packetHandler;
	private readonly HashSet<string> _subscriptions = new(StringComparer.Ordinal);
	private bool _disposed;

	public WebUiDataPlaneSession(
		string sessionId,
		IWebUiDataTransport transport,
		Func<WebUiDataPlaneSession, WebUiInboundPacket, CancellationToken, ValueTask> packetHandler)
	{
		SessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("Session id is required.", nameof(sessionId))
			: sessionId.Trim();
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_packetHandler = packetHandler ?? throw new ArgumentNullException(nameof(packetHandler));
		Queue = new WebUiOutboundQueue(transport.Capabilities.MaxPacketBytes);
		_transport.PacketReceived += OnPacketReceived;
	}

	public string SessionId { get; }

	public IWebUiDataTransport Transport => _transport;

	public WebUiOutboundQueue Queue { get; }

	public WebUiDataPlaneDiagnostics Diagnostics => Queue.Diagnostics;

	public bool Subscribe(string topic) => !string.IsNullOrWhiteSpace(topic) && _subscriptions.Add(topic.Trim());

	public bool Unsubscribe(string topic) => !string.IsNullOrWhiteSpace(topic) && _subscriptions.Remove(topic.Trim());

	public bool IsSubscribed(string topic) => !string.IsNullOrWhiteSpace(topic) && _subscriptions.Contains(topic.Trim());

	public void Enqueue(WebUiOutboundPacket packet)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Queue.Enqueue(packet);
	}

	public ValueTask FlushAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return Queue.FlushAsync(_transport, cancellationToken);
	}

	private void OnPacketReceived(object? sender, WebUiInboundPacket packet)
	{
		if (_disposed)
		{
			return;
		}

		_ = _packetHandler(this, packet with { SessionId = SessionId }, CancellationToken.None);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_transport.PacketReceived -= OnPacketReceived;
		_subscriptions.Clear();
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		Dispose();
		await _transport.DisposeAsync().ConfigureAwait(false);
	}
}
