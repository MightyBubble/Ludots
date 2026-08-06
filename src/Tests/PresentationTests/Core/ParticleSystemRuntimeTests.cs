using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Particles;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class ParticleSystemRuntimeTests
{
    [Test]
    public void Runtime_IsDeterministicForSameAssetAndTimeSteps()
    {
        ParticleEffectAssetData effect = CreateEffect(
            maxParticles: 32,
            seed: 1234u,
            emissionRatePerSecond: 8f,
            burstCount: 3);
        var first = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);
        var second = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);

        for (int i = 0; i < 10; i++)
        {
            first.Update(effect, 0.1f, Vector3.Zero, Quaternion.Identity);
            second.Update(effect, 0.1f, Vector3.Zero, Quaternion.Identity);
        }

        ParticleSystemSnapshot firstSnapshot = first.GetSnapshot();
        ParticleSystemSnapshot secondSnapshot = second.GetSnapshot();
        Assert.That(firstSnapshot.Count, Is.EqualTo(secondSnapshot.Count));
        for (int i = 0; i < firstSnapshot.Count; i++)
        {
            Assert.That(firstSnapshot.Positions[i], Is.EqualTo(secondSnapshot.Positions[i]));
            Assert.That(firstSnapshot.Velocities[i], Is.EqualTo(secondSnapshot.Velocities[i]));
            Assert.That(firstSnapshot.Sizes[i], Is.EqualTo(secondSnapshot.Sizes[i]));
            Assert.That(firstSnapshot.Colors[i], Is.EqualTo(secondSnapshot.Colors[i]));
        }
    }

    [Test]
    public void Runtime_AppliesGravityAndRemovesExpiredParticles()
    {
        ParticleEffectAssetData effect = CreateEffect(
            maxParticles: 8,
            seed: 7u,
            emissionRatePerSecond: 0f,
            burstCount: 1,
            startLife: new ParticleValueRange(0.25f, 0.25f),
            startSpeed: new ParticleValueRange(1f, 1f),
            gravity: new Vector3(0f, -10f, 0f));
        var runtime = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);

        runtime.Update(effect, 0f, Vector3.Zero, Quaternion.Identity);
        ParticleSystemSnapshot initial = runtime.GetSnapshot();
        Assert.That(initial.Count, Is.EqualTo(1));

        runtime.Update(effect, 0.1f, Vector3.Zero, Quaternion.Identity);
        ParticleSystemSnapshot falling = runtime.GetSnapshot();
        Assert.That(falling.Count, Is.EqualTo(1));
        Assert.That(falling.Velocities[0].Y, Is.LessThan(0f));

        runtime.Update(effect, 0.2f, Vector3.Zero, Quaternion.Identity);
        Assert.That(runtime.GetSnapshot().Count, Is.EqualTo(0));
    }

    [Test]
    public void Runtime_ReportsCapacityRejectionExplicitly()
    {
        ParticleEffectAssetData effect = CreateEffect(
            maxParticles: 2,
            seed: 99u,
            emissionRatePerSecond: 0f,
            burstCount: 5);
        var runtime = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);

        runtime.Update(effect, 0f, Vector3.Zero, Quaternion.Identity);

        Assert.That(runtime.ParticleCount, Is.EqualTo(2));
        Assert.That(runtime.RejectedSpawnCount, Is.EqualTo(3));
        Assert.That(runtime.GetSnapshot().RejectedSpawnCount, Is.EqualTo(3));
    }

    [Test]
    public void Runtime_RejectsZeroSeed()
    {
        Assert.That(
            () => new ParticleSystemRuntime(capacity: 8, seed: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("non-zero"));

        ParticleEffectAssetData effect = CreateEffect(
            maxParticles: 8,
            seed: 11u,
            emissionRatePerSecond: 0f,
            burstCount: 1);
        var runtime = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);

        Assert.That(
            () => runtime.Reset(seed: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("non-zero"));
    }

    [Test]
    public void ParticleRandom_RejectsZeroSeed()
    {
        Assert.That(
            () => new ParticleRandom(seed: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("non-zero"));
    }

    [Test]
    public void ParticleGradientAndCurve_InterpolateBetweenAuthoredKeys()
    {
        var curve = new ParticleScalarCurve(
            new[]
            {
                new ParticleCurveKey(0f, 0f),
                new ParticleCurveKey(1f, 2f),
            });
        var gradient = new ParticleColorGradient(
            new[]
            {
                new ParticleColorKey(0f, new Vector4(1f, 0f, 0f, 1f)),
                new ParticleColorKey(1f, new Vector4(0f, 0f, 1f, 0f)),
            });

        Assert.That(curve.Evaluate(0.25f), Is.EqualTo(0.5f));
        Assert.That(gradient.Evaluate(0.5f), Is.EqualTo(new Vector4(0.5f, 0f, 0.5f, 0.5f)));
    }

    private static ParticleEffectAssetData CreateEffect(
        int maxParticles,
        uint seed,
        float emissionRatePerSecond,
        int burstCount,
        ParticleValueRange? startLife = null,
        ParticleValueRange? startSpeed = null,
        in Vector3 gravity = default)
    {
        return new ParticleEffectAssetData(
            PrefabVfxSpawnMode.Once,
            ParticleEmitterShapeKind.Cone,
            ParticleRenderMode.Mesh,
            ParticlePrimitiveKind.Sphere,
            ParticleOverflowPolicy.DropNewest,
            maxParticles,
            seed,
            durationSeconds: 1f,
            emissionRatePerSecond,
            burstCount,
            shapeRadius: 0.1f,
            shapeAngleRadians: 0.4f,
            shapeThickness: 1f,
            startLife ?? new ParticleValueRange(1f, 1f),
            startSpeed ?? new ParticleValueRange(1f, 1f),
            new ParticleValueRange(0.1f, 0.1f),
            new Vector4(1f, 1f, 1f, 1f),
            new ParticleScalarCurve(
                new[]
                {
                    new ParticleCurveKey(0f, 1f),
                    new ParticleCurveKey(1f, 0f),
                }),
            new ParticleColorGradient(
                new[]
                {
                    new ParticleColorKey(0f, Vector4.One),
                    new ParticleColorKey(1f, Vector4.One),
                }),
            gravity,
            drag: 0f,
            worldSpace: true);
    }
}
