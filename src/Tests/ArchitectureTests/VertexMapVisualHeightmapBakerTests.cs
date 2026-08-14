using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;
using Ludots.Tool;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class VertexMapVisualHeightmapBakerTests
{
    [Test]
    public void Bake_PreservesEvenAndOddRowVertexHeightsInWorldSpace()
    {
        var map = new VertexMap();
        map.Initialize(1, 1);
        map.SetHeight(0, 0, 2);
        map.SetHeight(1, 0, 4);
        map.SetHeight(0, 1, 3);
        map.SetHeight(1, 1, 5);

        VisualHeightmapAsset asset = VertexMapVisualHeightmapBaker.Bake(
            map,
            heightStepCm: 200,
            hexEdgeLengthCm: 400);
        var runtime = new VisualHeightmapRuntime(asset);
        float hexWidthCm = MathF.Sqrt(3f) * 400f;

        Assert.Multiple(() =>
        {
            Assert.That(asset.SampleColumns, Is.EqualTo(VertexChunk.ChunkSize * 2));
            Assert.That(asset.SampleRows, Is.EqualTo(VertexChunk.ChunkSize));
            Assert.That(asset.InterpolationMode, Is.EqualTo(VisualHeightmapInterpolationMode.TriangleHeightfield));
            Assert.That(runtime.TrySampleHeightCm(0f, 0f, out float evenLeft), Is.True);
            Assert.That(evenLeft, Is.EqualTo(400f).Within(1f));
            Assert.That(runtime.TrySampleHeightCm(hexWidthCm, 0f, out float evenRight), Is.True);
            Assert.That(evenRight, Is.EqualTo(800f).Within(1f));
            Assert.That(runtime.TrySampleHeightCm(hexWidthCm * 0.5f, 600f, out float oddLeft), Is.True);
            Assert.That(oddLeft, Is.EqualTo(600f).Within(1f));
            Assert.That(runtime.TrySampleHeightCm(hexWidthCm * 1.5f, 600f, out float oddRight), Is.True);
            Assert.That(oddRight, Is.EqualTo(1000f).Within(1f));
        });
    }

    [Test]
    public void Bake_RoundTripsThroughFormalVisualHeightmapBinary()
    {
        var map = new VertexMap();
        map.Initialize(1, 1);
        map.SetHeight(7, 9, 4);
        VisualHeightmapAsset baked = VertexMapVisualHeightmapBaker.Bake(map, 200, 400);

        using var stream = new MemoryStream();
        VisualHeightmapBinary.Write(stream, baked);
        stream.Position = 0;
        VisualHeightmapAsset roundTripped = VisualHeightmapBinary.Read(stream);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Bounds, Is.EqualTo(baked.Bounds));
            Assert.That(roundTripped.SampleColumns, Is.EqualTo(baked.SampleColumns));
            Assert.That(roundTripped.SampleRows, Is.EqualTo(baked.SampleRows));
            Assert.That(roundTripped.HeightSamplesCm, Is.EqualTo(baked.HeightSamplesCm));
        });
    }
}
