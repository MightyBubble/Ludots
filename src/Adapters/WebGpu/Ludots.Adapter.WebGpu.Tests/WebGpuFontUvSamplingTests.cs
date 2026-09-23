using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class WebGpuFontUvSamplingTests
{
    [Test]
    public void SampleMinMax_UsesMinXyMaxZw_NotOriginPlusSize()
    {
        // Formal atlas UvRect: min=(0.25,0.50) max=(0.75,1.00)
        var uvRect = new Vector4(0.25f, 0.50f, 0.75f, 1.00f);

        Assert.That(
            WebGpuFontUvSampling.SampleMinMax(uvRect, new Vector2(-1f, -1f)),
            Is.EqualTo(new Vector2(0.25f, 0.50f)));
        Assert.That(
            WebGpuFontUvSampling.SampleMinMax(uvRect, new Vector2(1f, 1f)),
            Is.EqualTo(new Vector2(0.75f, 1.00f)));
        Assert.That(
            WebGpuFontUvSampling.SampleMinMax(uvRect, Vector2.Zero),
            Is.EqualTo(new Vector2(0.50f, 0.75f)));

        Vector2 wrongAtMaxCorner = WebGpuFontUvSampling.SampleOriginSizeIncorrect(uvRect, new Vector2(1f, 1f));
        Assert.That(wrongAtMaxCorner, Is.EqualTo(new Vector2(1.00f, 1.50f)));
        Assert.That(wrongAtMaxCorner, Is.Not.EqualTo(new Vector2(0.75f, 1.00f)));
    }
}
