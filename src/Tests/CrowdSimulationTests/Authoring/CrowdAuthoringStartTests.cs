using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using Ludots.Tool;
using NUnit.Framework;

namespace CrowdSimulationTests.Authoring;

[Category("ci-gate")]
[Category("crowd-authoring")]
[TestFixture]
public sealed class CrowdAuthoringStartTests
{
    private static readonly string[] LandWater = { "land", "water" };

    [Test]
    public void PaintTerrainAreas_FillsThenPaintsInclusiveRectsInOrder()
    {
        var rects = new[]
        {
            CrowdNavSurfaceAuthoring.CrowdTerrainRect.Parse("water:1,1,2,2"),
            CrowdNavSurfaceAuthoring.CrowdTerrainRect.Parse("land:2,2,3,3"),
        };

        byte[] cells = CrowdNavSurfaceAuthoring.PaintTerrainAreas(4, LandWater, "land", rects);

        byte[] expected =
        {
            0, 0, 0, 0,
            0, 1, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 0,
        };
        Assert.That(cells, Is.EqualTo(expected));
    }

    [TestCase("water:0,0,4,0", "超出")]
    [TestCase("water:2,0,1,0", "颠倒")]
    [TestCase("lava:0,0,0,0", "lava")]
    public void PaintTerrainAreas_RejectsBadRects(string rect, string fragment)
    {
        var parsed = new[] { CrowdNavSurfaceAuthoring.CrowdTerrainRect.Parse(rect) };
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdNavSurfaceAuthoring.PaintTerrainAreas(4, LandWater, "land", parsed));
        Assert.That(ex!.Message, Does.Contain(fragment));
    }

    [TestCase("water")]
    [TestCase("water:1,2,3")]
    [TestCase("water:1,2,x,4")]
    public void TerrainRect_RejectsMalformedText(string text)
    {
        Assert.Throws<InvalidOperationException>(() => CrowdNavSurfaceAuthoring.CrowdTerrainRect.Parse(text));
    }

    [Test]
    public void FlatHeightmap_IsUInt16Scaled_AndDecodesToRequestedHeight()
    {
        ContinuousHeightmapAsset height = CrowdNavSurfaceAuthoring.CreateFlatHeightmap(1_600_000, 256, 100);

        using var stream = new MemoryStream();
        ContinuousHeightmapBinary.Write(stream, height);
        stream.Position = 0;
        ContinuousHeightmapAsset read = ContinuousHeightmapBinary.Read(stream);
        NavHeightField field = NavHeightField.FromHeightmap(read, 256, 6250);

        Assert.Multiple(() =>
        {
            Assert.That(read.UsesRawUInt16Samples, Is.True);
            Assert.That(read.Bounds.Width, Is.EqualTo(1_600_000));
            Assert.That(read.Bounds.Height, Is.EqualTo(1_600_000));
            Assert.That(read.SampleColumns, Is.EqualTo(256));
            Assert.That(read.SampleRows, Is.EqualTo(256));
            Assert.That(field.HeightsCm[0].ToInt(), Is.EqualTo(100));
            Assert.That(field.HeightsCm[^1].ToInt(), Is.EqualTo(100));
        });
    }
}
