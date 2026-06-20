using System.Text;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiDataPlaneRuntimeTests
{
	[Test]
	public async Task Subscribe_CreatesSessionSubscription_AndImmediatelySendsInitialSnapshot()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport();
		runtime.RegisterTopic(new FakeTopicProducer("topic.units"));
		runtime.AttachSession("session-a", transport);

		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 7, "subscribe", "topic.units", new { }));

		WebUiOutboundPacket sent = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(sent.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
		Assert.That(sent.SessionId, Is.EqualTo("session-a"));
		Assert.That(sent.Topic, Is.EqualTo("topic.units"));
		Assert.That(sent.RequestId, Is.EqualTo(7));
	}

	[Test]
	public async Task Publish_SendsDeltaOnlyToSubscribedSessions()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var first = new FakeWebUiDataTransport();
		var second = new FakeWebUiDataTransport();
		runtime.RegisterTopic(new FakeTopicProducer("topic.units"));
		runtime.AttachSession("session-a", first);
		runtime.AttachSession("session-b", second);

		first.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 1, "subscribe", "topic.units", new { }));
		_ = await first.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);

		await runtime.PublishAsync(new WebUiOutboundPacket(
			string.Empty,
			"topic.units",
			WebUiPacketKind.Delta,
			WebUiDeliverySemantics.LatestWins,
			Encoding.UTF8.GetBytes("{\"tick\":1}")),
			TestContext.CurrentContext.CancellationToken);

		WebUiOutboundPacket delta = await first.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(delta.Kind, Is.EqualTo(WebUiPacketKind.Delta));
		Assert.That(delta.SessionId, Is.EqualTo("session-a"));
		Assert.That(second.Sent, Is.Empty);
	}

	[Test]
	public async Task PublishAsync_SerializesConcurrentFlushesForTheSameSession()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new ReentrancyDetectingWebUiDataTransport();
		WebUiDataPlaneSession session = runtime.AttachSession("session-a", transport);
		session.Subscribe("topic.units");

		Task[] publishers = Enumerable.Range(0, 64)
			.Select(tick => runtime.PublishAsync(new WebUiOutboundPacket(
				string.Empty,
				"topic.units",
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.ReliableOrdered,
				Encoding.UTF8.GetBytes($"{{\"tick\":{tick}}}")),
				TestContext.CurrentContext.CancellationToken).AsTask())
			.ToArray();

		await Task.WhenAll(publishers);

		Assert.That(transport.ConcurrentSendCount, Is.EqualTo(0));
		Assert.That(transport.Sent, Has.Count.EqualTo(64));
		Assert.That(session.Diagnostics.SentPackets, Is.EqualTo(64));
	}

	[Test]
	public async Task Unsubscribe_StopsFutureDelta()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport();
		runtime.RegisterTopic(new FakeTopicProducer("topic.units"));
		runtime.AttachSession("session-a", transport);
		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 1, "subscribe", "topic.units", new { }));
		_ = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 2, "unsubscribe", "topic.units", new { }));
		_ = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);

		await runtime.PublishAsync(new WebUiOutboundPacket(
			string.Empty,
			"topic.units",
			WebUiPacketKind.Delta,
			WebUiDeliverySemantics.LatestWins,
			Encoding.UTF8.GetBytes("{}")),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(transport.Sent, Is.Empty);
	}

	[Test]
	public void DetachSession_ReleasesSubscriptions_AndRuntimeNoLongerPublishes()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport();
		WebUiDataPlaneSession session = runtime.AttachSession("session-a", transport);
		session.Subscribe("topic.units");

		Assert.That(runtime.DetachSession("session-a"), Is.True);
		Assert.That(runtime.SessionCount, Is.EqualTo(0));
		Assert.That(session.IsSubscribed("topic.units"), Is.False);
	}

	[Test]
	public async Task DuplicateSubscribe_IsIdempotent_AndDoesNotSendDuplicateSnapshot()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport();
		runtime.RegisterTopic(new FakeTopicProducer("topic.units"));
		runtime.AttachSession("session-a", transport);

		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 1, "subscribe", "topic.units", new { }));
		_ = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 2, "subscribe", "topic.units", new { }));

		Assert.That(transport.Sent, Is.Empty);
	}
}
