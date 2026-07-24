using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DContactFieldTests
{
    [Test]
    public void Sensor_ProducesBeginStayEndWithoutImpulseAndParticipatesOnlyWhenRequestedByQuery()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 1, actuationCapacity: 4);
        Physics3DShapeId sensorShape = world.RegisterBoxShape(new Vector3(100f));
        Physics3DShapeId bodyShape = world.RegisterSphereShape(10f);
        Physics3DBodyId sensor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sensorShape,
            Vector3.Zero,
            mass: 0f,
            contactPolicy: Physics3DBodyContactPolicy.Sensor()));
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            bodyShape,
            Vector3.Zero,
            mass: 1f));

        world.Step();
        AssertSensorEvent(world, Physics3DContactEventKind.Begin);
        Assert.That(world.GetBodyState(body).LinearVelocityCmPerSecond, Is.EqualTo(Vector3.Zero));

        world.Step();
        AssertSensorEvent(world, Physics3DContactEventKind.Stay);

        var excludeSensors = new Physics3DQueryFilter(LayerMask.All, body, includeSensors: false);
        var includeSensors = new Physics3DQueryFilter(LayerMask.All, body, includeSensors: true);
        Assert.That(world.RaycastAny(new Vector3(-200f, 0f, 0f), Vector3.UnitX, 400f, excludeSensors), Is.False);
        Assert.That(world.RaycastClosest(
            new Vector3(-200f, 0f, 0f),
            Vector3.UnitX,
            400f,
            includeSensors,
            out Physics3DRaycastHit sensorHit), Is.True);
        Assert.That(sensorHit.Body, Is.EqualTo(sensor));

        Physics3DBodyState moved = world.GetBodyState(body);
        moved.PositionCm = new Vector3(500f, 0f, 0f);
        moved.Awake = true;
        world.SetBodyState(body, moved);
        world.Step();
        AssertSensorEvent(world, Physics3DContactEventKind.End);
    }

    [Test]
    public void SensorEvents_AreDeterministicallySortedByBodyPairAndKind()
    {
        using var world = CreateWorld(mobileCapacity: 3, staticCapacity: 1, actuationCapacity: 4);
        Physics3DShapeId sensorShape = world.RegisterBoxShape(new Vector3(200f));
        Physics3DShapeId bodyShape = world.RegisterSphereShape(10f);
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sensorShape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.Sensor()));
        for (int i = 0; i < 3; i++)
        {
            world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                bodyShape,
                new Vector3(i * 20f, 0f, 0f),
                1f));
        }

        world.Step();
        Span<Physics3DContactEvent> events = stackalloc Physics3DContactEvent[3];
        int count = world.CopyContactEvents(events);
        Assert.That(count, Is.EqualTo(3));
        for (int i = 0; i < count; i++)
        {
            Assert.That(events[i].ContactKind, Is.EqualTo(Physics3DContactKind.Sensor));
            Assert.That(events[i].Kind, Is.EqualTo(Physics3DContactEventKind.Begin));
            if (i > 0)
            {
                Assert.That(PairKey(events[i - 1]), Is.LessThan(PairKey(events[i])));
            }
        }
    }

    [Test]
    public void OneWayPlatform_AllowsBackfacePassageAndSupportsFrontFaceLanding()
    {
        using var world = CreateWorld(
            mobileCapacity: 1,
            staticCapacity: 1,
            actuationCapacity: 4,
            gravityCmPerSecondSquared: new Vector3(0f, -981f, 0f));
        Physics3DShapeId platformShape = world.RegisterBoxShape(new Vector3(400f, 20f, 400f));
        Physics3DShapeId bodyShape = world.RegisterSphereShape(10f);
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            platformShape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.OneWayPlatform(Vector3.UnitY)));
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            bodyShape,
            new Vector3(0f, -60f, 0f),
            1f,
            linearVelocityCmPerSecond: new Vector3(0f, 600f, 0f)));

        for (int i = 0; i < 12; i++)
        {
            world.Step();
        }

        Physics3DBodyState passedThrough = world.GetBodyState(body);
        Assert.That(passedThrough.PositionCm.Y, Is.GreaterThan(30f));
        Assert.That(passedThrough.LinearVelocityCmPerSecond.Y, Is.GreaterThan(350f));

        passedThrough.PositionCm = new Vector3(0f, 100f, 0f);
        passedThrough.LinearVelocityCmPerSecond = new Vector3(0f, -300f, 0f);
        passedThrough.AngularVelocityRadiansPerSecond = Vector3.Zero;
        passedThrough.Awake = true;
        world.SetBodyState(body, passedThrough);
        for (int i = 0; i < 120; i++)
        {
            world.Step();
        }

        Physics3DBodyState landed = world.GetBodyState(body);
        Assert.Multiple(() =>
        {
            Assert.That(landed.PositionCm.Y, Is.GreaterThan(18f));
            Assert.That(landed.PositionCm.Y, Is.LessThan(25f));
            Assert.That(MathF.Abs(landed.LinearVelocityCmPerSecond.Y), Is.LessThan(1f));
        });
    }

    [Test]
    public void KinematicNextPose_ComputesLinearAngularAndContactPointVelocity()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 0, actuationCapacity: 1);
        Physics3DShapeId shape = world.RegisterBoxShape(new Vector3(100f, 20f, 100f));
        Physics3DBodyId platform = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            shape,
            Vector3.Zero,
            0f));
        Quaternion targetOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);

        world.SetKinematicNextPose(platform, new Vector3(30f, 0f, 0f), targetOrientation);
        Physics3DBodyState beforeStep = world.GetBodyState(platform);
        Vector3 edgeVelocity = world.GetBodyVelocityAtWorldPoint(platform, new Vector3(100f, 0f, 0f));
        Assert.Multiple(() =>
        {
            Assert.That(beforeStep.LinearVelocityCmPerSecond.X, Is.EqualTo(30f / world.FixedDeltaSeconds).Within(1e-3f));
            Assert.That(beforeStep.AngularVelocityRadiansPerSecond.Y, Is.EqualTo((MathF.PI * 0.5f) / world.FixedDeltaSeconds).Within(1e-3f));
            Assert.That(edgeVelocity.X, Is.EqualTo(beforeStep.LinearVelocityCmPerSecond.X).Within(1e-3f));
            Assert.That(edgeVelocity.Z, Is.LessThan(-100f));
        });

        world.Step();
        Physics3DBodyState afterStep = world.GetBodyState(platform);
        Assert.That(afterStep.PositionCm.X, Is.EqualTo(30f).Within(1e-3f));
        Assert.That(MathF.Abs(Quaternion.Dot(afterStep.Orientation, targetOrientation)), Is.GreaterThan(0.999f));
    }

    [Test]
    public void SurfaceVelocity_DrivesFrictionWithoutMovingTheConveyorGeometry()
    {
        using var world = CreateWorld(
            mobileCapacity: 2,
            staticCapacity: 0,
            actuationCapacity: 1,
            gravityCmPerSecondSquared: new Vector3(0f, -981f, 0f));
        Physics3DShapeId conveyorShape = world.RegisterBoxShape(new Vector3(600f, 20f, 200f));
        Physics3DShapeId crateShape = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId conveyor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            conveyorShape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.SurfaceVelocity(new Vector3(180f, 0f, 0f))));
        Physics3DBodyId crate = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            crateShape,
            new Vector3(0f, 25f, 0f),
            1f));

        for (int i = 0; i < 90; i++)
        {
            world.Step();
        }

        Physics3DBodyState conveyorState = world.GetBodyState(conveyor);
        Physics3DBodyState crateState = world.GetBodyState(crate);
        Assert.Multiple(() =>
        {
            Assert.That(conveyorState.PositionCm, Is.EqualTo(Vector3.Zero));
            Assert.That(conveyorState.LinearVelocityCmPerSecond, Is.EqualTo(Vector3.Zero));
            Assert.That(crateState.PositionCm.X, Is.GreaterThan(100f));
            Assert.That(crateState.LinearVelocityCmPerSecond.X, Is.GreaterThan(80f));
        });
    }

    [Test]
    public void SurfaceVelocity_RejectsNonKinematicBodiesAndZeroVelocity()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 1, actuationCapacity: 1);
        Physics3DShapeId shape = world.RegisterBoxShape(new Vector3(20f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Physics3DBodyContactPolicy.SurfaceVelocity(Vector3.Zero));
        Assert.Throws<ArgumentException>(() => world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            shape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.SurfaceVelocity(Vector3.UnitX))));
        Assert.Throws<ArgumentException>(() => world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            Vector3.Zero,
            1f,
            Physics3DBodyContactPolicy.SurfaceVelocity(Vector3.UnitX))));
    }

    [Test]
    public void ForceFields_UseRelativeWindAndMakeLightBodyAccelerateMoreThanHeavyBody()
    {
        using var world = CreateWorld(mobileCapacity: 2, staticCapacity: 0, actuationCapacity: 4);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId light = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            new Vector3(-20f, 0f, 0f),
            1f));
        Physics3DBodyId heavy = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            new Vector3(20f, 0f, 0f),
            10f));
        var awake = new Physics3DAwakeBodyBuffer(2);
        var fields = new Physics3DForceFieldSet(fieldCapacity: 4, awakeBodyCapacity: 2);
        fields.Add(new Physics3DBoxWindField(
            Vector3.Zero,
            new Vector3(200f),
            Quaternion.Identity,
            new Vector3(120f, 0f, 0f),
            forcePerRelativeSpeed: 2f));
        fields.Add(new Physics3DRadialForceField(
            Vector3.Zero,
            100f,
            forceAtCenter: 30f,
            centerDirection: Vector3.UnitY,
            linearFalloff: true));

        world.CopyAwakeBodies(awake);
        fields.Apply(awake, world);
        Assert.That(world.PendingActuationCommandCount, Is.EqualTo(2));
        world.Step();

        float lightSpeed = world.GetBodyState(light).LinearVelocityCmPerSecond.Length();
        float heavySpeed = world.GetBodyState(heavy).LinearVelocityCmPerSecond.Length();
        Assert.That(lightSpeed, Is.GreaterThan(heavySpeed * 5f));
    }

    [Test]
    public void GustAndVortex_UseFixedTickEnvelopeAndRelativeWind()
    {
        using var world = CreateWorld(mobileCapacity: 2, staticCapacity: 0, actuationCapacity: 4);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId gustBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            new Vector3(-100f, 0f, 0f),
            1f));
        Physics3DBodyId vortexBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            new Vector3(140f, 0f, 0f),
            1f));
        var awake = new Physics3DAwakeBodyBuffer(2);
        var fields = new Physics3DForceFieldSet(fieldCapacity: 2, awakeBodyCapacity: 2);
        fields.Add(new Physics3DBoxGustField(
            new Vector3(-100f, 0f, 0f),
            new Vector3(100f),
            Quaternion.Identity,
            Vector3.Zero,
            new Vector3(120f, 0f, 0f),
            forcePerRelativeSpeed: 3f,
            attackTicks: 2,
            holdTicks: 1,
            releaseTicks: 2,
            calmTicks: 1,
            phaseOffsetTicks: 2));
        fields.Add(new Physics3DVortexWindField(
            new Vector3(100f, 0f, 0f),
            radiusCm: 80f,
            axis: Vector3.UnitY,
            tangentialSpeedCmPerSecond: 100f,
            axialSpeedCmPerSecond: 50f,
            forcePerRelativeSpeed: 2f,
            linearFalloff: false));

        world.CopyAwakeBodies(awake);
        fields.Apply(awake, world);
        world.Step();

        Vector3 gustVelocity = world.GetBodyState(gustBody).LinearVelocityCmPerSecond;
        Vector3 vortexVelocity = world.GetBodyState(vortexBody).LinearVelocityCmPerSecond;
        Assert.Multiple(() =>
        {
            Assert.That(gustVelocity.X, Is.GreaterThan(5f));
            Assert.That(MathF.Abs(gustVelocity.Y), Is.LessThan(0.001f));
            Assert.That(vortexVelocity.Y, Is.GreaterThan(1f));
            Assert.That(vortexVelocity.Z, Is.LessThan(-1f));
        });
    }

    [Test]
    public void GustEnvelope_RepeatsFromAuthoritativeStepIndex()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 0, actuationCapacity: 1);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            Vector3.Zero,
            1f));
        var awake = new Physics3DAwakeBodyBuffer(1);
        var fields = new Physics3DForceFieldSet(fieldCapacity: 1, awakeBodyCapacity: 1);
        fields.Add(new Physics3DBoxGustField(
            Vector3.Zero,
            new Vector3(100f),
            Quaternion.Identity,
            Vector3.Zero,
            new Vector3(120f, 0f, 0f),
            forcePerRelativeSpeed: 3f,
            attackTicks: 2,
            holdTicks: 1,
            releaseTicks: 2,
            calmTicks: 1));
        var speeds = new float[7];

        for (int tick = 0; tick < speeds.Length; tick++)
        {
            world.SetBodyVelocity(body, Vector3.Zero, Vector3.Zero);
            world.SetBodyAwake(body, true);
            world.CopyAwakeBodies(awake);
            fields.Apply(awake, world);
            world.Step();
            speeds[tick] = world.GetBodyState(body).LinearVelocityCmPerSecond.X;
        }

        Assert.Multiple(() =>
        {
            Assert.That(speeds[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(speeds[1], Is.GreaterThan(1f));
            Assert.That(speeds[2], Is.GreaterThan(speeds[1] * 1.8f));
            Assert.That(speeds[3], Is.EqualTo(speeds[1]).Within(0.01f));
            Assert.That(speeds[4], Is.EqualTo(0f).Within(0.001f));
            Assert.That(speeds[5], Is.EqualTo(0f).Within(0.001f));
            Assert.That(speeds[6], Is.EqualTo(0f).Within(0.001f));
        });
    }

    [Test]
    public void ForceFieldCapacityFailure_PrevalidatesWholeActuationBatchAndKeepsPointBurst()
    {
        using var world = CreateWorld(mobileCapacity: 2, staticCapacity: 0, actuationCapacity: 1);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(-10f, 0f, 0f), 1f));
        world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, shape, new Vector3(10f, 0f, 0f), 1f));
        var awake = new Physics3DAwakeBodyBuffer(2);
        var fields = new Physics3DForceFieldSet(1, 2);
        fields.Add(new Physics3DPointBurst(Vector3.Zero, 100f, 20f, Vector3.UnitY, linearFalloff: false));
        world.CopyAwakeBodies(awake);

        Assert.Throws<Physics3DCapacityExceededException>(() => fields.Apply(awake, world));
        Assert.Multiple(() =>
        {
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
            Assert.That(fields.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void RayAndShapeAllHits_AreDeterministicallySortedForEqualDistances()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 3, actuationCapacity: 1);
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        var bodies = new Physics3DBodyId[3];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = world.CreateBody(CreateBody(
                Physics3DBodyKind.Static,
                box,
                new Vector3(100f, 0f, 0f),
                0f));
        }

        Span<Physics3DRaycastHit> rayHits = stackalloc Physics3DRaycastHit[3];
        Span<Physics3DShapeCastHit> shapeHits = stackalloc Physics3DShapeCastHit[3];
        int rayCount = world.Raycast(Vector3.Zero, Vector3.UnitX, 200f, LayerMask.All, rayHits);
        int shapeCount = world.SphereCast(Vector3.Zero, 5f, Vector3.UnitX, 200f, LayerMask.All, shapeHits);
        Assert.Multiple(() =>
        {
            Assert.That(rayCount, Is.EqualTo(3));
            Assert.That(shapeCount, Is.EqualTo(3));
        });

        for (int i = 0; i < bodies.Length; i++)
        {
            Physics3DBodyId rayBody = rayHits[i].Body;
            Physics3DBodyId shapeBody = shapeHits[i].Body;
            Physics3DBodyId expectedBody = bodies[i];
            Assert.Multiple(() =>
            {
                Assert.That(rayBody, Is.EqualTo(expectedBody));
                Assert.That(shapeBody, Is.EqualTo(expectedBody));
            });
        }
    }

    [Test]
    public void ShapeClosestBatches_UsePerRequestFiltersPrevalidateAndRemainZeroGc()
    {
        using var world = CreateWorld(mobileCapacity: 1, staticCapacity: 3, actuationCapacity: 1);
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId near = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f),
            0f));
        Physics3DBodyId far = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f),
            0f));
        Physics3DBodyId above = world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(0f, 300f, 0f),
            0f));
        var boxRequests = new[]
        {
            new Physics3DBoxCastQuery(Vector3.Zero, new Vector3(5f), Quaternion.Identity, Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All, near)),
            new Physics3DBoxCastQuery(Vector3.Zero, new Vector3(5f), Quaternion.Identity, Vector3.UnitY, 500f, new Physics3DQueryFilter(LayerMask.All, above))
        };
        var sphereRequests = new[]
        {
            new Physics3DSphereCastQuery(Vector3.Zero, 5f, Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All, near)),
            new Physics3DSphereCastQuery(Vector3.Zero, 5f, -Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All))
        };
        var capsuleRequests = new[]
        {
            new Physics3DCapsuleCastQuery(Vector3.Zero, 5f, 10f, Quaternion.Identity, Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All, near)),
            new Physics3DCapsuleCastQuery(Vector3.Zero, 5f, 10f, Quaternion.Identity, -Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All))
        };
        var boxResults = new Physics3DBatchedShapeCastClosestResult[2];
        var sphereResults = new Physics3DBatchedShapeCastClosestResult[2];
        var capsuleResults = new Physics3DBatchedShapeCastClosestResult[2];

        world.BoxCastClosestBatch(boxRequests, boxResults);
        world.SphereCastClosestBatch(sphereRequests, sphereResults);
        world.CapsuleCastClosestBatch(capsuleRequests, capsuleResults);
        Assert.Multiple(() =>
        {
            Assert.That(boxResults[0].Hit, Is.True);
            Assert.That(boxResults[0].Value.Body, Is.EqualTo(far));
            Assert.That(boxResults[1].Hit, Is.False);
            Assert.That(sphereResults[0].Value.Body, Is.EqualTo(far));
            Assert.That(sphereResults[1].Hit, Is.False);
            Assert.That(capsuleResults[0].Value.Body, Is.EqualTo(far));
            Assert.That(capsuleResults[1].Hit, Is.False);
        });

        Physics3DBatchedShapeCastClosestResult prior = boxResults[0];
        var invalid = new[]
        {
            boxRequests[0],
            new Physics3DBoxCastQuery(Vector3.Zero, new Vector3(5f), Quaternion.Identity, Vector3.Zero, 500f, new Physics3DQueryFilter(LayerMask.All))
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => world.BoxCastClosestBatch(invalid, boxResults));
        Assert.Multiple(() =>
        {
            Assert.That(boxResults[0].Hit, Is.EqualTo(prior.Hit));
            Assert.That(boxResults[0].Value.Body, Is.EqualTo(prior.Value.Body));
        });

        for (int i = 0; i < 32; i++)
        {
            world.BoxCastClosestBatch(boxRequests, boxResults);
            world.SphereCastClosestBatch(sphereRequests, sphereResults);
            world.CapsuleCastClosestBatch(capsuleRequests, capsuleResults);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
        {
            world.BoxCastClosestBatch(boxRequests, boxResults);
            world.SphereCastClosestBatch(sphereRequests, sphereResults);
            world.CapsuleCastClosestBatch(capsuleRequests, capsuleResults);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed shape closest batches allocated {allocated} managed bytes.");
    }

    [Test]
    public void CollisionSubgroups_FilterAdjacentAssemblyBodiesKeepRecipePairsAndRemainZeroGc()
    {
        const uint group0 = 1u << 0;
        const uint group1 = 1u << 1;
        const uint group2 = 1u << 2;
        using (var world = CreateWorld(mobileCapacity: 3, staticCapacity: 1, actuationCapacity: 1))
        {
            Physics3DShapeId shape = world.RegisterSphereShape(10f);
            Physics3DBodyId body0 = world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                shape,
                Vector3.Zero,
                1f,
                collisionSubgroup: new Physics3DCollisionSubgroup(assemblyId: 7, subgroupIndex: 0, collidesWithSubgroups: group2)));
            Physics3DBodyId body1 = world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                shape,
                Vector3.Zero,
                1f,
                collisionSubgroup: new Physics3DCollisionSubgroup(assemblyId: 7, subgroupIndex: 1, collidesWithSubgroups: group2)));
            Physics3DBodyId body2 = world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                shape,
                Vector3.Zero,
                1f,
                collisionSubgroup: new Physics3DCollisionSubgroup(assemblyId: 7, subgroupIndex: 2, collidesWithSubgroups: group0 | group1)));
            Physics3DBodyId external = world.CreateBody(CreateBody(
                Physics3DBodyKind.Static,
                shape,
                new Vector3(100f, 0f, 0f),
                0f));

            world.Step();
            Span<Physics3DContactPair> pairs = stackalloc Physics3DContactPair[3];
            int count = world.CopyContactPairs(pairs);
            Assert.That(count, Is.EqualTo(2));
            for (int i = 0; i < count; i++)
            {
                bool adjacentPair =
                    (pairs[i].BodyA == body0 && pairs[i].BodyB == body1) ||
                    (pairs[i].BodyA == body1 && pairs[i].BodyB == body0);
                Assert.That(adjacentPair, Is.False);
                Assert.That(pairs[i].BodyA == body2 || pairs[i].BodyB == body2, Is.True);
            }

            var ignoreAssembly = new Physics3DQueryFilter(
                LayerMask.All,
                ignoredBody: default,
                includeSensors: false,
                ignoredAssemblyId: 7);
            Assert.That(world.RaycastClosest(
                new Vector3(-100f, 0f, 0f),
                Vector3.UnitX,
                300f,
                ignoreAssembly,
                out Physics3DRaycastHit hit), Is.True);
            Assert.That(hit.Body, Is.EqualTo(external));
        }

        Physics3DCapacityExceededException capacity = Assert.Throws<Physics3DCapacityExceededException>(() =>
            _ = new Physics3DCollisionSubgroup(assemblyId: 1, subgroupIndex: 32, collidesWithSubgroups: uint.MaxValue))!;
        Assert.That(capacity.Capacity, Is.EqualTo(32));

        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: 2,
            staticCapacity: 0,
            workerCount: 2,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        using var allocationWorld = new Physics3DWorld(config, dispatcher);
        Physics3DShapeId allocationShape = allocationWorld.RegisterSphereShape(10f);
        Physics3DBodyId deniedA = allocationWorld.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            allocationShape,
            Vector3.Zero,
            1f,
            collisionSubgroup: new Physics3DCollisionSubgroup(9, 0, collidesWithSubgroups: 0)));
        Physics3DBodyId deniedB = allocationWorld.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            allocationShape,
            Vector3.Zero,
            1f,
            collisionSubgroup: new Physics3DCollisionSubgroup(9, 1, collidesWithSubgroups: 0)));
        for (int i = 0; i < 64; i++)
        {
            ResetOverlapping(allocationWorld, deniedA, deniedB);
            allocationWorld.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long backgroundBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int i = 0; i < 128; i++)
        {
            ResetOverlapping(allocationWorld, deniedA, deniedB);
            allocationWorld.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long backgroundAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundBefore;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero);
            Assert.That(backgroundAllocated, Is.Zero);
            Assert.That(allocationWorld.ContactPairCount, Is.Zero);
        });
    }

    [Test]
    public void WarmedSensorPlatformForceFieldAndSortedQueries_HaveZeroManagedAllocations()
    {
        const int bodyCount = 8;
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            bodyCount,
            staticCapacity: 1,
            workerCount: 2,
            actuationCommandCapacity: bodyCount,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        using var world = new Physics3DWorld(config, dispatcher);
        Physics3DShapeId sensorShape = world.RegisterBoxShape(new Vector3(2_000f, 100f, 2_000f));
        Physics3DShapeId bodyShape = world.RegisterSphereShape(5f);
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            sensorShape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.Sensor()));
        for (int i = 0; i < bodyCount; i++)
        {
            world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                bodyShape,
                new Vector3(i * 20f, 0f, 0f),
                1f));
        }

        var awake = new Physics3DAwakeBodyBuffer(bodyCount);
        var fields = new Physics3DForceFieldSet(1, bodyCount);
        fields.Add(new Physics3DSphereWindField(Vector3.Zero, 1_000f, new Vector3(5f, 0f, 0f), 1f));
        var rayHits = new Physics3DRaycastHit[bodyCount + 1];
        var includeSensors = new Physics3DQueryFilter(LayerMask.All, default, includeSensors: true);
        for (int i = 0; i < 64; i++)
        {
            world.CopyAwakeBodies(awake);
            fields.Apply(awake, world);
            world.Step();
            world.Raycast(new Vector3(-100f, 0f, 0f), Vector3.UnitX, 1_000f, includeSensors, rayHits);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long backgroundBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int i = 0; i < 128; i++)
        {
            world.CopyAwakeBodies(awake);
            fields.Apply(awake, world);
            world.Step();
            world.Raycast(new Vector3(-100f, 0f, 0f), Vector3.UnitX, 1_000f, includeSensors, rayHits);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long backgroundAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundBefore;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, $"Warmed contact, field, and sorted query path allocated {allocated} managed bytes.");
            Assert.That(backgroundAllocated, Is.Zero, $"Warmed contact workers allocated {backgroundAllocated} managed bytes.");
        });
    }

    [Test]
    public void WarmedSurfaceVelocityContact_HasZeroManagedAllocationsOnAllThreads()
    {
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: 2,
            staticCapacity: 0,
            workerCount: 2,
            actuationCommandCapacity: 1,
            gravityCmPerSecondSquared: new Vector3(0f, -981f, 0f),
            linearDamping: 0f,
            angularDamping: 0f);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        using var world = new Physics3DWorld(config, dispatcher);
        Physics3DShapeId conveyorShape = world.RegisterBoxShape(new Vector3(400f, 20f, 200f));
        Physics3DShapeId crateShape = world.RegisterBoxShape(new Vector3(20f));
        world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            conveyorShape,
            Vector3.Zero,
            0f,
            Physics3DBodyContactPolicy.SurfaceVelocity(new Vector3(120f, 0f, 0f))));
        Physics3DBodyId crate = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            crateShape,
            new Vector3(0f, 25f, 0f),
            1f));

        void ResetAndStep()
        {
            world.SetBodyState(crate, new Physics3DBodyState
            {
                PositionCm = new Vector3(0f, 25f, 0f),
                Orientation = Quaternion.Identity,
                Awake = true
            });
            world.Step();
        }

        for (int i = 0; i < 64; i++)
        {
            ResetAndStep();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long backgroundBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int i = 0; i < 128; i++)
        {
            ResetAndStep();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long backgroundAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundBefore;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, $"Warmed surface velocity path allocated {allocated} managed bytes.");
            Assert.That(backgroundAllocated, Is.Zero, $"Warmed surface velocity workers allocated {backgroundAllocated} managed bytes.");
        });
    }

    private static void AssertSensorEvent(Physics3DWorld world, Physics3DContactEventKind expectedKind)
    {
        Span<Physics3DContactEvent> events = stackalloc Physics3DContactEvent[4];
        int count = world.CopyContactEvents(events);
        Physics3DContactEvent contactEvent = events[0];
        Assert.That(count, Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(contactEvent.Kind, Is.EqualTo(expectedKind));
            Assert.That(contactEvent.ContactKind, Is.EqualTo(Physics3DContactKind.Sensor));
        });
    }

    private static ulong PairKey(in Physics3DContactEvent contactEvent)
    {
        uint low = unchecked((uint)Math.Min(contactEvent.BodyA.Slot, contactEvent.BodyB.Slot));
        uint high = unchecked((uint)Math.Max(contactEvent.BodyA.Slot, contactEvent.BodyB.Slot));
        return ((ulong)low << 32) | high;
    }

    private static void ResetOverlapping(
        Physics3DWorld world,
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB)
    {
        Physics3DBodyState state = new()
        {
            PositionCm = Vector3.Zero,
            Orientation = Quaternion.Identity,
            Awake = true
        };
        world.SetBodyState(bodyA, state);
        world.SetBodyState(bodyB, state);
    }

    private static Physics3DWorld CreateWorld(
        int mobileCapacity,
        int staticCapacity,
        int actuationCapacity,
        Vector3? gravityCmPerSecondSquared = null)
        => new(Physics3DWorldTests.CreateConfig(
            mobileCapacity,
            staticCapacity,
            workerCount: 1,
            actuationCommandCapacity: actuationCapacity,
            gravityCmPerSecondSquared: gravityCmPerSecondSquared ?? Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f));

    private static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 positionCm,
        float mass,
        Physics3DBodyContactPolicy contactPolicy = default,
        Vector3 linearVelocityCmPerSecond = default,
        Physics3DCollisionSubgroup collisionSubgroup = default)
        => new(
            Entity.Null,
            kind,
            shape,
            positionCm,
            Quaternion.Identity,
            linearVelocityCmPerSecond,
            Vector3.Zero,
            mass,
            LayerMask.All,
            new Physics3DMaterial(0.8f, 200f, 30f, 1f),
            Physics3DContinuousDetectionMode.Passive,
            contactPolicy,
            collisionSubgroup);
}
