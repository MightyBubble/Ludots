using System.Collections.Concurrent;
using System.Text.Json;

namespace Ludots.WebUI.DataPlane;

public readonly record struct WebUiDataPlaneTickResult(int Commands, int TopicPackets);

public readonly record struct WebUiTopicPublishRequest(string Topic, long RequestId, JsonElement Parameters);

public sealed class WebUiQueuedCommandDispatcher : IWebUiCommandDispatcher, IDisposable, IAsyncDisposable
{
	private readonly ConcurrentQueue<PendingCommand> _queue = new();
	private readonly IWebUiCommandDispatcher _inner;
	private int _disposed;

	public WebUiQueuedCommandDispatcher(IWebUiCommandDispatcher inner)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
	}

	public int PendingCount => _queue.Count;

	public ValueTask<WebUiOutboundPacket> HandleAsync(
		WebUiInboundPacket packet,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (cancellationToken.IsCancellationRequested)
		{
			return new ValueTask<WebUiOutboundPacket>(Task.FromCanceled<WebUiOutboundPacket>(cancellationToken));
		}

		var pending = new PendingCommand(packet);
		_queue.Enqueue(pending);
		return new ValueTask<WebUiOutboundPacket>(pending.Completion.Task);
	}

	public ValueTask<int> FlushAsync(CancellationToken cancellationToken = default)
	{
		return FlushAsync(int.MaxValue, cancellationToken);
	}

	public async ValueTask<int> FlushAsync(int maxCommands, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (maxCommands < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxCommands), "Max commands must not be negative.");
		}

		int handled = 0;
		while (handled < maxCommands && _queue.TryDequeue(out PendingCommand? pending))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				WebUiOutboundPacket response = await _inner
					.HandleAsync(pending.Packet, cancellationToken)
					.ConfigureAwait(false);
				pending.Completion.SetResult(response);
				handled++;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				pending.Completion.SetCanceled(cancellationToken);
				throw;
			}
			catch (Exception ex)
			{
				pending.Completion.SetException(ex);
				throw;
			}
		}

		return handled;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		while (_queue.TryDequeue(out PendingCommand? pending))
		{
			pending.Completion.SetResult(CreateDisposedCommandError(pending.Packet));
		}
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private sealed class PendingCommand
	{
		public PendingCommand(WebUiInboundPacket packet)
		{
			Packet = packet;
		}

		public WebUiInboundPacket Packet { get; }

		public TaskCompletionSource<WebUiOutboundPacket> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private static WebUiOutboundPacket CreateDisposedCommandError(WebUiInboundPacket packet)
	{
		return WebUiDataPlaneProtocol.CreateControlResponse(
			packet.SessionId,
			packet.RequestId,
			"commandError",
			packet.Topic,
			new
			{
				packet.ClientSeq,
				code = "command_dispatcher_disposed",
				message = "The WebUI command dispatcher was disposed before the command reached the engine tick."
			},
			WebUiPacketKind.CommandError) with
		{
			ClientSeq = packet.ClientSeq,
			Delivery = WebUiDeliverySemantics.ReliableOrdered
		};
	}
}

public sealed class WebUiDataPlaneTickPump
{
	private readonly object _sync = new();
	private readonly WebUiDataPlaneRuntime _runtime;
	private readonly WebUiQueuedCommandDispatcher? _commandDispatcher;
	private readonly Dictionary<string, WebUiTopicPublishRequest> _topics = new(StringComparer.Ordinal);

	public WebUiDataPlaneTickPump(
		WebUiDataPlaneRuntime runtime,
		WebUiQueuedCommandDispatcher? commandDispatcher = null)
	{
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_commandDispatcher = commandDispatcher;
	}

	public int TrackedTopicCount
	{
		get
		{
			lock (_sync)
			{
				return _topics.Count;
			}
		}
	}

	public void TrackTopic(string topic, long requestId = 0, JsonElement parameters = default)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			throw new ArgumentException("Topic is required.", nameof(topic));
		}

		topic = topic.Trim();
		lock (_sync)
		{
			_topics[topic] = new WebUiTopicPublishRequest(topic, requestId, parameters);
		}
	}

	public bool UntrackTopic(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return false;
		}

		lock (_sync)
		{
			return _topics.Remove(topic.Trim());
		}
	}

	public void ClearTopics()
	{
		lock (_sync)
		{
			_topics.Clear();
		}
	}

	public async ValueTask<WebUiDataPlaneTickResult> FlushAsync(CancellationToken cancellationToken = default)
	{
		int commands = await FlushCommandsAsync(cancellationToken).ConfigureAwait(false);
		int topicPackets = await PublishTopicsAsync(cancellationToken).ConfigureAwait(false);
		return new WebUiDataPlaneTickResult(commands, topicPackets);
	}

	public ValueTask<int> FlushCommandsAsync(CancellationToken cancellationToken = default)
	{
		return _commandDispatcher == null
			? ValueTask.FromResult(0)
			: _commandDispatcher.FlushAsync(cancellationToken);
	}

	public async ValueTask<int> PublishTopicsAsync(CancellationToken cancellationToken = default)
	{
		WebUiTopicPublishRequest[] topics;
		lock (_sync)
		{
			topics = _topics.Values.ToArray();
		}

		int topicPackets = 0;
		for (int i = 0; i < topics.Length; i++)
		{
			topicPackets += await _runtime.PublishTopicAsync(
				topics[i].Topic,
				topics[i].Parameters,
				topics[i].RequestId,
				cancellationToken).ConfigureAwait(false);
		}

		return topicPackets;
	}
}
