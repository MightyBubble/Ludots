using System;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DAdvancedConstraintTests
{
    private static readonly Physics3DSpringSettings Spring = new(30f, 2f);
    private static readonly Physics3DServoSettings Servo = new(10_000f, 0f, 1_000_000f);
    private static readonly Physics3DMotorSettings Motor = new(1_000_000f, 0.001f);

    [Test]
    public void TypedConstraints_CreateExistDestroy_ThroughOneGenerationalStore()
    {
        using Physics3DWorld world = CreateWorld(mobileCapacity: 2, staticCapacity: 0, workerCount: 1);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId bodyA = CreateDynamicBody(world, shape, Vector3.Zero);
        Physics3DBodyId bodyB = CreateDynamicBody(world, shape, new Vector3(100f, 0f, 0f));

        Physics3DConstraintId pointOnLine = world.CreatePointOnLineServoConstraint(
            bodyA,
            bodyB,
            new Physics3DPointOnLineServoDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitX, Servo, Spring));
        AssertLiveThenDestroy(world, pointOnLine);

        Physics3DConstraintId linearServo = world.CreateLinearAxisServoConstraint(
            bodyA,
            bodyB,
            new Physics3DLinearAxisServoDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitX, 100f, Servo, Spring));
        AssertLiveThenDestroy(world, linearServo);

        Physics3DConstraintId linearLimit = world.CreateLinearAxisLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DLinearAxisLimitDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitX, 80f, 120f, Spring));
        AssertLiveThenDestroy(world, linearLimit);

        Physics3DConstraintId angularHinge = world.CreateAngularHingeConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularHingeDescription(Vector3.UnitX, Vector3.UnitX, Spring));
        AssertLiveThenDestroy(world, angularHinge);

        Physics3DConstraintId angularAxisMotor = world.CreateAngularAxisMotorConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitX, 2f, Motor));
        AssertLiveThenDestroy(world, angularAxisMotor);

        Physics3DConstraintId swingLimit = world.CreateSwingLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DSwingLimitDescription(Vector3.UnitY, Vector3.UnitY, MathF.PI * 0.5f, Spring));
        AssertLiveThenDestroy(world, swingLimit);

        Physics3DConstraintId twistLimit = world.CreateTwistLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DTwistLimitDescription(Quaternion.Identity, Quaternion.Identity, -0.5f, 0.5f, Spring));
        AssertLiveThenDestroy(world, twistLimit);

        Physics3DConstraintId angularMotor = world.CreateAngularMotorConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularMotorDescription(new Vector3(1f, 2f, 3f), Motor));
        AssertLiveThenDestroy(world, angularMotor);

        Physics3DConstraintId angularServo = world.CreateAngularServoConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularServoDescription(Quaternion.Identity, Servo, Spring));
        AssertLiveThenDestroy(world, angularServo);
    }

    [Test]
    public void RuntimeTargets_UpdateLinearServoAxisMotorAndAngularServoBehavior()
    {
        using Physics3DWorld world = CreateWorld(mobileCapacity: 6, staticCapacity: 0, workerCount: 1);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId linearA = CreateDynamicBody(world, shape, new Vector3(0f, 0f, 0f));
        Physics3DBodyId linearB = CreateDynamicBody(world, shape, new Vector3(0f, 100f, 0f));
        Physics3DBodyId motorA = CreateDynamicBody(world, shape, new Vector3(1_000f, 0f, 0f));
        Physics3DBodyId motorB = CreateDynamicBody(world, shape, new Vector3(1_000f, 100f, 0f));
        Physics3DBodyId servoA = CreateDynamicBody(world, shape, new Vector3(2_000f, 0f, 0f));
        Physics3DBodyId servoB = CreateDynamicBody(world, shape, new Vector3(2_000f, 100f, 0f));

        Physics3DConstraintId linear = world.CreateLinearAxisServoConstraint(
            linearA,
            linearB,
            new Physics3DLinearAxisServoDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitY, 100f, Servo, Spring));
        Physics3DConstraintId motor = world.CreateAngularAxisMotorConstraint(
            motorA,
            motorB,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitX, 0f, Motor));
        Physics3DConstraintId servo = world.CreateAngularServoConstraint(
            servoA,
            servoB,
            new Physics3DAngularServoDescription(Quaternion.Identity, Servo, Spring));

        world.UpdateLinearAxisServoTarget(linear, 25f);
        world.UpdateAngularAxisMotorTarget(motor, 4f);
        world.UpdateAngularServoTarget(servo, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f));
        for (int i = 0; i < 10; i++)
        {
            world.Step();
        }

        Physics3DBodyState linearStateA = world.GetBodyState(linearA);
        Physics3DBodyState linearStateB = world.GetBodyState(linearB);
        Physics3DBodyState motorStateA = world.GetBodyState(motorA);
        Physics3DBodyState motorStateB = world.GetBodyState(motorB);
        Physics3DBodyState servoStateA = world.GetBodyState(servoA);
        Physics3DBodyState servoStateB = world.GetBodyState(servoB);
        float linearSeparation = linearStateB.PositionCm.Y - linearStateA.PositionCm.Y;
        float motorRelativeSpeed = motorStateA.AngularVelocityRadiansPerSecond.X - motorStateB.AngularVelocityRadiansPerSecond.X;
        float servoOrientationDot = MathF.Abs(Quaternion.Dot(servoStateA.Orientation, servoStateB.Orientation));

        Assert.Multiple(() =>
        {
            Assert.That(linearSeparation, Is.LessThan(90f), "Updated suspension target should pull the anchors toward the new offset.");
            Assert.That(motorRelativeSpeed, Is.GreaterThan(1f), "Updated wheel motor target should create relative angular speed.");
            Assert.That(servoOrientationDot, Is.LessThan(0.999f), "Updated pose target should create a relative rotation.");
        });
    }

    [Test]
    public void InvalidDescriptionsStaticBodiesAndWrongUpdateTypes_FailExplicitly()
    {
        using Physics3DWorld world = CreateWorld(mobileCapacity: 2, staticCapacity: 1, workerCount: 1);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId bodyA = CreateDynamicBody(world, shape, Vector3.Zero);
        Physics3DBodyId bodyB = CreateDynamicBody(world, shape, new Vector3(100f, 0f, 0f));
        Physics3DBodyId staticBody = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            shape,
            new Vector3(200f, 0f, 0f),
            LayerMask.None));

        Assert.Throws<InvalidOperationException>(() => world.CreateAngularHingeConstraint(
            bodyA,
            staticBody,
            new Physics3DAngularHingeDescription(Vector3.UnitX, Vector3.UnitX, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreatePointOnLineServoConstraint(
            bodyA,
            bodyB,
            new Physics3DPointOnLineServoDescription(Vector3.Zero, Vector3.Zero, Vector3.Zero, Servo, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateLinearAxisServoConstraint(
            bodyA,
            bodyB,
            new Physics3DLinearAxisServoDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitX, float.NaN, Servo, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateLinearAxisLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DLinearAxisLimitDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitX, 10f, -10f, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateSwingLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DSwingLimitDescription(Vector3.UnitX, Vector3.UnitX, MathF.PI + 0.1f, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateTwistLimitConstraint(
            bodyA,
            bodyB,
            new Physics3DTwistLimitDescription(Quaternion.Identity, Quaternion.Identity, 0.5f, -0.5f, Spring)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CreateAngularMotorConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularMotorDescription(Vector3.Zero, new Physics3DMotorSettings(-1f, 0f))));

        Physics3DConstraintId hinge = world.CreateAngularHingeConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularHingeDescription(Vector3.UnitX, Vector3.UnitX, Spring));
        Assert.Throws<InvalidOperationException>(() => world.UpdateAngularAxisMotorTarget(hinge, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.UpdateAngularServoTarget(hinge, default));
    }

    [Test]
    public void ConstraintCreationAndTargetUpdates_AreRejectedDuringSimulationStep()
    {
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: 2,
            staticCapacity: 0,
            workerCount: 1,
            gravityCmPerSecondSquared: Vector3.Zero);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        var timestepper = new TrackingTimestepper();
        using var world = new Physics3DWorld(config, dispatcher, timestepper);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId bodyA = CreateDynamicBody(world, shape, Vector3.Zero);
        Physics3DBodyId bodyB = CreateDynamicBody(world, shape, new Vector3(100f, 0f, 0f));
        Physics3DConstraintId motor = world.CreateAngularAxisMotorConstraint(
            bodyA,
            bodyB,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitX, 0f, Motor));
        Exception? createFailure = null;
        Exception? updateFailure = null;
        timestepper.BeforeCollisionDetection += (_, _) =>
        {
            try
            {
                world.CreateAngularHingeConstraint(
                    bodyA,
                    bodyB,
                    new Physics3DAngularHingeDescription(Vector3.UnitX, Vector3.UnitX, Spring));
            }
            catch (Exception exception)
            {
                createFailure = exception;
            }

            try
            {
                world.UpdateAngularAxisMotorTarget(motor, 2f);
            }
            catch (Exception exception)
            {
                updateFailure = exception;
            }
        };

        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(createFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(updateFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(world.ActiveConstraintCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void WarmedTargetUpdatesAndFixedStep_HaveZeroManagedAllocationsOnAllThreads()
    {
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: 6,
            staticCapacity: 0,
            workerCount: 2,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        using var world = new Physics3DWorld(config, dispatcher);
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        Physics3DBodyId linearA = CreateDynamicBody(world, shape, new Vector3(0f, 0f, 0f));
        Physics3DBodyId linearB = CreateDynamicBody(world, shape, new Vector3(0f, 100f, 0f));
        Physics3DBodyId motorA = CreateDynamicBody(world, shape, new Vector3(1_000f, 0f, 0f));
        Physics3DBodyId motorB = CreateDynamicBody(world, shape, new Vector3(1_000f, 100f, 0f));
        Physics3DBodyId servoA = CreateDynamicBody(world, shape, new Vector3(2_000f, 0f, 0f));
        Physics3DBodyId servoB = CreateDynamicBody(world, shape, new Vector3(2_000f, 100f, 0f));
        Physics3DConstraintId linear = world.CreateLinearAxisServoConstraint(
            linearA,
            linearB,
            new Physics3DLinearAxisServoDescription(Vector3.Zero, Vector3.Zero, Vector3.UnitY, 100f, Servo, Spring));
        Physics3DConstraintId motor = world.CreateAngularAxisMotorConstraint(
            motorA,
            motorB,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitX, 0f, Motor));
        Physics3DConstraintId servo = world.CreateAngularServoConstraint(
            servoA,
            servoB,
            new Physics3DAngularServoDescription(Quaternion.Identity, Servo, Spring));
        Quaternion targetA = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.25f);
        Quaternion targetB = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f);

        for (int i = 0; i < 60; i++)
        {
            UpdateTargetsAndStep(world, linear, motor, servo, targetA, targetB, i);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long backgroundBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        for (int i = 0; i < 120; i++)
        {
            UpdateTargetsAndStep(world, linear, motor, servo, targetA, targetB, i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long backgroundAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundBefore;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, $"Advanced constraint target updates allocated {allocated} managed bytes after warmup.");
            Assert.That(backgroundAllocated, Is.Zero, $"Advanced constraint solving allocated {backgroundAllocated} managed bytes on workers after warmup.");
        });
    }

    private static void UpdateTargetsAndStep(
        Physics3DWorld world,
        Physics3DConstraintId linear,
        Physics3DConstraintId motor,
        Physics3DConstraintId servo,
        Quaternion targetA,
        Quaternion targetB,
        int index)
    {
        bool alternate = (index & 1) == 0;
        world.UpdateLinearAxisServoTarget(linear, alternate ? 75f : 125f);
        world.UpdateAngularAxisMotorTarget(motor, alternate ? 2f : -2f);
        world.UpdateAngularServoTarget(servo, alternate ? targetA : targetB);
        world.Step();
    }

    private static void AssertLiveThenDestroy(Physics3DWorld world, Physics3DConstraintId constraint)
    {
        Assert.Multiple(() =>
        {
            Assert.That(world.ContainsConstraint(constraint), Is.True);
            Assert.That(world.ActiveConstraintCount, Is.EqualTo(1));
        });
        world.DestroyConstraint(constraint);
        Assert.Multiple(() =>
        {
            Assert.That(world.ContainsConstraint(constraint), Is.False);
            Assert.That(world.ActiveConstraintCount, Is.Zero);
        });
    }

    private static Physics3DWorld CreateWorld(int mobileCapacity, int staticCapacity, int workerCount)
    {
        return new Physics3DWorld(Physics3DWorldTests.CreateConfig(
            mobileCapacity,
            staticCapacity,
            workerCount: workerCount,
            gravityCmPerSecondSquared: Vector3.Zero,
            linearDamping: 0f,
            angularDamping: 0f));
    }

    private static Physics3DBodyId CreateDynamicBody(
        Physics3DWorld world,
        Physics3DShapeId shape,
        Vector3 positionCm)
    {
        return world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            positionCm,
            LayerMask.None));
    }
}
