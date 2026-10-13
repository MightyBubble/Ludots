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
    public void DivPositiveInt_MatchesOperatorDiv_OnFixedSeed()
    {
        const int seed = 20261013;
        const int samples = 300_000;
        var rng = new Random(seed);
        for (int i = 0; i < samples; i++)
        {
            long raw = SampleRaw(rng, i);
            int divisor = SamplePositiveDivisor(rng, i);
            var value = Fix64.FromRaw(raw);
            long expected = (value / Fix64.FromInt(divisor)).RawValue;
            long actual = Fix64.DivPositiveInt(value, divisor).RawValue;
            if (actual != expected)
            {
                Assert.Fail($"seed {seed} sample {i}: raw {raw} / {divisor} = {actual}, operator / = {expected}");
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.Zero, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.FromRaw(long.MaxValue), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.FromRaw(long.MinValue), int.MinValue));
        var throwRng = new Random(seed);
        for (int i = 0; i < 4_096; i++)
        {
            int divisor = throwRng.Next(int.MinValue, 1);
            long raw = throwRng.NextInt64();
            Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.DivPositiveInt(Fix64.FromRaw(raw), divisor));
        }
    }

    private static long SampleRaw(Random rng, int index)
    {
        switch (index % 8)
        {
            case 0: return 0;
            case 1: return long.MaxValue;
            case 2: return long.MinValue;
            case 3: return 1;
            case 4: return -1;
            case 5: return rng.NextInt64(0, long.MaxValue);
            case 6: return -rng.NextInt64(1, long.MaxValue);
            default: return rng.NextInt64();
        }
    }

    private static int SamplePositiveDivisor(Random rng, int index)
    {
        switch (index % 5)
        {
            case 0: return 1;
            case 1: return int.MaxValue;
            case 2: return 6250;
            case 3: return rng.Next(1, 64);
            default: return rng.Next(1, int.MaxValue);
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
