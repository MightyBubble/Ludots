using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

public sealed class CrowdSpatialHashValidatorTests
{
    [Test]
    public void Empty_MatchesDenseReference()
    {
        var hash = Hash(4);
        var positions = Array.Empty<Fix64Vec2>();
        var radii = Array.Empty<Fix64>();
        hash.Build(positions, radii, 0);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, 0));
        Assert.That(hash.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void FullCapacity_MatchesDenseReference()
    {
        const int count = 8;
        var hash = Hash(count);
        var positions = new Fix64Vec2[count];
        var radii = new Fix64[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = new Fix64Vec2(Fix64.FromInt(30 + i * 110), Fix64.FromInt(20 + (i % 3) * 140));
            radii[i] = Fix64.FromInt(15 + i * 5);
        }

        hash.Build(positions, radii, count);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, count));
        hash.Items[0] = hash.Items[0] == 0 ? 1 : 0;
        var ex = Assert.Throws<CrowdSimulationInvariantException>(() =>
            CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, count));
        Assert.That(ex!.Message, Does.Contain("格内序"));
    }

    [Test]
    public void StampWrap_MatchesDenseReference()
    {
        var hash = Hash(4);
        var positions = new[]
        {
            new Fix64Vec2(Fix64.FromInt(10), Fix64.FromInt(10)),
            new Fix64Vec2(Fix64.FromInt(400), Fix64.FromInt(420)),
        };
        var radii = new[] { Fix64.FromInt(25), Fix64.FromInt(40) };
        hash.Build(positions, radii, 2);
        hash.PrimeStampGeneration(uint.MaxValue);
        positions[0] = new Fix64Vec2(Fix64.FromInt(880), Fix64.FromInt(30));
        hash.Build(positions, radii, 2);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, 2));
        hash.Build(positions, radii, 2);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, 2));
    }

    [Test]
    public void Boundary_ClampsOntoTheGrid()
    {
        var hash = new CrowdSpatialHash(1000, Fix64.FromInt(100), 6, 2, Fix64.FromInt(40));
        Assert.That(hash.CellsPerSide, Is.EqualTo(10));
        var positions = new[]
        {
            new Fix64Vec2(Fix64.FromInt(-1), Fix64.FromInt(-5)),
            new Fix64Vec2(Fix64.Zero, Fix64.Zero),
            new Fix64Vec2(Fix64.FromInt(99), Fix64.FromInt(100)),
            new Fix64Vec2(Fix64.FromInt(999), Fix64.FromInt(1000)),
            new Fix64Vec2(Fix64.FromInt(5000), Fix64.FromInt(1)),
        };
        var radii = new Fix64[positions.Length];
        for (int i = 0; i < radii.Length; i++) radii[i] = Fix64.FromInt(10);
        hash.Build(positions, radii, positions.Length);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, positions.Length));
        Assert.That(hash.CellOf[0], Is.EqualTo(0));
        Assert.That(hash.CellOf[4] % hash.CellsPerSide, Is.EqualTo(hash.CellsPerSide - 1));
    }

    [Test]
    public void FractionalCellSize_MatchesDenseReference()
    {
        Fix64 cell = Fix64.FromInt(100) + Fix64.OneValue / Fix64.FromInt(2);
        var hash = new CrowdSpatialHash(1000, cell, 4, 2, Fix64.FromInt(40));
        var positions = new[]
        {
            new Fix64Vec2(Fix64.FromInt(-3), Fix64.FromInt(10)),
            new Fix64Vec2(cell, cell * 2),
            new Fix64Vec2(Fix64.FromInt(999), Fix64.FromInt(1000)),
        };
        var radii = new[] { Fix64.FromInt(10), Fix64.FromInt(80), Fix64.FromInt(20) };
        hash.Build(positions, radii, positions.Length);
        Assert.DoesNotThrow(() => CrowdSimulationValidator.ValidateSpatialHash(hash, positions, radii, positions.Length));
    }

    private static CrowdSpatialHash Hash(int capacity) =>
        new(1000, Fix64.FromInt(100), capacity, 2, Fix64.FromInt(40));
}
