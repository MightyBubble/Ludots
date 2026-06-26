using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiEntityColumnarPacketTests
{
	[Test]
	public void SnapshotPacket_RoundTripsStableIdPositionHpTeamAndStateColumns()
	{
		var rows = new[]
		{
			new WebUiEntityColumnarRow(1001, 4, 1.5f, 2.5f, 94, 1, 2),
			new WebUiEntityColumnarRow(1002, 7, 3.5f, 4.5f, 63, 2, 1),
			new WebUiEntityColumnarRow(1003, 9, 5.5f, 6.5f, 12, 1, 0),
		};

		byte[] bytes = WebUiEntityColumnarPacket.EncodeSnapshot(17, rows);
		WebUiEntityColumnarSnapshot decoded = WebUiEntityColumnarPacket.DecodeSnapshot(bytes);

		Assert.That(decoded.SchemaId, Is.EqualTo(17));
		Assert.That(decoded.Rows, Is.EqualTo(rows));
	}

	[Test]
	public void SnapshotPacket_CanEncodeFiftyThousandRowsIntoReusableBuffer()
	{
		WebUiEntityColumnarRow[] rows = CreateRows(50_000);
		int byteCount = WebUiEntityColumnarPacket.GetSnapshotByteCount(rows.Length);
		var destination = new byte[byteCount + 128];

		bool encoded = WebUiEntityColumnarPacket.TryEncodeSnapshot(
			WebUiEntityColumnarPacket.CurrentSchemaId,
			rows,
			destination,
			out int bytesWritten);

		Assert.That(encoded, Is.True);
		Assert.That(bytesWritten, Is.EqualTo(1_000_020));
		WebUiEntityColumnarSnapshot decoded = WebUiEntityColumnarPacket.DecodeSnapshot(
			destination.AsSpan(0, bytesWritten));
		Assert.That(decoded.Rows, Has.Length.EqualTo(50_000));
		Assert.That(decoded.Rows[0], Is.EqualTo(rows[0]));
		Assert.That(decoded.Rows[^1], Is.EqualTo(rows[^1]));
	}

	[Test]
	public void DeltaBuilder_SeparatesCreatedRemovedAndChangedRows_WithoutUnchangedRows()
	{
		var previous = new[]
		{
			new WebUiEntityColumnarRow(1, 1, 10, 10, 100, 1, 0),
			new WebUiEntityColumnarRow(2, 1, 20, 20, 100, 1, 0),
			new WebUiEntityColumnarRow(3, 1, 30, 30, 100, 1, 0),
		};
		var current = new[]
		{
			previous[0],
			previous[1] with { X = 21 },
			new WebUiEntityColumnarRow(4, 1, 40, 40, 100, 2, 0),
		};

		WebUiEntityColumnarDelta delta = WebUiEntityColumnarPacket.BuildDelta(5, previous, current);

		Assert.That(delta.RemovedStableIds, Is.EqualTo(new[] { 3 }));
		Assert.That(delta.ChangedRows.Select(row => row.StableId), Is.EqualTo(new[] { 2, 4 }));

		byte[] encoded = WebUiEntityColumnarPacket.EncodeDelta(delta.SchemaId, delta.RemovedStableIds, delta.ChangedRows);
		WebUiEntityColumnarDelta decoded = WebUiEntityColumnarPacket.DecodeDelta(encoded);
		Assert.That(decoded.RemovedStableIds, Is.EqualTo(delta.RemovedStableIds));
		Assert.That(decoded.ChangedRows, Is.EqualTo(delta.ChangedRows));
	}

	[Test]
	public void IndexedDeltaPacket_CanEncodeSmallChangedSetForFiftyThousandEntityWorld()
	{
		int[] indices = { 0, 1024, 49_999 };
		WebUiEntityColumnarRow[] rows =
		{
			new(1001, 12, 1.5f, 2.5f, 94, 1, 2),
			new(2025, 12, 3.5f, 4.5f, 63, 2, 1),
			new(51_000, 12, 5.5f, 6.5f, 12, 1, 0),
		};
		int byteCount = WebUiEntityColumnarPacket.GetIndexedDeltaByteCount(indices.Length);
		var destination = new byte[byteCount];

		bool encoded = WebUiEntityColumnarPacket.TryEncodeIndexedDelta(
			WebUiEntityColumnarPacket.CurrentSchemaId,
			indices,
			rows,
			destination,
			out int bytesWritten);

		Assert.That(encoded, Is.True);
		Assert.That(bytesWritten, Is.EqualTo(20 + (3 * 19)));
		WebUiEntityColumnarIndexedDelta decoded = WebUiEntityColumnarPacket.DecodeIndexedDelta(destination);
		Assert.That(decoded.SchemaId, Is.EqualTo(WebUiEntityColumnarPacket.CurrentSchemaId));
		Assert.That(decoded.Indices, Is.EqualTo(indices));
		Assert.That(decoded.Rows.Select(row => row.Generation), Is.EqualTo(rows.Select(row => row.Generation)));
		Assert.That(decoded.Rows.Select(row => row.X), Is.EqualTo(rows.Select(row => row.X)));
		Assert.That(decoded.Rows.Select(row => row.Y), Is.EqualTo(rows.Select(row => row.Y)));
		Assert.That(decoded.Rows.Select(row => row.Hp), Is.EqualTo(rows.Select(row => row.Hp)));
		Assert.That(decoded.Rows.Select(row => row.State), Is.EqualTo(rows.Select(row => row.State)));
		Assert.That(decoded.Rows.All(static row => row.StableId == 0 && row.Team == 0), Is.True);
	}

	[Test]
	public void SoaSnapshotPacket_CanEncodeFiftyThousandFullAttributeUpdates()
	{
		const int rowCount = 50_000;
		int[] stableIds = new int[rowCount];
		int[] generations = new int[rowCount];
		float[] x = new float[rowCount];
		float[] y = new float[rowCount];
		ushort[] hp = new ushort[rowCount];
		byte[] team = new byte[rowCount];
		byte[] state = new byte[rowCount];
		for (int i = 0; i < rowCount; i++)
		{
			stableIds[i] = 1001 + i;
			generations[i] = 42;
			x[i] = i % 512;
			y[i] = i / 512;
			hp[i] = (ushort)(25 + (i % 75));
			team[i] = (byte)(i & 7);
			state[i] = (byte)(i & 3);
		}

		int byteCount = WebUiEntityColumnarPacket.GetSoaSnapshotByteCount(rowCount);
		var destination = new byte[byteCount];

		bool encoded = WebUiEntityColumnarPacket.TryEncodeSoaSnapshot(
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			sequence: 7,
			tick: 42,
			stableIds,
			generations,
			x,
			y,
			hp,
			team,
			state,
			destination,
			out int bytesWritten);

		Assert.That(encoded, Is.True);
		Assert.That(bytesWritten, Is.EqualTo(1_000_032));
		WebUiEntityColumnarSoaFrame decoded = WebUiEntityColumnarPacket.DecodeSoaSnapshot(destination);
		Assert.That(decoded.SchemaId, Is.EqualTo(WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId));
		Assert.That(decoded.Sequence, Is.EqualTo(7));
		Assert.That(decoded.Tick, Is.EqualTo(42));
		Assert.That(decoded.StableIds, Has.Length.EqualTo(rowCount));
		Assert.That(decoded.StableIds[0], Is.EqualTo(1001));
		Assert.That(decoded.StableIds[^1], Is.EqualTo(1000 + rowCount));
		Assert.That(decoded.Generations[1024], Is.EqualTo(42));
		Assert.That(decoded.X[1024], Is.EqualTo(x[1024]));
		Assert.That(decoded.Y[49_999], Is.EqualTo(y[49_999]));
		Assert.That(decoded.Hp[4096], Is.EqualTo(hp[4096]));
		Assert.That(decoded.Team[4096], Is.EqualTo(team[4096]));
		Assert.That(decoded.State[4096], Is.EqualTo(state[4096]));
	}

	[Test]
	public void SoaFullDeltaPacket_CanEncodeFiftyThousandDynamicAttributeUpdates()
	{
		const int rowCount = 50_000;
		int[] generations = new int[rowCount];
		float[] x = new float[rowCount];
		float[] y = new float[rowCount];
		ushort[] hp = new ushort[rowCount];
		byte[] state = new byte[rowCount];
		for (int i = 0; i < rowCount; i++)
		{
			generations[i] = 43;
			x[i] = 100 + (i % 512);
			y[i] = 200 + (i / 512);
			hp[i] = (ushort)(35 + (i % 65));
			state[i] = (byte)(i & 7);
		}

		int byteCount = WebUiEntityColumnarPacket.GetSoaFullDeltaByteCount(rowCount);
		var destination = new byte[byteCount];

		bool encoded = WebUiEntityColumnarPacket.TryEncodeSoaFullDelta(
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			sequence: 8,
			tick: 43,
			generations,
			x,
			y,
			hp,
			state,
			destination,
			out int bytesWritten);

		Assert.That(encoded, Is.True);
		Assert.That(bytesWritten, Is.EqualTo(750_032));
		WebUiEntityColumnarSoaFullDelta decoded = WebUiEntityColumnarPacket.DecodeSoaFullDelta(destination);
		Assert.That(decoded.SchemaId, Is.EqualTo(WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId));
		Assert.That(decoded.Sequence, Is.EqualTo(8));
		Assert.That(decoded.Tick, Is.EqualTo(43));
		Assert.That(decoded.Generations[1024], Is.EqualTo(43));
		Assert.That(decoded.X[1024], Is.EqualTo(x[1024]));
		Assert.That(decoded.Y[49_999], Is.EqualTo(y[49_999]));
		Assert.That(decoded.Hp[4096], Is.EqualTo(hp[4096]));
		Assert.That(decoded.State[4096], Is.EqualTo(state[4096]));
	}

	[Test]
	public void SoaSnapshotPacket_RejectsMismatchedColumnLengths()
	{
		var stableIds = new[] { 1001, 1002 };
		var generations = new[] { 1 };
		var values = new float[2];
		var hp = new ushort[2];
		var bytes = new byte[WebUiEntityColumnarPacket.GetSoaSnapshotByteCount(stableIds.Length)];

		Assert.Throws<ArgumentException>(() =>
			WebUiEntityColumnarPacket.TryEncodeSoaSnapshot(
				WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
				sequence: 1,
				tick: 1,
				stableIds,
				generations,
				values,
				values,
				hp,
				new byte[2],
				new byte[2],
				bytes,
				out _));
	}

	[Test]
	public void GenerationContract_CanRejectStaleEntityReferences()
	{
		var generations = new Dictionary<int, int>
		{
			[1001] = 3
		};

		Assert.That(WebUiEntityColumnarPacket.IsCurrentGeneration(new WebUiEntityRef(1001, 3), generations), Is.True);
		Assert.That(WebUiEntityColumnarPacket.IsCurrentGeneration(new WebUiEntityRef(1001, 2), generations), Is.False);
		Assert.That(WebUiEntityColumnarPacket.IsCurrentGeneration(new WebUiEntityRef(9999, 1), generations), Is.False);
	}

	private static WebUiEntityColumnarRow[] CreateRows(int count)
	{
		var rows = new WebUiEntityColumnarRow[count];
		for (int i = 0; i < rows.Length; i++)
		{
			rows[i] = new WebUiEntityColumnarRow(
				StableId: 100_000 + i,
				Generation: 4,
				X: i % 512,
				Y: i / 512,
				Hp: (ushort)(25 + (i % 75)),
				Team: (byte)(i % 8),
				State: (byte)(i % 6));
		}

		return rows;
	}
}
