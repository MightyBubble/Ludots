using System.Text.Json;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiTransportNegotiationTests
{
	[Test]
	public async Task Handshake_ReturnsStandardTransportCapabilityView()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport(WebUiTransportCapabilities.MessageBridge(
			maxPacketBytes: 4096,
			chunkSize: 1024,
			expectedManagedCopiesPerPayload: 2));
		runtime.AttachSession("session-a", transport);

		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 1, "handshake", "system", new
		{
			capabilities = new[] { "handshake", "subscribe", "command", "binary.base64" },
			sdkVersion = "test"
		}));

		WebUiOutboundPacket sent = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(sent.Payload.Span, out WebUiControlEnvelope envelope, out string error), Is.True, error);
		Assert.That(envelope.Kind, Is.EqualTo("handshakeAck"));
		Assert.That(envelope.Payload.GetProperty("transportMode").GetString(), Is.EqualTo("message"));
		JsonElement capabilities = envelope.Payload.GetProperty("capabilities");
		Assert.That(capabilities.GetProperty("modeName").GetString(), Is.EqualTo("message"));
		Assert.That(capabilities.GetProperty("supportsBase64Chunks").GetBoolean(), Is.True);
		Assert.That(capabilities.GetProperty("supportsChunking").GetBoolean(), Is.True);
		Assert.That(capabilities.GetProperty("maxPacketBytes").GetInt32(), Is.EqualTo(4096));
		Assert.That(capabilities.GetProperty("chunkSize").GetInt32(), Is.EqualTo(1024));
		Assert.That(capabilities.GetProperty("expectedManagedCopiesPerPayload").GetInt32(), Is.EqualTo(2));
		Assert.That(capabilities.GetProperty("deliverySemantics").EnumerateArray().Select(item => item.GetString()), Does.Contain("ReliableOrdered"));
		Assert.That(capabilities.GetProperty("deliverySemantics").EnumerateArray().Select(item => item.GetString()), Does.Contain("LatestWins"));
	}

	[Test]
	public async Task Handshake_WhenSharedMemoryIsRequiredOnMessageTransport_ReturnsCapabilityMismatch()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var transport = new FakeWebUiDataTransport(WebUiTransportCapabilities.MessageBridge(maxPacketBytes: 4096));
		runtime.AttachSession("session-a", transport);

		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 2, "handshake", "system", new
		{
			requiredCapabilities = new[] { "shared-memory" },
			sdkVersion = "test"
		}));

		WebUiOutboundPacket sent = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(sent.Payload.Span, out WebUiControlEnvelope envelope, out string error), Is.True, error);
		Assert.That(envelope.Kind, Is.EqualTo("error"));
		Assert.That(envelope.Payload.GetProperty("code").GetString(), Is.EqualTo("transport_capability_mismatch"));
		Assert.That(envelope.Payload.GetProperty("requiredCapabilities").EnumerateArray().Select(item => item.GetString()), Does.Contain("shared-memory"));
		Assert.That(envelope.Payload.GetProperty("capabilities").GetProperty("modeName").GetString(), Is.EqualTo("message"));
	}

	[Test]
	public async Task Handshake_ForSharedMemoryTransport_ReturnsSharedBufferDescriptors()
	{
		await using var runtime = new WebUiDataPlaneRuntime();
		var ring = new WebUiSharedBufferRing(
			"buffer.entity.0",
			"webui.entityCollection",
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			new byte[1024]);
		var transport = new FakeWebUiDataTransport(WebUiTransportCapabilities.SharedMemory(
			sharedBuffers: new[] { ring.CreateDescriptor() }));
		runtime.AttachSession("session-a", transport);

		transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket("session-a", 3, "handshake", "system", new
		{
			requiredCapabilities = new[] { "shared-memory", "shared-buffer-descriptor" },
			sdkVersion = "test"
		}));

		WebUiOutboundPacket sent = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(sent.Payload.Span, out WebUiControlEnvelope envelope, out string error), Is.True, error);
		Assert.That(envelope.Kind, Is.EqualTo("handshakeAck"));
		Assert.That(envelope.Payload.GetProperty("transportMode").GetString(), Is.EqualTo("shared-memory"));
		JsonElement sharedBuffer = envelope.Payload
			.GetProperty("capabilities")
			.GetProperty("sharedBuffers")
			.EnumerateArray()
			.Single();
		Assert.That(sharedBuffer.GetProperty("bufferId").GetString(), Is.EqualTo("buffer.entity.0"));
		Assert.That(sharedBuffer.GetProperty("topic").GetString(), Is.EqualTo("webui.entityCollection"));
		Assert.That(sharedBuffer.GetProperty("layout").GetString(), Is.EqualTo(WebUiSharedBufferDescriptor.RingBufferLayout));
		Assert.That(sharedBuffer.GetProperty("capacityBytes").GetInt32(), Is.EqualTo(1024));
	}
}
