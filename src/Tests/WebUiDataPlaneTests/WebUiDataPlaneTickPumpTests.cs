using System.Text;
using System.Text.Json;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiDataPlaneTickPumpTests
{
	[Test]
	public async Task QueuedCommandDispatcher_ExecutesCommandOnlyWhenFlushed()
	{
		var inner = new RecordingCommandDispatcher();
		await using var dispatcher = new WebUiQueuedCommandDispatcher(inner);
		await using var runtime = new WebUiDataPlaneRuntime(dispatcher);
		var transport = new FakeWebUiDataTransport();
		runtime.AttachSession("session-a", transport);

		transport.Receive(CreateCommandPacket("session-a", 12, 91, "unit.select"));

		Assert.That(inner.HandledCount, Is.EqualTo(0));
		Assert.That(dispatcher.PendingCount, Is.EqualTo(1));
		Assert.That(transport.Sent, Is.Empty);

		int flushed = await dispatcher.FlushAsync(TestContext.CurrentContext.CancellationToken);

		Assert.That(flushed, Is.EqualTo(1));
		Assert.That(inner.HandledCount, Is.EqualTo(1));
		WebUiOutboundPacket ack = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(ack.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
		Assert.That(ack.ClientSeq, Is.EqualTo(91));
	}

	[Test]
	public async Task TickPump_PublishesTrackedTopicsOnlyWhenFlushed()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport();
		runtime.RegisterTopic(new FakeTopicProducer("topic.units", context => new WebUiOutboundPacket(
			context.SessionId,
			context.Topic,
			WebUiPacketKind.Delta,
			WebUiDeliverySemantics.LatestWins,
			Encoding.UTF8.GetBytes($"{{\"requestId\":{context.RequestId}}}"),
			"application/json",
			context.RequestId)));
		runtime.AttachSession("session-a", transport);
		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 1, "subscribe", "topic.units", new { }));
		_ = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		var pump = new WebUiDataPlaneTickPump(runtime);
		pump.TrackTopic("topic.units", requestId: 77, JsonSerializer.SerializeToElement(new { window = 256 }));

		Assert.That(transport.Sent, Is.Empty);

		WebUiDataPlaneTickResult result = await pump.FlushAsync(TestContext.CurrentContext.CancellationToken);

		Assert.That(result.TopicPackets, Is.EqualTo(1));
		WebUiOutboundPacket delta = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(delta.Kind, Is.EqualTo(WebUiPacketKind.Delta));
		Assert.That(delta.RequestId, Is.EqualTo(77));
		Assert.That(delta.SessionId, Is.EqualTo("session-a"));
	}

	[Test]
	public async Task QueuedCommandDispatcher_DisposeCompletesPendingCommandWithCommandError()
	{
		var inner = new RecordingCommandDispatcher();
		var dispatcher = new WebUiQueuedCommandDispatcher(inner);
		Task<WebUiOutboundPacket> pending = dispatcher
			.HandleAsync(CreateCommandPacket("session-a", 12, 91, "unit.select"))
			.AsTask();

		dispatcher.Dispose();

		WebUiOutboundPacket error = await pending.WaitAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(error.Kind, Is.EqualTo(WebUiPacketKind.CommandError));
		Assert.That(error.ClientSeq, Is.EqualTo(91));
		Assert.That(inner.HandledCount, Is.EqualTo(0));
	}

	private static WebUiInboundPacket CreateCommandPacket(
		string sessionId,
		long requestId,
		long clientSeq,
		string name)
	{
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
		{
			name,
			clientSeq,
			entityRefs = Array.Empty<WebUiEntityRef>(),
			payload = new { }
		}, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		return new WebUiInboundPacket(
			sessionId,
			"command",
			WebUiPacketKind.Command,
			WebUiDeliverySemantics.ReliableOrdered,
			payload,
			"application/json",
			requestId,
			clientSeq);
	}

	private sealed class RecordingCommandDispatcher : IWebUiCommandDispatcher
	{
		public int HandledCount { get; private set; }

		public ValueTask<WebUiOutboundPacket> HandleAsync(
			WebUiInboundPacket packet,
			CancellationToken cancellationToken = default)
		{
			HandledCount++;
			return ValueTask.FromResult(new WebUiOutboundPacket(
				packet.SessionId,
				packet.Topic,
				WebUiPacketKind.CommandAck,
				WebUiDeliverySemantics.ReliableOrdered,
				Encoding.UTF8.GetBytes("{}"),
				"application/json",
				packet.RequestId,
				packet.ClientSeq));
		}
	}
}
