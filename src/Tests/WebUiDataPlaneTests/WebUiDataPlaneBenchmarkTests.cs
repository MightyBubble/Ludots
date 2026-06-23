using System.Diagnostics;
using System.Text.Json;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiDataPlaneBenchmarkTests
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	[Test]
	public async Task Benchmark_WebUiDataPlaneTransportBaseline_WritesMachineReadableJsonl()
	{
		var rows = new List<WebUiDataPlaneBenchmarkRow>
		{
			await MeasureMessageEntityDeltaAsync(),
			MeasureSharedBufferEntityDelta()
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
	}

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureMessageEntityDeltaAsync()
	{
		const int iterations = 16;
		WebUiEntityColumnarRow[] rows = CreateRows(10_000);
		byte[] payload = WebUiEntityColumnarPacket.EncodeSnapshot(WebUiEntityColumnarPacket.CurrentSchemaId, rows);
		var transport = new FakeWebUiDataTransport(WebUiTransportCapabilities.MessageBridge(
			maxPacketBytes: payload.Length * 2,
			chunkSize: payload.Length * 2));
		var queue = new WebUiOutboundQueue(maxPacketBytes: payload.Length * 2);

		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		for (int i = 0; i < iterations; i++)
		{
			queue.Enqueue(new WebUiOutboundPacket(
				"bench-session",
				"webui.entityCollection",
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

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-10k-delta",
			TransportMode: "message",
			EntityRows: rows.Length,
			PacketCount: diagnostics.SentPackets,
			PayloadBytes: diagnostics.SentBytes,
			PublishCpuMs: Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds,
			ManagedAllocatedBytes: allocated,
			ExpectedManagedCopiesPerPayload: transport.Capabilities.ExpectedManagedCopiesPerPayload,
			CoalescedPackets: diagnostics.CoalescedPackets,
			DroppedPackets: diagnostics.DroppedPackets,
			CommandRttP50Ms: null,
			InputLatencyP50Ms: null,
			BrowserFrameTimeP50Ms: null);
	}

	private static WebUiDataPlaneBenchmarkRow MeasureSharedBufferEntityDelta()
	{
		const int iterations = 16;
		WebUiEntityColumnarRow[] rows = CreateRows(10_000);
		byte[] payload = WebUiEntityColumnarPacket.EncodeSnapshot(WebUiEntityColumnarPacket.CurrentSchemaId, rows);
		var ring = new WebUiSharedBufferRing(
			"bench.entity.0",
			"webui.entityCollection",
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			new byte[payload.Length * 2]);
		WebUiTransportCapabilities capabilities = WebUiTransportCapabilities.SharedMemory(
			sharedBuffers: new[] { ring.CreateDescriptor() });

		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		WebUiSharedBufferWriteResult result = default;
		for (int i = 0; i < iterations; i++)
		{
			result = ring.WriteLatestWins(payload, tick: i + 1);
			Assert.That(result.Accepted, Is.True, result.Error);
		}
		long stop = Stopwatch.GetTimestamp();
		long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-10k-delta",
			TransportMode: capabilities.ModeName,
			EntityRows: rows.Length,
			PacketCount: result.Descriptor.Sequence,
			PayloadBytes: (long)payload.Length * iterations,
			PublishCpuMs: Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds,
			ManagedAllocatedBytes: allocated,
			ExpectedManagedCopiesPerPayload: capabilities.ExpectedManagedCopiesPerPayload,
			CoalescedPackets: result.Descriptor.CoalescedPackets,
			DroppedPackets: result.Descriptor.DroppedPackets,
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
		long CoalescedPackets,
		long DroppedPackets,
		double? CommandRttP50Ms,
		double? InputLatencyP50Ms,
		double? BrowserFrameTimeP50Ms);
}
