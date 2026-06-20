namespace Ludots.WebUI.DataPlane;

public readonly record struct WebUiDataPlaneDiagnostics(
	long SentPackets,
	long SentBytes,
	long QueuedReliable,
	long QueuedLatestWins,
	long CoalescedPackets,
	long DroppedPackets,
	string LastError);

public sealed class WebUiOutboundQueue
{
	private readonly Queue<WebUiOutboundPacket> _reliable = new();
	private readonly Dictionary<string, WebUiOutboundPacket> _latestWins = new(StringComparer.Ordinal);
	private readonly int _maxPacketBytes;
	private long _sentPackets;
	private long _sentBytes;
	private long _coalescedPackets;
	private long _droppedPackets;
	private string _lastError = string.Empty;

	public WebUiOutboundQueue(int maxPacketBytes)
	{
		_maxPacketBytes = Math.Max(1, maxPacketBytes);
	}

	public WebUiDataPlaneDiagnostics Diagnostics => new(
		_sentPackets,
		_sentBytes,
		_reliable.Count,
		_latestWins.Count,
		_coalescedPackets,
		_droppedPackets,
		_lastError);

	public void Enqueue(WebUiOutboundPacket packet)
	{
		if (packet.Payload.Length > _maxPacketBytes && packet.Kind != WebUiPacketKind.BinaryChunk)
		{
			_droppedPackets++;
			_lastError = $"Packet topic '{packet.Topic}' size {packet.Payload.Length} exceeds max packet bytes {_maxPacketBytes}.";
			if (packet.Delivery == WebUiDeliverySemantics.ReliableOrdered)
			{
				_reliable.Enqueue(WebUiDataPlaneProtocol.CreateControlResponse(
					packet.SessionId,
					packet.RequestId,
					"error",
					packet.Topic,
					new { error = _lastError },
					WebUiPacketKind.CommandError));
			}

			return;
		}

		if (packet.Delivery == WebUiDeliverySemantics.LatestWins)
		{
			string key = string.Concat(packet.SessionId, "|", packet.Topic, "|", packet.Kind.ToString());
			if (_latestWins.ContainsKey(key))
			{
				_coalescedPackets++;
			}

			_latestWins[key] = packet;
			return;
		}

		_reliable.Enqueue(packet);
	}

	public async ValueTask FlushAsync(IWebUiDataTransport transport, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(transport);
		while (_reliable.Count > 0)
		{
			await SendAsync(transport, _reliable.Dequeue(), cancellationToken).ConfigureAwait(false);
		}

		if (_latestWins.Count == 0)
		{
			return;
		}

		WebUiOutboundPacket[] latest = _latestWins.Values.ToArray();
		_latestWins.Clear();
		for (int i = 0; i < latest.Length; i++)
		{
			await SendAsync(transport, latest[i], cancellationToken).ConfigureAwait(false);
		}
	}

	private async ValueTask SendAsync(
		IWebUiDataTransport transport,
		WebUiOutboundPacket packet,
		CancellationToken cancellationToken)
	{
		await transport.SendAsync(packet, cancellationToken).ConfigureAwait(false);
		_sentPackets++;
		_sentBytes += packet.Payload.Length;
	}
}
