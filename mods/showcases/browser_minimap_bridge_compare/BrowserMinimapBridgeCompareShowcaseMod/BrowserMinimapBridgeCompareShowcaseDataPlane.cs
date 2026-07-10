using System.Buffers.Binary;
using System.Numerics;
using Ludots.Core.Presentation.Minimap;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapBridgeCompareShowcaseMod;

internal static class BrowserMinimapBridgeCompareTopics
{
	public const string CompactMarkers = "webui.minimapMarkers";
	public const int DefaultMarkerCount = 30_000;
}

internal sealed class BrowserMinimapBridgeCompareMarkerWorld
{
	private static readonly Vector4[] Palette =
	{
		new(0.16f, 0.74f, 0.90f, 0.90f),
		new(0.34f, 0.84f, 0.48f, 0.90f),
		new(0.98f, 0.76f, 0.25f, 0.92f),
		new(0.89f, 0.45f, 0.78f, 0.88f),
		new(0.92f, 0.52f, 0.34f, 0.86f),
		new(0.70f, 0.63f, 0.96f, 0.88f),
	};

	private readonly object _sync = new();
	private readonly MinimapMarkerBuffer _markers;
	private readonly byte[] _compactPayload;
	private int _tick;
	private long _compactPayloadBytes;

	public BrowserMinimapBridgeCompareMarkerWorld(int markerCount)
	{
		if (markerCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(markerCount));
		}

		MarkerCount = markerCount;
		_markers = new MinimapMarkerBuffer(markerCount);
		_compactPayload = new byte[20 + (markerCount * 24)];
		Advance(0f);
	}

	public int MarkerCount { get; }

	public int Tick
	{
		get
		{
			lock (_sync)
			{
				return _tick;
			}
		}
	}

	public long CompactPayloadBytes
	{
		get
		{
			lock (_sync)
			{
				return _compactPayloadBytes;
			}
		}
	}

	public void Advance(float deltaSeconds)
	{
		lock (_sync)
		{
			_tick++;
			_markers.BeginFrame();
			float time = _tick * MathF.Max(0.0001f, deltaSeconds <= 0f ? 1f / 60f : deltaSeconds);
			for (int i = 0; i < MarkerCount; i++)
			{
				CreateMarker(i, MarkerCount, time, out int stableId, out float x, out float y, out Vector4 color, out float size, out uint flags);
				_markers.TryAdd(stableId, x, y, in color, size, flags);
			}
		}
	}

	public WebUiOutboundPacket CreateCompactPacket(string sessionId, long requestId)
	{
		lock (_sync)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(_compactPayload, 0x4d4d4457); // WDMM
			BinaryPrimitives.WriteInt32LittleEndian(_compactPayload.AsSpan(4), WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId);
			BinaryPrimitives.WriteInt32LittleEndian(_compactPayload.AsSpan(8), _markers.Count);
			BinaryPrimitives.WriteInt32LittleEndian(_compactPayload.AsSpan(12), _markers.DroppedSinceClear);
			BinaryPrimitives.WriteInt32LittleEndian(_compactPayload.AsSpan(16), _markers.DroppedTotal);
			int offset = 20;
			for (int i = 0; i < _markers.Count; i++)
			{
				BinaryPrimitives.WriteInt32LittleEndian(_compactPayload.AsSpan(offset), _markers.GetStableId(i));
				BinaryPrimitives.WriteSingleLittleEndian(_compactPayload.AsSpan(offset + 4), _markers.GetWorldXcm(i));
				BinaryPrimitives.WriteSingleLittleEndian(_compactPayload.AsSpan(offset + 8), _markers.GetWorldYcm(i));
				BinaryPrimitives.WriteUInt32LittleEndian(_compactPayload.AsSpan(offset + 12), MinimapScreenMarkerBuffer.PackColorKey(_markers.GetColor(i)));
				BinaryPrimitives.WriteSingleLittleEndian(_compactPayload.AsSpan(offset + 16), _markers.GetSizePx(i));
				BinaryPrimitives.WriteUInt32LittleEndian(_compactPayload.AsSpan(offset + 20), _markers.GetFlags(i));
				offset += 24;
			}

			_compactPayloadBytes = offset;
			return new WebUiOutboundPacket(
				sessionId,
				BrowserMinimapBridgeCompareTopics.CompactMarkers,
				WebUiPacketKind.Snapshot,
				WebUiDeliverySemantics.LatestWins,
				_compactPayload.AsMemory(0, offset),
				WebUiDataPlaneProtocol.BinaryContentType,
				requestId,
				_tick);
		}
	}

	private static void CreateMarker(
		int index,
		int markerCount,
		float time,
		out int stableId,
		out float x,
		out float y,
		out Vector4 color,
		out float size,
		out uint flags)
	{
		stableId = 10_000 + index;
		float normalizedX = Hash01((uint)index * 0x9e3779b1u);
		float normalizedY = Hash01(((uint)index * 0x85ebca6bu) ^ 0x27d4eb2fu);
		float angle = (Hash01(((uint)index * 0xc2b2ae35u) ^ 0x165667b1u) * MathF.Tau) + (time * 0.18f);
		float orbit = 22f + (Hash01(((uint)index * 0x27d4eb2du) ^ 0x9e3779b9u) * 44f);
		x = ((normalizedX - 0.5f) * 28_000f) + (MathF.Cos(angle) * orbit);
		y = ((normalizedY - 0.5f) * 28_000f) + (MathF.Sin(angle) * orbit);
		color = Palette[index % Palette.Length];
		size = 1.0f + (Hash01(((uint)index * 0x165667b1u) ^ 0xc2b2ae35u) * 0.55f);
		flags = 0u;
	}

	private static float Hash01(uint value)
	{
		value ^= value >> 16;
		value *= 0x7feb352du;
		value ^= value >> 15;
		value *= 0x846ca68bu;
		value ^= value >> 16;
		return (value & 0x00ffffffu) / 16_777_215f;
	}
}

internal sealed class BrowserMinimapBridgeCompactMarkerTopicProducer : IWebUiTopicProducer
{
	private readonly BrowserMinimapBridgeCompareMarkerWorld _world;

	public BrowserMinimapBridgeCompactMarkerTopicProducer(BrowserMinimapBridgeCompareMarkerWorld world)
	{
		_world = world ?? throw new ArgumentNullException(nameof(world));
	}

	public string Topic => BrowserMinimapBridgeCompareTopics.CompactMarkers;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		packet = _world.CreateCompactPacket(context.SessionId, context.RequestId);
		return true;
	}
}
