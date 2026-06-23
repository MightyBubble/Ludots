using System.Diagnostics;
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
public sealed class WebUiDataPlaneBenchmarkTests
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	[Test]
	public async Task Benchmark_WebUiDataPlaneTransportBaseline_WritesMachineReadableJsonl()
	{
		var rows = new List<WebUiDataPlaneBenchmarkRow>
		{
			await MeasureMessageEntityDeltaAsync(),
			await MeasureSharedMemoryEntityDeltaAsync()
		};
		string artifactDirectory = GetArtifactDirectory();
		Directory.CreateDirectory(artifactDirectory);
		string path = Path.Combine(artifactDirectory, "transport-baseline.jsonl");
		await File.WriteAllLinesAsync(
			path,
			rows.Select(row => JsonSerializer.Serialize(row, JsonOptions)),
			TestContext.CurrentContext.CancellationToken);

		TestContext.AddTestAttachment(path);
		Assert.That(File.Exists(path), Is.True);
		Assert.That(rows.Select(row => row.TransportMode), Does.Contain("message"));
		Assert.That(rows.Select(row => row.TransportMode), Does.Contain("shared-memory"));
		Assert.That(rows.All(row => row.PacketCount > 0), Is.True);
		Assert.That(rows.All(row => row.PublishCpuMs >= 0), Is.True);
		Assert.That(rows.Single(row => row.TransportMode == "shared-memory").ObservedBase64Chunks, Is.Zero);
	}

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureMessageEntityDeltaAsync()
	{
		const int iterations = 16;
		const string topic = "webui.entityCollection";
		WebUiEntityColumnarRow[] rows = CreateRows(10_000);
		byte[] payload = WebUiEntityColumnarPacket.EncodeSnapshot(WebUiEntityColumnarPacket.CurrentSchemaId, rows);
		var bridge = new FakeBrowserMessageBridge();
		await using var transport = new BrowserMessageBridgeDataTransport(
			bridge,
			chunkSize: payload.Length * 2);
		var queue = new WebUiOutboundQueue(maxPacketBytes: payload.Length * 2);

		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		for (int i = 0; i < iterations; i++)
		{
			queue.Enqueue(new WebUiOutboundPacket(
				"bench-session",
				topic,
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.LatestWins,
				payload,
				WebUiDataPlaneProtocol.BinaryContentType,
				ClientSeq: i + 1));
			await queue.FlushAsync(transport, TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
		}
		long stop = Stopwatch.GetTimestamp();
		long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
		WebUiDataPlaneDiagnostics diagnostics = queue.Diagnostics;
		BrowserScriptMessage[] posted = bridge.Posted.ToArray();
		Assert.That(posted, Has.Length.EqualTo(iterations));
		Assert.That(posted.All(static message => message.Channel == BrowserMessageBridgeDataTransport.BinaryChunkChannel), Is.True);
		Assert.That(posted.All(static message => message.Payload.Contains("base64", StringComparison.OrdinalIgnoreCase)), Is.True);

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-10k-delta",
			TransportMode: transport.Capabilities.ModeName,
			EntityRows: rows.Length,
			PacketCount: diagnostics.SentPackets,
			PayloadBytes: diagnostics.SentBytes,
			PublishCpuMs: Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds,
			ManagedAllocatedBytes: allocated,
			ExpectedManagedCopiesPerPayload: transport.Capabilities.ExpectedManagedCopiesPerPayload,
			DescriptorMessageBytes: 0,
			ObservedBase64Chunks: posted.Length,
			CoalescedPackets: diagnostics.CoalescedPackets,
			DroppedPackets: diagnostics.DroppedPackets,
			CommandRttP50Ms: null,
			InputLatencyP50Ms: null,
			BrowserFrameTimeP50Ms: null);
	}

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureSharedMemoryEntityDeltaAsync()
	{
		const int iterations = 16;
		const string topic = "webui.entityCollection";
		WebUiEntityColumnarRow[] rows = CreateRows(10_000);
		byte[] payload = WebUiEntityColumnarPacket.EncodeSnapshot(WebUiEntityColumnarPacket.CurrentSchemaId, rows);
		var bridge = new FakeBrowserMessageBridge();
		var sharedBuffers = new BrowserSharedBufferBridge();
		await using var store = new BrowserSharedMemoryBufferStore(sharedBuffers);
		await using var transport = new BrowserSharedMemoryDataTransport(
			bridge,
			store,
			new[]
			{
				new BrowserSharedMemoryTopicBuffer(
					topic,
					"bench.entity.0",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					payload.Length * 2)
			});
		var queue = new WebUiOutboundQueue(transport.Capabilities.MaxPacketBytes);

		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		for (int i = 0; i < iterations; i++)
		{
			queue.Enqueue(new WebUiOutboundPacket(
				"bench-session",
				topic,
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.LatestWins,
				payload,
				WebUiDataPlaneProtocol.BinaryContentType,
				ClientSeq: i + 1));
			await queue.FlushAsync(transport, TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
		}
		long stop = Stopwatch.GetTimestamp();
		long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
		BrowserScriptMessage[] posted = bridge.Posted.ToArray();
		Assert.That(posted, Has.Length.EqualTo(iterations));
		Assert.That(posted.All(static message => message.Channel == BrowserSharedMemoryDataTransport.SharedBufferChannel), Is.True);
		Assert.That(posted.All(static message => !message.Payload.Contains("base64", StringComparison.OrdinalIgnoreCase)), Is.True);

		using JsonDocument document = JsonDocument.Parse(posted[^1].Payload);
		JsonElement descriptor = document.RootElement.GetProperty("payload").GetProperty("sharedBuffer");
		byte[] bridgeRead = sharedBuffers.ReadSharedBuffer(
			descriptor.GetProperty("bufferId").GetString()!,
			descriptor.GetProperty("byteOffset").GetInt32(),
			descriptor.GetProperty("byteLength").GetInt32(),
			descriptor.GetProperty("sequence").GetInt64());
		Assert.That(bridgeRead, Is.EqualTo(payload));

		BrowserSharedMemoryBufferInfo info = store.GetBufferInfo("bench.entity.0");
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
		WebUiDataPlaneDiagnostics diagnostics = queue.Diagnostics;

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-10k-delta",
			TransportMode: transport.Capabilities.ModeName,
			EntityRows: rows.Length,
			PacketCount: diagnostics.SentPackets,
			PayloadBytes: (long)payload.Length * iterations,
			PublishCpuMs: Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds,
			ManagedAllocatedBytes: allocated,
			ExpectedManagedCopiesPerPayload: transport.Capabilities.ExpectedManagedCopiesPerPayload,
			DescriptorMessageBytes: posted.Sum(static message => message.Payload.Length),
			ObservedBase64Chunks: 0,
			CoalescedPackets: descriptor.GetProperty("coalescedPackets").GetInt64(),
			DroppedPackets: descriptor.GetProperty("droppedPackets").GetInt64(),
			CommandRttP50Ms: null,
			InputLatencyP50Ms: null,
			BrowserFrameTimeP50Ms: null);
	}

	private static WebUiEntityColumnarRow[] CreateRows(int count)
	{
		var rows = new WebUiEntityColumnarRow[count];
		for (int i = 0; i < rows.Length; i++)
		{
			rows[i] = new WebUiEntityColumnarRow(
				i + 1,
				1,
				X: i % 512,
				Y: i / 512,
				Hp: (ushort)(50 + (i % 50)),
				Team: (byte)(i % 8),
				State: (byte)(i % 4));
		}

		return rows;
	}

	private static string GetArtifactDirectory()
	{
		string current = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrEmpty(current))
		{
			if (File.Exists(Path.Combine(current, "Ludots.sln")) ||
				File.Exists(Path.Combine(current, ".git")) ||
				Directory.Exists(Path.Combine(current, ".git")))
			{
				return Path.Combine(current, "artifacts", "benchmarks", "webui-dataplane");
			}

			current = Directory.GetParent(current)?.FullName ?? string.Empty;
		}

		return Path.Combine(TestContext.CurrentContext.TestDirectory, "artifacts", "benchmarks", "webui-dataplane");
	}

	private sealed record WebUiDataPlaneBenchmarkRow(
		string Scenario,
		string TransportMode,
		int EntityRows,
		long PacketCount,
		long PayloadBytes,
		double PublishCpuMs,
		long ManagedAllocatedBytes,
		int ExpectedManagedCopiesPerPayload,
		long DescriptorMessageBytes,
		long ObservedBase64Chunks,
		long CoalescedPackets,
		long DroppedPackets,
		double? CommandRttP50Ms,
		double? InputLatencyP50Ms,
		double? BrowserFrameTimeP50Ms);
}
