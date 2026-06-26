using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiSharedBufferRingTests
{
	[Test]
	public void WriteLatestWins_ProducesDescriptorWithSequenceOffsetAndDiagnostics()
	{
		var storage = new byte[512];
		var ring = new WebUiSharedBufferRing(
			"buffer.entity.0",
			"webui.entityCollection",
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			storage);

		WebUiSharedBufferWriteResult first = ring.WriteLatestWins(new byte[] { 1, 2, 3, 4 }, tick: 101);
		WebUiSharedBufferWriteResult second = ring.WriteLatestWins(new byte[] { 5, 6 }, tick: 102);

		Assert.That(first.Accepted, Is.True);
		Assert.That(first.Descriptor.Sequence, Is.EqualTo(1));
		Assert.That(second.Accepted, Is.True);
		Assert.That(second.Descriptor.BufferId, Is.EqualTo("buffer.entity.0"));
		Assert.That(second.Descriptor.Topic, Is.EqualTo("webui.entityCollection"));
		Assert.That(second.Descriptor.SchemaId, Is.EqualTo(WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId));
		Assert.That(second.Descriptor.Layout, Is.EqualTo(WebUiSharedBufferDescriptor.RingBufferLayout));
		Assert.That(second.Descriptor.Sequence, Is.EqualTo(2));
		Assert.That(second.Descriptor.Tick, Is.EqualTo(102));
		Assert.That(second.Descriptor.ByteLength, Is.EqualTo(2));
		Assert.That(second.Descriptor.CoalescedPackets, Is.EqualTo(1));
		Assert.That(storage[second.Descriptor.ByteOffset], Is.EqualTo(5));
		Assert.That(storage[second.Descriptor.ByteOffset + 1], Is.EqualTo(6));
	}

	[Test]
	public void WriteLatestWins_WhenPayloadExceedsRingCapacity_DropsAndReportsNoFallback()
	{
		var ring = new WebUiSharedBufferRing(
			"buffer.minimap.0",
			"webui.minimapMarkers",
			WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId,
			new byte[128]);

		WebUiSharedBufferWriteResult result = ring.WriteLatestWins(new byte[96], tick: 7);

		Assert.That(result.Accepted, Is.False);
		Assert.That(result.Error, Does.Contain("exceeds shared buffer capacity"));
		Assert.That(result.Descriptor.DroppedPackets, Is.EqualTo(1));
		Assert.That(result.Descriptor.ByteLength, Is.EqualTo(0));
	}

	[Test]
	public void TransportCapabilities_CanAdvertiseSharedBufferDescriptors()
	{
		var descriptor = new WebUiSharedBufferDescriptor(
			"buffer.entity.0",
			"webui.entityCollection",
			WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
			WebUiSharedBufferDescriptor.RingBufferLayout,
			CapacityBytes: 1024 * 1024,
			HeaderBytes: WebUiSharedBufferRing.DefaultHeaderBytes,
			ByteOffset: 0,
			ByteLength: 0,
			Sequence: 0,
			Tick: 0,
			DroppedPackets: 0,
			CoalescedPackets: 0);

		WebUiTransportCapabilities capabilities = WebUiTransportCapabilities.SharedMemory(
			sharedBuffers: new[] { descriptor });

		Assert.That(capabilities.ModeName, Is.EqualTo("shared-memory"));
		Assert.That(capabilities.SupportsSharedMemory, Is.True);
		Assert.That(capabilities.SupportsBase64Chunks, Is.False);
		Assert.That(capabilities.ExpectedManagedCopiesPerPayload, Is.EqualTo(0));
		Assert.That(capabilities.SharedBuffers.Single(), Is.EqualTo(descriptor));
		Assert.That(capabilities.Satisfies("shared-buffer-descriptor"), Is.True);
	}
}
