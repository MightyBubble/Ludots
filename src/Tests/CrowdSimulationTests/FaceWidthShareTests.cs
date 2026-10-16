using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>拖线线宽以厘米存储。</summary>
public sealed class FaceWidthShareTests
{
    [Test]
    public void FaceWidthCm_IsSharedInCentimeters()
    {
        var widthCm = Fix64.FromInt(4000);
        var order = new CrowdOrderBook().Create(
            1, CrowdOrderMode.Formation, "box", new Fix64Vec2(Fix64.OneValue, Fix64.Zero), widthCm);
        Assert.That(order.FaceWidthCm.RawValue, Is.EqualTo(widthCm.RawValue));

        var share = CrowdSimPlanner.FaceWidthShareCm(order.FaceWidthCm, memberCount: 2, total: 4);
        Assert.That(share.RawValue, Is.EqualTo(Fix64.FromInt(2000).RawValue));
        Assert.That(share.RawValue, Is.Not.EqualTo(Fix64.FromInt(200000).RawValue));
    }
}
