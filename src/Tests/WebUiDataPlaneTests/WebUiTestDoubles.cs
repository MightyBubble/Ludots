using System.Collections.Concurrent;
using Ludots.UI.Browser;
using Ludots.WebUI.DataPlane;

namespace Ludots.Tests.WebUiDataPlane;

internal sealed class FakeWebUiDataTransport : IWebUiDataTransport
{
	private readonly ConcurrentQueue<WebUiOutboundPacket> _sent = new();
	private readonly SemaphoreSlim _sentSignal = new(0);

	public FakeWebUiDataTransport(WebUiTransportCapabilities? capabilities = null)
	{
		Capabilities = capabilities ?? WebUiTransportCapabilities.StringBridge();
	}

	public WebUiTransportCapabilities Capabilities { get; }

	public event EventHandler<WebUiInboundPacket>? PacketReceived;

	public IReadOnlyCollection<WebUiOutboundPacket> Sent => _sent.ToArray();

	public ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
	{
		_sent.Enqueue(packet);
		_sentSignal.Release();
		return ValueTask.CompletedTask;
	}

	public void Receive(WebUiInboundPacket packet)
	{
		PacketReceived?.Invoke(this, packet);
	}

	public async Task<WebUiOutboundPacket> WaitForSentAsync(CancellationToken cancellationToken = default)
	{
		await _sentSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
		if (_sent.TryDequeue(out WebUiOutboundPacket? packet))
		{
			return packet;
		}

		throw new InvalidOperationException("Send signal was raised without a packet.");
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}

internal sealed class FakeBrowserMessageBridge : IBrowserMessageBridge
{
	private readonly ConcurrentQueue<BrowserScriptMessage> _posted = new();
	private readonly SemaphoreSlim _postedSignal = new(0);

	public event EventHandler<BrowserScriptMessage>? MessageReceived;

	public IReadOnlyCollection<BrowserScriptMessage> Posted => _posted.ToArray();

	public Exception? PostException { get; set; }

	public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
	{
		if (PostException != null)
		{
			throw PostException;
		}

		_posted.Enqueue(message);
		_postedSignal.Release();
		return ValueTask.CompletedTask;
	}

	public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
	{
		return ValueTask.CompletedTask;
	}

	public void Receive(BrowserScriptMessage message)
	{
		MessageReceived?.Invoke(this, message);
	}

	public async Task<BrowserScriptMessage> WaitForPostAsync(CancellationToken cancellationToken = default)
	{
		await _postedSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
		if (_posted.TryDequeue(out BrowserScriptMessage? message))
		{
			return message;
		}

		throw new InvalidOperationException("Post signal was raised without a message.");
	}
}

internal sealed class FakeTopicProducer : IWebUiTopicProducer
{
	private readonly Func<WebUiTopicContext, WebUiOutboundPacket> _snapshotFactory;

	public FakeTopicProducer(string topic, Func<WebUiTopicContext, WebUiOutboundPacket>? snapshotFactory = null)
	{
		Topic = topic;
		_snapshotFactory = snapshotFactory ?? (context => new WebUiOutboundPacket(
			context.SessionId,
			context.Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			System.Text.Encoding.UTF8.GetBytes("{\"snapshot\":true}"),
			"application/json",
			context.RequestId));
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		packet = _snapshotFactory(context);
		return true;
	}
}
