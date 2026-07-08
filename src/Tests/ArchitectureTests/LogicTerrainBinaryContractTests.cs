using System;
using System.IO;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LogicTerrainBinaryContractTests
{
    [Test]
    public void WriteRead_RoundTripsGridCellsAndScale()
    {
        var terrain = new MutableGridLogicTerrainField(widthCells: 3, heightCells: 2, cellSizeCm: 75, chunkSizeCells: 2);
        terrain.SetCell(0, 0, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.None, areaId: 0, cost: 1f));
        terrain.SetCell(1, 0, new LogicTerrainCell(2, 1, LogicTerrainSurfaceFlags.Water, areaId: 4, cost: 1.5f));
        terrain.SetCell(2, 1, new LogicTerrainCell(7, 0, LogicTerrainSurfaceFlags.Blocked | LogicTerrainSurfaceFlags.Ramp, areaId: 9, cost: 3.25f));

        using var stream = new MemoryStream();
        LogicTerrainBinary.Write(stream, terrain);
        stream.Position = 0;

        LogicTerrainField read = LogicTerrainBinary.Read(stream);

        Assert.That(read.Topology, Is.EqualTo(LogicTerrainTopology.Grid));
        Assert.That(read.WidthCells, Is.EqualTo(3));
        Assert.That(read.HeightCells, Is.EqualTo(2));
        Assert.That(read.HorizontalStepCm, Is.EqualTo(75));
        Assert.That(read.ChunkSizeCells, Is.EqualTo(2));
        Assert.That(read.GetCell(0, 0).HeightLevel, Is.EqualTo(1));
        Assert.That(read.GetCell(1, 0).WaterHeightLevel, Is.EqualTo(1));
        Assert.That(read.GetCell(1, 0).SurfaceFlags, Is.EqualTo(LogicTerrainSurfaceFlags.Water));
        Assert.That(read.GetCell(1, 0).AreaId, Is.EqualTo(4));
        Assert.That(read.GetCell(1, 0).Cost, Is.EqualTo(1.5f));
        Assert.That(read.GetCell(2, 1).SurfaceFlags, Is.EqualTo(LogicTerrainSurfaceFlags.Blocked | LogicTerrainSurfaceFlags.Ramp));
        Assert.That(read.GetCell(2, 1).AreaId, Is.EqualTo(9));
        Assert.That(read.GetCell(2, 1).Cost, Is.EqualTo(3.25f));
    }

    [Test]
    public void Read_RejectsUnknownMagic()
    {
        using var stream = new MemoryStream(new byte[] { (byte)'B', (byte)'A', (byte)'D', (byte)'!', 1, 0, 0, 0 });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => LogicTerrainBinary.Read(stream))!;

        Assert.That(ex.Message, Does.Contain("magic"));
    }

    [Test]
    public void Write_RejectsNonGridTopology()
    {
        using var stream = new MemoryStream();
        var terrain = new NonGridTerrainField();

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => LogicTerrainBinary.Write(stream, terrain))!;

        Assert.That(ex.Message, Does.Contain("Grid"));
    }

    private sealed class NonGridTerrainField : LogicTerrainField
    {
        public NonGridTerrainField()
            : base(widthCells: 1, heightCells: 1, chunkSizeCells: 1)
        {
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Hex;

        public override int HorizontalStepCm => 100;

        public override int VerticalStepCm => 100;

        public override LogicTerrainCell GetCell(int col, int row)
            => new(0, 0, LogicTerrainSurfaceFlags.None);

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = 0f;
            zMeters = 0f;
        }
    }
}
