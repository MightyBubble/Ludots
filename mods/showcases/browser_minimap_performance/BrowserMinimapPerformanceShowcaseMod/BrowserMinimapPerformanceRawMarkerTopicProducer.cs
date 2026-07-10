using System.Buffers.Binary;
using Ludots.Core.Presentation.Minimap;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapPerformanceShowcaseMod;

internal sealed class BrowserMinimapPerformanceRawMarkerTopicProducer : IWebUiTopicProducer
{
	public const int RawSchemaId = 1002;
	private const uint RawMagic = 0x4d4d5257; // WRMM
	private const int HeaderBytes = 20;
	private const int BytesPerMarker = 36;

	private readonly MinimapMarkerBuffer _markers;

	public BrowserMinimapPerformanceRawMarkerTopicProducer(string topic, MinimapMarkerBuffer markers)
	{
		Topic = string.IsNullOrWhiteSpace(topic) ? throw new ArgumentException("Topic is required.", nameof(topic)) : topic.Trim();
		_markers = markers ?? throw new ArgumentNullException(nameof(markers));
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		int count = _markers.Count;
		byte[] payload = new byte[HeaderBytes + (count * BytesPerMarker)];
		BinaryPrimitives.WriteUInt32LittleEndian(payload, RawMagic);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), RawSchemaId);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), count);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), _markers.DroppedSinceClear);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), _markers.DroppedTotal);
		int offset = HeaderBytes;
		for (int i = 0; i < count; i++)
		{
			System.Numerics.Vector4 color = _markers.GetColor(i);
			BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), _markers.GetStableId(i));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 4), _markers.GetWorldXcm(i));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 8), _markers.GetWorldYcm(i));
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 12), color.X);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 16), color.Y);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 20), color.Z);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 24), color.W);
			BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 28), _markers.GetSizePx(i));
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 32), _markers.GetFlags(i));
			offset += BytesPerMarker;
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
