using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Character3DTests
{
    [Test]
    public void GroundMoveJumpAndRotatingPlatformVelocity_UseFormalPhysicsCommands()
    {
        using var world = CreateWorld(mobileCapacity: 3, staticCapacity: 0);
        Physics3DShapeId platformShape = world.RegisterBoxShape(new Vector3(500f, 20f, 500f));
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DBodyId platform = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            platformShape,
            new Vector3(0f, -10f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 82f, 0f)));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, CreateProfile());

        world.SetKinematicNextPose(platform, new Vector3(10f, -10f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.05f));
        characters.SubmitIntent(character, new Character3DIntent(Vector2.Zero, jumpRequested: false));
        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();

        Character3DState supported = characters.GetState(character);
        Assert.Multiple(() =>
        {
            Assert.That(supported.IsGrounded, Is.True);
            Assert.That(supported.SupportBody, Is.EqualTo(platform));
            Assert.That(supported.SupportVelocityCmPerSecond.X, Is.EqualTo(300f).Within(3f));
            Assert.That(supported.LinearVelocityCmPerSecond.X, Is.GreaterThan(100f));
        });

        world.SetKinematicNextPose(platform, new Vector3(20f, -10f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f));
        characters.SubmitIntent(character, new Character3DIntent(Vector2.UnitX, jumpRequested: true));
        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();

        Character3DState jumped = characters.GetState(character);
        Assert.Multiple(() =>
        {
            Assert.That(jumped.LocomotionMode, Is.EqualTo(Character3DLocomotionMode.Airborne));
            Assert.That(jumped.LinearVelocityCmPerSecond.Y, Is.GreaterThan(300f));
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
        });
    }

    [TestCase(30f, true)]
    [TestCase(65f, false)]
    public void SupportProbe_FiltersGroundByConfiguredSlope(float slopeDegrees, bool expectedGrounded)
    {
        using var world = CreateWorld(mobileCapacity: 2, staticCapacity: 1);
        Physics3DShapeId slopeShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        float slopeRadians = slopeDegrees * (MathF.PI / 180f);
        world.CreateBody(CreateOrientedBody(
            Physics3DBodyKind.Static,
            slopeShape,
            new Vector3(0f, -10f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, slopeRadians)));
        Physics3DBodyId anchor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 40f + (45f / MathF.Cos(slopeRadians)), 0f)));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, CreateProfile());

        StepCharacter(world, characters, character, new Character3DIntent(Vector2.Zero, false));

        Assert.That(characters.GetState(character).IsGrounded, Is.EqualTo(expectedGrounded));
    }

    [Test]
    public void WalkableStep_ActivatesStepAssistAndProducesUpwardVelocity()
    {
        using var world = CreateWorld(mobileCapacity: 2, staticCapacity: 2);
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId stepShape = world.RegisterBoxShape(new Vector3(20f, 30f, 200f));
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, stepShape, new Vector3(60f, 15f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId body = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 82f, 0f)));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, CreateProfile());

        StepCharacter(world, characters, character, new Character3DIntent(Vector2.UnitX, false));
        Character3DState state = characters.GetState(character);

        Assert.Multiple(() =>
        {
            Assert.That(state.StepAssistActive, Is.True);
            Assert.That(state.LinearVelocityCmPerSecond.Y, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void MissingIntentAndCapacityExhaustion_FailBeforePartialActuation()
    {
        using var world = CreateWorld(mobileCapacity: 4, staticCapacity: 0);
        Physics3DShapeId capsule = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DBodyId anchor = world.CreateBody(CreateBody(Physics3DBodyKind.Kinematic, anchorShape, new Vector3(0f, -1_000f, 0f)));
        Physics3DBodyId first = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, capsule, new Vector3(0f, 500f, 0f)));
        Physics3DBodyId second = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, capsule, new Vector3(200f, 500f, 0f)));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 4);
        Character3DHandle character = characters.Register(first, anchor, CreateProfile());

        Assert.Throws<Character3DCapacityExceededException>(() => characters.Register(second, anchor, CreateProfile()));
        InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(() => characters.PrepareFixedStep());
        Assert.Multiple(() =>
        {
            Assert.That(missing!.Message, Does.Contain("no intent"));
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
            Assert.That(characters.ActiveCount, Is.EqualTo(1));
        });

        characters.SubmitIntent(character, new Character3DIntent(Vector2.Zero, false));
        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();
    }

    [Test]
    public void SharedActuationCapacityExhaustion_PreservesExistingCommandsAndEveryCharacterIntent()
    {
        using var world = CreateWorld(
            mobileCapacity: 3,
            staticCapacity: 0,
            actuationCommandCapacity: 2);
        Physics3DShapeId capsule = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DBodyId anchor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -1_000f, 0f)));
        Physics3DBodyId firstBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            capsule,
            new Vector3(0f, 500f, 0f)));
        Physics3DBodyId secondBody = world.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            capsule,
            new Vector3(200f, 500f, 0f)));
        var characters = new Character3DControllerSet(world, capacity: 2, overlapHitCapacity: 4);
        Character3DHandle first = characters.Register(firstBody, anchor, CreateProfile());
        Character3DHandle second = characters.Register(secondBody, anchor, CreateProfile());

        world.EnqueueForce(firstBody, Vector3.UnitY);
        characters.SubmitIntent(first, new Character3DIntent(Vector2.UnitX, jumpRequested: false));
        characters.SubmitIntent(second, new Character3DIntent(Vector2.UnitX, jumpRequested: false));

        Physics3DCapacityExceededException exception = Assert.Throws<Physics3DCapacityExceededException>(
            () => characters.PrepareFixedStep())!;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Resource, Is.EqualTo("actuation commands"));
            Assert.That(exception.Capacity, Is.EqualTo(2));
            Assert.That(world.PendingActuationCommandCount, Is.EqualTo(1));
            Assert.That(world.StepIndex, Is.Zero);
        });

        world.ClearActuationCommands();
        characters.PrepareFixedStep();

        Assert.That(world.PendingActuationCommandCount, Is.EqualTo(2));
        world.Step();
        characters.ObserveFixedStep();
    }

    [Test]
    public void WarmedCharacterBatch_HasZeroManagedAllocations()
    {
        using var world = CreateWorld(mobileCapacity: 3, staticCapacity: 1, workerCount: 1);
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(CreateBody(Physics3DBodyKind.Kinematic, anchorShape, new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId body = world.CreateBody(CreateBody(Physics3DBodyKind.Dynamic, capsuleShape, new Vector3(0f, 82f, 0f)));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, CreateProfile());

        for (int i = 0; i < 60; i++)
        {
            StepCharacter(world, characters, character, new Character3DIntent(new Vector2(0.25f, 0f), false));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            StepCharacter(world, characters, character, new Character3DIntent(new Vector2(0.25f, 0f), false));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed Character3D fixed steps allocated {allocated} managed bytes.");
    }

    [Test]
    [Category("scale")]
    public void OneHundredFiftyCharacters_ThirtyHzBatchRemainsFiniteAndAllocationFree()
    {
        const int characterCount = 150;
        using var world = CreateWorld(
            mobileCapacity: characterCount + 1,
            staticCapacity: 1,
            workerCount: 1,
            contactPairCapacityPerWorker: 512);
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(4_000f, 20f, 4_000f));
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        world.CreateBody(CreateBody(Physics3DBodyKind.Static, floorShape, new Vector3(0f, -10f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        var characters = new Character3DControllerSet(world, characterCount, overlapHitCapacity: 32);
        var handles = new Character3DHandle[characterCount];
        for (int i = 0; i < characterCount; i++)
        {
            int column = i % 15;
            int row = i / 15;
            Physics3DBodyId body = world.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                capsuleShape,
                new Vector3((column - 7) * 180f, 82f, (row - 5) * 180f)));
            handles[i] = characters.Register(body, anchor, CreateProfile());
        }

        var intent = new Character3DIntent(new Vector2(0.2f, 0f), false);
        for (int step = 0; step < 60; step++)
        {
            StepCharacterBatch(world, characters, handles, intent);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 120; step++)
        {
            StepCharacterBatch(world, characters, handles, intent);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Character3DState first = characters.GetState(handles[0]);
        Character3DState last = characters.GetState(handles[^1]);
        Assert.Multiple(() =>
        {
            Assert.That(characters.ActiveCount, Is.EqualTo(characterCount));
            Assert.That(allocated, Is.Zero, $"Warmed 150-character batch allocated {allocated} managed bytes.");
            Assert.That(
                float.IsFinite(first.PositionCm.X) &&
                float.IsFinite(first.PositionCm.Y) &&
                float.IsFinite(first.PositionCm.Z),
                Is.True);
            Assert.That(
                float.IsFinite(last.PositionCm.X) &&
                float.IsFinite(last.PositionCm.Y) &&
                float.IsFinite(last.PositionCm.Z),
                Is.True);
        });
    }

    internal static Character3DProfile CreateProfile()
        => new(
            radiusCm: 30f,
            cylinderLengthCm: 100f,
            maximumGroundSpeedCmPerSecond: 500f,
            maximumGroundAccelerationCmPerSecondSquared: 5_000f,
            maximumAirSpeedCmPerSecond: 400f,
            maximumAirAccelerationCmPerSecondSquared: 1_500f,
            jumpSpeedCmPerSecond: 500f,
            maximumSlopeDegrees: 50f,
            supportProbeDistanceCm: 12f,
            skinWidthCm: 2f,
            maximumStepHeightCm: 40f,
            stepForwardProbeDistanceCm: 45f,
            stepAssistSpeedCmPerSecond: 260f,
            coyoteTicks: 3,
            LayerMask.All,
            new Physics3DServoSettings(maximumSpeed: 20f, baseSpeed: 0f, maximumForce: 100_000f),
            new Physics3DSpringSettings(angularFrequency: 30f, twiceDampingRatio: 1f));

    internal static Physics3DWorld CreateWorld(
        int mobileCapacity,
        int staticCapacity,
        int workerCount = 1,
        int contactPairCapacityPerWorker = 128,
        int? actuationCommandCapacity = null)
        => new(new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticCapacity,
            ShapeCapacity = 16,
            InactiveIslandCapacity = Math.Max(1, mobileCapacity),
            ConstraintCapacity = Math.Max(8, mobileCapacity * 2),
            ConstraintsPerTypeBatchCapacity = Math.Max(8, mobileCapacity * 2),
            ConstraintCountPerBodyEstimate = 4,
            ContactPairCapacityPerWorker = contactPairCapacityPerWorker,
            ActuationCommandCapacity = actuationCommandCapacity ?? Math.Max(16, mobileCapacity * 4),
            WorkerCount = workerCount,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = 2,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
            LinearDamping = 0f,
            AngularDamping = 0f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = 32,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        });

    internal static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 position,
        Physics3DBodyContactPolicy contactPolicy = default)
        => new(
            Entity.Null,
            kind,
            shape,
            position,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            kind == Physics3DBodyKind.Dynamic ? 80f : 0f,
            LayerMask.All,
            new Physics3DMaterial(0.8f, 200f, 30f, 1f),
            Physics3DContinuousDetectionMode.Passive,
            contactPolicy);

    internal static Physics3DBodyDescription CreateOrientedBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 position,
        Quaternion orientation,
        Physics3DBodyContactPolicy contactPolicy = default)
        => new(
            Entity.Null,
            kind,
            shape,
            position,
            orientation,
            Vector3.Zero,
            Vector3.Zero,
            kind == Physics3DBodyKind.Dynamic ? 80f : 0f,
            LayerMask.All,
            new Physics3DMaterial(0.8f, 200f, 30f, 1f),
            Physics3DContinuousDetectionMode.Passive,
            contactPolicy);

    private static void StepCharacter(
        Physics3DWorld world,
        Character3DControllerSet characters,
        Character3DHandle character,
        in Character3DIntent intent)
    {
        characters.SubmitIntent(character, intent);
        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();
    }

    private static void StepCharacterBatch(
        Physics3DWorld world,
        Character3DControllerSet characters,
        Character3DHandle[] handles,
        in Character3DIntent intent)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            characters.SubmitIntent(handles[i], intent);
        }

        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();
    }
}
