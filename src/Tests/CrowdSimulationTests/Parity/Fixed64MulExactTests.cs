using System;
using System.Numerics;
using FixPointCS;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// MulExact 先取绝对值分段相乘再回填符号,负数向零截断。Mul / operator* 用算术右移 32 位拆整数部,
/// 负数向下取整;负结果且低 32 位余量非 0 时比向零截断更远离零 1 个最小单位。积非负时两者相同
/// (af·bf 小于 2^64,逻辑右移不回绕)。期望值是 BigInteger 对 (a*b)/2^32 的向零截断。
/// </summary>
public sealed class Fixed64MulExactTests
{
    [Test]
    public void MulExact_NegativeRemainder_TruncatesTowardZero_AndDiffersFromMulByOneUlp()
    {
        // raw -1 × raw 1:精确积 -2^-64。向零截断 0;向下取整 -1。
        AssertSignedUlp(-1, 1, expectedExact: 0, expectedMul: -1);
        AssertSignedUlp(1, -1, expectedExact: 0, expectedMul: -1);

        // raw -(2^32+1) × raw (2^32+1):
        // |a|·|b| = (2^32+1)^2 = 2^64 + 2^33 + 1,/ 2^32 = 2^32 + 2 余 1。
        // 向零截断 -(2^32+2);向下取整 -(2^32+3)。
        const long a = -(1L << 32) - 1;
        const long b = (1L << 32) + 1;
        AssertSignedUlp(a, b, expectedExact: -(1L << 32) - 2, expectedMul: -(1L << 32) - 3);
        AssertSignedUlp(b, a, expectedExact: -(1L << 32) - 2, expectedMul: -(1L << 32) - 3);
    }

    [Test]
    public void MulExact_MatchesBigIntegerTowardZero_OnBoundsAndRandomSigns()
    {
        // 两侧分数 3037000500:af·bf = 2^63 + 145474192,小于 2^64。逻辑右移得 2147483648,与向零截断相同。
        const long halfSqrt = 3037000500L;
        const long halfSqrtProduct = 2147483648L;
        Assert.That(TowardZero(halfSqrt, halfSqrt), Is.EqualTo(halfSqrtProduct));
        Assert.That(Fixed64.Mul(halfSqrt, halfSqrt), Is.EqualTo(halfSqrtProduct));
        Assert.That(Fixed64.MulExact(halfSqrt, halfSqrt), Is.EqualTo(halfSqrtProduct));

        long[] magnitudes =
        {
            0L,
            1L,
            (1L << 32) - 1,
            1L << 32,
            (1L << 32) + 1,
            halfSqrt,
            1L << 48,
            1L << 46,
            (1L << 62) - 1,
            long.MaxValue,
        };
        for (int i = 0; i < magnitudes.Length; i++)
        {
            for (int j = i; j < magnitudes.Length; j++)
            {
                long b = BoundMagnitude(magnitudes[i], magnitudes[j]);
                CheckAllSigns(magnitudes[i], b);
            }
        }

        var rng = new Random(0x4D756C45);
        for (int n = 0; n < 64; n++)
        {
            long a = NextMagnitude(rng);
            long b = NextBounded(rng, a);
            CheckAllSigns(a, b);
        }
    }

    private static void AssertSignedUlp(long a, long b, long expectedExact, long expectedMul)
    {
        Assert.That(TowardZero(a, b), Is.EqualTo(expectedExact), $"手算与 BigInteger 不一致: {a}, {b}");
        Assert.That(Fixed64.MulExact(a, b), Is.EqualTo(expectedExact), $"MulExact({a}, {b})");
        Assert.That(Fixed64.Mul(a, b), Is.EqualTo(expectedMul), $"Mul({a}, {b})");
        Assert.That(expectedMul, Is.EqualTo(expectedExact - 1), $"Mul 与 MulExact 应差 1 个最小单位: {a}, {b}");

        Fix64 fa = Fix64.FromRaw(a);
        Fix64 fb = Fix64.FromRaw(b);
        Assert.That((fa * fb).RawValue, Is.EqualTo(expectedMul), $"operator*({a}, {b})");
        Assert.That(Fix64Math.MulExact(fa, fb).RawValue, Is.EqualTo(expectedExact), $"Fix64Math.MulExact({a}, {b})");
    }

    private static void CheckAllSigns(long magnitudeA, long magnitudeB)
    {
        if (magnitudeA < 0 || magnitudeB < 0)
            throw new ArgumentOutOfRangeException(nameof(magnitudeA), "幅度必须非负");
        Check(magnitudeA, magnitudeB);
        Check(-magnitudeA, magnitudeB);
        Check(magnitudeA, -magnitudeB);
        Check(-magnitudeA, -magnitudeB);
    }

    private static void Check(long a, long b)
    {
        long expect = TowardZero(a, b);
        Assert.That(Fixed64.MulExact(a, b), Is.EqualTo(expect), $"MulExact({a}, {b})");
        Assert.That(Fix64Math.MulExact(Fix64.FromRaw(a), Fix64.FromRaw(b)).RawValue, Is.EqualTo(expect),
            $"Fix64Math.MulExact({a}, {b})");
        if ((a < 0) == (b < 0))
            Assert.That(Fixed64.Mul(a, b), Is.EqualTo(expect), $"Mul({a}, {b}) 积非负应与向零截断相同");
    }

    private static long BoundMagnitude(long magnitudeA, long magnitudeB)
    {
        long maxB = MaxAbsFactor(magnitudeA);
        return magnitudeB > maxB ? maxB : magnitudeB;
    }

    private static long NextMagnitude(Random rng)
    {
        switch (rng.Next(4))
        {
            case 0: return long.MaxValue;
            case 1: return (1L << rng.Next(0, 62)) - 1L;
            case 2: return (long)rng.Next() << rng.Next(0, 32);
            default:
                ulong bits = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                return (long)(bits & (ulong)long.MaxValue);
        }
    }

    private static long NextBounded(Random rng, long magnitudeA)
    {
        long maxB = MaxAbsFactor(magnitudeA);
        if (maxB == long.MaxValue)
        {
            ulong bits = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
            return (long)(bits & (ulong)long.MaxValue);
        }

        return rng.NextInt64(0, maxB + 1);
    }

    private static long MaxAbsFactor(long magnitude)
    {
        if (magnitude < 0)
            throw new ArgumentOutOfRangeException(nameof(magnitude), magnitude, "幅度必须非负");
        if (magnitude == 0) return long.MaxValue;
        BigInteger max = (((BigInteger.One << 63) - 1) << 32) / magnitude;
        return max > long.MaxValue ? long.MaxValue : (long)max;
    }

    private static long TowardZero(long a, long b)
    {
        BigInteger q = ((BigInteger)a * b) / (BigInteger.One << Fixed64.Shift);
        if (q < long.MinValue || q > long.MaxValue)
            throw new InvalidOperationException($"向零截断超出有符号 64 位: a={a}, b={b}, q={q}");
        return (long)q;
    }
}
