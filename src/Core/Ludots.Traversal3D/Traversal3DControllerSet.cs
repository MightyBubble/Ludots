using System;
using System.Numerics;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Traversal3D;

/// <summary>
/// Fixed-capacity traversal state machine. It consumes Physics3D queries and submits exactly one
/// Character3D intent per registered controller; it does not own a world, query engine, or clock.
/// </summary>
public sealed class Traversal3DControllerSet
{
    private readonly IPhysics3DWorld _world;
    private readonly Character3DControllerSet _characters;
    private readonly int _capacity;
    private readonly Physics3DOverlapHit[] _overlapScratch;
    private readonly byte[] _active;
    private readonly int[] _generations;
    private readonly Character3DHandle[] _characterHandles;
    private readonly Physics3DBodyId[] _characterBodies;
    private readonly float[] _attachProbeDistances;
    private readonly float[] _attachSpeeds;
    private readonly float[] _climbSpeeds;
    private readonly float[] _lateralSpeeds;
    private readonly float[] _maximumAccelerations;
    private readonly float[] _ledgeProbeHeights;
    private readonly float[] _ledgeProbeForwards;
    private readonly float[] _ledgeProbeDowns;
    private readonly float[] _minimumLedgeHeights;
    private readonly float[] _handClearanceRadii;
    private readonly float[] _mantleForwards;
    private readonly float[] _mantleSpeeds;
    private readonly float[] _mantleCompletionDistances;
    private readonly float[] _minimumTopNormalYs;
    private readonly float[] _detachUpSpeeds;
    private readonly float[] _detachOutSpeeds;
    private readonly Vector2[] _moves;
    private readonly Vector3[] _facingDirections;
    private readonly byte[] _engageRequests;
    private readonly byte[] _jumpRequests;
    private readonly byte[] _intentSubmitted;
    private readonly Traversal3DState[] _states;
    private readonly int[] _stateTicks;
    private readonly Physics3DBodyId[] _surfaceBodies;
    private readonly Traversal3DSurfaceKind[] _surfaceKinds;
    private readonly Vector3[] _surfaceNormals;
    private readonly Vector3[] _targetPositions;
    private readonly Vector3[] _mantleTargets;
    private readonly byte[] _clearanceValid;
    private readonly int[] _surfaceGenerationsByBodySlot;
    private readonly Traversal3DSurfaceKind[] _surfaceKindsByBodySlot;

    private int _activeCount;
    private int _registeredSurfaceCount;
    private long _lastPreparedStepIndex = -1;

    public Traversal3DControllerSet(
        IPhysics3DWorld world,
        Character3DControllerSet characters,
        int capacity,
        int bodySlotCapacity,
        int overlapHitCapacity)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Traversal capacity must be greater than zero.");
        }

        if (bodySlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodySlotCapacity), bodySlotCapacity, "Body slot capacity must be greater than zero.");
        }

        if (overlapHitCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapHitCapacity), overlapHitCapacity, "Overlap hit capacity must be greater than zero.");
        }

        _capacity = capacity;
        _overlapScratch = new Physics3DOverlapHit[overlapHitCapacity];
        _active = new byte[capacity];
        _generations = new int[capacity];
        _characterHandles = new Character3DHandle[capacity];
        _characterBodies = new Physics3DBodyId[capacity];
        _attachProbeDistances = new float[capacity];
        _attachSpeeds = new float[capacity];
        _climbSpeeds = new float[capacity];
        _lateralSpeeds = new float[capacity];
        _maximumAccelerations = new float[capacity];
        _ledgeProbeHeights = new float[capacity];
        _ledgeProbeForwards = new float[capacity];
        _ledgeProbeDowns = new float[capacity];
        _minimumLedgeHeights = new float[capacity];
        _handClearanceRadii = new float[capacity];
        _mantleForwards = new float[capacity];
        _mantleSpeeds = new float[capacity];
        _mantleCompletionDistances = new float[capacity];
        _minimumTopNormalYs = new float[capacity];
        _detachUpSpeeds = new float[capacity];
        _detachOutSpeeds = new float[capacity];
        _moves = new Vector2[capacity];
        _facingDirections = new Vector3[capacity];
        _engageRequests = new byte[capacity];
        _jumpRequests = new byte[capacity];
        _intentSubmitted = new byte[capacity];
        _states = new Traversal3DState[capacity];
        _stateTicks = new int[capacity];
        _surfaceBodies = new Physics3DBodyId[capacity];
        _surfaceKinds = new Traversal3DSurfaceKind[capacity];
        _surfaceNormals = new Vector3[capacity];
        _targetPositions = new Vector3[capacity];
        _mantleTargets = new Vector3[capacity];
        _clearanceValid = new byte[capacity];
        _surfaceGenerationsByBodySlot = new int[bodySlotCapacity];
        _surfaceKindsByBodySlot = new Traversal3DSurfaceKind[bodySlotCapacity];
    }

    public int Capacity => _capacity;
    public int ActiveCount => _activeCount;
    public int RegisteredSurfaceCount => _registeredSurfaceCount;

    public void RegisterSurface(Physics3DBodyId body, Traversal3DSurfaceKind kind)
    {
        RequireLiveBody(body, nameof(body));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown traversal surface kind.");
        }

        if ((uint)body.Slot >= (uint)_surfaceGenerationsByBodySlot.Length)
        {
            throw new Traversal3DCapacityExceededException(
                $"surface body slot {body.Slot}",
                _surfaceGenerationsByBodySlot.Length);
        }

        if (_surfaceGenerationsByBodySlot[body.Slot] != 0)
        {
            throw new InvalidOperationException($"Traversal surface body slot {body.Slot} is already registered.");
        }

        _surfaceGenerationsByBodySlot[body.Slot] = body.Generation;
        _surfaceKindsByBodySlot[body.Slot] = kind;
        _registeredSurfaceCount++;
    }

    public void UnregisterSurface(Physics3DBodyId body)
    {
        if (!TryGetSurfaceKind(body, out _))
        {
            throw new InvalidOperationException($"Traversal surface '{body}' is not registered or is stale.");
        }

        _surfaceGenerationsByBodySlot[body.Slot] = 0;
        _surfaceKindsByBodySlot[body.Slot] = default;
        _registeredSurfaceCount--;
    }

    public Traversal3DHandle RegisterCharacter(Character3DHandle character, in Traversal3DProfile profile)
    {
        profile.Validate(nameof(profile));
        Character3DGeometry geometry = _characters.GetGeometry(character);
        RequireLiveBody(geometry.Body, nameof(character));
        int slot = FindFreeSlot();
        if (slot < 0)
        {
            throw new Traversal3DCapacityExceededException("registered traversal controllers", _capacity);
        }

        int generation = unchecked(_generations[slot] + 1);
        if (generation <= 0)
        {
            generation = 1;
        }

        _generations[slot] = generation;
        _active[slot] = 1;
        _characterHandles[slot] = character;
        _characterBodies[slot] = geometry.Body;
        _attachProbeDistances[slot] = profile.AttachProbeDistanceCm;
        _attachSpeeds[slot] = profile.AttachSpeedCmPerSecond;
        _climbSpeeds[slot] = profile.ClimbSpeedCmPerSecond;
        _lateralSpeeds[slot] = profile.LateralSpeedCmPerSecond;
        _maximumAccelerations[slot] = profile.MaximumAccelerationCmPerSecondSquared;
        _ledgeProbeHeights[slot] = profile.LedgeProbeHeightCm;
        _ledgeProbeForwards[slot] = profile.LedgeProbeForwardCm;
        _ledgeProbeDowns[slot] = profile.LedgeProbeDownCm;
        _minimumLedgeHeights[slot] = profile.MinimumLedgeHeightCm;
        _handClearanceRadii[slot] = profile.HandClearanceRadiusCm;
        _mantleForwards[slot] = profile.MantleForwardCm;
        _mantleSpeeds[slot] = profile.MantleSpeedCmPerSecond;
        _mantleCompletionDistances[slot] = profile.MantleCompletionDistanceCm;
        _minimumTopNormalYs[slot] = profile.MinimumTopNormalY;
        _detachUpSpeeds[slot] = profile.DetachUpSpeedCmPerSecond;
        _detachOutSpeeds[slot] = profile.DetachOutSpeedCmPerSecond;
        _states[slot] = Traversal3DState.NormalMovement;
        _stateTicks[slot] = 0;
        _activeCount++;
        return new Traversal3DHandle(slot, generation);
    }

    public void UnregisterCharacter(Traversal3DHandle handle)
    {
        int slot = RequireSlot(handle);
        _active[slot] = 0;
        _characterHandles[slot] = default;
        _characterBodies[slot] = default;
        _surfaceBodies[slot] = default;
        _surfaceKinds[slot] = default;
        _intentSubmitted[slot] = 0;
        _activeCount--;
    }

    public void SubmitIntent(Traversal3DHandle handle, in Traversal3DIntent intent)
    {
        int slot = RequireSlot(handle);
        intent.Validate(nameof(intent));
        if (_intentSubmitted[slot] != 0)
        {
            throw new InvalidOperationException($"Traversal controller '{handle}' received more than one intent before the next fixed step.");
        }

        _moves[slot] = intent.Move;
        _facingDirections[slot] = intent.FacingDirection;
        _engageRequests[slot] = intent.EngageRequested ? (byte)1 : (byte)0;
        _jumpRequests[slot] = intent.JumpRequested ? (byte)1 : (byte)0;
        _intentSubmitted[slot] = 1;
    }

    public Traversal3DStatus GetStatus(Traversal3DHandle handle)
    {
        int slot = RequireSlot(handle);
        return new Traversal3DStatus(
            _states[slot],
            _surfaceBodies[slot],
            _surfaceKinds[slot],
            _surfaceNormals[slot],
            _targetPositions[slot],
            _clearanceValid[slot] != 0,
            _stateTicks[slot]);
    }

    public void PrepareFixedStep()
    {
        if (_lastPreparedStepIndex == _world.StepIndex)
        {
            throw new InvalidOperationException(
                $"Traversal3D fixed step {_world.StepIndex} was prepared more than once before Physics3D advanced.");
        }

        ValidateBatchBeforeMutation();
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (_active[slot] == 0)
            {
                continue;
            }

            PrepareController(slot);
            _intentSubmitted[slot] = 0;
        }

        _lastPreparedStepIndex = _world.StepIndex;
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
                    $"Traversal slot {slot} has no intent for Physics3D step {_world.StepIndex}. Missing input is not replaced by a default intent.");
            }

            Character3DGeometry geometry = _characters.GetGeometry(_characterHandles[slot]);
            if (geometry.Body != _characterBodies[slot])
            {
                throw new InvalidOperationException(
                    $"Traversal slot {slot} character body changed from '{_characterBodies[slot]}' to '{geometry.Body}'.");
            }

            RequireCharacterBody(slot, geometry.Body);
            if (_states[slot] != Traversal3DState.NormalMovement && !TryGetSurfaceKind(_surfaceBodies[slot], out _))
            {
                throw new InvalidOperationException(
                    $"Traversal slot {slot} lost active surface '{_surfaceBodies[slot]}' while in state '{_states[slot]}'.");
            }
        }
    }

    private void PrepareController(int slot)
    {
        Traversal3DState previousState = _states[slot];
        switch (previousState)
        {
            case Traversal3DState.NormalMovement:
                PrepareNormal(slot);
                break;
            case Traversal3DState.Attached:
                PrepareAttached(slot);
                break;
            case Traversal3DState.Climbing:
                PrepareClimbing(slot);
                break;
            case Traversal3DState.LedgeHang:
                PrepareLedgeHang(slot);
                break;
            case Traversal3DState.Mantling:
                PrepareMantling(slot);
                break;
            case Traversal3DState.Detaching:
                PrepareDetaching(slot);
                break;
            default:
                throw new InvalidOperationException($"Traversal slot {slot} has unsupported state '{previousState}'.");
        }

        _stateTicks[slot] = _states[slot] == previousState ? checked(_stateTicks[slot] + 1) : 0;
    }

    private void PrepareNormal(int slot)
    {
        _clearanceValid[slot] = 0;
        if (_engageRequests[slot] != 0 && TryFindAttachSurface(slot, out Physics3DShapeCastHit hit, out Traversal3DSurfaceKind kind))
        {
            _states[slot] = Traversal3DState.Attached;
            _surfaceBodies[slot] = hit.Body;
            _surfaceKinds[slot] = kind;
            _surfaceNormals[slot] = NormalizeHorizontal(hit.Normal, _facingDirections[slot]);
            Character3DState character = _characters.GetState(_characterHandles[slot]);
            _targetPositions[slot] = character.PositionCm +
                                     (_facingDirections[slot] * MathF.Max(0f, hit.DistanceCm - 2f));
            SubmitTargetPosition(slot, character.PositionCm, _targetPositions[slot], _attachSpeeds[slot]);
            return;
        }

        _characters.SubmitIntent(
            _characterHandles[slot],
            new Character3DIntent(_moves[slot], _jumpRequests[slot] != 0));
    }

    private void PrepareAttached(int slot)
    {
        Character3DState character = _characters.GetState(_characterHandles[slot]);
        if (_jumpRequests[slot] != 0)
        {
            BeginDetach(slot, character.PositionCm);
            return;
        }

        SubmitTargetPosition(slot, character.PositionCm, _targetPositions[slot], _attachSpeeds[slot]);
        _states[slot] = Traversal3DState.Climbing;
    }

    private void PrepareClimbing(int slot)
    {
        Character3DState character = _characters.GetState(_characterHandles[slot]);
        if (_jumpRequests[slot] != 0)
        {
            BeginDetach(slot, character.PositionCm);
            return;
        }

        if (_moves[slot].Y > 0.1f && TryBuildLedgeTarget(slot, character.PositionCm, out Vector3 hangTarget, out Vector3 mantleTarget))
        {
            _states[slot] = Traversal3DState.LedgeHang;
            _targetPositions[slot] = hangTarget;
            _clearanceValid[slot] = 1;
            SubmitTargetPosition(slot, character.PositionCm, hangTarget, _attachSpeeds[slot]);
            _mantleTargets[slot] = mantleTarget;
            return;
        }

        Vector3 normal = _surfaceNormals[slot];
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        Vector3 surfaceVelocity = _world.GetBodyVelocityAtWorldPoint(_surfaceBodies[slot], character.PositionCm);
        Vector3 targetVelocity = surfaceVelocity +
                                 (Vector3.UnitY * (_moves[slot].Y * _climbSpeeds[slot])) +
                                 (tangent * (_moves[slot].X * _lateralSpeeds[slot]));
        _characters.SubmitIntent(
            _characterHandles[slot],
            Character3DIntent.TraversalVelocity(targetVelocity, _maximumAccelerations[slot]));
    }

    private void PrepareLedgeHang(int slot)
    {
        Character3DState character = _characters.GetState(_characterHandles[slot]);
        if (_jumpRequests[slot] != 0)
        {
            BeginDetach(slot, character.PositionCm);
            return;
        }

        if (_engageRequests[slot] != 0 || _moves[slot].Y > 0.25f)
        {
            _states[slot] = Traversal3DState.Mantling;
            _targetPositions[slot] = _mantleTargets[slot];
            SubmitTargetPosition(slot, character.PositionCm, _targetPositions[slot], _mantleSpeeds[slot]);
            return;
        }

        SubmitTargetPosition(slot, character.PositionCm, _targetPositions[slot], _attachSpeeds[slot]);
    }

    private void PrepareMantling(int slot)
    {
        Character3DState character = _characters.GetState(_characterHandles[slot]);
        Vector3 delta = _targetPositions[slot] - character.PositionCm;
        if (delta.LengthSquared() <= _mantleCompletionDistances[slot] * _mantleCompletionDistances[slot])
        {
            _states[slot] = Traversal3DState.NormalMovement;
            _surfaceBodies[slot] = default;
            _surfaceKinds[slot] = default;
            _surfaceNormals[slot] = default;
            _clearanceValid[slot] = 0;
            _characters.SubmitIntent(_characterHandles[slot], new Character3DIntent(_moves[slot], false));
            return;
        }

        SubmitTargetPosition(slot, character.PositionCm, _targetPositions[slot], _mantleSpeeds[slot]);
    }

    private void PrepareDetaching(int slot)
    {
        _states[slot] = Traversal3DState.NormalMovement;
        _surfaceBodies[slot] = default;
        _surfaceKinds[slot] = default;
        _surfaceNormals[slot] = default;
        _clearanceValid[slot] = 0;
        _characters.SubmitIntent(_characterHandles[slot], new Character3DIntent(_moves[slot], false));
    }

    private void BeginDetach(int slot, Vector3 positionCm)
    {
        _states[slot] = Traversal3DState.Detaching;
        Vector3 outward = _surfaceNormals[slot];
        Vector3 surfaceVelocity = _world.GetBodyVelocityAtWorldPoint(_surfaceBodies[slot], positionCm);
        Vector3 targetVelocity = surfaceVelocity +
                                 (Vector3.UnitY * _detachUpSpeeds[slot]) +
                                 (outward * _detachOutSpeeds[slot]);
        _characters.SubmitIntent(
            _characterHandles[slot],
            Character3DIntent.TraversalVelocity(targetVelocity, _maximumAccelerations[slot]));
    }

    private bool TryFindAttachSurface(
        int slot,
        out Physics3DShapeCastHit hit,
        out Traversal3DSurfaceKind surfaceKind)
    {
        Character3DGeometry geometry = _characters.GetGeometry(_characterHandles[slot]);
        Character3DState state = _characters.GetState(_characterHandles[slot]);
        var filter = new Physics3DQueryFilter(geometry.QueryLayer, geometry.Body, includeSensors: true);
        bool found = _world.SphereCastClosest(
            state.PositionCm,
            MathF.Min(geometry.RadiusCm * 0.45f, 18f),
            _facingDirections[slot],
            _attachProbeDistances[slot],
            filter,
            out hit);
        if (!found || !TryGetSurfaceKind(hit.Body, out surfaceKind))
        {
            hit = default;
            surfaceKind = default;
            return false;
        }

        return true;
    }

    private bool TryBuildLedgeTarget(
        int slot,
        Vector3 characterPositionCm,
        out Vector3 hangTarget,
        out Vector3 mantleTarget)
    {
        Character3DGeometry geometry = _characters.GetGeometry(_characterHandles[slot]);
        Vector3 inward = -_surfaceNormals[slot];
        Vector3 rayOrigin = characterPositionCm +
                            (Vector3.UnitY * _ledgeProbeHeights[slot]) +
                            (inward * _ledgeProbeForwards[slot]);
        var filter = new Physics3DQueryFilter(geometry.QueryLayer, geometry.Body, includeSensors: false);
        bool foundTop = _world.RaycastClosest(
            rayOrigin,
            -Vector3.UnitY,
            _ledgeProbeDowns[slot],
            filter,
            out Physics3DRaycastHit top);
        if (!foundTop ||
            top.Normal.Y < _minimumTopNormalYs[slot] ||
            top.PositionCm.Y - characterPositionCm.Y < _minimumLedgeHeights[slot])
        {
            hangTarget = default;
            mantleTarget = default;
            _clearanceValid[slot] = 0;
            return false;
        }

        Vector3 handPoint = top.PositionCm + (_surfaceNormals[slot] * _handClearanceRadii[slot]);
        int handOverlaps = _world.OverlapSphere(
            handPoint,
            _handClearanceRadii[slot],
            new Physics3DQueryFilter(geometry.QueryLayer, geometry.Body, includeSensors: false),
            _overlapScratch);
        for (int i = 0; i < handOverlaps; i++)
        {
            Physics3DBodyId overlap = _overlapScratch[i].Body;
            if (overlap != _surfaceBodies[slot] && overlap != top.Body)
            {
                hangTarget = default;
                mantleTarget = default;
                _clearanceValid[slot] = 0;
                return false;
            }
        }

        mantleTarget = top.PositionCm +
                        (Vector3.UnitY * (geometry.HalfHeightCm + 2f)) +
                        (inward * _mantleForwards[slot]);
        int landingOverlaps = _world.OverlapCapsule(
            mantleTarget,
            geometry.RadiusCm,
            geometry.CylinderLengthCm,
            Quaternion.Identity,
            new Physics3DQueryFilter(geometry.QueryLayer, geometry.Body, includeSensors: false),
            _overlapScratch);
        for (int i = 0; i < landingOverlaps; i++)
        {
            if (_overlapScratch[i].Body != top.Body)
            {
                hangTarget = default;
                mantleTarget = default;
                _clearanceValid[slot] = 0;
                return false;
            }
        }

        hangTarget = top.PositionCm +
                     (_surfaceNormals[slot] * (geometry.RadiusCm + 2f)) -
                     (Vector3.UnitY * (geometry.HalfHeightCm * 0.35f));
        return true;
    }

    private void SubmitTargetPosition(int slot, Vector3 currentPositionCm, Vector3 targetPositionCm, float maximumSpeed)
    {
        Vector3 targetVelocity = (targetPositionCm - currentPositionCm) / _world.FixedDeltaSeconds;
        targetVelocity = ClampMagnitude(targetVelocity, maximumSpeed);
        targetVelocity += _world.GetBodyVelocityAtWorldPoint(_surfaceBodies[slot], currentPositionCm);
        _characters.SubmitIntent(
            _characterHandles[slot],
            Character3DIntent.TraversalVelocity(targetVelocity, _maximumAccelerations[slot]));
    }

    private bool TryGetSurfaceKind(Physics3DBodyId body, out Traversal3DSurfaceKind kind)
    {
        if ((uint)body.Slot >= (uint)_surfaceGenerationsByBodySlot.Length ||
            _surfaceGenerationsByBodySlot[body.Slot] != body.Generation)
        {
            kind = default;
            return false;
        }

        kind = _surfaceKindsByBodySlot[body.Slot];
        return true;
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

    private int RequireSlot(Traversal3DHandle handle)
    {
        if ((uint)handle.Slot >= (uint)_capacity ||
            _active[handle.Slot] == 0 ||
            _generations[handle.Slot] != handle.Generation)
        {
            throw new InvalidOperationException($"Traversal handle '{handle}' is stale or not registered.");
        }

        return handle.Slot;
    }

    private void RequireLiveBody(Physics3DBodyId body, string parameterName)
    {
        if (!body.IsValid || !_world.ContainsBody(body))
        {
            throw new InvalidOperationException($"{parameterName} '{body}' is invalid or no longer exists.");
        }
    }

    private void RequireCharacterBody(int slot, Physics3DBodyId body)
    {
        if (!body.IsValid || !_world.ContainsBody(body))
        {
            throw new InvalidOperationException(
                $"Traversal slot {slot} character body '{body}' is invalid or no longer exists.");
        }
    }

    private static Vector3 NormalizeHorizontal(Vector3 value, Vector3 fallback)
    {
        Vector3 horizontal = new(value.X, 0f, value.Z);
        return horizontal.LengthSquared() > 1e-8f ? Vector3.Normalize(horizontal) : -fallback;
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximumMagnitude)
    {
        float lengthSquared = value.LengthSquared();
        float maximumSquared = maximumMagnitude * maximumMagnitude;
        return lengthSquared <= maximumSquared
            ? value
            : value * (maximumMagnitude / MathF.Sqrt(lengthSquared));
    }
}
