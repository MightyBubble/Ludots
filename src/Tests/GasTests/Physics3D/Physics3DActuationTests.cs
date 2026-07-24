using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DActuationTests
{
    [Test]
    public void FixedStep_ReplaysAllActuationKindsAndConsumesCommands()
    {
        using var world = CreateWorld(mobileCapacity: 1, actuationCommandCapacity: 6);
        Physics3DShapeId sphere = world.RegisterSphereShape(10f);
        Physics3DBodyId body = world.CreateBody(CreateDynamicBody(sphere, Vector3.Zero, mass: 2f));

        world.EnqueueForce(body, new Vector3(120f, 0f, 0f));
        world.EnqueueAcceleration(body, new Vector3(0f, 120f, 0f));
        world.EnqueueTorque(body, new Vector3(0f, 0f, 4_800f));
        world.EnqueueLinearImpulse(body, new Vector3(0f, 0f, 4f));
        world.EnqueueAngularImpulse(body, new Vector3(80f, 0f, 0f));
        world.EnqueueImpulseAtWorldPoint(body, Vector3.Zero, Vector3.Zero);

        Assert.That(world.PendingActuationCommandCount, Is.EqualTo(6));
        world.Step();

        Physics3DBodyState state = world.GetBodyState(body);
        Assert.Multiple(() =>
        {
            Assert.That(state.LinearVelocityCmPerSecond.X, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(state.LinearVelocityCmPerSecond.Y, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(state.LinearVelocityCmPerSecond.Z, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(state.AngularVelocityRadiansPerSecond.X, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(state.AngularVelocityRadiansPerSecond.Z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
            Assert.That(world.StepIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void ImpulseAtWorldPoint_ProducesLinearAndAngularVelocity()
    {
        using var world = CreateWorld(mobileCapacity: 1, actuationCommandCapacity: 1);
        Physics3DShapeId sphere = world.RegisterSphereShape(10f);
        Physics3DBodyId body = world.CreateBody(CreateDynamicBody(sphere, Vector3.Zero, mass: 2f));

        world.EnqueueImpulseAtWorldPoint(
            body,
            new Vector3(2f, 0f, 0f),
            new Vector3(0f, 10f, 0f));
        world.Step();

        Physics3DBodyState state = world.GetBodyState(body);
        Assert.Multiple(() =>
        {
            Assert.That(state.LinearVelocityCmPerSecond.X, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(state.AngularVelocityRadiansPerSecond.Z, Is.EqualTo(-0.25f).Within(1e-5f));
        });
    }

    [Test]
    public void Commands_AreMergedByBodySlotRegardlessOfCrossBodySubmissionOrder()
    {
        using var left = CreateWorld(mobileCapacity: 2, actuationCommandCapacity: 4);
        using var right = CreateWorld(mobileCapacity: 2, actuationCommandCapacity: 4);
        Physics3DShapeId leftShape = left.RegisterSphereShape(10f);
        Physics3DShapeId rightShape = right.RegisterSphereShape(10f);
        Physics3DBodyId leftA = left.CreateBody(CreateDynamicBody(leftShape, new Vector3(-100f, 0f, 0f), 1f));
        Physics3DBodyId leftB = left.CreateBody(CreateDynamicBody(leftShape, new Vector3(100f, 0f, 0f), 1f));
        Physics3DBodyId rightA = right.CreateBody(CreateDynamicBody(rightShape, new Vector3(-100f, 0f, 0f), 1f));
        Physics3DBodyId rightB = right.CreateBody(CreateDynamicBody(rightShape, new Vector3(100f, 0f, 0f), 1f));

        left.EnqueueLinearImpulse(leftB, new Vector3(0f, 0f, 3f));
        left.EnqueueForce(leftA, new Vector3(60f, 0f, 0f));
        left.EnqueueAngularImpulse(leftB, new Vector3(0f, 40f, 0f));
        left.EnqueueAcceleration(leftA, new Vector3(0f, 60f, 0f));

        right.EnqueueForce(rightA, new Vector3(60f, 0f, 0f));
        right.EnqueueAcceleration(rightA, new Vector3(0f, 60f, 0f));
        right.EnqueueLinearImpulse(rightB, new Vector3(0f, 0f, 3f));
        right.EnqueueAngularImpulse(rightB, new Vector3(0f, 40f, 0f));

        left.Step();
        right.Step();

        Assert.That(left.ComputeObservableBodyStateHash(), Is.EqualTo(right.ComputeObservableBodyStateHash()));
    }

    [Test]
    public void CapacityOverflow_ThrowsAndNeverDropsACommandSilently()
    {
        using var world = CreateWorld(mobileCapacity: 1, actuationCommandCapacity: 2);
        Physics3DShapeId sphere = world.RegisterSphereShape(10f);
        Physics3DBodyId body = world.CreateBody(CreateDynamicBody(sphere, Vector3.Zero, 1f));
        world.EnqueueForce(body, Vector3.UnitX);
        world.EnqueueForce(body, Vector3.UnitY);

        Physics3DCapacityExceededException exception = Assert.Throws<Physics3DCapacityExceededException>(
            () => world.EnqueueForce(body, Vector3.UnitZ))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Resource, Is.EqualTo("actuation commands"));
            Assert.That(exception.Capacity, Is.EqualTo(2));
            Assert.That(world.PendingActuationCommandCount, Is.EqualTo(2));
        });

        world.ClearActuationCommands();
        Assert.That(world.PendingActuationCommandCount, Is.Zero);
    }

    [Test]
    public void StaleBodyCommand_RejectsTheWholeBatchBeforeApplyingAnyImpulse()
    {
        using var world = CreateWorld(mobileCapacity: 2, actuationCommandCapacity: 2);
        Physics3DShapeId sphere = world.RegisterSphereShape(10f);
        Physics3DBodyId validBody = world.CreateBody(CreateDynamicBody(sphere, Vector3.Zero, 1f));
        Physics3DBodyId staleBody = world.CreateBody(CreateDynamicBody(sphere, new Vector3(100f, 0f, 0f), 1f));
        world.EnqueueLinearImpulse(validBody, new Vector3(10f, 0f, 0f));
        world.EnqueueLinearImpulse(staleBody, new Vector3(10f, 0f, 0f));
        world.DestroyBody(staleBody);

        Assert.Throws<InvalidOperationException>(() => world.Step());

        Assert.Multiple(() =>
        {
            Assert.That(world.GetBodyState(validBody).LinearVelocityCmPerSecond, Is.EqualTo(Vector3.Zero));
            Assert.That(world.PendingActuationCommandCount, Is.EqualTo(2));
            Assert.That(world.StepIndex, Is.Zero);
        });

        world.ClearActuationCommands();
    }

    [Test]
    public void WarmedActuationAndFixedStep_HaveZeroManagedAllocationsOnAllThreads()
    {
        const int bodyCount = 64;
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            bodyCount,
            staticCapacity: 0,
            workerCount: 2,
            actuationCommandCapacity: bodyCount,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        using var world = new Physics3DWorld(config, dispatcher);
        Physics3DShapeId sphere = world.RegisterSphereShape(5f);
        var bodies = new Physics3DBodyId[bodyCount];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = world.CreateBody(CreateDynamicBody(
                sphere,
                new Vector3((i % 16) * 100f, (i / 16) * 100f, 0f),
                mass: 1f));
        }

        for (int step = 0; step < 60; step++)
        {
            EnqueueForAll(world, bodies);
            world.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long backgroundBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int step = 0; step < 120; step++)
        {
            EnqueueForAll(world, bodies);
            world.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long backgroundAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundBefore;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, $"Physics3D actuation and fixed steps allocated {allocated} managed bytes on the calling thread after warmup.");
            Assert.That(backgroundAllocated, Is.Zero, $"Physics3D fixed steps allocated {backgroundAllocated} managed bytes on background workers after warmup.");
        });
    }

    [Test]
    public void SparseActuationInFiftyThousandBodyCapacity_RemainsZeroAllocationAfterWarmup()
    {
        const int bodyCapacity = 50_000;
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            bodyCapacity,
            staticCapacity: 0,
            workerCount: 1,
            actuationCommandCapacity: 1,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f);
        using var world = new Physics3DWorld(config);
        Physics3DShapeId sphere = world.RegisterSphereShape(5f);
        Physics3DBodyId body = world.CreateBody(CreateDynamicBody(sphere, Vector3.Zero, mass: 1f));

        for (int step = 0; step < 60; step++)
        {
            world.EnqueueForce(body, Vector3.UnitX);
            world.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 120; step++)
        {
            world.EnqueueForce(body, Vector3.UnitX);
            world.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, $"Sparse 50K-capacity actuation allocated {allocated} managed bytes after warmup.");
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
            Assert.That(world.StepIndex, Is.EqualTo(180));
        });
    }

    private static void EnqueueForAll(Physics3DWorld world, Physics3DBodyId[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            world.EnqueueForce(bodies[i], Vector3.UnitX);
        }
    }

    private static Physics3DWorld CreateWorld(int mobileCapacity, int actuationCommandCapacity)
    {
        return new Physics3DWorld(Physics3DWorldTests.CreateConfig(
            mobileCapacity,
            staticCapacity: 0,
            workerCount: 1,
            actuationCommandCapacity: actuationCommandCapacity,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f));
    }

    private static Physics3DBodyDescription CreateDynamicBody(
        Physics3DShapeId shape,
        Vector3 positionCm,
        float mass)
    {
        return new Physics3DBodyDescription(
            Entity.Null,
            Physics3DBodyKind.Dynamic,
            shape,
            positionCm,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            mass,
            LayerMask.All,
            new Physics3DMaterial(
                frictionCoefficient: 0.8f,
                maximumRecoveryVelocityCmPerSecond: 200f,
                springAngularFrequency: 30f,
                springTwiceDampingRatio: 1f),
            Physics3DContinuousDetectionMode.Passive);
    }
}
