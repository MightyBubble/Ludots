using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiColumnarPacketSchemaTests
{
	[Test]
	public void Registry_RegistersEntityCollectionAndMinimapMarkerSchemas()
	{
		WebUiColumnarPacketSchemaRegistry registry = WebUiColumnarPacketSchemaRegistry.CreateDefault();

		Assert.That(registry.TryGetByTopic("webui.entityCollection", out WebUiColumnarPacketSchema entitySchema), Is.True);
		Assert.That(entitySchema.SchemaId, Is.EqualTo(1));
		Assert.That(entitySchema.Topic, Is.EqualTo("webui.entityCollection"));
		Assert.That(entitySchema.PacketKind, Is.EqualTo(WebUiColumnarPacketKind.EntityCollection));

		Assert.That(registry.TryGetByTopic("webui.minimapMarkers", out WebUiColumnarPacketSchema minimapSchema), Is.True);
		Assert.That(minimapSchema.SchemaId, Is.EqualTo(2));
		Assert.That(minimapSchema.PacketKind, Is.EqualTo(WebUiColumnarPacketKind.MinimapMarkers));
	}

	[Test]
	public void Header_RoundTripsMagicVersionTopicSchemaSequenceTickAndRowCount()
	{
		var header = new WebUiColumnarPacketHeader(
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			WebUiColumnarPacketKind.EntityCollection,
			WebUiColumnarPacketFrameKind.Snapshot,
			RowCount: 128,
			Sequence: 55,
			Tick: 901);

		byte[] bytes = WebUiColumnarPacketHeader.Encode(header);
		WebUiColumnarPacketHeader decoded = WebUiColumnarPacketHeader.Decode(bytes);

		Assert.That(decoded, Is.EqualTo(header));
	}

	[Test]
	public void Header_RejectsBadMagicUnsupportedVersionAndNegativeRowCount()
	{
		var header = new WebUiColumnarPacketHeader(
			WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId,
			WebUiColumnarPacketKind.MinimapMarkers,
			WebUiColumnarPacketFrameKind.Delta,
			RowCount: 4,
			Sequence: 7,
			Tick: 11);
		byte[] bytes = WebUiColumnarPacketHeader.Encode(header);

		bytes[0] = 0;
		Assert.Throws<InvalidOperationException>(() => WebUiColumnarPacketHeader.Decode(bytes));

		bytes = WebUiColumnarPacketHeader.Encode(header);
		bytes[4] = 99;
		Assert.Throws<InvalidOperationException>(() => WebUiColumnarPacketHeader.Decode(bytes));

		bytes = WebUiColumnarPacketHeader.Encode(header with { RowCount = -1 });
		Assert.Throws<InvalidOperationException>(() => WebUiColumnarPacketHeader.Decode(bytes));
	}
}
