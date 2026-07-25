using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Vehicle3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Vehicle3DScaleTests
{
    private const int VehicleCount = 150;
    private const int WheelsPerVehicle = 4;
    private const int WheelCount = VehicleCount * WheelsPerVehicle;
    private const int MobileBodyCount = VehicleCount * (1 + WheelsPerVehicle);
    private const int ConstraintCount = WheelCount * 5;
    private const uint GroundCategory = 1u << 0;
    private const uint VehicleCategory = 1u << 1;
    private static readonly LayerMask GroundBodyLayer = new(GroundCategory, VehicleCategory);
    private static readonly LayerMask VehicleBodyLayer = new(VehicleCategory, GroundCategory);
    private static readonly LayerMask GroundQueryLayer = new(GroundCategory, GroundCategory);

    [TestCase(Vehicle3DWheelKind.Physical)]
    [TestCase(Vehicle3DWheelKind.Box)]
    public void WarmedOneHundredFiftyRealWheelVehicles_AreZeroAllocAndStayInsideThirtyHertz(
        Vehicle3DWheelKind wheelKind)
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            MobileBodyCount,
            staticCapacity: 1,
            ConstraintCount,
            workerCount: 4);
        Physics3DShapeId floorShape = physics.RegisterBoxShape(new Vector3(500_000f, 20f, 500_000f));
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(210f, 60f, 320f));
        Physics3DShapeId wheelShape = wheelKind == Vehicle3DWheelKind.Physical
            ? physics.RegisterCylinderShape(radiusCm: 44f, lengthCm: 34f)
            : physics.RegisterBoxShape(new Vector3(34f, 44f * MathF.Sqrt(2f), 44f * MathF.Sqrt(2f)));
        physics.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            floorShape,
            new Vector3(0f, -10f, 0f),
            Quaternion.Identity,
            mass: 0f,
            GroundBodyLayer));
        using var vehicles = new Vehicle3DWorld(physics, new Vehicle3DConfig
        {
            VehicleCapacity = VehicleCount,
            WheelCapacity = WheelCount,
            QueryBatchCapacity = WheelCount,
            FixedStepHz = 30
        });
        var vehicleIds = new Vehicle3DVehicleId[VehicleCount];
        RegisterVehicles(physics, vehicles, chassisShape, wheelShape, wheelKind, vehicleIds);
        var input = new Vehicle3DInput(throttle: 0.25f, brake: 0f, steering: 0f);

        // Warm collision types, solver batches, straight-line contact transitions, and worker-local buffers.
        for (int step = 0; step < 256; step++)
        {
            StepVehicles(physics, vehicles, vehicleIds, input);
        }

        var samples = new double[120];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int maximumPreparedCommands = 0;
        for (int step = 0; step < samples.Length; step++)
        {
            long timestamp = Stopwatch.GetTimestamp();
            for (int vehicleIndex = 0; vehicleIndex < vehicleIds.Length; vehicleIndex++)
            {
                vehicles.SetInput(vehicleIds[vehicleIndex], input);
            }

            vehicles.PrepareFixedStep();
            int preparedCommands = physics.PendingActuationCommandCount;
            physics.Step();
            vehicles.ObserveFixedStep();
            samples[step] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
            maximumPreparedCommands = Math.Max(maximumPreparedCommands, preparedCommands);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double p95Milliseconds = Percentile(samples, 0.95);
        double p99Milliseconds = Percentile(samples, 0.99);
        double fixedStepBudgetMilliseconds = physics.FixedDeltaSeconds * 1_000d;
        TestContext.Out.WriteLine(
            $"Warmed {wheelKind}, vehicles={VehicleCount}, bodies={MobileBodyCount}, constraints={ConstraintCount}, " +
            $"P95={p95Milliseconds:F3}ms, P99={p99Milliseconds:F3}ms, allocated={allocatedBytes} bytes.");

        Assert.Multiple(() =>
        {
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(MobileBodyCount));
            Assert.That(physics.AwakeBodyCount, Is.EqualTo(MobileBodyCount), "A held drive command must keep every vehicle assembly active.");
            Assert.That(physics.ActiveConstraintCount, Is.EqualTo(ConstraintCount));
            Assert.That(vehicles.ActiveVehicleCount, Is.EqualTo(VehicleCount));
            Assert.That(vehicles.ActiveWheelCount, Is.EqualTo(WheelCount));
            Assert.That(maximumPreparedCommands, Is.Zero, "Real wheels must not enqueue a second tire-force path.");
            Assert.That(allocatedBytes, Is.Zero);
            Assert.That(p95Milliseconds, Is.LessThan(fixedStepBudgetMilliseconds));
            Assert.That(p99Milliseconds, Is.LessThan(fixedStepBudgetMilliseconds));
        });
    }

    [TestCase(Vehicle3DWheelKind.Physical)]
    [TestCase(Vehicle3DWheelKind.Box)]
    public void UnchangedIdleTargets_AllowSleepAndChangedMotorTargetWakesTheAssembly(
        Vehicle3DWheelKind wheelKind)
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 2,
            staticCapacity: 1,
            constraintCapacity: 5,
            workerCount: 1);
        Physics3DShapeId floorShape = physics.RegisterBoxShape(new Vector3(2_000f, 20f, 2_000f));
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(120f, 30f, 180f));
        Physics3DShapeId wheelShape = wheelKind == Vehicle3DWheelKind.Physical
            ? physics.RegisterCylinderShape(44f, 34f)
            : physics.RegisterBoxShape(new Vector3(34f, 44f * MathF.Sqrt(2f), 44f * MathF.Sqrt(2f)));
        physics.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            floorShape,
            new Vector3(0f, -10f, 0f),
            Quaternion.Identity,
            mass: 0f,
            GroundBodyLayer));
        Physics3DBodyId chassis = physics.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            chassisShape,
            new Vector3(0f, 129f, 0f),
            Quaternion.Identity,
            mass: 120f,
            VehicleBodyLayer));
        Quaternion wheelOrientation = WheelOrientation(wheelKind);
        Physics3DBodyId wheel = physics.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            wheelShape,
            new Vector3(0f, 44f, 0f),
            wheelOrientation,
            mass: 20f,
            VehicleBodyLayer));
        using var vehicles = new Vehicle3DWorld(physics, new Vehicle3DConfig
        {
            VehicleCapacity = 1,
            WheelCapacity = 1,
            QueryBatchCapacity = 1,
            FixedStepHz = 30
        });
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreateWheel(wheelKind, wheel, Vector3.Zero, steeringScale: 0f)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        Vehicle3DVehicleId vehicle = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        for (int step = 0; step < 600; step++)
        {
            StepVehicle(physics, vehicles, vehicle, default);
        }

        bool sleepingChassis = !physics.GetBodyState(chassis).Awake;
        bool sleepingWheel = !physics.GetBodyState(wheel).Awake;
        StepVehicle(physics, vehicles, vehicle, new Vehicle3DInput(1f, 0f, 0f));

        Assert.Multiple(() =>
        {
            Assert.That(sleepingChassis, Is.True);
            Assert.That(sleepingWheel, Is.True);
            Assert.That(physics.GetBodyState(chassis).Awake, Is.True);
            Assert.That(physics.GetBodyState(wheel).Awake, Is.True);
            Assert.That(physics.PendingActuationCommandCount, Is.Zero);
        });
    }

    [TestCase(Vehicle3DWheelKind.Physical)]
    [TestCase(Vehicle3DWheelKind.Box)]
    public void HeldInput_KeepsAwakeOnlyVehiclesWithAnEffectiveWheelDrive(
        Vehicle3DWheelKind wheelKind)
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 4,
            staticCapacity: 1,
            constraintCapacity: 10,
            workerCount: 1);
        Physics3DShapeId floorShape = physics.RegisterBoxShape(new Vector3(4_000f, 20f, 2_000f));
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(120f, 30f, 180f));
        Physics3DShapeId wheelShape = wheelKind == Vehicle3DWheelKind.Physical
            ? physics.RegisterCylinderShape(44f, 34f)
            : physics.RegisterBoxShape(new Vector3(34f, 44f * MathF.Sqrt(2f), 44f * MathF.Sqrt(2f)));
        physics.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            floorShape,
            new Vector3(0f, -10f, 0f),
            Quaternion.Identity,
            mass: 0f,
            GroundBodyLayer));

        using var vehicles = new Vehicle3DWorld(physics, new Vehicle3DConfig
        {
            VehicleCapacity = 2,
            WheelCapacity = 2,
            QueryBatchCapacity = 2,
            FixedStepHz = 30
        });
        Quaternion wheelOrientation = WheelOrientation(wheelKind);
        Vehicle3DVehicleId brakeIgnoredVehicle = RegisterSingleWheelVehicle(
            physics,
            vehicles,
            chassisShape,
            wheelShape,
            wheelKind,
            wheelOrientation,
            new Vector3(-500f, 0f, 0f),
            driveScale: 1f,
            brakeScale: 0f,
            out Physics3DBodyId brakeIgnoredWheel);
        Vehicle3DVehicleId driveIgnoredVehicle = RegisterSingleWheelVehicle(
            physics,
            vehicles,
            chassisShape,
            wheelShape,
            wheelKind,
            wheelOrientation,
            new Vector3(500f, 0f, 0f),
            driveScale: 0f,
            brakeScale: 1f,
            out Physics3DBodyId driveIgnoredWheel);
        var brakeIgnoredInput = new Vehicle3DInput(throttle: 1f, brake: 1f, steering: 0f);
        var driveIgnoredInput = new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f);

        for (int step = 0; step < 600; step++)
        {
            vehicles.SetInput(brakeIgnoredVehicle, brakeIgnoredInput);
            vehicles.SetInput(driveIgnoredVehicle, driveIgnoredInput);
            vehicles.PrepareFixedStep();
            physics.Step();
            vehicles.ObserveFixedStep();
        }

        Assert.Multiple(() =>
        {
            Assert.That(physics.GetBodyState(brakeIgnoredWheel).Awake, Is.True,
                "A wheel whose brake scale is zero must keep using its effective drive input.");
            Assert.That(physics.GetBodyState(driveIgnoredWheel).Awake, Is.False,
                "A wheel whose drive scale is zero must be allowed to sleep.");
        });
    }

    private static void RegisterVehicles(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        Physics3DShapeId chassisShape,
        Physics3DShapeId wheelShape,
        Vehicle3DWheelKind wheelKind,
        Span<Vehicle3DVehicleId> vehicleIds)
    {
        Span<Vector3> mounts = stackalloc Vector3[WheelsPerVehicle]
        {
            new(-95f, 20f, -115f),
            new(95f, 20f, -115f),
            new(-95f, 20f, 115f),
            new(95f, 20f, 115f)
        };
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[WheelsPerVehicle];
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[WheelsPerVehicle];
        Quaternion wheelOrientation = WheelOrientation(wheelKind);
        for (int vehicleIndex = 0; vehicleIndex < vehicleIds.Length; vehicleIndex++)
        {
            Vector3 chassisPosition = new(
                (vehicleIndex % 25) * 500f,
                170f,
                (vehicleIndex / 25) * 700f);
            Physics3DBodyId chassis = physics.CreateBody(CreateBody(
                Physics3DBodyKind.Dynamic,
                chassisShape,
                chassisPosition,
                Quaternion.Identity,
                mass: 900f,
                VehicleBodyLayer));
            for (int wheelIndex = 0; wheelIndex < mounts.Length; wheelIndex++)
            {
                Vector3 wheelPosition = chassisPosition + mounts[wheelIndex] + new Vector3(0f, -85f, 0f);
                Physics3DBodyId wheel = physics.CreateBody(CreateBody(
                    Physics3DBodyKind.Dynamic,
                    wheelShape,
                    wheelPosition,
                    wheelOrientation,
                    mass: 28f,
                    VehicleBodyLayer));
                descriptions[wheelIndex] = CreateWheel(
                    wheelKind,
                    wheel,
                    mounts[wheelIndex],
                    steeringScale: wheelIndex < 2 ? 1f : 0f);
            }

            vehicleIds[vehicleIndex] = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
        }
    }

    private static Vehicle3DWheelDescription CreateWheel(
        Vehicle3DWheelKind wheelKind,
        Physics3DBodyId wheel,
        Vector3 mount,
        float steeringScale,
        float driveScale = 1f,
        float brakeScale = 1f)
        => Vehicle3DWheelDescription.Physical(
            wheelKind,
            Vehicle3DWheelQueryKind.Raycast,
            wheel,
            mount,
            -Vector3.UnitY,
            Vector3.UnitZ,
            radiusCm: 44f,
            minimumLengthCm: 55f,
            restLengthCm: 85f,
            maximumLengthCm: 115f,
            maximumSteeringAngleRadians: 0.55f,
            suspensionStiffness: 10_000f,
            suspensionDamping: 600f,
            maximumSuspensionForce: 400_000f,
            longitudinalGrip: 5_000f,
            lateralGrip: 5_000f,
            maximumDriveForce: 180_000f,
            maximumBrakeForce: 60_000f,
            maximumLateralForce: 220_000f,
            maximumWheelAngularSpeedRadiansPerSecond: 10f,
            steeringScale,
            driveScale,
            brakeScale,
            GroundQueryLayer,
            new Vehicle3DWheelJointSettings(
                new Physics3DSpringSettings(30f, 2f),
                new Physics3DSpringSettings(12f, 2f),
                new Physics3DSpringSettings(30f, 2f),
                new Physics3DServoSettings(1_500f, 0f, 2_000_000f),
                new Physics3DMotorSettings(16_000_000f, 0.001f)));

    private static Vehicle3DVehicleId RegisterSingleWheelVehicle(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        Physics3DShapeId chassisShape,
        Physics3DShapeId wheelShape,
        Vehicle3DWheelKind wheelKind,
        Quaternion wheelOrientation,
        Vector3 horizontalOffset,
        float driveScale,
        float brakeScale,
        out Physics3DBodyId wheel)
    {
        Vector3 chassisPosition = horizontalOffset + new Vector3(0f, 129f, 0f);
        Physics3DBodyId chassis = physics.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            chassisShape,
            chassisPosition,
            Quaternion.Identity,
            mass: 120f,
            VehicleBodyLayer));
        wheel = physics.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            wheelShape,
            horizontalOffset + new Vector3(0f, 44f, 0f),
            wheelOrientation,
            mass: 20f,
            VehicleBodyLayer));
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreateWheel(
                wheelKind,
                wheel,
                Vector3.Zero,
                steeringScale: 0f,
                driveScale,
                brakeScale)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        return vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
    }

    private static int StepVehicles(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        ReadOnlySpan<Vehicle3DVehicleId> vehicleIds,
        in Vehicle3DInput input)
    {
        for (int vehicleIndex = 0; vehicleIndex < vehicleIds.Length; vehicleIndex++)
        {
            vehicles.SetInput(vehicleIds[vehicleIndex], input);
        }

        vehicles.PrepareFixedStep();
        int preparedCommands = physics.PendingActuationCommandCount;
        physics.Step();
        vehicles.ObserveFixedStep();
        return preparedCommands;
    }

    private static void StepVehicle(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        Vehicle3DVehicleId vehicle,
        in Vehicle3DInput input)
    {
        vehicles.SetInput(vehicle, input);
        vehicles.PrepareFixedStep();
        physics.Step();
        vehicles.ObserveFixedStep();
    }

    private static Quaternion WheelOrientation(Vehicle3DWheelKind wheelKind)
        => wheelKind == Vehicle3DWheelKind.Physical
            ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f)
            : Quaternion.Identity;

    private static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 position,
        Quaternion orientation,
        float mass,
        LayerMask layer)
        => new(
            Entity.Null,
            kind,
            shape,
            position,
            orientation,
            Vector3.Zero,
            Vector3.Zero,
            mass,
            layer,
            new Physics3DMaterial(0.8f, 200f, 30f, 1f),
            Physics3DContinuousDetectionMode.Passive);

    private static Physics3DWorld CreatePhysicsWorld(
        int mobileCapacity,
        int staticCapacity,
        int constraintCapacity,
        int workerCount)
        => new(new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticCapacity,
            ShapeCapacity = 4,
            InactiveIslandCapacity = mobileCapacity,
            ConstraintCapacity = constraintCapacity,
            ConstraintsPerTypeBatchCapacity = constraintCapacity,
            ConstraintCountPerBodyEstimate = 8,
            ContactPairCapacityPerWorker = Math.Max(256, mobileCapacity * 4),
            ActuationCommandCapacity = 1,
            WorkerCount = workerCount,
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
        });

    private static double Percentile(double[] samples, double percentile)
    {
        Array.Sort(samples);
        int index = (int)Math.Ceiling(percentile * samples.Length) - 1;
        return samples[Math.Clamp(index, 0, samples.Length - 1)];
    }

}
