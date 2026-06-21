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
	private readonly object _sync = new();
	private readonly SemaphoreSlim _flushGate = new(1, 1);
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
		GetSentPackets(),
		GetSentBytes(),
		GetQueuedReliable(),
		GetQueuedLatestWins(),
		GetCoalescedPackets(),
		GetDroppedPackets(),
		GetLastError());

	public void Enqueue(WebUiOutboundPacket packet)
	{
		lock (_sync)
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
	}

	public async ValueTask FlushAsync(IWebUiDataTransport transport, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(transport);
		await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			while (TryDequeueReliable(out WebUiOutboundPacket reliable))
			{
				await SendAsync(transport, reliable, cancellationToken).ConfigureAwait(false);
			}

			WebUiOutboundPacket[] latest = DrainLatestWins();
			for (int i = 0; i < latest.Length; i++)
			{
				await SendAsync(transport, latest[i], cancellationToken).ConfigureAwait(false);
			}
		}
		finally
		{
			_flushGate.Release();
		}
	}

	private bool TryDequeueReliable(out WebUiOutboundPacket packet)
	{
		lock (_sync)
		{
			if (_reliable.Count == 0)
			{
				packet = new WebUiOutboundPacket(
					string.Empty,
					string.Empty,
					WebUiPacketKind.Control,
					WebUiDeliverySemantics.ReliableOrdered,
					Array.Empty<byte>());
				return false;
			}

			packet = _reliable.Dequeue();
			return true;
		}
	}

	private WebUiOutboundPacket[] DrainLatestWins()
	{
		lock (_sync)
		{
			if (_latestWins.Count == 0)
			{
				return Array.Empty<WebUiOutboundPacket>();
			}

			WebUiOutboundPacket[] latest = _latestWins.Values.ToArray();
			_latestWins.Clear();
			return latest;
		}
	}

	private async ValueTask SendAsync(
		IWebUiDataTransport transport,
		WebUiOutboundPacket packet,
		CancellationToken cancellationToken)
	{
		await transport.SendAsync(packet, cancellationToken).ConfigureAwait(false);
		lock (_sync)
		{
			_sentPackets++;
			_sentBytes += packet.Payload.Length;
		}
	}

	private long GetSentPackets()
	{
		lock (_sync)
		{
			return _sentPackets;
		}
	}

	private long GetSentBytes()
	{
		lock (_sync)
		{
			return _sentBytes;
		}
	}

	private long GetQueuedReliable()
	{
		lock (_sync)
		{
			return _reliable.Count;
		}
	}

	private long GetQueuedLatestWins()
	{
		lock (_sync)
		{
			return _latestWins.Count;
		}
	}

	private long GetCoalescedPackets()
	{
		lock (_sync)
		{
			return _coalescedPackets;
		}
	}

	private long GetDroppedPackets()
	{
		lock (_sync)
		{
			return _droppedPackets;
		}
	}

	private string GetLastError()
	{
		lock (_sync)
		{
			return _lastError;
		}
	}
}
