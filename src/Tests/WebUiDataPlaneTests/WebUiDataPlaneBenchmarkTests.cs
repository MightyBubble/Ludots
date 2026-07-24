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
[Category("benchmark")]
public sealed class WebUiDataPlaneBenchmarkTests
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	[Test]
	public async Task Benchmark_WebUiDataPlaneTransportBaseline_WritesMachineReadableJsonl()
	{
		var rows = new List<WebUiDataPlaneBenchmarkRow>
		{
			await MeasureMessageEntitySnapshotAsync(),
			await MeasureSharedMemoryEntitySoaFullDeltaAsync(),
			await MeasureSharedMemoryEntityIndexedDeltaAsync()
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
		Assert.That(rows.Where(row => row.TransportMode == "shared-memory").All(static row => row.ObservedBase64Chunks == 0), Is.True);
		Assert.That(rows.Single(row => row.Scenario == "entity-50k-soa-full-delta").PayloadBytes, Is.LessThan(
			rows.Single(row => row.Scenario == "entity-50k-row-snapshot-message").PayloadBytes));
		Assert.That(rows.Single(row => row.Scenario == "entity-50k-indexed-delta-2k").PayloadBytes, Is.LessThan(
			rows.Single(row => row.Scenario == "entity-50k-soa-full-delta").PayloadBytes));
	}

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureMessageEntitySnapshotAsync()
	{
		const int iterations = 8;
		const string topic = "webui.entityCollection";
		WebUiEntityColumnarRow[] rows = CreateRows(50_000);
		byte[] payload = EncodeRows(rows);
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
			Scenario: "entity-50k-row-snapshot-message",
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

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureSharedMemoryEntitySoaFullDeltaAsync()
	{
		const int iterations = 8;
		const string topic = "webui.entityCollection";
		const int rowCount = 50_000;
		int[] stableIds = new int[rowCount];
		byte[] team = new byte[rowCount];
		int[] generations = new int[rowCount];
		float[] x = new float[rowCount];
		float[] y = new float[rowCount];
		ushort[] hp = new ushort[rowCount];
		byte[] state = new byte[rowCount];
		InitializeColumns(stableIds, team, generations, x, y, hp, state);
		byte[] payload = new byte[WebUiEntityColumnarPacket.GetSoaFullDeltaByteCount(rowCount)];
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
					payload.Length * 3)
			});
		var queue = new WebUiOutboundQueue(transport.Capabilities.MaxPacketBytes);

		int bytesWritten = 0;
		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		for (int i = 0; i < iterations; i++)
		{
			UpdateAllDynamicColumns(i + 1, generations, x, y, hp, state);
			bool encoded = WebUiEntityColumnarPacket.TryEncodeSoaFullDelta(
				WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
				sequence: i + 1,
				tick: i + 1,
				generations,
				x,
				y,
				hp,
				state,
				payload,
				out bytesWritten);
			Assert.That(encoded, Is.True);
			queue.Enqueue(new WebUiOutboundPacket(
				"bench-session",
				topic,
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.LatestWins,
				payload.AsMemory(0, bytesWritten),
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
		Assert.That(bridgeRead, Is.EqualTo(payload.AsSpan(0, bytesWritten).ToArray()));

		BrowserSharedMemoryBufferInfo info = store.GetBufferInfo("bench.entity.0");
		using MemoryMappedFile opened = MemoryMappedFile.OpenExisting(
			info.MemoryMapName,
			MemoryMappedFileRights.Read);
		using MemoryMappedViewAccessor accessor = opened.CreateViewAccessor(
			descriptor.GetProperty("byteOffset").GetInt32(),
			bytesWritten,
			MemoryMappedFileAccess.Read);
		byte[] mappedRead = new byte[bytesWritten];
		accessor.ReadArray(0, mappedRead, 0, mappedRead.Length);
		Assert.That(mappedRead, Is.EqualTo(payload.AsSpan(0, bytesWritten).ToArray()));
		WebUiDataPlaneDiagnostics diagnostics = queue.Diagnostics;

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-50k-soa-full-delta",
			TransportMode: transport.Capabilities.ModeName,
			EntityRows: rowCount,
			PacketCount: diagnostics.SentPackets,
			PayloadBytes: diagnostics.SentBytes,
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

	private static async Task<WebUiDataPlaneBenchmarkRow> MeasureSharedMemoryEntityIndexedDeltaAsync()
	{
		const int iterations = 8;
		const int rowCount = 50_000;
		const int changedRows = 2048;
		const string topic = "webui.entityCollection";
		var indices = new int[changedRows];
		var rows = new WebUiEntityColumnarRow[changedRows];
		byte[] payload = new byte[WebUiEntityColumnarPacket.GetIndexedDeltaByteCount(changedRows)];
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
					"bench.entity.delta.0",
					WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
					payload.Length * 3)
			});
		var queue = new WebUiOutboundQueue(transport.Capabilities.MaxPacketBytes);

		int bytesWritten = 0;
		GC.GetAllocatedBytesForCurrentThread();
		long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		for (int iteration = 0; iteration < iterations; iteration++)
		{
			FillIndexedDeltaRows(iteration + 1, rowCount, indices, rows);
			bool encoded = WebUiEntityColumnarPacket.TryEncodeIndexedDelta(
				WebUiEntityColumnarPacket.CurrentSchemaId,
				indices,
				rows,
				payload,
				out bytesWritten);
			Assert.That(encoded, Is.True);
			queue.Enqueue(new WebUiOutboundPacket(
				"bench-session",
				topic,
				WebUiPacketKind.Delta,
				WebUiDeliverySemantics.LatestWins,
				payload.AsMemory(0, bytesWritten),
				WebUiDataPlaneProtocol.BinaryContentType,
				ClientSeq: iteration + 1));
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
		Assert.That(bridgeRead, Is.EqualTo(payload.AsSpan(0, bytesWritten).ToArray()));
		WebUiDataPlaneDiagnostics diagnostics = queue.Diagnostics;

		return new WebUiDataPlaneBenchmarkRow(
			Scenario: "entity-50k-indexed-delta-2k",
			TransportMode: transport.Capabilities.ModeName,
			EntityRows: changedRows,
			PacketCount: diagnostics.SentPackets,
			PayloadBytes: diagnostics.SentBytes,
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

	private static byte[] EncodeRows(WebUiEntityColumnarRow[] rows)
	{
		byte[] payload = new byte[WebUiEntityColumnarPacket.GetSnapshotByteCount(rows.Length)];
		bool encoded = WebUiEntityColumnarPacket.TryEncodeSnapshot(
			WebUiEntityColumnarPacket.CurrentSchemaId,
			rows,
			payload,
			out int bytesWritten);
		Assert.That(encoded, Is.True);
		Assert.That(bytesWritten, Is.EqualTo(payload.Length));
		return payload;
	}

	private static void InitializeColumns(
		int[] stableIds,
		byte[] team,
		int[] generations,
		float[] x,
		float[] y,
		ushort[] hp,
		byte[] state)
	{
		for (int i = 0; i < stableIds.Length; i++)
		{
			stableIds[i] = i + 1;
			team[i] = (byte)(i & 7);
			generations[i] = 0;
			x[i] = i & 511;
			y[i] = i >> 9;
			hp[i] = (ushort)(50 + (i % 50));
			state[i] = (byte)(i & 3);
		}
	}

	private static void UpdateAllDynamicColumns(
		int tick,
		int[] generations,
		float[] x,
		float[] y,
		ushort[] hp,
		byte[] state)
	{
		Array.Fill(generations, tick);
		for (int i = 0; i < generations.Length; i++)
		{
			int phase = (tick + i) & 255;
			float wave = (phase - 128) * (1f / 128f);
			x[i] = (i & 511) + wave;
			y[i] = (i >> 9) - wave;
			hp[i] = (ushort)(50 + ((i + tick) % 50));
			state[i] = (byte)((i + tick) & 7);
		}
	}

	private static void FillIndexedDeltaRows(
		int tick,
		int rowCount,
		int[] indices,
		WebUiEntityColumnarRow[] rows)
	{
		for (int i = 0; i < indices.Length; i++)
		{
			int index = ((tick - 1) * indices.Length + i) % rowCount;
			indices[i] = index;
			rows[i] = new WebUiEntityColumnarRow(
				StableId: index + 1,
				Generation: tick,
				X: index & 511,
				Y: index >> 9,
				Hp: (ushort)(50 + ((index + tick) % 50)),
				Team: (byte)(index & 7),
				State: (byte)((index + tick) & 7));
		}
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
