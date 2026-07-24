using System;
using System.Numerics;

namespace Ludots.Core.Physics3D;

public readonly struct Physics3DBoxWindField
{
    public Physics3DBoxWindField(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 windVelocityCmPerSecond,
        float forcePerRelativeSpeed)
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Physics3DValidation.RequireFinite(sizeCm, nameof(sizeCm));
        Physics3DValidation.RequireFinitePositive(sizeCm.X, $"{nameof(sizeCm)}.X");
        Physics3DValidation.RequireFinitePositive(sizeCm.Y, $"{nameof(sizeCm)}.Y");
        Physics3DValidation.RequireFinitePositive(sizeCm.Z, $"{nameof(sizeCm)}.Z");
        Physics3DValidation.RequireFinite(windVelocityCmPerSecond, nameof(windVelocityCmPerSecond));
        Physics3DValidation.RequireFiniteNonNegative(forcePerRelativeSpeed, nameof(forcePerRelativeSpeed));
        CenterCm = centerCm;
        HalfSizeCm = sizeCm * 0.5f;
        Orientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        WindVelocityCmPerSecond = windVelocityCmPerSecond;
        ForcePerRelativeSpeed = forcePerRelativeSpeed;
    }

    public Vector3 CenterCm { get; }
    public Vector3 HalfSizeCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 WindVelocityCmPerSecond { get; }
    public float ForcePerRelativeSpeed { get; }
}

public readonly struct Physics3DSphereWindField
{
    public Physics3DSphereWindField(
        Vector3 centerCm,
        float radiusCm,
        Vector3 windVelocityCmPerSecond,
        float forcePerRelativeSpeed)
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFinite(windVelocityCmPerSecond, nameof(windVelocityCmPerSecond));
        Physics3DValidation.RequireFiniteNonNegative(forcePerRelativeSpeed, nameof(forcePerRelativeSpeed));
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        WindVelocityCmPerSecond = windVelocityCmPerSecond;
        ForcePerRelativeSpeed = forcePerRelativeSpeed;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public Vector3 WindVelocityCmPerSecond { get; }
    public float ForcePerRelativeSpeed { get; }
}

public readonly struct Physics3DBoxGustField
{
    public Physics3DBoxGustField(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 baseWindVelocityCmPerSecond,
        Vector3 peakWindVelocityCmPerSecond,
        float forcePerRelativeSpeed,
        int attackTicks,
        int holdTicks,
        int releaseTicks,
        int calmTicks,
        int phaseOffsetTicks = 0)
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Physics3DValidation.RequireFinite(sizeCm, nameof(sizeCm));
        Physics3DValidation.RequireFinitePositive(sizeCm.X, $"{nameof(sizeCm)}.X");
        Physics3DValidation.RequireFinitePositive(sizeCm.Y, $"{nameof(sizeCm)}.Y");
        Physics3DValidation.RequireFinitePositive(sizeCm.Z, $"{nameof(sizeCm)}.Z");
        Physics3DValidation.RequireFinite(baseWindVelocityCmPerSecond, nameof(baseWindVelocityCmPerSecond));
        Physics3DValidation.RequireFinite(peakWindVelocityCmPerSecond, nameof(peakWindVelocityCmPerSecond));
        Physics3DValidation.RequireFiniteNonNegative(forcePerRelativeSpeed, nameof(forcePerRelativeSpeed));
        if (attackTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackTicks), attackTicks, "Attack ticks must be positive.");
        }

        if (holdTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdTicks), holdTicks, "Hold ticks must be positive.");
        }

        if (releaseTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseTicks), releaseTicks, "Release ticks must be positive.");
        }

        if (calmTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calmTicks), calmTicks, "Calm ticks cannot be negative.");
        }

        int cycleTicks = checked(attackTicks + holdTicks + releaseTicks + calmTicks);
        if (phaseOffsetTicks < 0 || phaseOffsetTicks >= cycleTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phaseOffsetTicks),
                phaseOffsetTicks,
                $"Phase offset must be in the range [0, {cycleTicks - 1}].");
        }

        CenterCm = centerCm;
        HalfSizeCm = sizeCm * 0.5f;
        Orientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        BaseWindVelocityCmPerSecond = baseWindVelocityCmPerSecond;
        PeakWindVelocityCmPerSecond = peakWindVelocityCmPerSecond;
        ForcePerRelativeSpeed = forcePerRelativeSpeed;
        AttackTicks = attackTicks;
        HoldTicks = holdTicks;
        ReleaseTicks = releaseTicks;
        CalmTicks = calmTicks;
        PhaseOffsetTicks = phaseOffsetTicks;
    }

    public Vector3 CenterCm { get; }
    public Vector3 HalfSizeCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 BaseWindVelocityCmPerSecond { get; }
    public Vector3 PeakWindVelocityCmPerSecond { get; }
    public float ForcePerRelativeSpeed { get; }
    public int AttackTicks { get; }
    public int HoldTicks { get; }
    public int ReleaseTicks { get; }
    public int CalmTicks { get; }
    public int PhaseOffsetTicks { get; }
}

public readonly struct Physics3DVortexWindField
{
    public Physics3DVortexWindField(
        Vector3 centerCm,
        float radiusCm,
        Vector3 axis,
        float tangentialSpeedCmPerSecond,
        float axialSpeedCmPerSecond,
        float forcePerRelativeSpeed,
        bool linearFalloff)
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFinite(axis, nameof(axis));
        if (!(axis.LengthSquared() > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Vortex axis length must be greater than zero.");
        }

        Physics3DValidation.RequireFinite(tangentialSpeedCmPerSecond, nameof(tangentialSpeedCmPerSecond));
        Physics3DValidation.RequireFinite(axialSpeedCmPerSecond, nameof(axialSpeedCmPerSecond));
        Physics3DValidation.RequireFiniteNonNegative(forcePerRelativeSpeed, nameof(forcePerRelativeSpeed));
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        Axis = Vector3.Normalize(axis);
        TangentialSpeedCmPerSecond = tangentialSpeedCmPerSecond;
        AxialSpeedCmPerSecond = axialSpeedCmPerSecond;
        ForcePerRelativeSpeed = forcePerRelativeSpeed;
        LinearFalloff = linearFalloff;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public Vector3 Axis { get; }
    public float TangentialSpeedCmPerSecond { get; }
    public float AxialSpeedCmPerSecond { get; }
    public float ForcePerRelativeSpeed { get; }
    public bool LinearFalloff { get; }
}

public readonly struct Physics3DRadialForceField
{
    public Physics3DRadialForceField(
        Vector3 centerCm,
        float radiusCm,
        float forceAtCenter,
        Vector3 centerDirection,
        bool linearFalloff)
    {
        Validate(centerCm, radiusCm, forceAtCenter, centerDirection);
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        ForceAtCenter = forceAtCenter;
        CenterDirection = Vector3.Normalize(centerDirection);
        LinearFalloff = linearFalloff;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public float ForceAtCenter { get; }
    public Vector3 CenterDirection { get; }
    public bool LinearFalloff { get; }

    internal static void Validate(Vector3 centerCm, float radiusCm, float magnitude, Vector3 centerDirection)
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFinite(magnitude, nameof(magnitude));
        Physics3DValidation.RequireFinite(centerDirection, nameof(centerDirection));
        if (!(centerDirection.LengthSquared() > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(nameof(centerDirection), centerDirection, "Center direction length must be greater than zero.");
        }
    }
}

public readonly struct Physics3DPointBurst
{
    public Physics3DPointBurst(
        Vector3 centerCm,
        float radiusCm,
        float impulseAtCenter,
        Vector3 centerDirection,
        bool linearFalloff)
    {
        Physics3DRadialForceField.Validate(centerCm, radiusCm, impulseAtCenter, centerDirection);
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        ImpulseAtCenter = impulseAtCenter;
        CenterDirection = Vector3.Normalize(centerDirection);
        LinearFalloff = linearFalloff;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public float ImpulseAtCenter { get; }
    public Vector3 CenterDirection { get; }
    public bool LinearFalloff { get; }
}

public sealed class Physics3DForceFieldSet
{
    private enum FieldKind : byte
    {
        BoxWind = 1,
        SphereWind = 2,
        RadialForce = 3,
        PointBurst = 4,
        BoxGust = 5,
        VortexWind = 6
    }

    private readonly FieldKind[] _kinds;
    private readonly Vector3[] _centersCm;
    private readonly Vector3[] _halfSizesOrCenterDirections;
    private readonly Quaternion[] _orientations;
    private readonly Vector3[] _windVelocitiesCmPerSecond;
    private readonly Vector3[] _secondaryWindVelocitiesCmPerSecond;
    private readonly float[] _radiiCm;
    private readonly float[] _magnitudes;
    private readonly float[] _secondaryMagnitudes;
    private readonly float[] _tertiaryMagnitudes;
    private readonly bool[] _linearFalloff;
    private readonly int[] _attackTicks;
    private readonly int[] _holdTicks;
    private readonly int[] _releaseTicks;
    private readonly int[] _calmTicks;
    private readonly int[] _phaseOffsetTicks;
    private readonly Physics3DBodyId[] _affectedBodies;
    private readonly Vector3[] _forces;
    private readonly Vector3[] _impulses;

    public Physics3DForceFieldSet(int fieldCapacity, int awakeBodyCapacity)
    {
        if (fieldCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldCapacity));
        }

        if (awakeBodyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(awakeBodyCapacity));
        }

        _kinds = new FieldKind[fieldCapacity];
        _centersCm = new Vector3[fieldCapacity];
        _halfSizesOrCenterDirections = new Vector3[fieldCapacity];
        _orientations = new Quaternion[fieldCapacity];
        _windVelocitiesCmPerSecond = new Vector3[fieldCapacity];
        _secondaryWindVelocitiesCmPerSecond = new Vector3[fieldCapacity];
        _radiiCm = new float[fieldCapacity];
        _magnitudes = new float[fieldCapacity];
        _secondaryMagnitudes = new float[fieldCapacity];
        _tertiaryMagnitudes = new float[fieldCapacity];
        _linearFalloff = new bool[fieldCapacity];
        _attackTicks = new int[fieldCapacity];
        _holdTicks = new int[fieldCapacity];
        _releaseTicks = new int[fieldCapacity];
        _calmTicks = new int[fieldCapacity];
        _phaseOffsetTicks = new int[fieldCapacity];
        _affectedBodies = new Physics3DBodyId[awakeBodyCapacity];
        _forces = new Vector3[awakeBodyCapacity];
        _impulses = new Vector3[awakeBodyCapacity];
    }

    public int Capacity => _kinds.Length;
    public int AwakeBodyCapacity => _affectedBodies.Length;
    public int Count { get; private set; }

    public void Add(in Physics3DBoxWindField field)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.BoxWind;
        _centersCm[index] = field.CenterCm;
        _halfSizesOrCenterDirections[index] = field.HalfSizeCm;
        _orientations[index] = field.Orientation;
        _windVelocitiesCmPerSecond[index] = field.WindVelocityCmPerSecond;
        _magnitudes[index] = field.ForcePerRelativeSpeed;
    }

    public void Add(in Physics3DSphereWindField field)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.SphereWind;
        _centersCm[index] = field.CenterCm;
        _radiiCm[index] = field.RadiusCm;
        _windVelocitiesCmPerSecond[index] = field.WindVelocityCmPerSecond;
        _magnitudes[index] = field.ForcePerRelativeSpeed;
    }

    public void Add(in Physics3DBoxGustField field)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.BoxGust;
        _centersCm[index] = field.CenterCm;
        _halfSizesOrCenterDirections[index] = field.HalfSizeCm;
        _orientations[index] = field.Orientation;
        _windVelocitiesCmPerSecond[index] = field.BaseWindVelocityCmPerSecond;
        _secondaryWindVelocitiesCmPerSecond[index] = field.PeakWindVelocityCmPerSecond;
        _magnitudes[index] = field.ForcePerRelativeSpeed;
        _attackTicks[index] = field.AttackTicks;
        _holdTicks[index] = field.HoldTicks;
        _releaseTicks[index] = field.ReleaseTicks;
        _calmTicks[index] = field.CalmTicks;
        _phaseOffsetTicks[index] = field.PhaseOffsetTicks;
    }

    public void Add(in Physics3DVortexWindField field)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.VortexWind;
        _centersCm[index] = field.CenterCm;
        _radiiCm[index] = field.RadiusCm;
        _halfSizesOrCenterDirections[index] = field.Axis;
        _secondaryMagnitudes[index] = field.TangentialSpeedCmPerSecond;
        _tertiaryMagnitudes[index] = field.AxialSpeedCmPerSecond;
        _magnitudes[index] = field.ForcePerRelativeSpeed;
        _linearFalloff[index] = field.LinearFalloff;
    }

    public void Add(in Physics3DRadialForceField field)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.RadialForce;
        _centersCm[index] = field.CenterCm;
        _radiiCm[index] = field.RadiusCm;
        _magnitudes[index] = field.ForceAtCenter;
        _halfSizesOrCenterDirections[index] = field.CenterDirection;
        _linearFalloff[index] = field.LinearFalloff;
    }

    public void Add(in Physics3DPointBurst burst)
    {
        int index = Allocate();
        _kinds[index] = FieldKind.PointBurst;
        _centersCm[index] = burst.CenterCm;
        _radiiCm[index] = burst.RadiusCm;
        _magnitudes[index] = burst.ImpulseAtCenter;
        _halfSizesOrCenterDirections[index] = burst.CenterDirection;
        _linearFalloff[index] = burst.LinearFalloff;
    }

    public void Clear() => Count = 0;

    public void Apply(Physics3DAwakeBodyBuffer awakeBodies, IPhysics3DWorld world)
    {
        ArgumentNullException.ThrowIfNull(awakeBodies);
        ArgumentNullException.ThrowIfNull(world);
        if (awakeBodies.Count > AwakeBodyCapacity)
        {
            throw new Physics3DCapacityExceededException("force field awake bodies", AwakeBodyCapacity);
        }

        ReadOnlySpan<Physics3DBodyId> bodyIds = awakeBodies.BodyIds;
        ReadOnlySpan<Physics3DBodyKind> bodyKinds = awakeBodies.BodyKinds;
        ReadOnlySpan<Vector3> positions = awakeBodies.PositionsCm;
        ReadOnlySpan<Vector3> velocities = awakeBodies.LinearVelocitiesCmPerSecond;
        long targetTick = checked(world.StepIndex + 1);
        int affectedCount = 0;
        int requiredCommands = 0;
        for (int bodyIndex = 0; bodyIndex < awakeBodies.Count; bodyIndex++)
        {
            if (bodyKinds[bodyIndex] != Physics3DBodyKind.Dynamic)
            {
                continue;
            }

            Physics3DBodyId body = bodyIds[bodyIndex];
            if (!world.ContainsBody(body) || world.GetBodyKind(body) != Physics3DBodyKind.Dynamic)
            {
                throw new InvalidOperationException($"Force field awake buffer contains stale or non-dynamic body '{body}'.");
            }

            Vector3 force = Vector3.Zero;
            Vector3 impulse = Vector3.Zero;
            for (int fieldIndex = 0; fieldIndex < Count; fieldIndex++)
            {
                Accumulate(fieldIndex, targetTick, positions[bodyIndex], velocities[bodyIndex], ref force, ref impulse);
            }

            Physics3DValidation.RequireFinite(force, "forceFieldForce");
            Physics3DValidation.RequireFinite(impulse, "forceFieldImpulse");
            if (force == Vector3.Zero && impulse == Vector3.Zero)
            {
                continue;
            }

            _affectedBodies[affectedCount] = body;
            _forces[affectedCount] = force;
            _impulses[affectedCount] = impulse;
            requiredCommands += force != Vector3.Zero ? 1 : 0;
            requiredCommands += impulse != Vector3.Zero ? 1 : 0;
            affectedCount++;
        }

        if (requiredCommands > world.ActuationCommandCapacity - world.PendingActuationCommandCount)
        {
            throw new Physics3DCapacityExceededException("actuation commands", world.ActuationCommandCapacity);
        }

        for (int index = 0; index < affectedCount; index++)
        {
            if (_forces[index] != Vector3.Zero)
            {
                world.EnqueueForce(_affectedBodies[index], _forces[index]);
            }

            if (_impulses[index] != Vector3.Zero)
            {
                world.EnqueueLinearImpulse(_affectedBodies[index], _impulses[index]);
            }
        }

        RemoveConsumedPointBursts();
    }

    private int Allocate()
    {
        if (Count == Capacity)
        {
            throw new Physics3DCapacityExceededException("force fields", Capacity);
        }

        return Count++;
    }

    private void Accumulate(
        int fieldIndex,
        long targetTick,
        Vector3 positionCm,
        Vector3 velocityCmPerSecond,
        ref Vector3 force,
        ref Vector3 impulse)
    {
        Vector3 offset = positionCm - _centersCm[fieldIndex];
        switch (_kinds[fieldIndex])
        {
            case FieldKind.BoxWind:
                Vector3 local = Vector3.Transform(offset, Quaternion.Conjugate(_orientations[fieldIndex]));
                Vector3 halfSize = _halfSizesOrCenterDirections[fieldIndex];
                if (MathF.Abs(local.X) <= halfSize.X &&
                    MathF.Abs(local.Y) <= halfSize.Y &&
                    MathF.Abs(local.Z) <= halfSize.Z)
                {
                    force += (_windVelocitiesCmPerSecond[fieldIndex] - velocityCmPerSecond) * _magnitudes[fieldIndex];
                }

                break;
            case FieldKind.SphereWind:
                if (offset.LengthSquared() <= _radiiCm[fieldIndex] * _radiiCm[fieldIndex])
                {
                    force += (_windVelocitiesCmPerSecond[fieldIndex] - velocityCmPerSecond) * _magnitudes[fieldIndex];
                }

                break;
            case FieldKind.RadialForce:
                force += ComputeRadial(fieldIndex, offset);
                break;
            case FieldKind.PointBurst:
                impulse += ComputeRadial(fieldIndex, offset);
                break;
            case FieldKind.BoxGust:
                Vector3 gustLocal = Vector3.Transform(offset, Quaternion.Conjugate(_orientations[fieldIndex]));
                Vector3 gustHalfSize = _halfSizesOrCenterDirections[fieldIndex];
                if (MathF.Abs(gustLocal.X) <= gustHalfSize.X &&
                    MathF.Abs(gustLocal.Y) <= gustHalfSize.Y &&
                    MathF.Abs(gustLocal.Z) <= gustHalfSize.Z)
                {
                    float blend = EvaluateGustBlend(fieldIndex, targetTick);
                    Vector3 gustVelocity = Vector3.Lerp(
                        _windVelocitiesCmPerSecond[fieldIndex],
                        _secondaryWindVelocitiesCmPerSecond[fieldIndex],
                        blend);
                    force += (gustVelocity - velocityCmPerSecond) * _magnitudes[fieldIndex];
                }

                break;
            case FieldKind.VortexWind:
                force += ComputeVortex(fieldIndex, offset, velocityCmPerSecond);
                break;
            default:
                throw new InvalidOperationException($"Unknown Physics3D force field kind '{_kinds[fieldIndex]}'.");
        }
    }

    private float EvaluateGustBlend(int fieldIndex, long targetTick)
    {
        long cycleTicks = (long)_attackTicks[fieldIndex] +
            _holdTicks[fieldIndex] +
            _releaseTicks[fieldIndex] +
            _calmTicks[fieldIndex];
        long phase = ((targetTick - 1 + _phaseOffsetTicks[fieldIndex]) % cycleTicks + cycleTicks) % cycleTicks;
        int attackTicks = _attackTicks[fieldIndex];
        if (phase < attackTicks)
        {
            return (float)phase / attackTicks;
        }

        phase -= attackTicks;
        if (phase < _holdTicks[fieldIndex])
        {
            return 1f;
        }

        phase -= _holdTicks[fieldIndex];
        int releaseTicks = _releaseTicks[fieldIndex];
        if (phase < releaseTicks)
        {
            return 1f - ((float)(phase + 1) / releaseTicks);
        }

        return 0f;
    }

    private Vector3 ComputeVortex(int fieldIndex, Vector3 offset, Vector3 velocityCmPerSecond)
    {
        float distanceSquared = offset.LengthSquared();
        float radius = _radiiCm[fieldIndex];
        if (distanceSquared > radius * radius)
        {
            return Vector3.Zero;
        }

        Vector3 axis = _halfSizesOrCenterDirections[fieldIndex];
        Vector3 radial = offset - (axis * Vector3.Dot(offset, axis));
        float radialLengthSquared = radial.LengthSquared();
        Vector3 tangent = radialLengthSquared > 1e-12f
            ? Vector3.Cross(axis, radial) / MathF.Sqrt(radialLengthSquared)
            : Vector3.Zero;
        float distance = MathF.Sqrt(distanceSquared);
        float scale = _linearFalloff[fieldIndex] ? 1f - (distance / radius) : 1f;
        Vector3 targetWind = (
            tangent * _secondaryMagnitudes[fieldIndex] +
            axis * _tertiaryMagnitudes[fieldIndex]) * scale;
        return (targetWind - velocityCmPerSecond) * _magnitudes[fieldIndex];
    }

    private Vector3 ComputeRadial(int fieldIndex, Vector3 offset)
    {
        float distanceSquared = offset.LengthSquared();
        float radius = _radiiCm[fieldIndex];
        if (distanceSquared > radius * radius)
        {
            return Vector3.Zero;
        }

        float distance = MathF.Sqrt(distanceSquared);
        Vector3 direction = distance > 1e-6f
            ? offset / distance
            : _halfSizesOrCenterDirections[fieldIndex];
        float scale = _linearFalloff[fieldIndex] ? 1f - distance / radius : 1f;
        return direction * (_magnitudes[fieldIndex] * scale);
    }

    private void RemoveConsumedPointBursts()
    {
        int output = 0;
        for (int input = 0; input < Count; input++)
        {
            if (_kinds[input] == FieldKind.PointBurst)
            {
                continue;
            }

            if (output != input)
            {
                _kinds[output] = _kinds[input];
                _centersCm[output] = _centersCm[input];
                _halfSizesOrCenterDirections[output] = _halfSizesOrCenterDirections[input];
                _orientations[output] = _orientations[input];
                _windVelocitiesCmPerSecond[output] = _windVelocitiesCmPerSecond[input];
                _secondaryWindVelocitiesCmPerSecond[output] = _secondaryWindVelocitiesCmPerSecond[input];
                _radiiCm[output] = _radiiCm[input];
                _magnitudes[output] = _magnitudes[input];
                _secondaryMagnitudes[output] = _secondaryMagnitudes[input];
                _tertiaryMagnitudes[output] = _tertiaryMagnitudes[input];
                _linearFalloff[output] = _linearFalloff[input];
                _attackTicks[output] = _attackTicks[input];
                _holdTicks[output] = _holdTicks[input];
                _releaseTicks[output] = _releaseTicks[input];
                _calmTicks[output] = _calmTicks[input];
                _phaseOffsetTicks[output] = _phaseOffsetTicks[input];
            }

            output++;
        }

        Count = output;
    }
}
