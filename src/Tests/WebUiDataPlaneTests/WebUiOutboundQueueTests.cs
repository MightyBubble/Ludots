using System.Text;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiOutboundQueueTests
{
	[Test]
	public async Task LatestWinsQueue_CoalescesSameTopicState_AndSendsOnlyNewestPacket()
	{
		var transport = new FakeWebUiDataTransport();
		var queue = new WebUiOutboundQueue(maxPacketBytes: 1024);
		for (int i = 0; i < 100; i++)
		{
			queue.Enqueue(new WebUiOutboundPacket(
				"s",
				"topic.units",
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.LatestWins,
				Encoding.UTF8.GetBytes($"{{\"tick\":{i}}}")));
		}

		await queue.FlushAsync(transport, TestContext.CurrentContext.CancellationToken);

		Assert.That(transport.Sent, Has.Count.EqualTo(1));
		WebUiOutboundPacket sent = transport.Sent.Single();
		Assert.That(WebUiDataPlaneProtocol.PayloadToString(sent.Payload), Does.Contain("\"tick\":99"));
		Assert.That(queue.Diagnostics.CoalescedPackets, Is.EqualTo(99));
	}

	[Test]
	public async Task ReliableCommands_AreNotDroppedByLatestWinsBackpressure()
	{
		var transport = new FakeWebUiDataTransport();
		var queue = new WebUiOutboundQueue(maxPacketBytes: 1024);
		queue.Enqueue(Packet("s", "orders", WebUiPacketKind.Command, WebUiDeliverySemantics.ReliableOrdered, "one"));
		queue.Enqueue(Packet("s", "topic.units", WebUiPacketKind.Delta, WebUiDeliverySemantics.LatestWins, "old"));
		queue.Enqueue(Packet("s", "orders", WebUiPacketKind.Command, WebUiDeliverySemantics.ReliableOrdered, "two"));
		queue.Enqueue(Packet("s", "topic.units", WebUiPacketKind.Delta, WebUiDeliverySemantics.LatestWins, "new"));

		await queue.FlushAsync(transport, TestContext.CurrentContext.CancellationToken);

		string[] payloads = transport.Sent.Select(packet => WebUiDataPlaneProtocol.PayloadToString(packet.Payload)).ToArray();
		Assert.That(payloads, Is.EqualTo(new[] { "one", "two", "new" }));
	}

	[Test]
	public async Task OversizeReliablePacket_EmitsStructuredError_AndRecordsDiagnostics()
	{
		var transport = new FakeWebUiDataTransport();
		var queue = new WebUiOutboundQueue(maxPacketBytes: 8);
		queue.Enqueue(Packet("s", "orders", WebUiPacketKind.Command, WebUiDeliverySemantics.ReliableOrdered, "this is too large"));

		await queue.FlushAsync(transport, TestContext.CurrentContext.CancellationToken);

		Assert.That(queue.Diagnostics.DroppedPackets, Is.EqualTo(1));
		Assert.That(queue.Diagnostics.LastError, Does.Contain("exceeds max packet bytes"));
		WebUiOutboundPacket error = transport.Sent.Single();
		Assert.That(error.Kind, Is.EqualTo(WebUiPacketKind.CommandError));
		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(error.Payload.Span, out WebUiControlEnvelope envelope, out _), Is.True);
		Assert.That(envelope.Kind, Is.EqualTo("error"));
	}

	private static WebUiOutboundPacket Packet(
		string session,
		string topic,
		WebUiPacketKind kind,
		WebUiDeliverySemantics delivery,
		string payload)
	{
		return new WebUiOutboundPacket(session, topic, kind, delivery, Encoding.UTF8.GetBytes(payload));
	}
}
