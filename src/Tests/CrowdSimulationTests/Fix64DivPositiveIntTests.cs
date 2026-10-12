using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

public sealed class Fix64DivPositiveIntTests
{
    [Test]
    public void DivPositiveInt_MatchesFix64Division_OnNegativeEdges()
    {
        long[] raws =
        [
            long.MinValue,
            long.MinValue + 1,
            -(1L << 32) - 1,
            -(1L << 32),
            -1,
            -2,
        ];
        int[] divisors = [1, 2, 3, 6250, int.MaxValue];
        foreach (long raw in raws)
        {
            var value = Fix64.FromRaw(raw);
            foreach (int divisor in divisors)
            {
                long expected = (value / Fix64.FromInt(divisor)).RawValue;
                Assert.That(
                    Fix64.DivPositiveInt(value, divisor).RawValue,
                    Is.EqualTo(expected),
                    $"raw {raw} / {divisor}");
            }
        }
    }

    [Test]
    public void DivPositiveInt_RejectsNonPositiveDivisor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.OneValue, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.FromRaw(long.MinValue), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrowdSimulationSpace.NavCellCoord(Fix64.FromInt(-1), -5));
    }
}
