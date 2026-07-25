using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Presentation.Assets;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DCylinderTests
{
    [Test]
    public void CylinderShape_IsSharedCapacityBoundedAndProducesARealContact()
    {
        using var world = new Physics3DWorld(CreateConfig());
        Physics3DShapeId cylinder = world.RegisterCylinderShape(radiusCm: 20f, lengthCm: 12f);
        Physics3DShapeId duplicate = world.RegisterCylinderShape(radiusCm: 20f, lengthCm: 12f);
        Physics3DShapeId floor = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate, Is.EqualTo(cylinder));
            Assert.That(world.RegisteredShapeCount, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.RegisterCylinderShape(0f, 12f));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.RegisterCylinderShape(20f, 0f));
            Assert.Throws<Physics3DCapacityExceededException>(() => world.RegisterSphereShape(5f));
        });

        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            floor,
            new Vector3(0f, -10f, 0f),
            Quaternion.Identity,
            mass: 0f));
        Physics3DBodyId wheel = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            cylinder,
            new Vector3(0f, 100f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f),
            mass: 20f));

        for (int step = 0; step < 180; step++)
        {
            world.Step();
        }

        Physics3DBodyState state = world.GetBodyState(wheel);
        Assert.Multiple(() =>
        {
            Assert.That(world.ContactPairCount, Is.GreaterThan(0));
            Assert.That(float.IsFinite(state.PositionCm.Y), Is.True);
            Assert.That(state.PositionCm.Y, Is.InRange(19f, 22f));
            Assert.That(state.LinearVelocityCmPerSecond.Length(), Is.LessThan(1f));
        });

        world.DestroyBody(wheel);
        Assert.That(world.ContainsBody(wheel), Is.False);
        Assert.Throws<InvalidOperationException>(() => world.GetBodyState(wheel));
    }

    [Test]
    public void CylinderPrimitive_IsRegisteredAsAFirstClassVisualMesh()
    {
        var meshes = new MeshAssetRegistry();
        int cylinderMeshId = meshes.GetId(WellKnownMeshKeys.Cylinder);

        Assert.Multiple(() =>
        {
            Assert.That(cylinderMeshId, Is.GreaterThan(0));
            Assert.That(meshes.TryGetPrimitiveKind(cylinderMeshId, out PrimitiveMeshKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(PrimitiveMeshKind.Cylinder));
        });
    }

    private static Physics3DWorldConfig CreateConfig()
        => new()
        {
            MobileBodyCapacity = 1,
            StaticBodyCapacity = 1,
            ShapeCapacity = 2,
            InactiveIslandCapacity = 1,
            ConstraintCapacity = 1,
            ConstraintsPerTypeBatchCapacity = 1,
            ConstraintCountPerBodyEstimate = 1,
            ContactPairCapacityPerWorker = 64,
            ActuationCommandCapacity = 1,
            WorkerCount = 1,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 2,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
            LinearDamping = 0.03f,
            AngularDamping = 0.03f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = 32,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        };

    private static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 positionCm,
        Quaternion orientation,
        float mass)
        => new(
            Entity.Null,
            kind,
            shape,
            positionCm,
            orientation,
            Vector3.Zero,
            Vector3.Zero,
            mass,
            LayerMask.All,
            new Physics3DMaterial(0.8f, 200f, 30f, 1f),
            Physics3DContinuousDetectionMode.Passive);
}
