using System;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Character3D;

/// <summary>
/// Fixed-capacity Character3D simulation lane. Registration is structural; SubmitIntent,
/// PrepareFixedStep, Physics3D Step, and ObserveFixedStep form the steady-state tick path.
/// </summary>
public sealed class Character3DControllerSet
{
    private readonly IPhysics3DWorld _world;
    private readonly int _capacity;
    private readonly Physics3DOverlapHit[] _overlapScratch;
    private readonly Physics3DCapsuleCastQuery[] _supportProbeRequests;
    private readonly Physics3DBatchedShapeCastClosestResult[] _supportProbeResults;
    private readonly int[] _supportProbeSlots;
    private readonly byte[] _active;
    private readonly int[] _generations;
    private readonly Physics3DBodyId[] _bodies;
    private readonly Physics3DBodyId[] _uprightAnchors;
    private readonly Physics3DConstraintId[] _uprightConstraints;
    private readonly float[] _radiiCm;
    private readonly float[] _cylinderLengthsCm;
    private readonly float[] _maximumGroundSpeeds;
    private readonly float[] _maximumGroundAccelerations;
    private readonly float[] _maximumAirSpeeds;
    private readonly float[] _maximumAirAccelerations;
    private readonly float[] _jumpSpeeds;
    private readonly float[] _minimumSupportNormalY;
    private readonly float[] _supportProbeDistances;
    private readonly float[] _skinWidths;
    private readonly float[] _maximumStepHeights;
    private readonly float[] _stepForwardProbeDistances;
    private readonly float[] _stepAssistSpeeds;
    private readonly int[] _coyoteTicks;
    private readonly LayerMask[] _queryLayers;
    private readonly Vector2[] _planarMoves;
    private readonly byte[] _jumpRequests;
    private readonly byte[] _hasVelocityOverrides;
    private readonly Vector3[] _targetVelocityOverrides;
    private readonly float[] _maximumOverrideAccelerations;
    private readonly byte[] _intentSubmitted;
    private readonly Character3DLocomotionMode[] _locomotionModes;
    private readonly Physics3DBodyId[] _supportBodies;
    private readonly Vector3[] _supportPointsCm;
    private readonly Vector3[] _supportNormals;
    private readonly Vector3[] _supportVelocities;
    private readonly byte[] _stepAssistActive;
    private readonly int[] _ticksSinceSupport;
    private readonly Vector3[] _observedPositions;
    private readonly Vector3[] _observedVelocities;

    private int _activeCount;
    private long _lastPreparedStepIndex = -1;

    public Character3DControllerSet(IPhysics3DWorld world, int capacity, int overlapHitCapacity)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Character capacity must be greater than zero.");
        }

        if (overlapHitCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapHitCapacity), overlapHitCapacity, "Overlap hit capacity must be greater than zero.");
        }

        _capacity = capacity;
        _overlapScratch = new Physics3DOverlapHit[overlapHitCapacity];
        _supportProbeRequests = new Physics3DCapsuleCastQuery[capacity];
        _supportProbeResults = new Physics3DBatchedShapeCastClosestResult[capacity];
        _supportProbeSlots = new int[capacity];
        _active = new byte[capacity];
        _generations = new int[capacity];
        _bodies = new Physics3DBodyId[capacity];
        _uprightAnchors = new Physics3DBodyId[capacity];
        _uprightConstraints = new Physics3DConstraintId[capacity];
        _radiiCm = new float[capacity];
        _cylinderLengthsCm = new float[capacity];
        _maximumGroundSpeeds = new float[capacity];
        _maximumGroundAccelerations = new float[capacity];
        _maximumAirSpeeds = new float[capacity];
        _maximumAirAccelerations = new float[capacity];
        _jumpSpeeds = new float[capacity];
        _minimumSupportNormalY = new float[capacity];
        _supportProbeDistances = new float[capacity];
        _skinWidths = new float[capacity];
        _maximumStepHeights = new float[capacity];
        _stepForwardProbeDistances = new float[capacity];
        _stepAssistSpeeds = new float[capacity];
        _coyoteTicks = new int[capacity];
        _queryLayers = new LayerMask[capacity];
        _planarMoves = new Vector2[capacity];
        _jumpRequests = new byte[capacity];
        _hasVelocityOverrides = new byte[capacity];
        _targetVelocityOverrides = new Vector3[capacity];
        _maximumOverrideAccelerations = new float[capacity];
        _intentSubmitted = new byte[capacity];
        _locomotionModes = new Character3DLocomotionMode[capacity];
        _supportBodies = new Physics3DBodyId[capacity];
        _supportPointsCm = new Vector3[capacity];
        _supportNormals = new Vector3[capacity];
        _supportVelocities = new Vector3[capacity];
        _stepAssistActive = new byte[capacity];
        _ticksSinceSupport = new int[capacity];
        _observedPositions = new Vector3[capacity];
        _observedVelocities = new Vector3[capacity];
    }

    public int Capacity => _capacity;
    public int ActiveCount => _activeCount;
    public int OverlapHitCapacity => _overlapScratch.Length;

    public Character3DHandle Register(
        Physics3DBodyId body,
        Physics3DBodyId uprightAnchor,
        in Character3DProfile profile)
    {
        profile.Validate(nameof(profile));
        RequireBody(body, Physics3DBodyKind.Dynamic, nameof(body));
        RequireBody(uprightAnchor, Physics3DBodyKind.Kinematic, nameof(uprightAnchor));
        if (body == uprightAnchor)
        {
            throw new ArgumentException("Character body and upright anchor must be distinct.", nameof(uprightAnchor));
        }

        int slot = FindFreeSlot();
        if (slot < 0)
        {
            throw new Character3DCapacityExceededException("registered characters", _capacity);
        }

        Physics3DConstraintId upright = _world.CreateAngularServoConstraint(
            body,
            uprightAnchor,
            new Physics3DAngularServoDescription(Quaternion.Identity, profile.UprightServo, profile.UprightSpring));
        if (!upright.IsValid)
        {
            throw new InvalidOperationException("Physics3D returned an invalid upright constraint id.");
        }

        int generation = unchecked(_generations[slot] + 1);
        if (generation <= 0)
        {
            generation = 1;
        }

        _generations[slot] = generation;
        _active[slot] = 1;
        _bodies[slot] = body;
        _uprightAnchors[slot] = uprightAnchor;
        _uprightConstraints[slot] = upright;
        _radiiCm[slot] = profile.RadiusCm;
        _cylinderLengthsCm[slot] = profile.CylinderLengthCm;
        _maximumGroundSpeeds[slot] = profile.MaximumGroundSpeedCmPerSecond;
        _maximumGroundAccelerations[slot] = profile.MaximumGroundAccelerationCmPerSecondSquared;
        _maximumAirSpeeds[slot] = profile.MaximumAirSpeedCmPerSecond;
        _maximumAirAccelerations[slot] = profile.MaximumAirAccelerationCmPerSecondSquared;
        _jumpSpeeds[slot] = profile.JumpSpeedCmPerSecond;
        _minimumSupportNormalY[slot] = MathF.Cos(profile.MaximumSlopeDegrees * (MathF.PI / 180f));
        _supportProbeDistances[slot] = profile.SupportProbeDistanceCm;
        _skinWidths[slot] = profile.SkinWidthCm;
        _maximumStepHeights[slot] = profile.MaximumStepHeightCm;
        _stepForwardProbeDistances[slot] = profile.StepForwardProbeDistanceCm;
        _stepAssistSpeeds[slot] = profile.StepAssistSpeedCmPerSecond;
        _coyoteTicks[slot] = profile.CoyoteTicks;
        _queryLayers[slot] = profile.QueryLayer;
        _locomotionModes[slot] = Character3DLocomotionMode.Airborne;
        _ticksSinceSupport[slot] = profile.CoyoteTicks + 1;
        Physics3DBodyState initialState = _world.GetBodyState(body);
        _observedPositions[slot] = initialState.PositionCm;
        _observedVelocities[slot] = initialState.LinearVelocityCmPerSecond;
        _activeCount++;
        return new Character3DHandle(slot, generation);
    }

    public void Unregister(Character3DHandle handle)
    {
        int slot = RequireSlot(handle);
        Physics3DConstraintId upright = _uprightConstraints[slot];
        if (!_world.ContainsConstraint(upright))
        {
            throw new InvalidOperationException($"Character '{handle}' lost upright constraint '{upright}'.");
        }

        _world.DestroyConstraint(upright);
        _active[slot] = 0;
        _bodies[slot] = default;
        _uprightAnchors[slot] = default;
        _uprightConstraints[slot] = default;
        _intentSubmitted[slot] = 0;
        _supportBodies[slot] = default;
        _activeCount--;
    }

    public void SubmitIntent(Character3DHandle handle, in Character3DIntent intent)
    {
        int slot = RequireSlot(handle);
        intent.Validate(nameof(intent));
        if (_intentSubmitted[slot] != 0)
        {
            throw new InvalidOperationException($"Character '{handle}' received more than one intent before the next fixed step.");
        }

        _planarMoves[slot] = intent.PlanarMove;
        _jumpRequests[slot] = intent.JumpRequested ? (byte)1 : (byte)0;
        _hasVelocityOverrides[slot] = intent.HasVelocityOverride ? (byte)1 : (byte)0;
        _targetVelocityOverrides[slot] = intent.TargetVelocityCmPerSecond;
        _maximumOverrideAccelerations[slot] = intent.MaximumOverrideAccelerationCmPerSecondSquared;
        _intentSubmitted[slot] = 1;
    }

    public Character3DState GetState(Character3DHandle handle)
    {
        int slot = RequireSlot(handle);
        return new Character3DState(
            _bodies[slot],
            _locomotionModes[slot],
            _observedPositions[slot],
            _observedVelocities[slot],
            _supportBodies[slot],
            _supportPointsCm[slot],
            _supportNormals[slot],
            _supportVelocities[slot],
            _stepAssistActive[slot] != 0,
            _ticksSinceSupport[slot]);
    }

    public Character3DGeometry GetGeometry(Character3DHandle handle)
    {
        int slot = RequireSlot(handle);
        return new Character3DGeometry(
            _bodies[slot],
            _radiiCm[slot],
            _cylinderLengthsCm[slot],
            _queryLayers[slot]);
    }

    public void PrepareFixedStep()
    {
        if (_lastPreparedStepIndex == _world.StepIndex)
        {
            throw new InvalidOperationException(
                $"Character3D fixed step {_world.StepIndex} was prepared more than once before Physics3D advanced.");
        }

        ValidateBatchBeforeMutation();
        ExecuteSupportProbeBatch();
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_active[slot] == 0)
            {
                continue;
            }

            PrepareCharacterActuation(slot);
            _intentSubmitted[slot] = 0;
        }

        _lastPreparedStepIndex = _world.StepIndex;
    }

    public void ObserveFixedStep()
    {
        if (_world.StepIndex <= _lastPreparedStepIndex)
        {
            throw new InvalidOperationException(
                $"Character3D observation requires Physics3D to advance beyond prepared step {_lastPreparedStepIndex}; current step is {_world.StepIndex}.");
        }

        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_active[slot] == 0)
            {
                continue;
            }

            Physics3DBodyId body = _bodies[slot];
            if (!_world.ContainsBody(body))
            {
                throw new InvalidOperationException($"Character slot {slot} lost body '{body}' before observation.");
            }

            Physics3DBodyState state = _world.GetBodyState(body);
            _observedPositions[slot] = state.PositionCm;
            _observedVelocities[slot] = state.LinearVelocityCmPerSecond;
        }
    }

    private void ValidateBatchBeforeMutation()
    {
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_active[slot] == 0)
            {
                continue;
            }

            if (_intentSubmitted[slot] == 0)
            {
                throw new InvalidOperationException(
                    $"Character slot {slot} has no intent for Physics3D step {_world.StepIndex}. Missing input is not replaced by a default intent.");
            }

            RequireCharacterBody(slot, _bodies[slot], Physics3DBodyKind.Dynamic, uprightAnchor: false);
            RequireCharacterBody(slot, _uprightAnchors[slot], Physics3DBodyKind.Kinematic, uprightAnchor: true);
            if (!_world.ContainsConstraint(_uprightConstraints[slot]))
            {
                throw new InvalidOperationException(
                    $"Character slot {slot} lost upright constraint '{_uprightConstraints[slot]}'.");
            }
        }
    }

    private void ExecuteSupportProbeBatch()
    {
        int requestCount = 0;
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_active[slot] == 0)
            {
                continue;
            }

            Physics3DBodyId body = _bodies[slot];
            Physics3DBodyState state = _world.GetBodyState(body);
            _observedPositions[slot] = state.PositionCm;
            _observedVelocities[slot] = state.LinearVelocityCmPerSecond;
            var filter = new Physics3DQueryFilter(_queryLayers[slot], body);
            _supportProbeRequests[requestCount] = new Physics3DCapsuleCastQuery(
                state.PositionCm + (Vector3.UnitY * _skinWidths[slot]),
                _radiiCm[slot],
                _cylinderLengthsCm[slot],
                Quaternion.Identity,
                -Vector3.UnitY,
                _supportProbeDistances[slot] + _skinWidths[slot],
                filter);
            _supportProbeSlots[requestCount] = slot;
            requestCount++;
        }

        _world.CapsuleCastClosestBatch(
            _supportProbeRequests.AsSpan(0, requestCount),
            _supportProbeResults.AsSpan(0, requestCount));
        for (int requestIndex = 0; requestIndex < requestCount; requestIndex++)
        {
            int slot = _supportProbeSlots[requestIndex];
            Physics3DBatchedShapeCastClosestResult result = _supportProbeResults[requestIndex];
            Physics3DShapeCastHit hit = result.Value;
            bool walkable = result.Hit && hit.Normal.Y >= _minimumSupportNormalY[slot];
            if (walkable)
            {
                _supportBodies[slot] = hit.Body;
                _supportPointsCm[slot] = hit.PositionCm;
                _supportNormals[slot] = hit.Normal;
                _supportVelocities[slot] = _world.GetBodyVelocityAtWorldPoint(hit.Body, hit.PositionCm);
                _ticksSinceSupport[slot] = 0;
                _locomotionModes[slot] = _hasVelocityOverrides[slot] != 0
                    ? Character3DLocomotionMode.Traversal
                    : Character3DLocomotionMode.Grounded;
            }
            else
            {
                _supportBodies[slot] = default;
                _supportPointsCm[slot] = default;
                _supportNormals[slot] = Vector3.UnitY;
                _supportVelocities[slot] = default;
                _ticksSinceSupport[slot] = checked(_ticksSinceSupport[slot] + 1);
                _locomotionModes[slot] = _hasVelocityOverrides[slot] != 0
                    ? Character3DLocomotionMode.Traversal
                    : Character3DLocomotionMode.Airborne;
            }
        }
    }

    private void PrepareCharacterActuation(int slot)
    {
        Physics3DBodyState bodyState = _world.GetBodyState(_bodies[slot]);
        Vector3 acceleration;
        _stepAssistActive[slot] = 0;
        if (_hasVelocityOverrides[slot] != 0)
        {
            Vector3 velocityError = _targetVelocityOverrides[slot] - bodyState.LinearVelocityCmPerSecond;
            acceleration = ClampMagnitude(
                velocityError / _world.FixedDeltaSeconds,
                _maximumOverrideAccelerations[slot]);
        }
        else
        {
            bool supported = _locomotionModes[slot] == Character3DLocomotionMode.Grounded;
            Vector2 move = _planarMoves[slot];
            float maximumSpeed = supported ? _maximumGroundSpeeds[slot] : _maximumAirSpeeds[slot];
            Vector3 desiredRelativeVelocity = new(move.X * maximumSpeed, 0f, move.Y * maximumSpeed);
            if (supported)
            {
                Vector3 normal = _supportNormals[slot];
                desiredRelativeVelocity -= normal * Vector3.Dot(desiredRelativeVelocity, normal);
            }

            Vector3 desiredWorldVelocity = desiredRelativeVelocity + _supportVelocities[slot];
            Vector3 velocityError = desiredWorldVelocity - bodyState.LinearVelocityCmPerSecond;
            if (!supported)
            {
                velocityError.Y = 0f;
            }

            float maximumAcceleration = supported
                ? _maximumGroundAccelerations[slot]
                : _maximumAirAccelerations[slot];
            acceleration = ClampMagnitude(velocityError / _world.FixedDeltaSeconds, maximumAcceleration);

            bool canJump = _ticksSinceSupport[slot] <= _coyoteTicks[slot];
            if (_jumpRequests[slot] != 0 && canJump)
            {
                float inheritedVerticalVelocity = _supportVelocities[slot].Y;
                float targetVerticalVelocity = inheritedVerticalVelocity + _jumpSpeeds[slot];
                acceleration.Y = (targetVerticalVelocity - bodyState.LinearVelocityCmPerSecond.Y) /
                                 _world.FixedDeltaSeconds;
                _ticksSinceSupport[slot] = _coyoteTicks[slot] + 1;
                _locomotionModes[slot] = Character3DLocomotionMode.Airborne;
            }
            else if (supported && move.LengthSquared() > 1e-6f && CanStep(slot, in bodyState, desiredRelativeVelocity))
            {
                float targetVerticalVelocity = _supportVelocities[slot].Y + _stepAssistSpeeds[slot];
                acceleration.Y = MathF.Max(
                    acceleration.Y,
                    (targetVerticalVelocity - bodyState.LinearVelocityCmPerSecond.Y) / _world.FixedDeltaSeconds);
                _stepAssistActive[slot] = 1;
            }
        }

        if (acceleration.LengthSquared() > 1e-8f)
        {
            _world.EnqueueAcceleration(_bodies[slot], acceleration);
        }
    }

    private bool CanStep(int slot, in Physics3DBodyState bodyState, Vector3 desiredRelativeVelocity)
    {
        Vector3 forward = Vector3.Normalize(new Vector3(desiredRelativeVelocity.X, 0f, desiredRelativeVelocity.Z));
        float halfHeight = (_cylinderLengthsCm[slot] * 0.5f) + _radiiCm[slot];
        Vector3 foot = bodyState.PositionCm - (Vector3.UnitY * halfHeight);
        Vector3 shinOrigin = foot + (Vector3.UnitY * MathF.Min(_maximumStepHeights[slot] * 0.5f, halfHeight));
        var filter = new Physics3DQueryFilter(_queryLayers[slot], _bodies[slot]);
        bool blocked = _world.RaycastClosest(
            shinOrigin,
            forward,
            _radiiCm[slot] + _stepForwardProbeDistances[slot],
            filter,
            out Physics3DRaycastHit obstacle);
        if (!blocked || obstacle.Normal.Y >= _minimumSupportNormalY[slot])
        {
            return false;
        }

        Vector3 raisedCenter = bodyState.PositionCm + (Vector3.UnitY * _maximumStepHeights[slot]);
        if (_world.OverlapCapsule(
                raisedCenter,
                _radiiCm[slot],
                _cylinderLengthsCm[slot],
                Quaternion.Identity,
                filter,
                _overlapScratch) != 0)
        {
            return false;
        }

        if (_world.CapsuleCastAny(
                raisedCenter,
                _radiiCm[slot],
                _cylinderLengthsCm[slot],
                Quaternion.Identity,
                forward,
                _stepForwardProbeDistances[slot],
                filter))
        {
            return false;
        }

        Vector3 landingProbeCenter = raisedCenter + (forward * _stepForwardProbeDistances[slot]);
        bool foundLanding = _world.CapsuleCastClosest(
            landingProbeCenter,
            _radiiCm[slot],
            _cylinderLengthsCm[slot],
            Quaternion.Identity,
            -Vector3.UnitY,
            _maximumStepHeights[slot] + _supportProbeDistances[slot],
            filter,
            out Physics3DShapeCastHit landing);
        return foundLanding && landing.Normal.Y >= _minimumSupportNormalY[slot];
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (_active[i] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int RequireSlot(Character3DHandle handle)
    {
        if ((uint)handle.Slot >= (uint)_capacity ||
            _active[handle.Slot] == 0 ||
            _generations[handle.Slot] != handle.Generation)
        {
            throw new InvalidOperationException($"Character handle '{handle}' is stale or not registered.");
        }

        return handle.Slot;
    }

    private void RequireBody(Physics3DBodyId body, Physics3DBodyKind expectedKind, string parameterName)
    {
        if (!body.IsValid || !_world.ContainsBody(body))
        {
            throw new InvalidOperationException($"{parameterName} '{body}' is invalid or no longer exists.");
        }

        Physics3DBodyKind actualKind = _world.GetBodyKind(body);
        if (actualKind != expectedKind)
        {
            throw new InvalidOperationException(
                $"{parameterName} '{body}' must be {expectedKind}, but is {actualKind}.");
        }
    }

    private void RequireCharacterBody(
        int slot,
        Physics3DBodyId body,
        Physics3DBodyKind expectedKind,
        bool uprightAnchor)
    {
        if (!body.IsValid || !_world.ContainsBody(body))
        {
            string field = uprightAnchor ? "upright anchor" : "body";
            throw new InvalidOperationException($"Character slot {slot} {field} '{body}' is invalid or no longer exists.");
        }

        Physics3DBodyKind actualKind = _world.GetBodyKind(body);
        if (actualKind != expectedKind)
        {
            string field = uprightAnchor ? "upright anchor" : "body";
            throw new InvalidOperationException(
                $"Character slot {slot} {field} '{body}' must be {expectedKind}, but is {actualKind}.");
        }
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximumMagnitude)
    {
        float lengthSquared = value.LengthSquared();
        float maximumSquared = maximumMagnitude * maximumMagnitude;
        if (lengthSquared <= maximumSquared)
        {
            return value;
        }

        return value * (maximumMagnitude / MathF.Sqrt(lengthSquared));
    }
}
