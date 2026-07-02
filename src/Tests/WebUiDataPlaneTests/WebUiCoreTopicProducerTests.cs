using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Registry;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiCoreTopicProducerTests
{
	[Test]
	public void EntityCollectionTopicProducer_ReadsWindowFromEntityCollectionStore()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		Entity first = world.Create();
		Entity second = world.Create();
		Entity third = world.Create();
		var store = new EntityCollectionStore(new StringIntRegistry(8, 1, 0, StringComparer.Ordinal));
		var descriptor = EntityCollectionDescriptor.Create(
			"webui.visible.units",
			EntityCollectionSourceKind.SpatialQuery,
			EntityCollectionRoleKind.Display,
			owner,
			first,
			"Visible Units",
			"3 rows");
		store.Replace(owner, descriptor, new[] { first, second, third });
		var producer = new EntityCollectionWebUiTopicProducer(
			"topic.collection.visible",
			store,
			owner,
			"webui.visible.units",
			startIndex: 1,
			windowSize: 1);
		var context = new WebUiTopicContext("session-a", producer.Topic, 44, JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);

		Assert.That(packet.Delivery, Is.EqualTo(WebUiDeliverySemantics.LatestWins));
		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
		Assert.That(root.GetProperty("startIndex").GetInt32(), Is.EqualTo(1));
		Assert.That(root.GetProperty("key").GetString(), Is.EqualTo("webui.visible.units"));
		Assert.That(root.GetProperty("sourceKind").GetString(), Is.EqualTo(EntityCollectionSourceKind.SpatialQuery.ToString()));
		Assert.That(root.GetProperty("role").GetString(), Is.EqualTo(EntityCollectionRoleKind.Display.ToString()));
		JsonElement row = root.GetProperty("rows")[0];
		Assert.That(row.GetProperty("entityId").GetInt32(), Is.EqualTo(second.Id));
		Assert.That(row.GetProperty("ordinal").GetInt32(), Is.EqualTo(1));
	}

	[Test]
	public void MinimapMarkerTopicProducer_EmitsSoABinarySnapshot_WithDropDiagnostics()
	{
		var markers = new MinimapMarkerBuffer(capacity: 2);
		markers.BeginFrame();
		Assert.That(markers.TryAdd(1001, 10, 20, new Vector4(1, 0, 0, 1), 4), Is.True);
		Assert.That(markers.TryAdd(1002, 30, 40, new Vector4(0, 1, 0, 1), 5), Is.True);
		Assert.That(markers.TryAdd(1003, 50, 60, new Vector4(0, 0, 1, 1), 6), Is.False);
		var producer = new MinimapMarkerWebUiTopicProducer("topic.minimap", markers, schemaId: 8);
		var context = new WebUiTopicContext("session-a", producer.Topic, 1, JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);

		Assert.That(packet.ContentType, Is.EqualTo(WebUiDataPlaneProtocol.BinaryContentType));
		ReadOnlySpan<byte> bytes = packet.Payload.Span;
		Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(0x4d4d4457));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)), Is.EqualTo(8));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)), Is.EqualTo(2));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12)), Is.EqualTo(1));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16)), Is.EqualTo(1));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20)), Is.EqualTo(1001));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(24)), Is.EqualTo(10f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(28)), Is.EqualTo(20f));
	}
}
