using System;
using System.Numerics;
using Arch.Core;
using BepuPhysics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DWorldTests
{
    [Test]
    public void CollisionMetadataFastPath_UnboundRollbackAndBoundReleaseKeepCountsBalanced()
    {
        var store = new Physics3DBodyStore(mobileCapacity: 2, staticCapacity: 0);
        int rolledBackSlot = store.AllocateSlot(Physics3DBodyKind.Dynamic);
        store.RollbackSlot(rolledBackSlot);

        int reboundSlot = store.AllocateSlot(Physics3DBodyKind.Dynamic);
        Physics3DBodyDescription description = CreateBody(
            Physics3DBodyKind.Dynamic,
            default,
            Vector3.Zero,
            new LayerMask(1u, 1u));
        store.BindMobile(reboundSlot, new BodyHandle(0), in description);

        Assert.That(store.HasCustomCollisionFilters, Is.True);
        Assert.That(store.HasNonSolidContactPolicies, Is.False);

        store.Release(store.GetId(reboundSlot));
        Assert.That(store.HasCustomCollisionFilters, Is.False);
        Assert.That(store.HasNonSolidContactPolicies, Is.False);
    }

    [Test]
    public void SharedShapeAndGenerationalBodyIds_AreCapacityBounded()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 2, staticCapacity: 1, shapeCapacity: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(100f));
        Physics3DShapeId duplicateBox = world.RegisterBoxShape(new Vector3(100f));
        Physics3DBodyId first = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, box, new Vector3(0f, 200f, 0f)));
        Physics3DBodyId second = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, box, new Vector3(200f, 200f, 0f)));

        Assert.That(world.RegisteredShapeCount, Is.EqualTo(1));
        Assert.That(duplicateBox, Is.EqualTo(box));
        Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(2));
        Assert.Throws<Physics3DCapacityExceededException>(() =>
            world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, box, new Vector3(400f, 200f, 0f))));
        Assert.Throws<Physics3DCapacityExceededException>(() => world.RegisterSphereShape(10f));

        world.DestroyBody(first);
        Assert.That(world.ContainsBody(first), Is.False);
        Assert.Throws<InvalidOperationException>(() => world.GetBodyState(first));

        Physics3DBodyId replacement = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, box, new Vector3(0f, 300f, 0f)));
        Assert.That(replacement.Slot, Is.EqualTo(first.Slot));
        Assert.That(replacement.Generation, Is.Not.EqualTo(first.Generation));
        Assert.That(world.ContainsBody(second), Is.True);
        Assert.That(world.ContainsBody(replacement), Is.True);
    }

    [Test]
    public void SetBodyAwake_ResetsSleepCandidateAndRejectsStaticBodies()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 1, staticCapacity: 1, workerCount: 1));
        Physics3DShapeId sphere = world.RegisterSphereShape(5f);
        Physics3DBodyId dynamicBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            sphere,
            new Vector3(0f, 1_000f, 0f)));
        Physics3DBodyId staticBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sphere,
            Vector3.Zero));

        world.SetBodyAwake(dynamicBody, false);
        Assert.That(world.GetBodyState(dynamicBody).Awake, Is.False);

        world.SetBodyAwake(dynamicBody, true);
        Assert.That(world.GetBodyState(dynamicBody).Awake, Is.True);

        world.SetBodyVelocity(dynamicBody, Vector3.UnitX, Vector3.Zero);
        Assert.That(world.GetBodyState(dynamicBody).Awake, Is.True);
        Assert.Throws<InvalidOperationException>(() => world.SetBodyAwake(staticBody, true));
    }

    [Test]
    public void ConstructorFailure_DisposesOwnedThreadDispatcher()
    {
        Physics3DWorldConfig config = CreateConfig(mobileCapacity: 1, staticCapacity: 0, workerCount: 2);
        var dispatcher = new TrackingThreadDispatcher(threadCount: 1);

        Assert.Throws<ArgumentException>(() => new Physics3DWorld(config, dispatcher));
        Assert.That(dispatcher.IsDisposed, Is.True);
    }

    [Test]
    public void ProductionStepMetrics_AttributeEveryKernelStageAndRemainZeroGcAfterWarmup()
    {
        using var world = new Physics3DWorld(CreateConfig(
            mobileCapacity: 8,
            staticCapacity: 1,
            workerCount: 2,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f));
        Physics3DShapeId floor = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floor, new Vector3(0f, -100f, 0f)));
        for (int i = 0; i < 8; i++)
        {
            world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                box,
                new Vector3(i * 50f, 500f, 0f)));
        }

        for (int i = 0; i < 64; i++)
        {
            world.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        world.Step();
        Physics3DStepMetrics metrics = world.LastStepMetrics;
        Physics3DStageMetrics[] stages =
        [
            metrics.CommandReplay,
            metrics.Sleep,
            metrics.PredictBounds,
            metrics.CollisionDetection,
            metrics.ContactSurface,
            metrics.Solve,
            metrics.Optimize,
            metrics.ContactFinalize
        ];

        Assert.Multiple(() =>
        {
            Assert.That(metrics.StepIndex, Is.EqualTo(world.StepIndex));
            Assert.That(metrics.HasKernelStageBreakdown, Is.True);
            Assert.That(metrics.Total.ElapsedMilliseconds, Is.GreaterThan(0d));
            Assert.That(metrics.Total.CallingThreadAllocatedBytes, Is.Zero);
            Assert.That(metrics.Total.BackgroundWorkerAllocatedBytes, Is.Zero);
        });
        foreach (Physics3DStageMetrics stage in stages)
        {
            Assert.Multiple(() =>
            {
                Assert.That(stage.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(0d));
                Assert.That(stage.CallingThreadAllocatedBytes, Is.Zero);
                Assert.That(stage.BackgroundWorkerAllocatedBytes, Is.Zero);
                Assert.That(stage.BackgroundWorkerDispatchElapsedTimestampTicks, Is.GreaterThanOrEqualTo(0L));
            });
        }
    }

    [Test]
    public void FixedStepGravityContactAndRaycast_UseRealBepuWorld()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 4, staticCapacity: 2, workerCount: 2));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId sphereShape = world.RegisterSphereShape(20f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId sphere = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, sphereShape, new Vector3(0f, 300f, 0f)));

        for (int i = 0; i < 180; i++)
        {
            world.Step();
        }

        Physics3DBodyState state = world.GetBodyState(sphere);
        Assert.That(state.PositionCm.Y, Is.InRange(18f, 24f));
        Span<Physics3DRaycastHit> hits = stackalloc Physics3DRaycastHit[4];
        int hitCount = world.Raycast(
            new Vector3(0f, 500f, 0f),
            -Vector3.UnitY,
            1_000f,
            LayerMask.All,
            hits);
        Assert.That(hitCount, Is.EqualTo(2));
        Assert.That(hits[0].Body, Is.EqualTo(sphere));
        Assert.That(hits[0].DistanceCm, Is.LessThan(hits[1].DistanceCm));
    }

    [Test]
    public void ShapeCasts_AreNarrowPhaseLayerFilteredStableAndReportInitialOverlap()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 1, staticCapacity: 3));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId first = world.CreateBody(CreateBody(Physics3DBodyKind.Static, box, new Vector3(100f, 0f, 0f)));
        Physics3DBodyId second = world.CreateBody(CreateBody(Physics3DBodyKind.Static, box, new Vector3(300f, 0f, 0f)));
        LayerMask excludedLayer = new(category: 1u << 2, mask: uint.MaxValue);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, box, new Vector3(200f, 0f, 0f), excludedLayer));

        Span<Physics3DShapeCastHit> hits = stackalloc Physics3DShapeCastHit[4];
        LayerMask queryLayer = new(category: 1u, mask: 1u);
        int sphereHitCount = world.SphereCast(
            Vector3.Zero,
            radiusCm: 10f,
            Vector3.UnitX,
            maximumDistanceCm: 500f,
            queryLayer,
            hits);

        Assert.That(sphereHitCount, Is.EqualTo(2));
        Assert.That(hits[0].Body, Is.EqualTo(first));
        Assert.That(hits[1].Body, Is.EqualTo(second));
        Assert.That(hits[0].DistanceCm, Is.EqualTo(80f).Within(0.1f));
        Assert.That(hits[1].DistanceCm, Is.EqualTo(280f).Within(0.1f));
        Assert.That(hits[0].StartedOverlapping, Is.False);

        int initialOverlapCount = world.BoxCast(
            new Vector3(100f, 0f, 0f),
            new Vector3(10f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.25f),
            Vector3.UnitX,
            maximumDistanceCm: 50f,
            queryLayer,
            hits);
        Assert.That(initialOverlapCount, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(first));
        Assert.That(hits[0].DistanceCm, Is.Zero);
        Assert.That(hits[0].StartedOverlapping, Is.True);

        int capsuleHitCount = world.CapsuleCast(
            Vector3.Zero,
            radiusCm: 5f,
            cylinderLengthCm: 20f,
            Quaternion.Identity,
            Vector3.UnitX,
            maximumDistanceCm: 150f,
            queryLayer,
            hits);
        Assert.That(capsuleHitCount, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(first));
        Assert.Throws<Physics3DCapacityExceededException>(() => world.SphereCast(
            Vector3.Zero,
            10f,
            Vector3.UnitX,
            500f,
            queryLayer,
            Span<Physics3DShapeCastHit>.Empty));
    }

    [Test]
    public void Overlaps_UseRealNarrowPhaseForRotatedBoxSphereAndCapsule()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 1, staticCapacity: 4));
        Physics3DShapeId sphere = world.RegisterSphereShape(2f);
        Physics3DBodyId onRotatedBoxAxis = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sphere,
            new Vector3(20f, 20f, 0f)));
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sphere,
            new Vector3(30f, -30f, 0f)));
        Physics3DBodyId onCapsuleAxis = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sphere,
            new Vector3(0f, 40f, 0f)));
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sphere,
            new Vector3(40f, 0f, 0f)));

        Span<Physics3DOverlapHit> hits = stackalloc Physics3DOverlapHit[4];
        int boxCount = world.OverlapBox(
            Vector3.Zero,
            new Vector3(100f, 10f, 10f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.25f),
            LayerMask.All,
            hits);
        Assert.That(boxCount, Is.EqualTo(1), "The off-axis sphere only overlaps the rotated box AABB and must not be reported.");
        Assert.That(hits[0].Body, Is.EqualTo(onRotatedBoxAxis));

        int sphereCount = world.OverlapSphere(new Vector3(20f, 20f, 0f), 5f, LayerMask.All, hits);
        Assert.That(sphereCount, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(onRotatedBoxAxis));

        int capsuleCount = world.OverlapCapsule(
            Vector3.Zero,
            radiusCm: 5f,
            cylinderLengthCm: 100f,
            Quaternion.Identity,
            LayerMask.All,
            hits);
        Assert.That(capsuleCount, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(onCapsuleAxis));
        Assert.Throws<Physics3DCapacityExceededException>(() => world.OverlapBox(
            Vector3.Zero,
            new Vector3(100f),
            Quaternion.Identity,
            LayerMask.All,
            Span<Physics3DOverlapHit>.Empty));
    }

    [Test]
    public void CollisionLayerMismatch_DoesNotGenerateContacts()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 2, staticCapacity: 1));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId sphereShape = world.RegisterSphereShape(20f);
        LayerMask floorLayer = new(category: 1u << 1, mask: 1u << 1);
        LayerMask bodyLayer = new(category: 1u, mask: 1u);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f), floorLayer));
        Physics3DBodyId sphere = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, sphereShape, new Vector3(0f, 100f, 0f), bodyLayer));

        for (int i = 0; i < 90; i++)
        {
            world.Step();
        }

        Assert.That(world.GetBodyState(sphere).PositionCm.Y, Is.LessThan(-500f));
    }

    [Test]
    public void ContactPairs_AreCapacityBoundedDeduplicatedAndStableOrdered()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 2, staticCapacity: 1, workerCount: 2));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId sphereShape = world.RegisterSphereShape(20f);
        Physics3DBodyId floor = world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId sphere = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, sphereShape, new Vector3(0f, 50f, 0f)));

        for (int i = 0; i < 120 && world.ContactPairCount == 0; i++)
        {
            world.Step();
        }

        Assert.That(world.ContactPairCount, Is.EqualTo(1));
        Span<Physics3DContactPair> contacts = stackalloc Physics3DContactPair[1];
        Assert.That(world.CopyContactPairs(contacts), Is.EqualTo(1));
        Assert.That(contacts[0].BodyA.Slot, Is.LessThan(contacts[0].BodyB.Slot));
        Assert.That(
            (contacts[0].BodyA == floor && contacts[0].BodyB == sphere) ||
            (contacts[0].BodyA == sphere && contacts[0].BodyB == floor),
            Is.True);
        Assert.Throws<Physics3DCapacityExceededException>(() => world.CopyContactPairs(Span<Physics3DContactPair>.Empty));
    }

    [Test]
    public void ContactEvents_TrackBeginStayAndEndAcrossSleepingAndDestruction()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 1, staticCapacity: 1, workerCount: 2));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId sphereShape = world.RegisterSphereShape(20f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId sphere = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, sphereShape, new Vector3(0f, 20f, 0f)));
        Span<Physics3DContactEvent> events = stackalloc Physics3DContactEvent[4];

        world.Step();
        Assert.That(world.CopyContactEvents(events), Is.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(Physics3DContactEventKind.Begin));

        Physics3DBodyState sleeping = world.GetBodyState(sphere);
        sleeping.Awake = false;
        world.SetBodyState(sphere, sleeping);
        world.Step();
        Assert.That(world.ContactPairCount, Is.EqualTo(1));
        Assert.That(world.CopyContactEvents(events), Is.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(Physics3DContactEventKind.Stay));

        Physics3DBodyState separated = world.GetBodyState(sphere);
        separated.PositionCm = new Vector3(0f, 500f, 0f);
        separated.Awake = true;
        world.SetBodyState(sphere, separated);
        world.Step();
        Assert.That(world.ContactPairCount, Is.Zero);
        Assert.That(world.CopyContactEvents(events), Is.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(Physics3DContactEventKind.End));

        separated.PositionCm = new Vector3(0f, 20f, 0f);
        world.SetBodyState(sphere, separated);
        world.Step();
        Assert.That(world.CopyContactEvents(events), Is.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(Physics3DContactEventKind.Begin));

        world.DestroyBody(sphere);
        Assert.That(world.ContactPairCount, Is.Zero);
        Assert.That(world.CopyContactEvents(events), Is.EqualTo(2));
        Assert.That(events[1].Kind, Is.EqualTo(Physics3DContactEventKind.End));
    }

    [Test]
    public void ConstraintStore_IsGenerationalAndBodyRemovalCascadesWithoutScanningDictionaries()
    {
        using var world = new Physics3DWorld(CreateConfig(
            mobileCapacity: 3,
            staticCapacity: 0,
            workerCount: 2,
            constraintCapacity: 1));
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId bodyA = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(0f, 100f, 0f)));
        Physics3DBodyId bodyB = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(100f, 100f, 0f)));
        Physics3DBodyId bodyC = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(200f, 100f, 0f)));
        var spring = new Physics3DSpringSettings(angularFrequency: 30f, twiceDampingRatio: 1f);
        Physics3DConstraintId constraint = world.CreateBallSocketConstraint(
            bodyA,
            bodyB,
            new Vector3(50f, 0f, 0f),
            new Vector3(-50f, 0f, 0f),
            spring);

        Assert.That(world.ContainsConstraint(constraint), Is.True);
        Assert.That(world.ActiveConstraintCount, Is.EqualTo(1));
        Assert.Throws<Physics3DCapacityExceededException>(() => world.CreateWeldConstraint(
            bodyB,
            bodyC,
            new Vector3(100f, 0f, 0f),
            Quaternion.Identity,
            spring));

        for (int i = 0; i < 120; i++)
        {
            world.Step();
        }

        float distance = Vector3.Distance(world.GetBodyState(bodyA).PositionCm, world.GetBodyState(bodyB).PositionCm);
        Assert.That(distance, Is.InRange(99f, 101f));
        Assert.That(world.GetConstraintImpulseMagnitude(constraint), Is.GreaterThanOrEqualTo(0f));

        world.DestroyBody(bodyA);
        Assert.That(world.ActiveConstraintCount, Is.Zero);
        Assert.That(world.ContainsConstraint(constraint), Is.False);
        Assert.Throws<InvalidOperationException>(() => world.GetConstraintImpulseMagnitude(constraint));
    }

    [Test]
    public void HingeAndWeldConstraints_ReuseSlotsAndRemoveAllBodyAdjacency()
    {
        using var world = new Physics3DWorld(CreateConfig(
            mobileCapacity: 3,
            staticCapacity: 0,
            workerCount: 2,
            constraintCapacity: 2));
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId bodyA = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(0f, 100f, 0f)));
        Physics3DBodyId bodyB = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(100f, 100f, 0f)));
        Physics3DBodyId bodyC = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(200f, 100f, 0f)));
        var spring = new Physics3DSpringSettings(angularFrequency: 30f, twiceDampingRatio: 1f);
        Physics3DConstraintId hinge = world.CreateHingeConstraint(
            bodyA,
            bodyB,
            new Vector3(50f, 0f, 0f),
            Vector3.UnitZ,
            new Vector3(-50f, 0f, 0f),
            Vector3.UnitZ,
            spring);
        Physics3DConstraintId weld = world.CreateWeldConstraint(
            bodyB,
            bodyC,
            new Vector3(100f, 0f, 0f),
            Quaternion.Identity,
            spring);

        world.DestroyConstraint(hinge);
        Physics3DConstraintId replacement = world.CreateBallSocketConstraint(
            bodyA,
            bodyB,
            new Vector3(50f, 0f, 0f),
            new Vector3(-50f, 0f, 0f),
            spring);
        Assert.That(replacement.Slot, Is.EqualTo(hinge.Slot));
        Assert.That(replacement.Generation, Is.Not.EqualTo(hinge.Generation));
        Assert.That(world.ContainsConstraint(hinge), Is.False);
        Assert.That(world.ContainsConstraint(replacement), Is.True);
        Assert.That(world.ContainsConstraint(weld), Is.True);

        world.DestroyBody(bodyB);
        Assert.That(world.ActiveConstraintCount, Is.Zero);
        Assert.That(world.ContainsConstraint(replacement), Is.False);
        Assert.That(world.ContainsConstraint(weld), Is.False);
    }

    [Test]
    public void MultithreadedSameBuildReplay_ProducesIdenticalHashes()
    {
        using var left = CreateStackWorld(workerCount: 2);
        using var right = CreateStackWorld(workerCount: 2);
        Assert.That(left.WorkerCount, Is.EqualTo(2));
        Assert.That(right.WorkerCount, Is.EqualTo(2));

        for (int i = 0; i < 180; i++)
        {
            left.Step();
            right.Step();
            Assert.That(left.ComputeStateHash(), Is.EqualTo(right.ComputeStateHash()), $"Replay diverged at step {i + 1}.");
        }
    }

    [Test]
    public void WarmedFixedStep_HasZeroManagedAllocationsOnCallingThread()
    {
        using Physics3DWorld world = CreateSeparatedBodyWorld(bodyCount: 256, workerCount: 2);
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            world.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Physics3D fixed steps allocated {allocated} managed bytes on the calling thread after warmup.");
    }

    [Test]
    public void WarmedSpatialQueries_HaveZeroManagedAllocationsOnCallingThread()
    {
        using var world = new Physics3DWorld(CreateConfig(mobileCapacity: 1, staticCapacity: 4, workerCount: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        for (int i = 0; i < 4; i++)
        {
            world.CreateBody(CreateBody(
                Physics3DBodyKind.Static,
                box,
                new Vector3(100f + i * 100f, 0f, 0f)));
        }

        Span<Physics3DRaycastHit> rayHits = stackalloc Physics3DRaycastHit[4];
        Span<Physics3DShapeCastHit> castHits = stackalloc Physics3DShapeCastHit[4];
        Span<Physics3DOverlapHit> overlapHits = stackalloc Physics3DOverlapHit[4];
        for (int i = 0; i < 64; i++)
        {
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, LayerMask.All, rayHits);
            world.SphereCast(Vector3.Zero, 5f, Vector3.UnitX, 500f, LayerMask.All, castHits);
            world.OverlapBox(new Vector3(250f, 0f, 0f), new Vector3(500f, 30f, 30f), Quaternion.Identity, LayerMask.All, overlapHits);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, LayerMask.All, rayHits);
            world.SphereCast(Vector3.Zero, 5f, Vector3.UnitX, 500f, LayerMask.All, castHits);
            world.OverlapBox(new Vector3(250f, 0f, 0f), new Vector3(500f, 30f, 30f), Quaternion.Identity, LayerMask.All, overlapHits);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Physics3D spatial queries allocated {allocated} managed bytes after warmup.");
    }

    [Test]
    public void AwakeSnapshot_ContainsOnlyBepuActiveSetAndUsesSoABuffers()
    {
        using Physics3DWorld world = CreateSeparatedBodyWorld(bodyCount: 32, workerCount: 1);
        Span<Physics3DBodyId> ids = stackalloc Physics3DBodyId[32];
        int count = world.CopyActiveBodyIds(ids);
        for (int i = 8; i < count; i++)
        {
            Physics3DBodyState state = world.GetBodyState(ids[i]);
            state.Awake = false;
            world.SetBodyState(ids[i], state);
        }

        var buffer = new Physics3DAwakeBodyBuffer(capacity: 8);
        world.CopyAwakeBodies(buffer);

        Assert.That(buffer.Count, Is.EqualTo(8));
        Assert.That(buffer.BodyIds.Length, Is.EqualTo(8));
        Assert.That(buffer.PositionsCm.Length, Is.EqualTo(8));
        Assert.That(buffer.StepIndex, Is.EqualTo(world.StepIndex));
    }

    [Test]
    public void EcsSimulationSystem_UsesChunkComponentsWithoutStructuralChanges()
    {
        using var physicsWorld = new Physics3DWorld(CreateConfig(mobileCapacity: 4, staticCapacity: 0, workerCount: 1));
        Physics3DShapeId shape = physicsWorld.RegisterSphereShape(10f);
        Physics3DBodyId body = physicsWorld.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(0f, 100f, 0f)));
        using World ecsWorld = World.Create();
        Entity entity = ecsWorld.Create(
            new Physics3DBodyCm { Id = body, Kind = Physics3DBodyKind.Dynamic },
            new Physics3DPoseCm { Position = new Vector3(0f, 100f, 0f), Orientation = Quaternion.Identity },
            new PreviousPhysics3DPoseCm { Position = new Vector3(0f, 100f, 0f), Orientation = Quaternion.Identity });
        var system = new Physics3DSimulationSystem(ecsWorld, physicsWorld, sourceFixedStepHz: 60, maximumPhysicsStepsPerSourceTick: 1);

        system.Update(1f / 60f);

        Assert.That(ecsWorld.Get<Physics3DPoseCm>(entity).Position.Y, Is.LessThan(100f));
        Assert.That(ecsWorld.Get<PreviousPhysics3DPoseCm>(entity).Position.Y, Is.EqualTo(100f));
        Assert.That(system.PhysicsStepsLastUpdate, Is.EqualTo(1));
    }

    [Test]
    public void ContactCapacityOverflow_AfterSimulationAdvance_EntersTerminalFaultAndRejectsRetry()
    {
        // Arrange: one worker with a single contact-pair slot so any second pair overflows during callbacks.
        // Floor + two resting mobiles produce at least two pairs on the first Timestep.
        using var world = new Physics3DWorld(CreateConfig(
            mobileCapacity: 2,
            staticCapacity: 1,
            workerCount: 1,
            contactPairCapacityPerWorker: 1));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId boxShape = world.RegisterBoxShape(new Vector3(40f));
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, boxShape, new Vector3(-50f, 20f, 0f)));
        world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, boxShape, new Vector3(50f, 20f, 0f)));
        Assert.That(world.IsTerminalFaulted, Is.False);
        Assert.That(world.TerminalFault, Is.Null);
        Assert.That(world.StepIndex, Is.Zero);

        // Act: first Step advances Bepu and StepIndex, then contact finalization fails.
        Physics3DCapacityExceededException capacity = Assert.Throws<Physics3DCapacityExceededException>(() => world.Step())!;

        // Assert: original capacity failure is preserved as the diagnostic cause; StepIndex already advanced.
        Assert.Multiple(() =>
        {
            Assert.That(capacity.Resource, Is.EqualTo("contact pairs per worker"));
            Assert.That(capacity.Capacity, Is.EqualTo(1));
            Assert.That(world.StepIndex, Is.EqualTo(1), "Simulation advanced before contact finalization failed.");
            Assert.That(world.IsTerminalFaulted, Is.True);
            Assert.That(world.TerminalFault, Is.SameAs(capacity));
        });

        // Act: catch-and-retry must fail before any further mutation.
        long stepIndexAfterFault = world.StepIndex;
        Physics3DTerminalFaultException retry = Assert.Throws<Physics3DTerminalFaultException>(() => world.Step())!;

        Assert.Multiple(() =>
        {
            Assert.That(retry.StepIndex, Is.EqualTo(stepIndexAfterFault));
            Assert.That(retry.TerminalFault, Is.SameAs(capacity));
            Assert.That(retry.InnerException, Is.SameAs(capacity));
            Assert.That(world.StepIndex, Is.EqualTo(stepIndexAfterFault), "Retry must not advance StepIndex again.");
            Assert.That(world.IsTerminalFaulted, Is.True);
            Assert.That(world.TerminalFault, Is.SameAs(capacity));
        });
    }

    [Test]
    public void ContactCapacityOverflow_TerminalFault_RejectsStructuralMutationAndAllowsDispose()
    {
        using var world = new Physics3DWorld(CreateConfig(
            mobileCapacity: 2,
            staticCapacity: 1,
            workerCount: 1,
            contactPairCapacityPerWorker: 1));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId boxShape = world.RegisterBoxShape(new Vector3(40f));
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId left = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, boxShape, new Vector3(-50f, 20f, 0f)));
        world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, boxShape, new Vector3(50f, 20f, 0f)));

        Physics3DCapacityExceededException capacity = Assert.Throws<Physics3DCapacityExceededException>(() => world.Step())!;
        Assert.That(world.IsTerminalFaulted, Is.True);

        Physics3DTerminalFaultException createBody = Assert.Throws<Physics3DTerminalFaultException>(() =>
            world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, boxShape, new Vector3(0f, 200f, 0f))))!;
        Physics3DTerminalFaultException registerShape = Assert.Throws<Physics3DTerminalFaultException>(() =>
            world.RegisterSphereShape(5f))!;
        Physics3DTerminalFaultException enqueue = Assert.Throws<Physics3DTerminalFaultException>(() =>
            world.EnqueueForce(left, Vector3.UnitY))!;

        Assert.Multiple(() =>
        {
            Assert.That(createBody.TerminalFault, Is.SameAs(capacity));
            Assert.That(registerShape.TerminalFault, Is.SameAs(capacity));
            Assert.That(enqueue.TerminalFault, Is.SameAs(capacity));
            Assert.That(world.StepIndex, Is.EqualTo(1));
            Assert.That(world.TerminalFault, Is.SameAs(capacity));
        });

        // Dispose remains valid on a terminal-faulted world; no rollback/retry contract.
        Assert.DoesNotThrow(() => world.Dispose());
        Assert.Throws<ObjectDisposedException>(() => world.Step());
    }

    private static Physics3DWorld CreateStackWorld(int workerCount)
    {
        var world = new Physics3DWorld(CreateConfig(mobileCapacity: 128, staticCapacity: 1, workerCount: workerCount));
        Physics3DShapeId floor = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(40f));
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floor, new Vector3(0f, -10f, 0f)));
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                world.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    box,
                    new Vector3((x - 3.5f) * 42f, 20f + y * 42f, 0f)));
            }
        }

        return world;
    }

    private static Physics3DWorld CreateSeparatedBodyWorld(int bodyCount, int workerCount)
    {
        var world = new Physics3DWorld(CreateConfig(mobileCapacity: bodyCount, staticCapacity: 0, workerCount: workerCount));
        Physics3DShapeId sphere = world.RegisterSphereShape(5f);
        for (int i = 0; i < bodyCount; i++)
        {
            world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                sphere,
                new Vector3((i % 32) * 100f, 10_000f + (i / 32) * 100f, 0f)));
        }

        return world;
    }

    internal static Physics3DWorldConfig CreateConfig(
        int mobileCapacity,
        int staticCapacity,
        int shapeCapacity = 8,
        int workerCount = 2,
        int? constraintCapacity = null,
        byte minimumTimestepCountUnderSleepThreshold = 32,
        int? actuationCommandCapacity = null,
        Vector3? gravityCmPerSecondSquared = null,
        float linearDamping = 0.03f,
        float angularDamping = 0.03f,
        int fixedStepHz = 60,
        int? contactPairCapacityPerWorker = null)
    {
        return new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticCapacity,
            ShapeCapacity = shapeCapacity,
            InactiveIslandCapacity = Math.Max(1, mobileCapacity),
            ConstraintCapacity = constraintCapacity ?? Math.Max(1, mobileCapacity * 8),
            ConstraintsPerTypeBatchCapacity = Math.Max(1, mobileCapacity * 4),
            ConstraintCountPerBodyEstimate = 8,
            ContactPairCapacityPerWorker = contactPairCapacityPerWorker ?? Math.Max(64, mobileCapacity * 4),
            ActuationCommandCapacity = actuationCommandCapacity ?? Math.Max(1, mobileCapacity * 8),
            WorkerCount = workerCount,
            FixedStepHz = fixedStepHz,
            MaximumPhysicsStepsPerSourceTick = 2,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = gravityCmPerSecondSquared ?? new Vector3(0f, -981f, 0f),
            LinearDamping = linearDamping,
            AngularDamping = angularDamping,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = minimumTimestepCountUnderSleepThreshold,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        };
    }

    internal static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 position,
        LayerMask? layer = null)
    {
        return new Physics3DBodyDescription(
            Entity.Null,
            kind,
            shape,
            position,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            kind == Physics3DBodyKind.Dynamic ? 1f : 0f,
            layer ?? LayerMask.All,
            new Physics3DMaterial(
                frictionCoefficient: 0.8f,
                maximumRecoveryVelocityCmPerSecond: 200f,
                springAngularFrequency: 30f,
                springTwiceDampingRatio: 1f),
            Physics3DContinuousDetectionMode.Passive);
    }
}
