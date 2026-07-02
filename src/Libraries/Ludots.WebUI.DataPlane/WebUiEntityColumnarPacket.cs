using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Ludots.WebUI.DataPlane;

public readonly record struct WebUiEntityColumnarRow(
	int StableId,
	int Generation,
	float X,
	float Y,
	ushort Hp,
	byte Team,
	byte State);

public sealed record WebUiEntityColumnarSnapshot(
	int SchemaId,
	WebUiEntityColumnarRow[] Rows);

public sealed record WebUiEntityColumnarDelta(
	int SchemaId,
	int[] RemovedStableIds,
	WebUiEntityColumnarRow[] ChangedRows);

public sealed record WebUiEntityColumnarIndexedDelta(
	int SchemaId,
	int[] Indices,
	WebUiEntityColumnarRow[] Rows);

public sealed record WebUiEntityColumnarSoaFrame(
	int SchemaId,
	long Sequence,
	long Tick,
	int[] StableIds,
	int[] Generations,
	float[] X,
	float[] Y,
	ushort[] Hp,
	byte[] Team,
	byte[] State);

public sealed record WebUiEntityColumnarSoaFullDelta(
	int SchemaId,
	long Sequence,
	long Tick,
	int[] Generations,
	float[] X,
	float[] Y,
	ushort[] Hp,
	byte[] State);

public static class WebUiEntityColumnarPacket
{
	public const int CurrentSchemaId = 1;
	private const uint Magic = 0x5044574c; // LWDP little-endian
	private const ushort Version = 1;
	private const byte KindSnapshot = 1;
	private const byte KindDelta = 2;
	private const byte KindIndexedDelta = 3;
	private const int HeaderSize = 4 + 2 + 1 + 1 + 4 + 4 + 4;
	private const int RowSize = 4 + 4 + 4 + 4 + 2 + 1 + 1;
	private const int IndexedDeltaRowSize = 4 + 4 + 4 + 4 + 2 + 1;
	private const int SoaFullDeltaRowSize = 4 + 4 + 4 + 2 + 1;

	public static int GetSnapshotByteCount(int rowCount)
	{
		if (rowCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(rowCount), "Row count must not be negative.");
		}

		return checked(HeaderSize + (rowCount * RowSize));
	}

	public static byte[] EncodeSnapshot(int schemaId, ReadOnlySpan<WebUiEntityColumnarRow> rows)
	{
		byte[] bytes = new byte[GetSnapshotByteCount(rows.Length)];
		TryEncodeSnapshot(schemaId, rows, bytes, out _);
		return bytes;
	}

	public static bool TryEncodeSnapshot(
		int schemaId,
		ReadOnlySpan<WebUiEntityColumnarRow> rows,
		Span<byte> destination,
		out int bytesWritten)
	{
		int requiredByteCount = GetSnapshotByteCount(rows.Length);
		if (destination.Length < requiredByteCount)
		{
			bytesWritten = 0;
			return false;
		}

		WriteHeader(destination, KindSnapshot, schemaId, rows.Length, removedCount: 0);
		int offset = HeaderSize;
		for (int i = 0; i < rows.Length; i++)
		{
			WriteRow(destination.Slice(offset, RowSize), rows[i]);
			offset += RowSize;
		}

		bytesWritten = requiredByteCount;
		return true;
	}

	public static WebUiEntityColumnarSnapshot DecodeSnapshot(ReadOnlySpan<byte> bytes)
	{
		ReadHeader(bytes, out byte kind, out int schemaId, out int rowCount, out int removedCount);
		if (kind != KindSnapshot || removedCount != 0)
		{
			throw new InvalidOperationException("Packet is not a WebUI entity columnar snapshot.");
		}

		var rows = new WebUiEntityColumnarRow[rowCount];
		int offset = HeaderSize;
		for (int i = 0; i < rowCount; i++)
		{
			rows[i] = ReadRow(bytes.Slice(offset, RowSize));
			offset += RowSize;
		}

		return new WebUiEntityColumnarSnapshot(schemaId, rows);
	}

	public static byte[] EncodeDelta(
		int schemaId,
		ReadOnlySpan<int> removedStableIds,
		ReadOnlySpan<WebUiEntityColumnarRow> changedRows)
	{
		byte[] bytes = new byte[GetDeltaByteCount(removedStableIds.Length, changedRows.Length)];
		TryEncodeDelta(schemaId, removedStableIds, changedRows, bytes, out _);
		return bytes;
	}

	public static int GetDeltaByteCount(int removedCount, int changedRowCount)
	{
		if (removedCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(removedCount), "Removed count must not be negative.");
		}

		if (changedRowCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(changedRowCount), "Changed row count must not be negative.");
		}

		return checked(HeaderSize + (removedCount * 4) + (changedRowCount * RowSize));
	}

	public static bool TryEncodeDelta(
		int schemaId,
		ReadOnlySpan<int> removedStableIds,
		ReadOnlySpan<WebUiEntityColumnarRow> changedRows,
		Span<byte> destination,
		out int bytesWritten)
	{
		int requiredByteCount = GetDeltaByteCount(removedStableIds.Length, changedRows.Length);
		if (destination.Length < requiredByteCount)
		{
			bytesWritten = 0;
			return false;
		}

		WriteHeader(destination, KindDelta, schemaId, changedRows.Length, removedStableIds.Length);
		int offset = HeaderSize;
		for (int i = 0; i < removedStableIds.Length; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), removedStableIds[i]);
			offset += 4;
		}

		for (int i = 0; i < changedRows.Length; i++)
		{
			WriteRow(destination.Slice(offset, RowSize), changedRows[i]);
			offset += RowSize;
		}

		bytesWritten = requiredByteCount;
		return true;
	}

	public static WebUiEntityColumnarDelta DecodeDelta(ReadOnlySpan<byte> bytes)
	{
		ReadHeader(bytes, out byte kind, out int schemaId, out int rowCount, out int removedCount);
		if (kind != KindDelta)
		{
			throw new InvalidOperationException("Packet is not a WebUI entity columnar delta.");
		}

		var removed = new int[removedCount];
		var rows = new WebUiEntityColumnarRow[rowCount];
		int offset = HeaderSize;
		for (int i = 0; i < removed.Length; i++)
		{
			removed[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;
		}

		for (int i = 0; i < rows.Length; i++)
		{
			rows[i] = ReadRow(bytes.Slice(offset, RowSize));
			offset += RowSize;
		}

		return new WebUiEntityColumnarDelta(schemaId, removed, rows);
	}

	public static int GetIndexedDeltaByteCount(int changedRowCount)
	{
		if (changedRowCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(changedRowCount), "Changed row count must not be negative.");
		}

		return checked(HeaderSize + (changedRowCount * IndexedDeltaRowSize));
	}

	public static bool TryEncodeIndexedDelta(
		int schemaId,
		ReadOnlySpan<int> indices,
		ReadOnlySpan<WebUiEntityColumnarRow> changedRows,
		Span<byte> destination,
		out int bytesWritten)
	{
		if (indices.Length != changedRows.Length)
		{
			throw new ArgumentException("Indexed delta requires one index per changed row.", nameof(indices));
		}

		int requiredByteCount = GetIndexedDeltaByteCount(changedRows.Length);
		if (destination.Length < requiredByteCount)
		{
			bytesWritten = 0;
			return false;
		}

		WriteHeader(destination, KindIndexedDelta, schemaId, changedRows.Length, removedCount: 0);
		int offset = HeaderSize;
		for (int i = 0; i < changedRows.Length; i++)
		{
			if (indices[i] < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(indices), "Indexed delta row index must not be negative.");
			}

			WriteIndexedDeltaRow(destination.Slice(offset, IndexedDeltaRowSize), indices[i], changedRows[i]);
			offset += IndexedDeltaRowSize;
		}

		bytesWritten = requiredByteCount;
		return true;
	}

	public static WebUiEntityColumnarIndexedDelta DecodeIndexedDelta(ReadOnlySpan<byte> bytes)
	{
		ReadHeader(bytes, out byte kind, out int schemaId, out int rowCount, out int removedCount);
		if (kind != KindIndexedDelta || removedCount != 0)
		{
			throw new InvalidOperationException("Packet is not a WebUI entity columnar indexed delta.");
		}

		var indices = new int[rowCount];
		var rows = new WebUiEntityColumnarRow[rowCount];
		int offset = HeaderSize;
		for (int i = 0; i < rowCount; i++)
		{
			indices[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
			rows[i] = ReadIndexedDeltaRow(bytes.Slice(offset, IndexedDeltaRowSize));
			offset += IndexedDeltaRowSize;
		}

		return new WebUiEntityColumnarIndexedDelta(schemaId, indices, rows);
	}

	public static int GetSoaSnapshotByteCount(int rowCount)
	{
		if (rowCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(rowCount), "Row count must not be negative.");
		}

		return checked(WebUiColumnarPacketHeader.Size + (rowCount * RowSize));
	}

	public static bool TryEncodeSoaSnapshot(
		int schemaId,
		long sequence,
		long tick,
		ReadOnlySpan<int> stableIds,
		ReadOnlySpan<int> generations,
		ReadOnlySpan<float> x,
		ReadOnlySpan<float> y,
		ReadOnlySpan<ushort> hp,
		ReadOnlySpan<byte> team,
		ReadOnlySpan<byte> state,
		Span<byte> destination,
		out int bytesWritten)
	{
		int rowCount = stableIds.Length;
		ValidateSoaColumnLengths(rowCount, generations, x, y, hp, team, state);
		int requiredByteCount = GetSoaSnapshotByteCount(rowCount);
		if (destination.Length < requiredByteCount)
		{
			bytesWritten = 0;
			return false;
		}

		var header = new WebUiColumnarPacketHeader(
			schemaId,
			WebUiColumnarPacketKind.EntityCollection,
			WebUiColumnarPacketFrameKind.Snapshot,
			rowCount,
			sequence,
			tick);
		WebUiColumnarPacketHeader.Write(destination, in header);

		int offset = WebUiColumnarPacketHeader.Size;
		WriteInt32Column(destination.Slice(offset, rowCount * sizeof(int)), stableIds);
		offset += rowCount * sizeof(int);
		WriteInt32Column(destination.Slice(offset, rowCount * sizeof(int)), generations);
		offset += rowCount * sizeof(int);
		WriteSingleColumn(destination.Slice(offset, rowCount * sizeof(float)), x);
		offset += rowCount * sizeof(float);
		WriteSingleColumn(destination.Slice(offset, rowCount * sizeof(float)), y);
		offset += rowCount * sizeof(float);
		WriteUInt16Column(destination.Slice(offset, rowCount * sizeof(ushort)), hp);
		offset += rowCount * sizeof(ushort);
		team.CopyTo(destination.Slice(offset, rowCount));
		offset += rowCount;
		state.CopyTo(destination.Slice(offset, rowCount));

		bytesWritten = requiredByteCount;
		return true;
	}

	public static WebUiEntityColumnarSoaFrame DecodeSoaSnapshot(ReadOnlySpan<byte> bytes)
	{
		WebUiColumnarPacketHeader header = WebUiColumnarPacketHeader.Decode(bytes);
		if (header.PacketKind != WebUiColumnarPacketKind.EntityCollection ||
			header.FrameKind != WebUiColumnarPacketFrameKind.Snapshot)
		{
			throw new InvalidOperationException("Packet is not a WebUI entity SoA snapshot.");
		}

		int requiredByteCount = GetSoaSnapshotByteCount(header.RowCount);
		if (bytes.Length < requiredByteCount)
		{
			throw new InvalidOperationException("WebUI entity SoA snapshot is truncated.");
		}

		var stableIds = new int[header.RowCount];
		var generations = new int[header.RowCount];
		var x = new float[header.RowCount];
		var y = new float[header.RowCount];
		var hp = new ushort[header.RowCount];
		var team = new byte[header.RowCount];
		var state = new byte[header.RowCount];
		int offset = WebUiColumnarPacketHeader.Size;
		ReadInt32Column(bytes.Slice(offset, header.RowCount * sizeof(int)), stableIds);
		offset += header.RowCount * sizeof(int);
		ReadInt32Column(bytes.Slice(offset, header.RowCount * sizeof(int)), generations);
		offset += header.RowCount * sizeof(int);
		ReadSingleColumn(bytes.Slice(offset, header.RowCount * sizeof(float)), x);
		offset += header.RowCount * sizeof(float);
		ReadSingleColumn(bytes.Slice(offset, header.RowCount * sizeof(float)), y);
		offset += header.RowCount * sizeof(float);
		ReadUInt16Column(bytes.Slice(offset, header.RowCount * sizeof(ushort)), hp);
		offset += header.RowCount * sizeof(ushort);
		bytes.Slice(offset, header.RowCount).CopyTo(team);
		offset += header.RowCount;
		bytes.Slice(offset, header.RowCount).CopyTo(state);

		return new WebUiEntityColumnarSoaFrame(
			header.SchemaId,
			header.Sequence,
			header.Tick,
			stableIds,
			generations,
			x,
			y,
			hp,
			team,
			state);
	}

	public static int GetSoaFullDeltaByteCount(int rowCount)
	{
		if (rowCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(rowCount), "Row count must not be negative.");
		}

		return checked(WebUiColumnarPacketHeader.Size + (rowCount * SoaFullDeltaRowSize));
	}

	public static bool TryEncodeSoaFullDelta(
		int schemaId,
		long sequence,
		long tick,
		ReadOnlySpan<int> generations,
		ReadOnlySpan<float> x,
		ReadOnlySpan<float> y,
		ReadOnlySpan<ushort> hp,
		ReadOnlySpan<byte> state,
		Span<byte> destination,
		out int bytesWritten)
	{
		int rowCount = generations.Length;
		ValidateSoaDynamicColumnLengths(rowCount, x, y, hp, state);
		int requiredByteCount = GetSoaFullDeltaByteCount(rowCount);
		if (destination.Length < requiredByteCount)
		{
			bytesWritten = 0;
			return false;
		}

		var header = new WebUiColumnarPacketHeader(
			schemaId,
			WebUiColumnarPacketKind.EntityCollection,
			WebUiColumnarPacketFrameKind.Delta,
			rowCount,
			sequence,
			tick);
		WebUiColumnarPacketHeader.Write(destination, in header);

		int offset = WebUiColumnarPacketHeader.Size;
		WriteInt32Column(destination.Slice(offset, rowCount * sizeof(int)), generations);
		offset += rowCount * sizeof(int);
		WriteSingleColumn(destination.Slice(offset, rowCount * sizeof(float)), x);
		offset += rowCount * sizeof(float);
		WriteSingleColumn(destination.Slice(offset, rowCount * sizeof(float)), y);
		offset += rowCount * sizeof(float);
		WriteUInt16Column(destination.Slice(offset, rowCount * sizeof(ushort)), hp);
		offset += rowCount * sizeof(ushort);
		state.CopyTo(destination.Slice(offset, rowCount));

		bytesWritten = requiredByteCount;
		return true;
	}

	public static WebUiEntityColumnarSoaFullDelta DecodeSoaFullDelta(ReadOnlySpan<byte> bytes)
	{
		WebUiColumnarPacketHeader header = WebUiColumnarPacketHeader.Decode(bytes);
		if (header.PacketKind != WebUiColumnarPacketKind.EntityCollection ||
			header.FrameKind != WebUiColumnarPacketFrameKind.Delta)
		{
			throw new InvalidOperationException("Packet is not a WebUI entity SoA full delta.");
		}

		int requiredByteCount = GetSoaFullDeltaByteCount(header.RowCount);
		if (bytes.Length < requiredByteCount)
		{
			throw new InvalidOperationException("WebUI entity SoA full delta is truncated.");
		}

		var generations = new int[header.RowCount];
		var x = new float[header.RowCount];
		var y = new float[header.RowCount];
		var hp = new ushort[header.RowCount];
		var state = new byte[header.RowCount];
		int offset = WebUiColumnarPacketHeader.Size;
		ReadInt32Column(bytes.Slice(offset, header.RowCount * sizeof(int)), generations);
		offset += header.RowCount * sizeof(int);
		ReadSingleColumn(bytes.Slice(offset, header.RowCount * sizeof(float)), x);
		offset += header.RowCount * sizeof(float);
		ReadSingleColumn(bytes.Slice(offset, header.RowCount * sizeof(float)), y);
		offset += header.RowCount * sizeof(float);
		ReadUInt16Column(bytes.Slice(offset, header.RowCount * sizeof(ushort)), hp);
		offset += header.RowCount * sizeof(ushort);
		bytes.Slice(offset, header.RowCount).CopyTo(state);

		return new WebUiEntityColumnarSoaFullDelta(
			header.SchemaId,
			header.Sequence,
			header.Tick,
			generations,
			x,
			y,
			hp,
			state);
	}

	public static WebUiEntityColumnarDelta BuildDelta(
		int schemaId,
		ReadOnlySpan<WebUiEntityColumnarRow> previous,
		ReadOnlySpan<WebUiEntityColumnarRow> current)
	{
		var previousById = new Dictionary<int, WebUiEntityColumnarRow>(previous.Length);
		var currentIds = new HashSet<int>();
		for (int i = 0; i < previous.Length; i++)
		{
			previousById[previous[i].StableId] = previous[i];
		}

		var changed = new List<WebUiEntityColumnarRow>();
		for (int i = 0; i < current.Length; i++)
		{
			WebUiEntityColumnarRow row = current[i];
			currentIds.Add(row.StableId);
			if (!previousById.TryGetValue(row.StableId, out WebUiEntityColumnarRow old) || !old.Equals(row))
			{
				changed.Add(row);
			}
		}

		var removed = new List<int>();
		for (int i = 0; i < previous.Length; i++)
		{
			if (!currentIds.Contains(previous[i].StableId))
			{
				removed.Add(previous[i].StableId);
			}
		}

		return new WebUiEntityColumnarDelta(schemaId, removed.ToArray(), changed.ToArray());
	}

	public static bool IsCurrentGeneration(WebUiEntityRef entityRef, IReadOnlyDictionary<int, int> generations)
	{
		return generations.TryGetValue(entityRef.StableId, out int current) && current == entityRef.Generation;
	}

	private static void WriteHeader(Span<byte> bytes, byte kind, int schemaId, int rowCount, int removedCount)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(4), Version);
		bytes[6] = kind;
		bytes[7] = 0;
		BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(8), schemaId);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(12), rowCount);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(16), removedCount);
	}

	private static void ReadHeader(
		ReadOnlySpan<byte> bytes,
		out byte kind,
		out int schemaId,
		out int rowCount,
		out int removedCount)
	{
		if (bytes.Length < HeaderSize ||
			BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
			BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4)) != Version)
		{
			throw new InvalidOperationException("Invalid WebUI entity columnar packet.");
		}

		kind = bytes[6];
		schemaId = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8));
		rowCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12));
		removedCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16));
	}

	private static void WriteRow(Span<byte> span, in WebUiEntityColumnarRow row)
	{
		BinaryPrimitives.WriteInt32LittleEndian(span, row.StableId);
		BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), row.Generation);
		BinaryPrimitives.WriteSingleLittleEndian(span.Slice(8), row.X);
		BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12), row.Y);
		BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(16), row.Hp);
		span[18] = row.Team;
		span[19] = row.State;
	}

	private static void WriteIndexedDeltaRow(Span<byte> span, int index, in WebUiEntityColumnarRow row)
	{
		BinaryPrimitives.WriteInt32LittleEndian(span, index);
		BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), row.Generation);
		BinaryPrimitives.WriteSingleLittleEndian(span.Slice(8), row.X);
		BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12), row.Y);
		BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(16), row.Hp);
		span[18] = row.State;
	}

	private static WebUiEntityColumnarRow ReadRow(ReadOnlySpan<byte> span)
	{
		return new WebUiEntityColumnarRow(
			BinaryPrimitives.ReadInt32LittleEndian(span),
			BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4)),
			BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)),
			BinaryPrimitives.ReadSingleLittleEndian(span.Slice(12)),
			BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(16)),
			span[18],
			span[19]);
	}

	private static WebUiEntityColumnarRow ReadIndexedDeltaRow(ReadOnlySpan<byte> span)
	{
		return new WebUiEntityColumnarRow(
			StableId: 0,
			Generation: BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4)),
			X: BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)),
			Y: BinaryPrimitives.ReadSingleLittleEndian(span.Slice(12)),
			Hp: BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(16)),
			Team: 0,
			State: span[18]);
	}

	private static void ValidateSoaColumnLengths(
		int rowCount,
		ReadOnlySpan<int> generations,
		ReadOnlySpan<float> x,
		ReadOnlySpan<float> y,
		ReadOnlySpan<ushort> hp,
		ReadOnlySpan<byte> team,
		ReadOnlySpan<byte> state)
	{
		if (generations.Length != rowCount ||
			x.Length != rowCount ||
			y.Length != rowCount ||
			hp.Length != rowCount ||
			team.Length != rowCount ||
			state.Length != rowCount)
		{
			throw new ArgumentException("All WebUI entity SoA columns must have the same row count.");
		}
	}

	private static void ValidateSoaDynamicColumnLengths(
		int rowCount,
		ReadOnlySpan<float> x,
		ReadOnlySpan<float> y,
		ReadOnlySpan<ushort> hp,
		ReadOnlySpan<byte> state)
	{
		if (x.Length != rowCount ||
			y.Length != rowCount ||
			hp.Length != rowCount ||
			state.Length != rowCount)
		{
			throw new ArgumentException("All WebUI entity SoA dynamic columns must have the same row count.");
		}
	}

	private static void WriteInt32Column(Span<byte> destination, ReadOnlySpan<int> values)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.AsBytes(values).CopyTo(destination);
			return;
		}

		for (int i = 0; i < values.Length; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(i * sizeof(int), sizeof(int)), values[i]);
		}
	}

	private static void WriteSingleColumn(Span<byte> destination, ReadOnlySpan<float> values)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.AsBytes(values).CopyTo(destination);
			return;
		}

		for (int i = 0; i < values.Length; i++)
		{
			BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(i * sizeof(float), sizeof(float)), values[i]);
		}
	}

	private static void WriteUInt16Column(Span<byte> destination, ReadOnlySpan<ushort> values)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.AsBytes(values).CopyTo(destination);
			return;
		}

		for (int i = 0; i < values.Length; i++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * sizeof(ushort), sizeof(ushort)), values[i]);
		}
	}

	private static void ReadInt32Column(ReadOnlySpan<byte> source, Span<int> destination)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.Cast<byte, int>(source).CopyTo(destination);
			return;
		}

		for (int i = 0; i < destination.Length; i++)
		{
			destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(i * sizeof(int), sizeof(int)));
		}
	}

	private static void ReadSingleColumn(ReadOnlySpan<byte> source, Span<float> destination)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.Cast<byte, float>(source).CopyTo(destination);
			return;
		}

		for (int i = 0; i < destination.Length; i++)
		{
			destination[i] = BinaryPrimitives.ReadSingleLittleEndian(source.Slice(i * sizeof(float), sizeof(float)));
		}
	}

	private static void ReadUInt16Column(ReadOnlySpan<byte> source, Span<ushort> destination)
	{
		if (BitConverter.IsLittleEndian)
		{
			MemoryMarshal.Cast<byte, ushort>(source).CopyTo(destination);
			return;
		}

		for (int i = 0; i < destination.Length; i++)
		{
			destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(i * sizeof(ushort), sizeof(ushort)));
		}
	}
}
