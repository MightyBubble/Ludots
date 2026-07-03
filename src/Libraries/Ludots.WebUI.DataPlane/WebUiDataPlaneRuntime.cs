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
	private readonly object _sync = new();
	private readonly Dictionary<string, IWebUiTopicProducer> _topics = new(StringComparer.Ordinal);
	private readonly Dictionary<string, WebUiDataPlaneSession> _sessions = new(StringComparer.Ordinal);
	private readonly IWebUiCommandDispatcher? _commandRouter;
	private bool _disposed;

	public WebUiDataPlaneRuntime(IWebUiCommandDispatcher? commandRouter = null)
	{
		_commandRouter = commandRouter;
	}

	public int SessionCount
	{
		get
		{
			lock (_sync)
			{
				return _sessions.Count;
			}
		}
	}

	public void RegisterTopic(IWebUiTopicProducer producer)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(producer);
		if (string.IsNullOrWhiteSpace(producer.Topic))
		{
			throw new ArgumentException("Topic producer requires a topic.", nameof(producer));
		}

		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_topics[producer.Topic] = producer;
		}
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
		WebUiDataPlaneSession? existing = null;
		WebUiDataPlaneSession session;
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_sessions.Remove(sessionId, out WebUiDataPlaneSession? removed))
			{
				existing = removed;
			}

			session = new WebUiDataPlaneSession(sessionId, transport, HandlePacketAsync);
			_sessions[session.SessionId] = session;
		}

		existing?.Dispose();
		return session;
	}

	public bool DetachSession(string sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionId))
		{
			return false;
		}

		WebUiDataPlaneSession? session;
		lock (_sync)
		{
			if (!_sessions.Remove(sessionId.Trim(), out session))
			{
				return false;
			}
		}

		session.Dispose();
		return true;
	}

	public async ValueTask PublishAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		WebUiDataPlaneSession[] sessions;
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			sessions = _sessions.Values.ToArray();
		}

		foreach (WebUiDataPlaneSession session in sessions)
		{
			if (!session.IsSubscribed(packet.Topic))
			{
				continue;
			}

			session.Enqueue(packet with { SessionId = session.SessionId });
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public async ValueTask<int> PublishTopicAsync(
		string topic,
		JsonElement parameters = default,
		long requestId = 0,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(topic))
		{
			throw new ArgumentException("Topic is required.", nameof(topic));
		}

		topic = topic.Trim();
		IWebUiTopicProducer? producer;
		WebUiDataPlaneSession[] sessions;
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (!_topics.TryGetValue(topic, out producer))
			{
				throw new InvalidOperationException($"Unknown WebUI topic '{topic}'.");
			}

			sessions = _sessions.Values.ToArray();
		}

		int published = 0;
		foreach (WebUiDataPlaneSession session in sessions)
		{
			if (!session.IsSubscribed(topic))
			{
				continue;
			}

			var context = new WebUiTopicContext(session.SessionId, topic, requestId, parameters);
			if (!producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet))
			{
				continue;
			}

			session.Enqueue(packet with { SessionId = session.SessionId, RequestId = requestId });
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
			published++;
		}

		return published;
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
				await HandleHandshakeAsync(session, envelope, cancellationToken).ConfigureAwait(false);
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

	private static async ValueTask HandleHandshakeAsync(
		WebUiDataPlaneSession session,
		WebUiControlEnvelope envelope,
		CancellationToken cancellationToken)
	{
		string[] missing = GetMissingRequiredCapabilities(envelope.Payload, session.Transport.Capabilities);
		if (missing.Length > 0)
		{
			session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
				session.SessionId,
				envelope.RequestId,
				"error",
				envelope.Topic,
				new
				{
					code = "transport_capability_mismatch",
					message = "The requested WebUI DataPlane transport capabilities are not available.",
					requiredCapabilities = missing,
					capabilities = session.Transport.Capabilities
				}));
			await session.FlushAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		session.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
			session.SessionId,
			envelope.RequestId,
			"handshakeAck",
			envelope.Topic,
			new
			{
				session.SessionId,
				transportMode = session.Transport.Capabilities.ModeName,
				capabilities = session.Transport.Capabilities,
				protocol = WebUiDataPlaneProtocol.CurrentSchemaVersion
			}));
		await session.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private static string[] GetMissingRequiredCapabilities(
		JsonElement payload,
		WebUiTransportCapabilities capabilities)
	{
		if (payload.ValueKind != JsonValueKind.Object ||
			!payload.TryGetProperty("requiredCapabilities", out JsonElement required) ||
			required.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<string>();
		}

		var missing = new List<string>();
		foreach (JsonElement item in required.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			string? capability = item.GetString();
			if (!string.IsNullOrWhiteSpace(capability) && !capabilities.Satisfies(capability))
			{
				missing.Add(capability.Trim());
			}
		}

		return missing.ToArray();
	}

	private async ValueTask SubscribeAsync(
		WebUiDataPlaneSession session,
		WebUiControlEnvelope envelope,
		CancellationToken cancellationToken)
	{
		IWebUiTopicProducer? producer;
		lock (_sync)
		{
			_topics.TryGetValue(envelope.Topic, out producer);
		}

		if (producer == null)
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
		WebUiDataPlaneSession[] sessions;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			sessions = _sessions.Values.ToArray();
			_sessions.Clear();
		}

		foreach (WebUiDataPlaneSession session in sessions)
		{
			await session.DisposeAsync().ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		WebUiDataPlaneSession[] sessions;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			sessions = _sessions.Values.ToArray();
			_sessions.Clear();
		}

		foreach (WebUiDataPlaneSession session in sessions)
		{
			session.Dispose();
		}
	}
}

public sealed class WebUiDataPlaneSession : IDisposable, IAsyncDisposable
{
	private readonly object _sync = new();
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

	public bool Subscribe(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return false;
		}

		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			return _subscriptions.Add(topic.Trim());
		}
	}

	public bool Unsubscribe(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return false;
		}

		lock (_sync)
		{
			return !_disposed && _subscriptions.Remove(topic.Trim());
		}
	}

	public bool IsSubscribed(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return false;
		}

		lock (_sync)
		{
			return !_disposed && _subscriptions.Contains(topic.Trim());
		}
	}

	public void Enqueue(WebUiOutboundPacket packet)
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
		}

		Queue.Enqueue(packet);
	}

	public ValueTask FlushAsync(CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
		}

		return Queue.FlushAsync(_transport, cancellationToken);
	}

	private void OnPacketReceived(object? sender, WebUiInboundPacket packet)
	{
		if (_disposed)
		{
			return;
		}

		_ = HandlePacketReceivedAsync(packet with { SessionId = SessionId });
	}

	private async Task HandlePacketReceivedAsync(WebUiInboundPacket packet)
	{
		try
		{
			await _packetHandler(this, packet, CancellationToken.None).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (OperationCanceledException)
		{
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
			_transport.PacketReceived -= OnPacketReceived;
			_subscriptions.Clear();
		}
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
