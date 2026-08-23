using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Presentation.Rendering;

[TestFixture]
public sealed class VisualHeightmapColorRampTests
{
    [Test]
    public void ResolveColorRanged_KeepsLowLandGreenWhenDeepSeaDominatesRange()
    {
        var land = VisualHeightmapColorRamp.ResolveColorRanged(
            heightCm: 120f,
            slope: 0f,
            minHeightCm: -6000f,
            maxHeightCm: 1800f,
            seaLevelCm: 0f,
            colorContrast: 1f);

        Assert.That(land.Y, Is.GreaterThan(land.X));
        Assert.That(land.Y, Is.GreaterThan(land.Z));
    }

    [Test]
    public void ResolveColorRanged_ShadesDeepWaterBlue()
    {
        var water = VisualHeightmapColorRamp.ResolveColorRanged(
            heightCm: -5200f,
            slope: 0f,
            minHeightCm: -6000f,
            maxHeightCm: 1800f,
            seaLevelCm: 0f,
            colorContrast: 1f);

        Assert.That(water.Z, Is.GreaterThan(water.X));
        Assert.That(water.Z, Is.GreaterThan(water.Y));
    }
}
