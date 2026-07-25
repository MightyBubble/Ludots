using System;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Vehicle3D;

public enum Vehicle3DWheelKind : byte
{
    Physical = 1,
    Box = 2,
    Scanning = 3
}

public enum Vehicle3DWheelQueryKind : byte
{
    Raycast = 1,
    SphereCast = 2
}

public readonly struct Vehicle3DVehicleId : IEquatable<Vehicle3DVehicleId>
{
    public Vehicle3DVehicleId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Vehicle3DVehicleId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Vehicle3DVehicleId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Vehicle3DVehicleId left, Vehicle3DVehicleId right) => left.Equals(right);
    public static bool operator !=(Vehicle3DVehicleId left, Vehicle3DVehicleId right) => !left.Equals(right);
    public override string ToString() => $"Vehicle3DVehicleId({Slot}:{Generation})";
}

public readonly struct Vehicle3DWheelId : IEquatable<Vehicle3DWheelId>
{
    public Vehicle3DWheelId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Vehicle3DWheelId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Vehicle3DWheelId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Vehicle3DWheelId left, Vehicle3DWheelId right) => left.Equals(right);
    public static bool operator !=(Vehicle3DWheelId left, Vehicle3DWheelId right) => !left.Equals(right);
    public override string ToString() => $"Vehicle3DWheelId({Slot}:{Generation})";
}

public sealed class Vehicle3DConfig
{
    public int VehicleCapacity { get; init; }
    public int WheelCapacity { get; init; }
    public int QueryBatchCapacity { get; init; }
    public int FixedStepHz { get; init; } = 30;

    internal void Validate()
    {
        RequirePositive(VehicleCapacity, nameof(VehicleCapacity));
        RequirePositive(WheelCapacity, nameof(WheelCapacity));
        RequirePositive(QueryBatchCapacity, nameof(QueryBatchCapacity));
        if (QueryBatchCapacity < WheelCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueryBatchCapacity),
                QueryBatchCapacity,
                $"Query batch capacity must cover every wheel slot ({WheelCapacity}).");
        }

        if (FixedStepHz != 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FixedStepHz),
                FixedStepHz,
                "Vehicle3D authoritative simulation is fixed at 30Hz.");
        }
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }
}

public readonly struct Vehicle3DInput
{
    public Vehicle3DInput(float throttle, float brake, float steering)
    {
        Vehicle3DValidation.RequireUnitRange(throttle, nameof(throttle));
        Vehicle3DValidation.RequireUnitInterval(brake, nameof(brake));
        Vehicle3DValidation.RequireUnitRange(steering, nameof(steering));
        Throttle = throttle;
        Brake = brake;
        Steering = steering;
    }

    public float Throttle { get; }
    public float Brake { get; }
    public float Steering { get; }
}

public readonly struct Vehicle3DWheelJointSettings
{
    public Vehicle3DWheelJointSettings(
        in Physics3DSpringSettings alignmentSpring,
        in Physics3DSpringSettings suspensionSpring,
        in Physics3DSpringSettings limitSpring,
        in Physics3DServoSettings lineServo,
        in Physics3DMotorSettings axleMotor)
    {
        AlignmentSpring = alignmentSpring;
        SuspensionSpring = suspensionSpring;
        LimitSpring = limitSpring;
        LineServo = lineServo;
        AxleMotor = axleMotor;
    }

    public Physics3DSpringSettings AlignmentSpring { get; }
    public Physics3DSpringSettings SuspensionSpring { get; }
    public Physics3DSpringSettings LimitSpring { get; }
    public Physics3DServoSettings LineServo { get; }
    public Physics3DMotorSettings AxleMotor { get; }
}

public readonly struct Vehicle3DWheelDescription
{
    private Vehicle3DWheelDescription(
        Vehicle3DWheelKind kind,
        Vehicle3DWheelQueryKind queryKind,
        Physics3DBodyId wheelBody,
        Vector3 localMountCm,
        Vector3 localSuspensionDirection,
        Vector3 localForwardDirection,
        float radiusCm,
        float minimumLengthCm,
        float restLengthCm,
        float maximumLengthCm,
        float maximumSteeringAngleRadians,
        float suspensionStiffness,
        float suspensionDamping,
        float maximumSuspensionForce,
        float longitudinalGrip,
        float lateralGrip,
        float maximumDriveForce,
        float maximumBrakeForce,
        float maximumLateralForce,
        float maximumWheelAngularSpeedRadiansPerSecond,
        float steeringScale,
        float driveScale,
        float brakeScale,
        in LayerMask groundLayer,
        in Vehicle3DWheelJointSettings joint)
    {
        Kind = kind;
        QueryKind = queryKind;
        WheelBody = wheelBody;
        LocalMountCm = localMountCm;
        LocalSuspensionDirection = localSuspensionDirection;
        LocalForwardDirection = localForwardDirection;
        RadiusCm = radiusCm;
        MinimumLengthCm = minimumLengthCm;
        RestLengthCm = restLengthCm;
        MaximumLengthCm = maximumLengthCm;
        MaximumSteeringAngleRadians = maximumSteeringAngleRadians;
        SuspensionStiffness = suspensionStiffness;
        SuspensionDamping = suspensionDamping;
        MaximumSuspensionForce = maximumSuspensionForce;
        LongitudinalGrip = longitudinalGrip;
        LateralGrip = lateralGrip;
        MaximumDriveForce = maximumDriveForce;
        MaximumBrakeForce = maximumBrakeForce;
        MaximumLateralForce = maximumLateralForce;
        MaximumWheelAngularSpeedRadiansPerSecond = maximumWheelAngularSpeedRadiansPerSecond;
        SteeringScale = steeringScale;
        DriveScale = driveScale;
        BrakeScale = brakeScale;
        GroundLayer = groundLayer;
        Joint = joint;
    }

    public Vehicle3DWheelKind Kind { get; }
    public Vehicle3DWheelQueryKind QueryKind { get; }
    public Physics3DBodyId WheelBody { get; }
    public Vector3 LocalMountCm { get; }
    public Vector3 LocalSuspensionDirection { get; }
    public Vector3 LocalForwardDirection { get; }
    public float RadiusCm { get; }
    public float MinimumLengthCm { get; }
    public float RestLengthCm { get; }
    public float MaximumLengthCm { get; }
    public float MaximumSteeringAngleRadians { get; }
    public float SuspensionStiffness { get; }
    public float SuspensionDamping { get; }
    public float MaximumSuspensionForce { get; }
    public float LongitudinalGrip { get; }
    public float LateralGrip { get; }
    public float MaximumDriveForce { get; }
    public float MaximumBrakeForce { get; }
    public float MaximumLateralForce { get; }
    public float MaximumWheelAngularSpeedRadiansPerSecond { get; }
    public float SteeringScale { get; }
    public float DriveScale { get; }
    public float BrakeScale { get; }
    public LayerMask GroundLayer { get; }
    public Vehicle3DWheelJointSettings Joint { get; }
    public bool HasPhysicalWheel => Kind is Vehicle3DWheelKind.Physical or Vehicle3DWheelKind.Box;

    public static Vehicle3DWheelDescription Scanning(
        Vehicle3DWheelQueryKind queryKind,
        Vector3 localMountCm,
        Vector3 localSuspensionDirection,
        Vector3 localForwardDirection,
        float radiusCm,
        float minimumLengthCm,
        float restLengthCm,
        float maximumLengthCm,
        float maximumSteeringAngleRadians,
        float suspensionStiffness,
        float suspensionDamping,
        float maximumSuspensionForce,
        float longitudinalGrip,
        float lateralGrip,
        float maximumDriveForce,
        float maximumBrakeForce,
        float maximumLateralForce,
        float maximumWheelAngularSpeedRadiansPerSecond,
        float steeringScale,
        float driveScale,
        float brakeScale,
        in LayerMask groundLayer)
    {
        var description = new Vehicle3DWheelDescription(
            Vehicle3DWheelKind.Scanning,
            queryKind,
            default,
            localMountCm,
            localSuspensionDirection,
            localForwardDirection,
            radiusCm,
            minimumLengthCm,
            restLengthCm,
            maximumLengthCm,
            maximumSteeringAngleRadians,
            suspensionStiffness,
            suspensionDamping,
            maximumSuspensionForce,
            longitudinalGrip,
            lateralGrip,
            maximumDriveForce,
            maximumBrakeForce,
            maximumLateralForce,
            maximumWheelAngularSpeedRadiansPerSecond,
            steeringScale,
            driveScale,
            brakeScale,
            groundLayer,
            default);
        description.Validate(nameof(Vehicle3DWheelDescription));
        return description;
    }

    public static Vehicle3DWheelDescription Physical(
        Vehicle3DWheelKind kind,
        Vehicle3DWheelQueryKind queryKind,
        Physics3DBodyId wheelBody,
        Vector3 localMountCm,
        Vector3 localSuspensionDirection,
        Vector3 localForwardDirection,
        float radiusCm,
        float minimumLengthCm,
        float restLengthCm,
        float maximumLengthCm,
        float maximumSteeringAngleRadians,
        float suspensionStiffness,
        float suspensionDamping,
        float maximumSuspensionForce,
        float longitudinalGrip,
        float lateralGrip,
        float maximumDriveForce,
        float maximumBrakeForce,
        float maximumLateralForce,
        float maximumWheelAngularSpeedRadiansPerSecond,
        float steeringScale,
        float driveScale,
        float brakeScale,
        in LayerMask groundLayer,
        in Vehicle3DWheelJointSettings joint)
    {
        if (kind is not (Vehicle3DWheelKind.Physical or Vehicle3DWheelKind.Box))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Physical wheel factory only accepts Physical or Box.");
        }

        var description = new Vehicle3DWheelDescription(
            kind,
            queryKind,
            wheelBody,
            localMountCm,
            localSuspensionDirection,
            localForwardDirection,
            radiusCm,
            minimumLengthCm,
            restLengthCm,
            maximumLengthCm,
            maximumSteeringAngleRadians,
            suspensionStiffness,
            suspensionDamping,
            maximumSuspensionForce,
            longitudinalGrip,
            lateralGrip,
            maximumDriveForce,
            maximumBrakeForce,
            maximumLateralForce,
            maximumWheelAngularSpeedRadiansPerSecond,
            steeringScale,
            driveScale,
            brakeScale,
            groundLayer,
            joint);
        description.Validate(nameof(Vehicle3DWheelDescription));
        return description;
    }

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(Kind)}", Kind, "Unknown wheel kind.");
        }

        if (!Enum.IsDefined(QueryKind))
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(QueryKind)}", QueryKind, "Unknown wheel query kind.");
        }

        if (HasPhysicalWheel)
        {
            if (!WheelBody.IsValid)
            {
                throw new ArgumentException("Physical and Box wheels require a valid wheel body.", parameterName);
            }
        }
        else if (WheelBody.IsValid)
        {
            throw new ArgumentException("Scanning wheels cannot own wheel bodies.", parameterName);
        }

        Vehicle3DValidation.RequireFinite(LocalMountCm, $"{parameterName}.{nameof(LocalMountCm)}");
        Vector3 suspension = Vehicle3DValidation.RequireUnitDirection(
            LocalSuspensionDirection,
            $"{parameterName}.{nameof(LocalSuspensionDirection)}");
        Vector3 forward = Vehicle3DValidation.RequireUnitDirection(
            LocalForwardDirection,
            $"{parameterName}.{nameof(LocalForwardDirection)}");
        if (MathF.Abs(Vector3.Dot(suspension, forward)) > 1e-3f)
        {
            throw new ArgumentException("Suspension and forward directions must be perpendicular.", parameterName);
        }

        Vehicle3DValidation.RequirePositive(RadiusCm, $"{parameterName}.{nameof(RadiusCm)}");
        Vehicle3DValidation.RequireNonNegative(MinimumLengthCm, $"{parameterName}.{nameof(MinimumLengthCm)}");
        Vehicle3DValidation.RequirePositive(RestLengthCm, $"{parameterName}.{nameof(RestLengthCm)}");
        Vehicle3DValidation.RequirePositive(MaximumLengthCm, $"{parameterName}.{nameof(MaximumLengthCm)}");
        if (MinimumLengthCm > RestLengthCm || RestLengthCm > MaximumLengthCm)
        {
            throw new ArgumentException("Suspension lengths must satisfy minimum <= rest <= maximum.", parameterName);
        }

        Vehicle3DValidation.RequireNonNegative(MaximumSteeringAngleRadians, $"{parameterName}.{nameof(MaximumSteeringAngleRadians)}");
        Vehicle3DValidation.RequireNonNegative(SuspensionStiffness, $"{parameterName}.{nameof(SuspensionStiffness)}");
        Vehicle3DValidation.RequireNonNegative(SuspensionDamping, $"{parameterName}.{nameof(SuspensionDamping)}");
        Vehicle3DValidation.RequireNonNegative(MaximumSuspensionForce, $"{parameterName}.{nameof(MaximumSuspensionForce)}");
        Vehicle3DValidation.RequireNonNegative(LongitudinalGrip, $"{parameterName}.{nameof(LongitudinalGrip)}");
        Vehicle3DValidation.RequireNonNegative(LateralGrip, $"{parameterName}.{nameof(LateralGrip)}");
        Vehicle3DValidation.RequireNonNegative(MaximumDriveForce, $"{parameterName}.{nameof(MaximumDriveForce)}");
        Vehicle3DValidation.RequireNonNegative(MaximumBrakeForce, $"{parameterName}.{nameof(MaximumBrakeForce)}");
        Vehicle3DValidation.RequireNonNegative(MaximumLateralForce, $"{parameterName}.{nameof(MaximumLateralForce)}");
        Vehicle3DValidation.RequireNonNegative(
            MaximumWheelAngularSpeedRadiansPerSecond,
            $"{parameterName}.{nameof(MaximumWheelAngularSpeedRadiansPerSecond)}");
        Vehicle3DValidation.RequireUnitRange(SteeringScale, $"{parameterName}.{nameof(SteeringScale)}");
        Vehicle3DValidation.RequireUnitRange(DriveScale, $"{parameterName}.{nameof(DriveScale)}");
        Vehicle3DValidation.RequireUnitInterval(BrakeScale, $"{parameterName}.{nameof(BrakeScale)}");
        if (HasPhysicalWheel)
        {
            Vehicle3DValidation.RequireNonNegative(
                Joint.AxleMotor.MaximumForce,
                $"{parameterName}.{nameof(Joint)}.{nameof(Joint.AxleMotor)}.{nameof(Joint.AxleMotor.MaximumForce)}");
            Vehicle3DValidation.RequireNonNegative(
                Joint.AxleMotor.Softness,
                $"{parameterName}.{nameof(Joint)}.{nameof(Joint.AxleMotor)}.{nameof(Joint.AxleMotor.Softness)}");
            float maximumLongitudinalForce = MathF.Max(
                MaximumDriveForce * MathF.Abs(DriveScale),
                MaximumBrakeForce * BrakeScale);
            float requiredAxleMotorForce = maximumLongitudinalForce * RadiusCm;
            Vehicle3DValidation.RequireNonNegative(
                requiredAxleMotorForce,
                $"{parameterName}.RequiredAxleMotorForce");
            if (Joint.AxleMotor.MaximumForce < requiredAxleMotorForce)
            {
                throw new ArgumentException(
                    $"Physical and Box wheel axle motor maximum force {Joint.AxleMotor.MaximumForce} must cover " +
                    $"the maximum longitudinal tire torque {requiredAxleMotorForce} " +
                    $"(max(drive, brake) force {maximumLongitudinalForce} * radius {RadiusCm}).",
                    parameterName);
            }
        }
    }
}

public readonly struct Vehicle3DWheelState
{
    public Vehicle3DWheelState(
        Vehicle3DWheelId wheel,
        Vehicle3DVehicleId vehicle,
        Vehicle3DWheelKind kind,
        bool grounded,
        float suspensionLengthCm,
        float compressionCm,
        Vector3 contactPointCm,
        Vector3 contactNormal,
        Vector3 slipVelocityCmPerSecond,
        float longitudinalSpeedCmPerSecond,
        float lateralSpeedCmPerSecond,
        float suspensionForce,
        float wheelAngularSpeedRadiansPerSecond)
    {
        Wheel = wheel;
        Vehicle = vehicle;
        Kind = kind;
        Grounded = grounded;
        SuspensionLengthCm = suspensionLengthCm;
        CompressionCm = compressionCm;
        ContactPointCm = contactPointCm;
        ContactNormal = contactNormal;
        SlipVelocityCmPerSecond = slipVelocityCmPerSecond;
        LongitudinalSpeedCmPerSecond = longitudinalSpeedCmPerSecond;
        LateralSpeedCmPerSecond = lateralSpeedCmPerSecond;
        SuspensionForce = suspensionForce;
        WheelAngularSpeedRadiansPerSecond = wheelAngularSpeedRadiansPerSecond;
    }

    public Vehicle3DWheelId Wheel { get; }
    public Vehicle3DVehicleId Vehicle { get; }
    public Vehicle3DWheelKind Kind { get; }
    public bool Grounded { get; }
    public float SuspensionLengthCm { get; }
    public float CompressionCm { get; }
    public Vector3 ContactPointCm { get; }
    public Vector3 ContactNormal { get; }
    public Vector3 SlipVelocityCmPerSecond { get; }
    public float LongitudinalSpeedCmPerSecond { get; }
    public float LateralSpeedCmPerSecond { get; }
    public float SuspensionForce { get; }
    public float WheelAngularSpeedRadiansPerSecond { get; }
}

public sealed class Vehicle3DCapacityExceededException : InvalidOperationException
{
    public Vehicle3DCapacityExceededException(string resource, int capacity, int required)
        : base($"Vehicle3D capacity exceeded for '{resource}' (required: {required}, configured capacity: {capacity}).")
    {
        Resource = resource;
        Capacity = capacity;
        Required = required;
    }

    public string Resource { get; }
    public int Capacity { get; }
    public int Required { get; }
}

internal static class Vehicle3DValidation
{
    public static void RequirePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and greater than zero.");
        }
    }

    public static void RequireNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and non-negative.");
        }
    }

    public static void RequireUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be in the inclusive range [0, 1].");
        }
    }

    public static void RequireUnitRange(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < -1f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be in the inclusive range [-1, 1].");
        }
    }

    public static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector components must be finite.");
        }
    }

    public static Vector3 RequireUnitDirection(Vector3 value, string parameterName)
    {
        RequireFinite(value, parameterName);
        float lengthSquared = value.LengthSquared();
        if (!(lengthSquared > 0.999f && lengthSquared < 1.001f))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Direction must be normalized.");
        }

        return value;
    }
}
