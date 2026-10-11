using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

public sealed class CrowdSimulationSpaceTests
{
    [Test]
    public void NavCellCoord_FloorsTowardNegativeInfinity()
    {
        const int navCellSizeCm = 6250;
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(0), navCellSizeCm), Is.EqualTo(0));
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(6249), navCellSizeCm), Is.EqualTo(0));
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(navCellSizeCm), navCellSizeCm), Is.EqualTo(1));
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(-1), navCellSizeCm), Is.EqualTo(-1));
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(-navCellSizeCm), navCellSizeCm), Is.EqualTo(-1));
        Assert.That(CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(-navCellSizeCm - 1), navCellSizeCm), Is.EqualTo(-2));
    }
}
