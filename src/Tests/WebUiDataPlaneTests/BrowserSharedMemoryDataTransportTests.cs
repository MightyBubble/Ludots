using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text.Json;
using Ludots.UI.Browser;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
[SupportedOSPlatform("windows")]
public sealed class BrowserSharedMemoryDataTransportTests
{
	[Test]
	public async Task BinaryLatestWinsPacket_WritesMemoryMappedBufferAndPostsDescriptorOnly()
	{
		var bridge = new FakeBrowserMessageBridge();
		var sharedBuffers = new Ludots.UI.Browser.BrowserSharedBufferBridge();
		await using var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			bridge,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					"webui.entityCollection",
					"test.entity.0",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					4096)
			});
		byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8 };
		var packet = new WebUiOutboundPacket(
			"session-a",
			"webui.entityCollection",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			WebUiDataPlaneProtocol.BinaryContentType,
			RequestId: 11,
			ClientSeq: 7);

		await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken);

		BrowserScriptMessage posted = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		Assert.That(posted.Channel, Is.EqualTo(BrowserSharedMemoryDataTransport.SharedBufferChannel));
		Assert.That(posted.Payload, Does.Not.Contain("base64"));
		using JsonDocument document = JsonDocument.Parse(posted.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("sessionId").GetString(), Is.EqualTo("session-a"));
		Assert.That(root.GetProperty("topic").GetString(), Is.EqualTo("webui.entityCollection"));
		Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo(WebUiPacketKind.Snapshot.ToString()));
		JsonElement descriptor = root.GetProperty("payload").GetProperty("sharedBuffer");
		Assert.That(descriptor.GetProperty("bufferId").GetString(), Is.EqualTo("test.entity.0"));
		Assert.That(descriptor.GetProperty("byteLength").GetInt32(), Is.EqualTo(payload.Length));
		Assert.That(descriptor.GetProperty("sequence").GetInt64(), Is.EqualTo(1));
		Assert.That(root.GetProperty("payload").TryGetProperty("memoryMapName", out _), Is.False);

		byte[] bridgeRead = sharedBuffers.ReadSharedBuffer(
			descriptor.GetProperty("bufferId").GetString()!,
			descriptor.GetProperty("byteOffset").GetInt32(),
			descriptor.GetProperty("byteLength").GetInt32(),
			descriptor.GetProperty("sequence").GetInt64());
		Assert.That(bridgeRead, Is.EqualTo(payload));

		BrowserSharedMemoryBufferInfo info = store.GetBufferInfo("test.entity.0");
		using MemoryMappedFile opened = MemoryMappedFile.OpenExisting(
			info.MemoryMapName,
			MemoryMappedFileRights.Read);
		using MemoryMappedViewAccessor accessor = opened.CreateViewAccessor(
			descriptor.GetProperty("byteOffset").GetInt32(),
			payload.Length,
			MemoryMappedFileAccess.Read);
		byte[] mappedRead = new byte[payload.Length];
		accessor.ReadArray(0, mappedRead, 0, mappedRead.Length);
		Assert.That(mappedRead, Is.EqualTo(payload));
	}

	[Test]
	public async Task ReadSharedBuffer_WhenRingRegionWasOverwritten_RejectsStaleDescriptor()
	{
		var bridge = new FakeBrowserMessageBridge();
		var sharedBuffers = new BrowserSharedBufferBridge();
		await using var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			bridge,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					"webui.entityCollection",
					"test.entity.wrap",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					WebUiSharedBufferRing.DefaultHeaderBytes + 8)
			});
		var first = new WebUiOutboundPacket(
			"session-a",
			"webui.entityCollection",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 },
			WebUiDataPlaneProtocol.BinaryContentType,
			ClientSeq: 1);
		WebUiOutboundPacket second = first with
		{
			Payload = new byte[] { 2, 2, 2, 2, 2, 2, 2, 2 },
			ClientSeq = 2
		};

		await transport.SendAsync(first, TestContext.CurrentContext.CancellationToken);
		BrowserScriptMessage firstPost = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		using JsonDocument firstDocument = JsonDocument.Parse(firstPost.Payload);
		JsonElement firstDescriptor = firstDocument.RootElement.GetProperty("payload").GetProperty("sharedBuffer");
		await transport.SendAsync(second, TestContext.CurrentContext.CancellationToken);

		Assert.Throws<InvalidOperationException>(() => sharedBuffers.ReadSharedBuffer(
			firstDescriptor.GetProperty("bufferId").GetString()!,
			firstDescriptor.GetProperty("byteOffset").GetInt32(),
			firstDescriptor.GetProperty("byteLength").GetInt32(),
			firstDescriptor.GetProperty("sequence").GetInt64()));
	}

	[Test]
	public async Task ReadSharedBuffer_WhenRingRegionWasPartiallyOverwritten_RejectsStaleDescriptor()
	{
		var bridge = new FakeBrowserMessageBridge();
		var sharedBuffers = new BrowserSharedBufferBridge();
		await using var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			bridge,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					"webui.entityCollection",
					"test.entity.partial-wrap",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					WebUiSharedBufferRing.DefaultHeaderBytes + 12)
			});
		var first = new WebUiOutboundPacket(
			"session-a",
			"webui.entityCollection",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			new byte[] { 1, 1, 1, 1 },
			WebUiDataPlaneProtocol.BinaryContentType,
			ClientSeq: 1);
		WebUiOutboundPacket second = first with
		{
			Payload = new byte[] { 2, 2, 2, 2 },
			ClientSeq = 2
		};
		WebUiOutboundPacket third = first with
		{
			Payload = new byte[] { 3, 3, 3, 3, 3, 3, 3, 3 },
			ClientSeq = 3
		};

		await transport.SendAsync(first, TestContext.CurrentContext.CancellationToken);
		_ = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		await transport.SendAsync(second, TestContext.CurrentContext.CancellationToken);
		BrowserScriptMessage secondPost = await bridge.WaitForPostAsync(TestContext.CurrentContext.CancellationToken);
		using JsonDocument secondDocument = JsonDocument.Parse(secondPost.Payload);
		JsonElement secondDescriptor = secondDocument.RootElement.GetProperty("payload").GetProperty("sharedBuffer");
		await transport.SendAsync(third, TestContext.CurrentContext.CancellationToken);

		Assert.Throws<InvalidOperationException>(() => sharedBuffers.ReadSharedBuffer(
			secondDescriptor.GetProperty("bufferId").GetString()!,
			secondDescriptor.GetProperty("byteOffset").GetInt32(),
			secondDescriptor.GetProperty("byteLength").GetInt32(),
			secondDescriptor.GetProperty("sequence").GetInt64()));
	}

	[Test]
	public async Task Dispose_ReleasesSharedBufferProviderAndRejectsLaterReads()
	{
		var sharedBuffers = new Ludots.UI.Browser.BrowserSharedBufferBridge();
		var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			new FakeBrowserMessageBridge(),
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					"webui.entityCollection",
					"test.entity.dispose",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					1024)
			});
		var packet = new WebUiOutboundPacket(
			"session-a",
			"webui.entityCollection",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			new byte[] { 9, 8, 7 },
			WebUiDataPlaneProtocol.BinaryContentType);
		await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken);

		await transport.DisposeAsync();

		Assert.Throws<InvalidOperationException>(() => sharedBuffers.ReadSharedBuffer(
			"test.entity.dispose",
			WebUiSharedBufferRing.DefaultHeaderBytes,
			3,
			1));
		Assert.ThrowsAsync<ObjectDisposedException>(async () =>
			await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken));
	}

	[Test]
	public async Task BinaryPacketForTopicWithoutSharedBuffer_FailsInsteadOfSendingBase64()
	{
		var bridge = new FakeBrowserMessageBridge();
		var sharedBuffers = new Ludots.UI.Browser.BrowserSharedBufferBridge();
		await using var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			bridge,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					"webui.entityCollection",
					"test.entity.strict",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					1024)
			});
		var packet = new WebUiOutboundPacket(
			"session-a",
			"webui.unconfiguredBinary",
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			new byte[] { 1, 2, 3 },
			WebUiDataPlaneProtocol.BinaryContentType);

		InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await transport.SendAsync(packet, TestContext.CurrentContext.CancellationToken));

		Assert.That(exception, Is.Not.Null);
		Assert.That(exception!.Message, Does.Contain("does not have a shared-memory buffer"));
		Assert.That(bridge.Posted, Is.Empty);
	}
}
