using System.Buffers.Binary;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Presentation.Minimap;

namespace Ludots.WebUI.DataPlane;

public sealed class EntityCollectionWebUiTopicProducer : IWebUiTopicProducer
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly EntityCollectionStore _store;
	private readonly Entity _owner;
	private readonly string _collectionKey;
	private readonly int _startIndex;
	private readonly int _windowSize;

	public EntityCollectionWebUiTopicProducer(
		string topic,
		EntityCollectionStore store,
		Entity owner,
		string collectionKey,
		int startIndex = 0,
		int windowSize = 256)
	{
		Topic = string.IsNullOrWhiteSpace(topic) ? throw new ArgumentException("Topic is required.", nameof(topic)) : topic.Trim();
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_owner = owner;
		_collectionKey = string.IsNullOrWhiteSpace(collectionKey) ? throw new ArgumentException("Collection key is required.", nameof(collectionKey)) : collectionKey.Trim();
		_startIndex = Math.Max(0, startIndex);
		_windowSize = Math.Max(1, windowSize);
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		packet = default!;
		if (!_store.TryGet(_owner, _collectionKey, out EntityCollectionHandle handle) ||
			!_store.TryGetView(handle, out EntityCollectionView view))
		{
			return false;
		}

		int capacity = Math.Min(_windowSize, Math.Max(0, view.Count - _startIndex));
		Entity[] entities = new Entity[capacity];
		int[] ordinals = new int[capacity];
		int[] roleIds = new int[capacity];
		EntityCollectionRowFlags[] flags = new EntityCollectionRowFlags[capacity];
		int written = _store.CopyWindow(handle, _startIndex, entities, ordinals, roleIds, flags);
		var rows = new EntityCollectionWebRow[written];
		for (int i = 0; i < written; i++)
		{
			rows[i] = new EntityCollectionWebRow(
				entities[i].Id,
				entities[i].WorldId,
				entities[i].Version,
				ordinals[i],
				roleIds[i],
				(byte)flags[i]);
		}

		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new EntityCollectionWebSnapshot(
			view.Revision,
			view.Count,
			_startIndex,
			view.Key,
			view.SourceKind.ToString(),
			view.Role.ToString(),
			rows), JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json+ludots-entity-collection",
			context.RequestId);
		return true;
	}

	public sealed record EntityCollectionWebSnapshot(
		uint Revision,
		int TotalCount,
		int StartIndex,
		string Key,
		string SourceKind,
		string Role,
		EntityCollectionWebRow[] Rows);

	public sealed record EntityCollectionWebRow(int EntityId, int WorldId, int Version, int Ordinal, int RoleId, byte Flags);
}

public sealed class MinimapMarkerWebUiTopicProducer : IWebUiTopicProducer
{
	private readonly MinimapMarkerBuffer _markers;
	private readonly int _schemaId;

	public MinimapMarkerWebUiTopicProducer(string topic, MinimapMarkerBuffer markers, int schemaId = 1)
	{
		Topic = string.IsNullOrWhiteSpace(topic) ? throw new ArgumentException("Topic is required.", nameof(topic)) : topic.Trim();
		_markers = markers ?? throw new ArgumentNullException(nameof(markers));
		_schemaId = schemaId;
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		int count = _markers.Count;
		byte[] payload = new byte[20 + (count * 24)];
		BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x4d4d4457); // WDMM
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), _schemaId);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), count);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), _markers.DroppedSinceClear);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), _markers.DroppedTotal);
		int offset = 20;
		for (int i = 0; i < count; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), _markers.GetStableId(i));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 4), _markers.GetWorldXcm(i));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 8), _markers.GetWorldYcm(i));
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 12), MinimapScreenMarkerBuffer.PackColorKey(_markers.GetColor(i)));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 16), _markers.GetSizePx(i));
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 20), _markers.GetFlags(i));
			offset += 24;
		}

		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			WebUiDataPlaneProtocol.BinaryContentType,
			context.RequestId);
		return true;
	}
}
