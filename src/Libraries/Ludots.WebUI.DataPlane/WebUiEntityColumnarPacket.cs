using System.Buffers.Binary;

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

public static class WebUiEntityColumnarPacket
{
	public const int CurrentSchemaId = 1;
	private const uint Magic = 0x5044574c; // LWDP little-endian
	private const ushort Version = 1;
	private const byte KindSnapshot = 1;
	private const byte KindDelta = 2;
	private const int HeaderSize = 4 + 2 + 1 + 1 + 4 + 4 + 4;
	private const int RowSize = 4 + 4 + 4 + 4 + 2 + 1 + 1;

	public static byte[] EncodeSnapshot(int schemaId, ReadOnlySpan<WebUiEntityColumnarRow> rows)
	{
		byte[] bytes = new byte[HeaderSize + (rows.Length * RowSize)];
		WriteHeader(bytes, KindSnapshot, schemaId, rows.Length, removedCount: 0);
		int offset = HeaderSize;
		for (int i = 0; i < rows.Length; i++)
		{
			WriteRow(bytes.AsSpan(offset, RowSize), rows[i]);
			offset += RowSize;
		}

		return bytes;
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
		byte[] bytes = new byte[HeaderSize + (removedStableIds.Length * 4) + (changedRows.Length * RowSize)];
		WriteHeader(bytes, KindDelta, schemaId, changedRows.Length, removedStableIds.Length);
		int offset = HeaderSize;
		for (int i = 0; i < removedStableIds.Length; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), removedStableIds[i]);
			offset += 4;
		}

		for (int i = 0; i < changedRows.Length; i++)
		{
			WriteRow(bytes.AsSpan(offset, RowSize), changedRows[i]);
			offset += RowSize;
		}

		return bytes;
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
}
