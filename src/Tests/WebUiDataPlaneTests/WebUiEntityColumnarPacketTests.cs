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
}
