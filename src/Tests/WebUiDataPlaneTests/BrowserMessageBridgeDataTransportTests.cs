using System.Text;
using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class BrowserMessageBridgeDataTransportTests
{
	[Test]
	public void BrowserTransportAssembly_DoesNotContainProviderPrivateGlobals()
	{
		byte[] bytes = File.ReadAllBytes(typeof(BrowserMessageBridgeDataTransport).Assembly.Location);

		Assert.That(ContainsEncoded(bytes, "CefSharp"), Is.False);
		Assert.That(ContainsEncoded(bytes, "cefsharp"), Is.False);
		Assert.That(ContainsEncoded(bytes, "BLUI"), Is.False);
		Assert.That(ContainsEncoded(bytes, "V8"), Is.False);
		Assert.That(ContainsEncoded(bytes, "Unreal"), Is.False);
		Assert.That(ContainsEncoded(bytes, "UE5"), Is.False);
	}

	[Test]
	public async Task InboundControlMessage_FromBrowserBridge_PublishesInboundPacket()
	{
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(bridge);
		WebUiInboundPacket? received = null;
		transport.PacketReceived += (_, packet) => received = packet;

		byte[] payload = WebUiDataPlaneProtocol.SerializeControlEnvelope(
			WebUiDataPlaneProtocol.CreateControlEnvelope("session-a", 3, "subscribe", "topic.units", new { }));
		bridge.Receive(new BrowserScriptMessage(BrowserMessageBridgeDataTransport.ControlChannel, Encoding.UTF8.GetString(payload)));

		Assert.That(received, Is.Not.Null);
		Assert.That(received!.SessionId, Is.EqualTo("session-a"));
		Assert.That(received.Topic, Is.EqualTo("topic.units"));
		Assert.That(received.Kind, Is.EqualTo(WebUiPacketKind.Control));
		Assert.That(received.RequestId, Is.EqualTo(3));
	}

	[Test]
	public async Task OutboundControlPacket_PostsSameChannelBrowserMessage()
	{
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(bridge);
		WebUiOutboundPacket packet = WebUiDataPlaneProtocol.CreateControlResponse(
			"session-a",
			4,
			"handshakeAck",
			"system",
			new { ok = true });

		await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken);

		BrowserScriptMessage posted = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(posted.Channel, Is.EqualTo(BrowserMessageBridgeDataTransport.ControlChannel));
		using JsonDocument document = JsonDocument.Parse(posted.Payload);
		Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
		Assert.That(document.RootElement.GetProperty("requestId").GetInt64(), Is.EqualTo(4));
		Assert.That(document.RootElement.GetProperty("kind").GetString(), Is.EqualTo(WebUiPacketKind.Control.ToString()));
	}

	[Test]
	public async Task StringOnlyBridge_SendsBinaryPayloadAsBase64Chunks_PreservingTopicAndRequest()
	{
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(
			bridge,
			WebUiTransportCapabilities.StringBridge(maxPacketBytes: 1024),
			chunkSize: 4);
		var packet = new WebUiOutboundPacket(
			"session-a",
			"topic.binary",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			new byte[] { 1, 2, 3, 4, 5, 6 },
			WebUiDataPlaneProtocol.BinaryContentType,
			RequestId: 9);

		await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken);

		BrowserScriptMessage chunk = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(chunk.Channel, Is.EqualTo(BrowserMessageBridgeDataTransport.BinaryChunkChannel));
		using JsonDocument document = JsonDocument.Parse(chunk.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
		Assert.That(root.GetProperty("topic").GetString(), Is.EqualTo("topic.binary"));
		Assert.That(root.GetProperty("requestId").GetInt64(), Is.EqualTo(9));
		Assert.That(root.GetProperty("encoding").GetString(), Is.EqualTo("base64"));
		Assert.That(Convert.FromBase64String(root.GetProperty("data").GetString()!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
	}

	[Test]
	public async Task DefaultCapabilities_DeclareMessageBridgeBase64ChunkMode()
	{
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(bridge, chunkSize: 2048);

		WebUiTransportCapabilities capabilities = transport.Capabilities;

		Assert.That(capabilities.ModeName, Is.EqualTo("message"));
		Assert.That(capabilities.SupportsBinary, Is.False);
		Assert.That(capabilities.SupportsSharedMemory, Is.False);
		Assert.That(capabilities.SupportsBase64Chunks, Is.True);
		Assert.That(capabilities.SupportsChunking, Is.True);
		Assert.That(capabilities.ChunkSize, Is.EqualTo(2048));
		Assert.That(capabilities.ExpectedManagedCopiesPerPayload, Is.EqualTo(2));
		Assert.That(capabilities.Satisfies("binary.base64"), Is.True);
		Assert.That(capabilities.Satisfies("shared-memory"), Is.False);
	}

	[Test]
	public async Task Dispose_DetachesBridgeAndRepeatDisposeDoesNotThrow()
	{
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(bridge);
		int received = 0;
		transport.PacketReceived += (_, _) => received++;

		await transport.DisposeAsync();
		await transport.DisposeAsync();
		bridge.Receive(new BrowserScriptMessage(
			BrowserMessageBridgeDataTransport.ControlChannel,
			Encoding.UTF8.GetString(WebUiDataPlaneProtocol.SerializeControlEnvelope(
				WebUiDataPlaneProtocol.CreateControlEnvelope("session-a", 1, "subscribe", "topic", new { })))));

		Assert.That(received, Is.EqualTo(0));
		Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.SendAsync(WebUiDataPlaneProtocol.CreateControlResponse(
			"session-a",
			1,
			"ack",
			"topic",
			new { })));
	}

	[Test]
	public async Task SendAsync_WhenUnderlyingBrowserWasDisposed_MarksTransportDisposedAndDetachesInbound()
	{
		var bridge = new FakeBrowserMessageBridge
		{
			PostException = new ObjectDisposedException("ChromiumWebBrowser")
		};
		await using var transport = new BrowserMessageBridgeDataTransport(bridge);
		int received = 0;
		transport.PacketReceived += (_, _) => received++;

		ObjectDisposedException? exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
			await transport.SendAsync(WebUiDataPlaneProtocol.CreateControlResponse(
				"session-a",
				1,
				"ack",
				"topic",
				new { })));

		Assert.That(exception, Is.Not.Null);
		Assert.That(exception!.ObjectName, Is.EqualTo(nameof(BrowserMessageBridgeDataTransport)));
		bridge.Receive(new BrowserScriptMessage(
			BrowserMessageBridgeDataTransport.ControlChannel,
			Encoding.UTF8.GetString(WebUiDataPlaneProtocol.SerializeControlEnvelope(
				WebUiDataPlaneProtocol.CreateControlEnvelope("session-a", 2, "subscribe", "topic", new { })))));

		Assert.That(received, Is.EqualTo(0));
	}

	private static bool ContainsEncoded(byte[] haystack, string needle)
	{
		return ContainsBytes(haystack, Encoding.ASCII.GetBytes(needle)) ||
			ContainsBytes(haystack, Encoding.Unicode.GetBytes(needle));
	}

	private static bool ContainsBytes(byte[] haystack, byte[] needleBytes)
	{
		for (int i = 0; i <= haystack.Length - needleBytes.Length; i++)
		{
			bool matched = true;
			for (int j = 0; j < needleBytes.Length; j++)
			{
				if (haystack[i + j] != needleBytes[j])
				{
					matched = false;
					break;
				}
			}

			if (matched)
			{
				return true;
			}
		}

		return false;
	}
}
