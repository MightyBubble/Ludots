using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Vehicle3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Vehicle3DTests
{
    private static readonly LayerMask GroundBodyLayer = new(category: 1u, mask: 1u << 1);
    private static readonly LayerMask VehicleBodyLayer = new(category: 1u << 1, mask: 1u);
    private static readonly LayerMask GroundQueryLayer = new(category: 0u, mask: 1u);

    [Test]
    public void PhysicalBoxAndScanningWheels_ShareOneVehicleAndExposeGroundEvidence()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 5, staticCapacity: 1, constraintCapacity: 14);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(160f, 30f, 260f));
        Physics3DShapeId carrierShape = physics.RegisterSphereShape(3f);
        Physics3DShapeId physicalShape = physics.RegisterSphereShape(22f);
        Physics3DShapeId boxWheelShape = physics.RegisterBoxShape(new Vector3(28f, 44f, 44f));
        AddFloor(physics);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 100f, 0f));
        Physics3DBodyId physicalCarrier = CreateDynamicBody(physics, carrierShape, new Vector3(-60f, 40f, 0f));
        Physics3DBodyId physicalWheel = CreateDynamicBody(physics, physicalShape, new Vector3(-60f, 40f, 0f));
        Physics3DBodyId boxCarrier = CreateDynamicBody(physics, carrierShape, new Vector3(0f, 40f, 0f));
        Physics3DBodyId boxWheel = CreateDynamicBody(physics, boxWheelShape, new Vector3(0f, 40f, 0f));

        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 3));
        var descriptions = new Vehicle3DWheelDescription[]
        {
            CreatePhysicalWheel(Vehicle3DWheelKind.Physical, physicalCarrier, physicalWheel, new Vector3(-60f, 0f, 0f)),
            CreatePhysicalWheel(Vehicle3DWheelKind.Box, boxCarrier, boxWheel, Vector3.Zero),
            CreateScanningWheel(new Vector3(60f, 0f, 0f), Vehicle3DWheelQueryKind.SphereCast)
        };
        var wheelIds = new Vehicle3DWheelId[descriptions.Length];
        Vehicle3DVehicleId vehicle = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
        vehicles.SetInput(vehicle, new Vehicle3DInput(throttle: 0.6f, brake: 0f, steering: 0.5f));

        vehicles.PrepareStep();

        Assert.Multiple(() =>
        {
            Assert.That(physics.ActiveConstraintCount, Is.EqualTo(14), "Each physical wheel must own the full seven-constraint wheel-joint bundle.");
            Assert.That(vehicles.GetWheelState(wheelIds[0]).Kind, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(vehicles.GetWheelState(wheelIds[1]).Kind, Is.EqualTo(Vehicle3DWheelKind.Box));
            Assert.That(vehicles.GetWheelState(wheelIds[2]).Kind, Is.EqualTo(Vehicle3DWheelKind.Scanning));
            Assert.That(vehicles.GetWheelState(wheelIds[0]).Grounded, Is.True);
            Assert.That(vehicles.GetWheelState(wheelIds[1]).Grounded, Is.True);
            Assert.That(vehicles.GetWheelState(wheelIds[2]).Grounded, Is.True);
            Assert.That(physics.PendingActuationCommandCount, Is.GreaterThan(0));
        });

        vehicles.RemoveVehicle(vehicle);
        Assert.That(physics.ActiveConstraintCount, Is.Zero);
    }

    [Test]
    public void PhysicalWheel_TravelLimitContainsAForcedCarrier()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 3,
            staticCapacity: 1,
            constraintCapacity: 7,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(120f, 30f, 180f));
        Physics3DShapeId carrierShape = physics.RegisterSphereShape(3f);
        Physics3DShapeId wheelShape = physics.RegisterSphereShape(20f);
        AddFloor(physics);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 140f, 0f));
        Physics3DBodyId carrier = CreateDynamicBody(physics, carrierShape, new Vector3(0f, 80f, 0f));
        Physics3DBodyId wheel = CreateDynamicBody(physics, wheelShape, new Vector3(0f, 80f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 1));
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreatePhysicalWheel(Vehicle3DWheelKind.Physical, carrier, wheel, Vector3.Zero)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        physics.EnqueueLinearImpulse(carrier, new Vector3(0f, -2_000f, 0f));
        for (int i = 0; i < 90; i++)
        {
            vehicles.PrepareStep();
            physics.Step();
        }

        Physics3DBodyState chassisState = physics.GetBodyState(chassis);
        Physics3DBodyState carrierState = physics.GetBodyState(carrier);
        float travel = chassisState.PositionCm.Y - carrierState.PositionCm.Y;
        Assert.That(travel, Is.InRange(28f, 82f), "Suspension carrier must stay within the authored 30-80cm travel range.");
    }

    [Test]
    public void SteeringDriveAndBrake_ChangePhysicalWheelMotion()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 3,
            staticCapacity: 1,
            constraintCapacity: 7,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(120f, 30f, 180f));
        Physics3DShapeId carrierShape = physics.RegisterSphereShape(3f);
        Physics3DShapeId wheelShape = physics.RegisterSphereShape(20f);
        AddFloor(physics);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 100f, 0f));
        Physics3DBodyId carrier = CreateDynamicBody(physics, carrierShape, new Vector3(0f, 40f, 0f));
        Physics3DBodyId wheel = CreateDynamicBody(physics, wheelShape, new Vector3(0f, 40f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 1));
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreatePhysicalWheel(Vehicle3DWheelKind.Physical, carrier, wheel, Vector3.Zero)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        Vehicle3DVehicleId vehicle = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
        vehicles.SetInput(vehicle, new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 1f));
        for (int i = 0; i < 20; i++)
        {
            vehicles.PrepareStep();
            physics.Step();
        }

        float drivenSpeed = MathF.Abs(vehicles.GetWheelState(wheelIds[0]).WheelAngularSpeedRadiansPerSecond);
        float steeringDot = MathF.Abs(Quaternion.Dot(
            physics.GetBodyState(chassis).Orientation,
            physics.GetBodyState(carrier).Orientation));
        vehicles.SetInput(vehicle, new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        for (int i = 0; i < 30; i++)
        {
            vehicles.PrepareStep();
            physics.Step();
        }

        float brakedSpeed = MathF.Abs(vehicles.GetWheelState(wheelIds[0]).WheelAngularSpeedRadiansPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(drivenSpeed, Is.GreaterThan(0.5f));
            Assert.That(steeringDot, Is.LessThan(0.9999f));
            Assert.That(brakedSpeed, Is.LessThan(drivenSpeed));
        });
    }

    [Test]
    public void ScanningWheel_UsesGroundPointVelocityForPlatformRelativeSlip()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 2, staticCapacity: 0, constraintCapacity: 1, gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DShapeId platformShape = physics.RegisterBoxShape(new Vector3(1_000f, 20f, 1_000f));
        Physics3DBodyId platform = physics.CreateBody(CreateBody(
            Physics3DBodyKind.Kinematic,
            platformShape,
            new Vector3(0f, -10f, 0f),
            new Vector3(0f, 0f, 120f),
            GroundBodyLayer));
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 80f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 1));
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreateScanningWheel(Vector3.Zero, Vehicle3DWheelQueryKind.Raycast)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        vehicles.PrepareStep();
        Vehicle3DWheelState state = vehicles.GetWheelState(wheelIds[0]);

        Assert.Multiple(() =>
        {
            Assert.That(state.Grounded, Is.True);
            Assert.That(state.LongitudinalSpeedCmPerSecond, Is.EqualTo(-120f).Within(0.01f));
            Assert.That(state.SlipVelocityCmPerSecond.Z, Is.EqualTo(-120f).Within(0.01f));
            Assert.That(physics.ContainsBody(platform), Is.True);
        });
    }

    [Test]
    public void GroundLoss_ClearsContactAndDoesNotInventActuation()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 1, staticCapacity: 0, constraintCapacity: 1, gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 1_000f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 1));
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[1]
        {
            CreateScanningWheel(Vector3.Zero, Vehicle3DWheelQueryKind.SphereCast)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[1];
        vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        vehicles.PrepareStep();
        Vehicle3DWheelState state = vehicles.GetWheelState(wheelIds[0]);

        Assert.Multiple(() =>
        {
            Assert.That(state.Grounded, Is.False);
            Assert.That(state.CompressionCm, Is.Zero);
            Assert.That(state.ContactPointCm, Is.EqualTo(Vector3.Zero));
            Assert.That(physics.PendingActuationCommandCount, Is.Zero);
        });
    }

    [Test]
    public void ActuationCapacityFailure_IsAtomicAndLeavesPriorWheelStateUntouched()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 1,
            staticCapacity: 1,
            constraintCapacity: 1,
            actuationCapacity: 3,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        AddFloor(physics);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 70f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 4));
        var descriptions = new Vehicle3DWheelDescription[4]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(-30f, 0f, 40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, 40f), Vehicle3DWheelQueryKind.Raycast)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[4];
        vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        Vehicle3DCapacityExceededException exception = Assert.Throws<Vehicle3DCapacityExceededException>(vehicles.PrepareStep)!;
        bool firstWheelGrounded = vehicles.GetWheelState(wheelIds[0]).Grounded;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Resource, Is.EqualTo("Physics3D actuation commands"));
            Assert.That(exception.Required, Is.EqualTo(4));
            Assert.That(physics.PendingActuationCommandCount, Is.Zero);
            Assert.That(firstWheelGrounded, Is.False, "Failed batches must not publish staged query state.");
        });
    }

    [TestCase(Vehicle3DWheelQueryKind.Raycast)]
    [TestCase(Vehicle3DWheelQueryKind.SphereCast)]
    public void QueryBatch_ProcessesEveryWheelWithoutTruncation(Vehicle3DWheelQueryKind queryKind)
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 1, staticCapacity: 1, constraintCapacity: 1, gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        AddFloor(physics);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 80f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 4));
        var descriptions = new Vehicle3DWheelDescription[4]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, -40f), queryKind),
            CreateScanningWheel(new Vector3(30f, 0f, -40f), queryKind),
            CreateScanningWheel(new Vector3(-30f, 0f, 40f), queryKind),
            CreateScanningWheel(new Vector3(30f, 0f, 40f), queryKind)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[4];
        vehicles.RegisterVehicle(chassis, descriptions, wheelIds);

        vehicles.PrepareStep();

        Span<Vehicle3DWheelState> states = stackalloc Vehicle3DWheelState[4];
        Assert.That(vehicles.CopyWheelStates(states), Is.EqualTo(4));
        for (int i = 0; i < states.Length; i++)
        {
            Assert.That(states[i].Grounded, Is.True, $"Wheel state {i} was omitted from the batch.");
        }
    }

    [Test]
    public void WarmedVehiclePreparation_HasZeroManagedAllocations()
    {
        const int vehicleCount = 32;
        const int wheelCount = vehicleCount * 4;
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: vehicleCount,
            staticCapacity: 1,
            constraintCapacity: 1,
            actuationCapacity: wheelCount,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        AddFloor(physics, sizeCm: 100_000f);
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(vehicleCount, wheelCount));
        RegisterScanningVehicles(
            physics,
            vehicles,
            chassisShape,
            vehicleCount,
            Vehicle3DWheelQueryKind.SphereCast);

        for (int i = 0; i < 64; i++)
        {
            vehicles.PrepareStep();
            physics.ClearActuationCommands();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            vehicles.PrepareStep();
            physics.ClearActuationCommands();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed Vehicle3D preparation allocated {allocated} managed bytes.");
    }

    [TestCase(50, Vehicle3DWheelQueryKind.Raycast)]
    [TestCase(100, Vehicle3DWheelQueryKind.Raycast)]
    [TestCase(150, Vehicle3DWheelQueryKind.Raycast)]
    [TestCase(50, Vehicle3DWheelQueryKind.SphereCast)]
    [TestCase(100, Vehicle3DWheelQueryKind.SphereCast)]
    [TestCase(150, Vehicle3DWheelQueryKind.SphereCast)]
    public void FourWheelScanningVehiclePressure_ProcessesDeclaredScale(
        int vehicleCount,
        Vehicle3DWheelQueryKind queryKind)
    {
        int wheelCount = vehicleCount * 4;
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: vehicleCount,
            staticCapacity: 1,
            constraintCapacity: 1,
            actuationCapacity: wheelCount,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        AddFloor(physics, sizeCm: 500_000f);
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(vehicleCount, wheelCount));
        RegisterScanningVehicles(physics, vehicles, chassisShape, vehicleCount, queryKind);

        vehicles.PrepareStep();

        var states = new Vehicle3DWheelState[wheelCount];
        int stateCount = vehicles.CopyWheelStates(states);
        int groundedCount = 0;
        for (int i = 0; i < states.Length; i++)
        {
            groundedCount += states[i].Grounded ? 1 : 0;
        }

        Assert.Multiple(() =>
        {
            Assert.That(vehicles.ActiveVehicleCount, Is.EqualTo(vehicleCount));
            Assert.That(vehicles.ActiveWheelCount, Is.EqualTo(wheelCount));
            Assert.That(stateCount, Is.EqualTo(wheelCount));
            Assert.That(groundedCount, Is.EqualTo(wheelCount));
            Assert.That(physics.PendingActuationCommandCount, Is.EqualTo(wheelCount));
        });
    }

    [TestCase(50, Vehicle3DWheelKind.Physical)]
    [TestCase(100, Vehicle3DWheelKind.Physical)]
    [TestCase(150, Vehicle3DWheelKind.Physical)]
    [TestCase(50, Vehicle3DWheelKind.Box)]
    [TestCase(100, Vehicle3DWheelKind.Box)]
    [TestCase(150, Vehicle3DWheelKind.Box)]
    public void FourWheelPhysicalVehiclePressure_RegistersFullJointBundlesAndProcessesEveryWheel(
        int vehicleCount,
        Vehicle3DWheelKind wheelKind)
    {
        int wheelCount = vehicleCount * 4;
        int mobileBodyCount = vehicleCount * 9;
        int constraintCount = wheelCount * 7;
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: mobileBodyCount,
            staticCapacity: 1,
            constraintCapacity: constraintCount,
            actuationCapacity: wheelCount,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DShapeId carrierShape = physics.RegisterSphereShape(3f);
        Physics3DShapeId wheelShape = wheelKind == Vehicle3DWheelKind.Box
            ? physics.RegisterBoxShape(new Vector3(30f, 40f, 40f))
            : physics.RegisterSphereShape(20f);
        AddFloor(physics, sizeCm: 500_000f);
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(vehicleCount, wheelCount));
        RegisterPhysicalVehicles(
            physics,
            vehicles,
            chassisShape,
            carrierShape,
            wheelShape,
            wheelKind,
            vehicleCount);

        vehicles.PrepareStep();

        var states = new Vehicle3DWheelState[wheelCount];
        int stateCount = vehicles.CopyWheelStates(states);
        int groundedCount = 0;
        for (int i = 0; i < states.Length; i++)
        {
            groundedCount += states[i].Grounded ? 1 : 0;
        }

        Assert.Multiple(() =>
        {
            Assert.That(vehicles.ActiveVehicleCount, Is.EqualTo(vehicleCount));
            Assert.That(vehicles.ActiveWheelCount, Is.EqualTo(wheelCount));
            Assert.That(physics.ActiveConstraintCount, Is.EqualTo(constraintCount));
            Assert.That(stateCount, Is.EqualTo(wheelCount));
            Assert.That(groundedCount, Is.EqualTo(wheelCount));
            Assert.That(physics.PendingActuationCommandCount, Is.EqualTo(wheelCount));
        });
    }

    [Test]
    public void PhysicalRegistrationConstraintFailure_RollsBackEveryCreatedConstraintAndSlot()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(
            mobileCapacity: 3,
            staticCapacity: 0,
            constraintCapacity: 6,
            gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DShapeId carrierShape = physics.RegisterSphereShape(3f);
        Physics3DShapeId wheelShape = physics.RegisterSphereShape(20f);
        Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(0f, 100f, 0f));
        Physics3DBodyId carrier = CreateDynamicBody(physics, carrierShape, new Vector3(0f, 40f, 0f));
        Physics3DBodyId wheel = CreateDynamicBody(physics, wheelShape, new Vector3(0f, 40f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 1));
        var descriptions = new[]
        {
            CreatePhysicalWheel(Vehicle3DWheelKind.Physical, carrier, wheel, Vector3.Zero)
        };
        var wheelIds = new Vehicle3DWheelId[1];

        Assert.Throws<Physics3DCapacityExceededException>(() =>
            vehicles.RegisterVehicle(chassis, descriptions, wheelIds));
        Assert.Multiple(() =>
        {
            Assert.That(vehicles.ActiveVehicleCount, Is.Zero);
            Assert.That(vehicles.ActiveWheelCount, Is.Zero);
            Assert.That(physics.ActiveConstraintCount, Is.Zero);
            Assert.That(wheelIds[0].IsValid, Is.False);
        });
    }

    [Test]
    public void WheelCapacityFailure_DoesNotPartiallyRegisterSecondVehicle()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 2, staticCapacity: 0, constraintCapacity: 1, gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DBodyId firstChassis = CreateDynamicBody(physics, chassisShape, Vector3.Zero);
        Physics3DBodyId secondChassis = CreateDynamicBody(physics, chassisShape, new Vector3(500f, 0f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(2, 4));
        var firstDescriptions = new Vehicle3DWheelDescription[3]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(0f, 0f, 40f), Vehicle3DWheelQueryKind.Raycast)
        };
        var secondDescriptions = new Vehicle3DWheelDescription[2]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, 0f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, 0f), Vehicle3DWheelQueryKind.Raycast)
        };
        var firstIds = new Vehicle3DWheelId[3];
        var secondIds = new Vehicle3DWheelId[2];
        vehicles.RegisterVehicle(firstChassis, firstDescriptions, firstIds);

        Vehicle3DCapacityExceededException exception = Assert.Throws<Vehicle3DCapacityExceededException>(() =>
            vehicles.RegisterVehicle(secondChassis, secondDescriptions, secondIds))!;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Resource, Is.EqualTo("wheels"));
            Assert.That(vehicles.ActiveVehicleCount, Is.EqualTo(1));
            Assert.That(vehicles.ActiveWheelCount, Is.EqualTo(3));
            Assert.That(secondIds[0].IsValid, Is.False);
        });
    }

    [Test]
    public void VehicleAndWheelCapacityFailures_DoNotPartiallyRegister()
    {
        using Physics3DWorld physics = CreatePhysicsWorld(mobileCapacity: 2, staticCapacity: 0, constraintCapacity: 1, gravity: Vector3.Zero);
        Physics3DShapeId chassisShape = physics.RegisterBoxShape(new Vector3(100f, 20f, 140f));
        Physics3DBodyId firstChassis = CreateDynamicBody(physics, chassisShape, Vector3.Zero);
        Physics3DBodyId secondChassis = CreateDynamicBody(physics, chassisShape, new Vector3(500f, 0f, 0f));
        using var vehicles = new Vehicle3DWorld(physics, CreateVehicleConfig(1, 4));
        var descriptions = new Vehicle3DWheelDescription[4]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, -40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(-30f, 0f, 40f), Vehicle3DWheelQueryKind.Raycast),
            CreateScanningWheel(new Vector3(30f, 0f, 40f), Vehicle3DWheelQueryKind.Raycast)
        };
        var firstIds = new Vehicle3DWheelId[4];
        vehicles.RegisterVehicle(firstChassis, descriptions, firstIds);
        var secondIds = new Vehicle3DWheelId[4];

        Assert.Throws<Vehicle3DCapacityExceededException>(() =>
            vehicles.RegisterVehicle(secondChassis, descriptions, secondIds));
        Assert.Multiple(() =>
        {
            Assert.That(vehicles.ActiveVehicleCount, Is.EqualTo(1));
            Assert.That(vehicles.ActiveWheelCount, Is.EqualTo(4));
            Assert.That(secondIds[0].IsValid, Is.False);
        });
    }

    private static void RegisterScanningVehicles(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        Physics3DShapeId chassisShape,
        int vehicleCount,
        Vehicle3DWheelQueryKind queryKind = Vehicle3DWheelQueryKind.Raycast)
    {
        Span<Vehicle3DWheelDescription> descriptions = stackalloc Vehicle3DWheelDescription[4]
        {
            CreateScanningWheel(new Vector3(-30f, 0f, -40f), queryKind),
            CreateScanningWheel(new Vector3(30f, 0f, -40f), queryKind),
            CreateScanningWheel(new Vector3(-30f, 0f, 40f), queryKind),
            CreateScanningWheel(new Vector3(30f, 0f, 40f), queryKind)
        };
        Span<Vehicle3DWheelId> wheelIds = stackalloc Vehicle3DWheelId[4];
        for (int i = 0; i < vehicleCount; i++)
        {
            float x = (i % 25) * 300f;
            float z = (i / 25) * 400f;
            Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, new Vector3(x, 70f, z));
            Vehicle3DVehicleId vehicle = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
            vehicles.SetInput(vehicle, new Vehicle3DInput(throttle: 0.4f, brake: 0f, steering: (i & 1) == 0 ? 0.2f : -0.2f));
        }
    }

    private static void RegisterPhysicalVehicles(
        Physics3DWorld physics,
        Vehicle3DWorld vehicles,
        Physics3DShapeId chassisShape,
        Physics3DShapeId carrierShape,
        Physics3DShapeId wheelShape,
        Vehicle3DWheelKind wheelKind,
        int vehicleCount)
    {
        var descriptions = new Vehicle3DWheelDescription[4];
        var wheelIds = new Vehicle3DWheelId[4];
        Vector3[] mounts =
        {
            new(-45f, 0f, -55f),
            new(45f, 0f, -55f),
            new(-45f, 0f, 55f),
            new(45f, 0f, 55f)
        };
        for (int vehicleIndex = 0; vehicleIndex < vehicleCount; vehicleIndex++)
        {
            float originX = (vehicleIndex % 25) * 300f;
            float originZ = (vehicleIndex / 25) * 400f;
            Vector3 chassisPosition = new(originX, 100f, originZ);
            Physics3DBodyId chassis = CreateDynamicBody(physics, chassisShape, chassisPosition);
            for (int wheelIndex = 0; wheelIndex < mounts.Length; wheelIndex++)
            {
                Vector3 bodyPosition = chassisPosition + mounts[wheelIndex] + new Vector3(0f, -60f, 0f);
                Physics3DBodyId carrier = CreateDynamicBody(physics, carrierShape, bodyPosition);
                Physics3DBodyId wheel = CreateDynamicBody(physics, wheelShape, bodyPosition);
                descriptions[wheelIndex] = CreatePhysicalWheel(
                    wheelKind,
                    carrier,
                    wheel,
                    mounts[wheelIndex]);
            }

            Vehicle3DVehicleId vehicle = vehicles.RegisterVehicle(chassis, descriptions, wheelIds);
            vehicles.SetInput(vehicle, new Vehicle3DInput(throttle: 0.25f, brake: 0f, steering: 0.15f));
        }
    }

    private static Vehicle3DWheelDescription CreateScanningWheel(
        Vector3 localMountCm,
        Vehicle3DWheelQueryKind queryKind)
    {
        return Vehicle3DWheelDescription.Scanning(
            queryKind,
            localMountCm,
            -Vector3.UnitY,
            Vector3.UnitZ,
            radiusCm: 20f,
            minimumLengthCm: 30f,
            restLengthCm: 70f,
            maximumLengthCm: 90f,
            maximumSteeringAngleRadians: 0.6f,
            suspensionStiffness: 1_000f,
            suspensionDamping: 80f,
            maximumSuspensionForce: 100_000f,
            longitudinalGrip: 100f,
            lateralGrip: 100f,
            maximumDriveForce: 10_000f,
            maximumBrakeForce: 20_000f,
            maximumLateralForce: 20_000f,
            maximumWheelAngularSpeedRadiansPerSecond: 50f,
            steeringScale: 1f,
            driveScale: 1f,
            brakeScale: 1f,
            GroundQueryLayer);
    }

    private static Vehicle3DWheelDescription CreatePhysicalWheel(
        Vehicle3DWheelKind kind,
        Physics3DBodyId carrier,
        Physics3DBodyId wheel,
        Vector3 localMountCm)
    {
        return Vehicle3DWheelDescription.Physical(
            kind,
            Vehicle3DWheelQueryKind.Raycast,
            carrier,
            wheel,
            localMountCm,
            -Vector3.UnitY,
            Vector3.UnitZ,
            radiusCm: 20f,
            minimumLengthCm: 30f,
            restLengthCm: 60f,
            maximumLengthCm: 80f,
            maximumSteeringAngleRadians: 0.6f,
            suspensionStiffness: 1_000f,
            suspensionDamping: 80f,
            maximumSuspensionForce: 100_000f,
            longitudinalGrip: 100f,
            lateralGrip: 100f,
            maximumDriveForce: 10_000f,
            maximumBrakeForce: 20_000f,
            maximumLateralForce: 20_000f,
            maximumWheelAngularSpeedRadiansPerSecond: 50f,
            steeringScale: 1f,
            driveScale: 1f,
            brakeScale: 1f,
            GroundQueryLayer,
            CreateJointSettings());
    }

    private static Vehicle3DWheelJointSettings CreateJointSettings()
    {
        var alignment = new Physics3DSpringSettings(30f, 2f);
        var suspension = new Physics3DSpringSettings(12f, 2f);
        var limit = new Physics3DSpringSettings(30f, 2f);
        var steering = new Physics3DSpringSettings(20f, 2f);
        var hub = new Physics3DSpringSettings(30f, 2f);
        var lineServo = new Physics3DServoSettings(10_000f, 0f, 1_000_000f);
        var steeringServo = new Physics3DServoSettings(20f, 0f, 1_000_000f);
        var motor = new Physics3DMotorSettings(1_000_000f, 0.001f);
        return new Vehicle3DWheelJointSettings(
            alignment,
            suspension,
            limit,
            steering,
            hub,
            lineServo,
            steeringServo,
            motor);
    }

    private static Vehicle3DConfig CreateVehicleConfig(int vehicleCapacity, int wheelCapacity)
    {
        return new Vehicle3DConfig
        {
            VehicleCapacity = vehicleCapacity,
            WheelCapacity = wheelCapacity,
            QueryBatchCapacity = wheelCapacity,
            FixedStepHz = 30
        };
    }

    private static Physics3DWorld CreatePhysicsWorld(
        int mobileCapacity,
        int staticCapacity,
        int constraintCapacity,
        int? actuationCapacity = null,
        Vector3? gravity = null)
    {
        return new Physics3DWorld(new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticCapacity,
            ShapeCapacity = 8,
            InactiveIslandCapacity = Math.Max(1, mobileCapacity),
            ConstraintCapacity = constraintCapacity,
            ConstraintsPerTypeBatchCapacity = Math.Max(256, constraintCapacity),
            ConstraintCountPerBodyEstimate = 16,
            ContactPairCapacityPerWorker = Math.Max(64, mobileCapacity * 8),
            ActuationCommandCapacity = actuationCapacity ?? Math.Max(1, mobileCapacity * 8),
            WorkerCount = 1,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = gravity ?? new Vector3(0f, -981f, 0f),
            LinearDamping = 0f,
            AngularDamping = 0f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = 32,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        });
    }

    private static Physics3DBodyId AddFloor(Physics3DWorld physics, float sizeCm = 10_000f)
    {
        Physics3DShapeId floorShape = physics.RegisterBoxShape(new Vector3(sizeCm, 20f, sizeCm));
        return physics.CreateBody(CreateBody(
            Physics3DBodyKind.Static,
            floorShape,
            new Vector3(0f, -10f, 0f),
            Vector3.Zero,
            GroundBodyLayer));
    }

    private static Physics3DBodyId CreateDynamicBody(
        Physics3DWorld physics,
        Physics3DShapeId shape,
        Vector3 positionCm)
    {
        return physics.CreateBody(CreateBody(
            Physics3DBodyKind.Dynamic,
            shape,
            positionCm,
            Vector3.Zero,
            VehicleBodyLayer));
    }

    private static Physics3DBodyDescription CreateBody(
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 positionCm,
        Vector3 linearVelocityCmPerSecond,
        LayerMask layer)
    {
        return new Physics3DBodyDescription(
            Entity.Null,
            kind,
            shape,
            positionCm,
            Quaternion.Identity,
            linearVelocityCmPerSecond,
            Vector3.Zero,
            kind == Physics3DBodyKind.Dynamic ? 10f : 0f,
            layer,
            new Physics3DMaterial(0.8f, 200f, 30f, 2f),
            Physics3DContinuousDetectionMode.Passive);
    }
}
