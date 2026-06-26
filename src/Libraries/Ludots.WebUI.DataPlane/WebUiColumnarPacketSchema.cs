using System.Buffers.Binary;

namespace Ludots.WebUI.DataPlane;

public enum WebUiColumnarPacketKind : byte
{
	EntityCollection = 1,
	MinimapMarkers = 2,
}

public enum WebUiColumnarPacketFrameKind : byte
{
	Snapshot = 1,
	Delta = 2,
}

public readonly record struct WebUiColumnarPacketSchema(
	int SchemaId,
	string Topic,
	WebUiColumnarPacketKind PacketKind,
	string Description);

public sealed class WebUiColumnarPacketSchemaRegistry
{
	public const int EntityCollectionSchemaId = 1;
	public const int MinimapMarkersSchemaId = 2;

	private readonly Dictionary<int, WebUiColumnarPacketSchema> _byId = new();
	private readonly Dictionary<string, WebUiColumnarPacketSchema> _byTopic = new(StringComparer.Ordinal);

	public static WebUiColumnarPacketSchemaRegistry CreateDefault()
	{
		var registry = new WebUiColumnarPacketSchemaRegistry();
		registry.Register(new WebUiColumnarPacketSchema(
			EntityCollectionSchemaId,
			"webui.entityCollection",
			WebUiColumnarPacketKind.EntityCollection,
			"Versioned columnar rows for entity collection windows and deltas."));
		registry.Register(new WebUiColumnarPacketSchema(
			MinimapMarkersSchemaId,
			"webui.minimapMarkers",
			WebUiColumnarPacketKind.MinimapMarkers,
			"Versioned columnar rows for minimap marker snapshots and deltas."));
		return registry;
	}

	public void Register(WebUiColumnarPacketSchema schema)
	{
		if (schema.SchemaId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(schema), "Schema id must be positive.");
		}

		if (string.IsNullOrWhiteSpace(schema.Topic))
		{
			throw new ArgumentException("Schema topic is required.", nameof(schema));
		}

		_byId[schema.SchemaId] = schema;
		_byTopic[schema.Topic] = schema;
	}

	public bool TryGetById(int schemaId, out WebUiColumnarPacketSchema schema)
	{
		return _byId.TryGetValue(schemaId, out schema);
	}

	public bool TryGetByTopic(string topic, out WebUiColumnarPacketSchema schema)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			schema = default;
			return false;
		}

		return _byTopic.TryGetValue(topic.Trim(), out schema);
	}
}

public readonly record struct WebUiColumnarPacketHeader(
	int SchemaId,
	WebUiColumnarPacketKind PacketKind,
	WebUiColumnarPacketFrameKind FrameKind,
	int RowCount,
	long Sequence,
	long Tick)
{
	public const int CurrentVersion = 1;
	public const int Size = 4 + 2 + 1 + 1 + 4 + 4 + 8 + 8;
	private const uint Magic = 0x5044574c; // LWDP

	public static byte[] Encode(in WebUiColumnarPacketHeader header)
	{
		byte[] bytes = new byte[Size];
		Write(bytes, in header);
		return bytes;
	}

	public static void Write(Span<byte> bytes, in WebUiColumnarPacketHeader header)
	{
		if (bytes.Length < Size)
		{
			throw new ArgumentException($"Columnar packet header requires at least {Size} bytes.", nameof(bytes));
		}

		BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(4), CurrentVersion);
		bytes[6] = (byte)header.PacketKind;
		bytes[7] = (byte)header.FrameKind;
		BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(8), header.SchemaId);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(12), header.RowCount);
		BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(16), header.Sequence);
		BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(24), header.Tick);
	}

	public static WebUiColumnarPacketHeader Decode(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < Size)
		{
			throw new InvalidOperationException("WebUI columnar packet header is truncated.");
		}

		uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
		if (magic != Magic)
		{
			throw new InvalidOperationException("Invalid WebUI columnar packet magic.");
		}

		ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4));
		if (version != CurrentVersion)
		{
			throw new InvalidOperationException($"Unsupported WebUI columnar packet version {version}.");
		}

		var header = new WebUiColumnarPacketHeader(
			BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)),
			(WebUiColumnarPacketKind)bytes[6],
			(WebUiColumnarPacketFrameKind)bytes[7],
			BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12)),
			BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(16)),
			BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(24)));

		if (header.SchemaId <= 0)
		{
			throw new InvalidOperationException("WebUI columnar packet schema id must be positive.");
		}

		if (header.RowCount < 0)
		{
			throw new InvalidOperationException("WebUI columnar packet row count must not be negative.");
		}

		if (!Enum.IsDefined(header.PacketKind))
		{
			throw new InvalidOperationException($"Unsupported WebUI columnar packet kind {header.PacketKind}.");
		}

		if (!Enum.IsDefined(header.FrameKind))
		{
			throw new InvalidOperationException($"Unsupported WebUI columnar packet frame kind {header.FrameKind}.");
		}

		return header;
	}
}
