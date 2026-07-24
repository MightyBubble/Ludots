using System;
using System.Numerics;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Ragdoll;

/// <summary>
/// Fixed-capacity ragdoll recipe and instance store. The caller owns the Physics3D step.
/// </summary>
public sealed class RagdollWorld : IDisposable
{
    private const float QuaternionLengthTolerance = 0.001f;
    private const float MassRatioTolerance = 0.0001f;

    private readonly IPhysics3DWorld _physics;
    private readonly int _maximumBonesPerInstance;

    private readonly byte[] _recipeActive;
    private readonly int[] _recipeGenerations;
    private readonly int[] _recipeStableIds;
    private readonly int[] _recipeBoneOffsets;
    private readonly int[] _recipeBoneCounts;
    private readonly RagdollRecoverySettings[] _recipeRecovery;

    private readonly int[] _recipeBoneStableIds;
    private readonly int[] _recipeBoneParents;
    private readonly Vector3[] _recipeBoneLocalPositionsCm;
    private readonly Quaternion[] _recipeBoneLocalOrientations;
    private readonly Physics3DShapeId[] _recipeBoneShapes;
    private readonly RagdollShapeKind[] _recipeBoneShapeKinds;
    private readonly Vector3[] _recipeBoneShapeDimensionsCm;
    private readonly float[] _recipeBoneMassRatios;
    private readonly Vector3[] _recipeBoneParentAnchorsCm;
    private readonly Vector3[] _recipeBoneAnchorsCm;
    private readonly Quaternion[] _recipeBoneJointFramesParent;
    private readonly Quaternion[] _recipeBoneJointFramesBone;
    private readonly float[] _recipeBoneMaximumSwingAngles;
    private readonly float[] _recipeBoneMinimumTwistAngles;
    private readonly float[] _recipeBoneMaximumTwistAngles;
    private readonly Physics3DSpringSettings[] _recipeBoneJointSprings;
    private readonly int[] _recipeBoneSubgroupIndices;
    private readonly uint[] _recipeBoneCollisionMasks;
    private readonly Physics3DServoSettings[] _recipeBoneActivePoseServos;
    private readonly Physics3DSpringSettings[] _recipeBoneActivePoseSprings;

    private readonly byte[] _instanceActive;
    private readonly int[] _instanceGenerations;
    private readonly int[] _instanceFree;
    private readonly int[] _instanceRecipeSlots;
    private readonly int[] _instanceBoneCounts;
    private readonly uint[] _instanceAssemblyIds;
    private readonly byte[] _instanceActivePoseEnabled;
    private readonly RagdollRecoveryState[] _instanceRecoveryStates;
    private readonly int[] _instanceRecoveryBlockerCounts;
    private readonly Vector3[] _instanceRecoveryCharacterCentersCm;
    private readonly Quaternion[] _instanceRecoveryCharacterOrientations;
    private readonly Vector3[] _instanceRecoveryVelocitiesCmPerSecond;

    private readonly int[] _boneStableIds;
    private readonly Physics3DBodyId[] _boneBodies;
    private readonly Physics3DConstraintId[] _boneBallSockets;
    private readonly Physics3DConstraintId[] _boneSwingLimits;
    private readonly Physics3DConstraintId[] _boneTwistLimits;
    private readonly Physics3DConstraintId[] _boneActivePoseServos;
    private readonly Quaternion[] _boneActivePoseTargets;
    private readonly Vector3[] _candidatePositionsCm;
    private readonly Quaternion[] _candidateOrientations;
    private readonly Physics3DOverlapHit[] _recoveryOverlapHits;

    private int _recipeCount;
    private int _recipeBoneCount;
    private int _instanceFreeCount;
    private int _activeInstanceCount;
    private int _activeBoneCount;
    private int _activeConstraintCount;
    private long _lastPreparedTick = -1;
    private bool _stepPrepared;
    private bool _disposed;

    public RagdollWorld(IPhysics3DWorld physics, RagdollConfig config)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        float expectedDelta = 1f / config.FixedStepHz;
        if (MathF.Abs(physics.FixedDeltaSeconds - expectedDelta) > 1e-6f)
        {
            throw new InvalidOperationException(
                $"Ragdoll requires a {config.FixedStepHz}Hz Physics3D world, but fixed delta is {physics.FixedDeltaSeconds} seconds.");
        }

        _maximumBonesPerInstance = config.MaximumBonesPerInstance;

        _recipeActive = new byte[config.RecipeCapacity];
        _recipeGenerations = new int[config.RecipeCapacity];
        _recipeStableIds = new int[config.RecipeCapacity];
        _recipeBoneOffsets = new int[config.RecipeCapacity];
        _recipeBoneCounts = new int[config.RecipeCapacity];
        _recipeRecovery = new RagdollRecoverySettings[config.RecipeCapacity];

        _recipeBoneStableIds = new int[config.RecipeBoneCapacity];
        _recipeBoneParents = new int[config.RecipeBoneCapacity];
        _recipeBoneLocalPositionsCm = new Vector3[config.RecipeBoneCapacity];
        _recipeBoneLocalOrientations = new Quaternion[config.RecipeBoneCapacity];
        _recipeBoneShapes = new Physics3DShapeId[config.RecipeBoneCapacity];
        _recipeBoneShapeKinds = new RagdollShapeKind[config.RecipeBoneCapacity];
        _recipeBoneShapeDimensionsCm = new Vector3[config.RecipeBoneCapacity];
        _recipeBoneMassRatios = new float[config.RecipeBoneCapacity];
        _recipeBoneParentAnchorsCm = new Vector3[config.RecipeBoneCapacity];
        _recipeBoneAnchorsCm = new Vector3[config.RecipeBoneCapacity];
        _recipeBoneJointFramesParent = new Quaternion[config.RecipeBoneCapacity];
        _recipeBoneJointFramesBone = new Quaternion[config.RecipeBoneCapacity];
        _recipeBoneMaximumSwingAngles = new float[config.RecipeBoneCapacity];
        _recipeBoneMinimumTwistAngles = new float[config.RecipeBoneCapacity];
        _recipeBoneMaximumTwistAngles = new float[config.RecipeBoneCapacity];
        _recipeBoneJointSprings = new Physics3DSpringSettings[config.RecipeBoneCapacity];
        _recipeBoneSubgroupIndices = new int[config.RecipeBoneCapacity];
        _recipeBoneCollisionMasks = new uint[config.RecipeBoneCapacity];
        _recipeBoneActivePoseServos = new Physics3DServoSettings[config.RecipeBoneCapacity];
        _recipeBoneActivePoseSprings = new Physics3DSpringSettings[config.RecipeBoneCapacity];

        _instanceActive = new byte[config.InstanceCapacity];
        _instanceGenerations = new int[config.InstanceCapacity];
        _instanceFree = new int[config.InstanceCapacity];
        _instanceRecipeSlots = new int[config.InstanceCapacity];
        _instanceBoneCounts = new int[config.InstanceCapacity];
        _instanceAssemblyIds = new uint[config.InstanceCapacity];
        _instanceActivePoseEnabled = new byte[config.InstanceCapacity];
        _instanceRecoveryStates = new RagdollRecoveryState[config.InstanceCapacity];
        _instanceRecoveryBlockerCounts = new int[config.InstanceCapacity];
        _instanceRecoveryCharacterCentersCm = new Vector3[config.InstanceCapacity];
        _instanceRecoveryCharacterOrientations = new Quaternion[config.InstanceCapacity];
        _instanceRecoveryVelocitiesCmPerSecond = new Vector3[config.InstanceCapacity];

        int instanceBoneCapacity = checked(config.InstanceCapacity * config.MaximumBonesPerInstance);
        _boneStableIds = new int[instanceBoneCapacity];
        _boneBodies = new Physics3DBodyId[instanceBoneCapacity];
        _boneBallSockets = new Physics3DConstraintId[instanceBoneCapacity];
        _boneSwingLimits = new Physics3DConstraintId[instanceBoneCapacity];
        _boneTwistLimits = new Physics3DConstraintId[instanceBoneCapacity];
        _boneActivePoseServos = new Physics3DConstraintId[instanceBoneCapacity];
        _boneActivePoseTargets = new Quaternion[instanceBoneCapacity];
        _candidatePositionsCm = new Vector3[instanceBoneCapacity];
        _candidateOrientations = new Quaternion[instanceBoneCapacity];
        _recoveryOverlapHits = new Physics3DOverlapHit[config.RecoveryOverlapHitCapacity];

        for (int i = 0; i < _recipeGenerations.Length; i++)
        {
            _recipeGenerations[i] = 1;
        }

        for (int i = 0; i < _instanceGenerations.Length; i++)
        {
            _instanceGenerations[i] = 1;
            _instanceFree[i] = _instanceGenerations.Length - 1 - i;
            _instanceRecipeSlots[i] = -1;
        }

        _instanceFreeCount = _instanceFree.Length;
    }

    public int RecipeCapacity => _recipeActive.Length;
    public int RecipeBoneCapacity => _recipeBoneStableIds.Length;
    public int InstanceCapacity => _instanceActive.Length;
    public int MaximumBonesPerInstance => _maximumBonesPerInstance;
    public int ActiveInstanceCount => _activeInstanceCount;
    public int ActiveBoneCount => _activeBoneCount;
    public int ActiveConstraintCount => _activeConstraintCount;
    public long LastPreparedTick => _lastPreparedTick;

    public RagdollRecipeId RegisterRecipe(RagdollRecipeDefinition definition)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        RagdollBoneDefinition[] bones = definition.Bones
            ?? throw new ArgumentNullException($"{nameof(definition)}.{nameof(definition.Bones)}");
        ValidateRecipe(definition, bones);

        if (_recipeCount >= RecipeCapacity)
        {
            throw new RagdollCapacityExceededException("recipes", RecipeCapacity, _recipeCount + 1);
        }

        if (_recipeBoneCount + bones.Length > RecipeBoneCapacity)
        {
            throw new RagdollCapacityExceededException(
                "recipe bones",
                RecipeBoneCapacity,
                _recipeBoneCount + bones.Length);
        }

        for (int i = 0; i < _recipeCount; i++)
        {
            if (_recipeActive[i] != 0 && _recipeStableIds[i] == definition.StableId)
            {
                throw new ArgumentException($"Ragdoll recipe stable id {definition.StableId} is already registered.", nameof(definition));
            }
        }

        int recipeSlot = _recipeCount;
        int boneOffset = _recipeBoneCount;
        for (int i = 0; i < bones.Length; i++)
        {
            RagdollBoneDefinition bone = bones[i];
            int target = boneOffset + i;
            _recipeBoneStableIds[target] = bone.StableId;
            _recipeBoneParents[target] = bone.ParentIndex;
            _recipeBoneLocalPositionsCm[target] = bone.LocalPositionCm;
            _recipeBoneLocalOrientations[target] = bone.LocalOrientation;
            _recipeBoneShapeKinds[target] = bone.Shape.Kind;
            _recipeBoneShapeDimensionsCm[target] = bone.Shape.DimensionsCm;
            _recipeBoneMassRatios[target] = bone.MassRatio;
            _recipeBoneParentAnchorsCm[target] = bone.ParentAnchorLocalCm;
            _recipeBoneAnchorsCm[target] = bone.BoneAnchorLocalCm;
            _recipeBoneJointFramesParent[target] = bone.JointFrameLocalParent;
            _recipeBoneJointFramesBone[target] = bone.JointFrameLocalBone;
            _recipeBoneMaximumSwingAngles[target] = bone.MaximumSwingAngleRadians;
            _recipeBoneMinimumTwistAngles[target] = bone.MinimumTwistAngleRadians;
            _recipeBoneMaximumTwistAngles[target] = bone.MaximumTwistAngleRadians;
            _recipeBoneJointSprings[target] = bone.JointSpring;
            _recipeBoneSubgroupIndices[target] = bone.CollisionSubgroupIndex;
            _recipeBoneCollisionMasks[target] = CompileCollisionMask(bones, i);
            _recipeBoneActivePoseServos[target] = bone.ActivePoseServo;
            _recipeBoneActivePoseSprings[target] = bone.ActivePoseSpring;
            RagdollShapeDefinition shape = bone.Shape;
            _recipeBoneShapes[target] = RegisterShape(in shape);
        }

        _recipeActive[recipeSlot] = 1;
        _recipeStableIds[recipeSlot] = definition.StableId;
        _recipeBoneOffsets[recipeSlot] = boneOffset;
        _recipeBoneCounts[recipeSlot] = bones.Length;
        _recipeRecovery[recipeSlot] = definition.Recovery;
        _recipeCount++;
        _recipeBoneCount += bones.Length;
        return new RagdollRecipeId(recipeSlot, _recipeGenerations[recipeSlot]);
    }

    public RagdollInstanceId TransitionFromAnimation(
        RagdollRecipeId recipe,
        long tick,
        in RagdollActivationDescription activation,
        ReadOnlySpan<RagdollBoneHandoff> animationBones,
        Span<Physics3DBodyId> createdBodies)
    {
        ThrowIfDisposed();
        RequireBoundaryTick(tick);
        int recipeSlot = RequireRecipeSlot(recipe);
        int boneCount = _recipeBoneCounts[recipeSlot];
        if (animationBones.Length != boneCount)
        {
            throw new ArgumentException(
                $"Animation handoff has {animationBones.Length} bones, but recipe {recipe} requires {boneCount}.",
                nameof(animationBones));
        }

        if (createdBodies.Length != boneCount)
        {
            throw new ArgumentException(
                $"Created body output has length {createdBodies.Length}, but recipe {recipe} requires {boneCount}.",
                nameof(createdBodies));
        }

        if (_instanceFreeCount == 0)
        {
            throw new RagdollCapacityExceededException("instances", InstanceCapacity, _activeInstanceCount + 1);
        }

        ValidateActivation(in activation);
        ValidateAnimationHandoff(animationBones);
        EnsureAssemblyIsUnique(activation.CollisionAssemblyId);

        int instanceSlot = _instanceFree[_instanceFreeCount - 1];
        int instanceBoneOffset = InstanceBoneOffset(instanceSlot);
        int recipeBoneOffset = _recipeBoneOffsets[recipeSlot];
        int createdBodyCount = 0;
        int createdJointCount = 0;
        try
        {
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                int recipeBone = recipeBoneOffset + boneIndex;
                int instanceBone = instanceBoneOffset + boneIndex;
                RagdollBoneHandoff handoff = animationBones[boneIndex];
                var subgroup = new Physics3DCollisionSubgroup(
                    activation.CollisionAssemblyId,
                    _recipeBoneSubgroupIndices[recipeBone],
                    _recipeBoneCollisionMasks[recipeBone]);
                Physics3DBodyId body = _physics.CreateBody(new Physics3DBodyDescription(
                    handoff.Entity,
                    Physics3DBodyKind.Dynamic,
                    _recipeBoneShapes[recipeBone],
                    handoff.PositionCm,
                    handoff.Orientation,
                    handoff.LinearVelocityCmPerSecond,
                    handoff.AngularVelocityRadiansPerSecond,
                    activation.TotalMass * _recipeBoneMassRatios[recipeBone],
                    activation.CollisionLayer,
                    activation.Material,
                    activation.ContinuousDetection,
                    collisionSubgroup: subgroup));
                _boneStableIds[instanceBone] = _recipeBoneStableIds[recipeBone];
                _boneBodies[instanceBone] = body;
                _boneActivePoseTargets[instanceBone] = _recipeBoneLocalOrientations[recipeBone];
                createdBodyCount++;
            }

            for (int boneIndex = 1; boneIndex < boneCount; boneIndex++)
            {
                CreatePassiveJointConstraints(instanceSlot, recipeSlot, boneIndex);
                createdJointCount++;
            }

            if (activation.ActivePoseEnabled)
            {
                CreateAllActivePoseServos(instanceSlot, recipeSlot, boneCount);
            }
        }
        catch
        {
            RollBackUncommittedInstance(instanceSlot, boneCount, createdBodyCount, createdJointCount);
            throw;
        }

        _instanceFreeCount--;
        _instanceActive[instanceSlot] = 1;
        _instanceRecipeSlots[instanceSlot] = recipeSlot;
        _instanceBoneCounts[instanceSlot] = boneCount;
        _instanceAssemblyIds[instanceSlot] = activation.CollisionAssemblyId;
        _instanceActivePoseEnabled[instanceSlot] = activation.ActivePoseEnabled ? (byte)1 : (byte)0;
        _instanceRecoveryStates[instanceSlot] = RagdollRecoveryState.None;
        _activeInstanceCount++;
        _activeBoneCount += boneCount;
        _activeConstraintCount += (boneCount - 1) * (activation.ActivePoseEnabled ? 4 : 3);

        for (int i = 0; i < boneCount; i++)
        {
            createdBodies[i] = _boneBodies[instanceBoneOffset + i];
        }

        return new RagdollInstanceId(instanceSlot, _instanceGenerations[instanceSlot]);
    }

    public bool ContainsInstance(RagdollInstanceId instance)
    {
        ThrowIfDisposed();
        return instance.Slot >= 0 &&
               instance.Slot < _instanceActive.Length &&
               _instanceActive[instance.Slot] != 0 &&
               _instanceGenerations[instance.Slot] == instance.Generation;
    }

    public RagdollInstanceState GetInstanceState(RagdollInstanceId instance)
    {
        ThrowIfDisposed();
        int slot = RequireInstanceSlot(instance);
        int recipeSlot = _instanceRecipeSlots[slot];
        return new RagdollInstanceState(
            new RagdollRecipeId(recipeSlot, _recipeGenerations[recipeSlot]),
            _instanceBoneCounts[slot],
            _instanceAssemblyIds[slot],
            _instanceActivePoseEnabled[slot] != 0,
            _instanceRecoveryStates[slot],
            _instanceRecoveryBlockerCounts[slot]);
    }

    public int CopyRecipeShapeIds(RagdollRecipeId recipe, Span<Physics3DShapeId> destination)
    {
        ThrowIfDisposed();
        int slot = RequireRecipeSlot(recipe);
        int count = _recipeBoneCounts[slot];
        if (destination.Length < count)
        {
            throw new RagdollCapacityExceededException("recipe shape destination", destination.Length, count);
        }

        _recipeBoneShapes.AsSpan(_recipeBoneOffsets[slot], count).CopyTo(destination);
        return count;
    }

    public int CopyBodies(RagdollInstanceId instance, Span<Physics3DBodyId> destination)
    {
        ThrowIfDisposed();
        int slot = RequireInstanceSlot(instance);
        int count = _instanceBoneCounts[slot];
        if (destination.Length < count)
        {
            throw new RagdollCapacityExceededException("body destination", destination.Length, count);
        }

        _boneBodies.AsSpan(InstanceBoneOffset(slot), count).CopyTo(destination);
        return count;
    }

    public int CopyBoneStates(RagdollInstanceId instance, long tick, Span<RagdollBoneState> destination)
    {
        ThrowIfDisposed();
        int slot = RequireInstanceSlot(instance);
        int count = _instanceBoneCounts[slot];
        if (destination.Length < count)
        {
            throw new RagdollCapacityExceededException("bone state destination", destination.Length, count);
        }

        ValidateInstanceRuntimeState(slot, tick);
        int offset = InstanceBoneOffset(slot);
        for (int i = 0; i < count; i++)
        {
            Physics3DBodyState state = _physics.GetBodyState(_boneBodies[offset + i]);
            destination[i] = new RagdollBoneState(_boneStableIds[offset + i], _boneBodies[offset + i], state);
        }

        return count;
    }

    public void SubmitActivePose(RagdollInstanceId instance, long tick, ReadOnlySpan<Quaternion> localBoneOrientations)
    {
        ThrowIfDisposed();
        RequireBoundaryTick(tick);
        int slot = RequireInstanceSlot(instance);
        int count = _instanceBoneCounts[slot];
        if (localBoneOrientations.Length != count)
        {
            throw new ArgumentException(
                $"Active pose has {localBoneOrientations.Length} bones, but instance {instance} requires {count}.",
                nameof(localBoneOrientations));
        }

        for (int i = 0; i < count; i++)
        {
            Quaternion orientation = localBoneOrientations[i];
            if (!IsUnitQuaternion(orientation))
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(localBoneOrientations)}[{i}]",
                    orientation,
                    "Quaternion must be finite and normalized.");
            }
        }

        localBoneOrientations.CopyTo(_boneActivePoseTargets.AsSpan(InstanceBoneOffset(slot), count));
        _instanceRecoveryStates[slot] = RagdollRecoveryState.None;
        _instanceRecoveryBlockerCounts[slot] = 0;
    }

    public void SetActivePoseEnabled(RagdollInstanceId instance, long tick, bool enabled)
    {
        ThrowIfDisposed();
        RequireBoundaryTick(tick);
        int slot = RequireInstanceSlot(instance);
        bool current = _instanceActivePoseEnabled[slot] != 0;
        if (current == enabled)
        {
            return;
        }

        int boneCount = _instanceBoneCounts[slot];
        if (enabled)
        {
            int recipeSlot = _instanceRecipeSlots[slot];
            CreateAllActivePoseServos(slot, recipeSlot, boneCount);
            _instanceActivePoseEnabled[slot] = 1;
            _activeConstraintCount += boneCount - 1;
        }
        else
        {
            DestroyAllActivePoseServos(slot, boneCount);
            _instanceActivePoseEnabled[slot] = 0;
            _activeConstraintCount -= boneCount - 1;
        }
    }

    public void PrepareFixedStep(long tick)
    {
        ThrowIfDisposed();
        if (_stepPrepared)
        {
            throw new InvalidOperationException($"Ragdoll tick {_lastPreparedTick} was prepared but not observed.");
        }

        if (tick < 0 || (_lastPreparedTick >= 0 && tick != _lastPreparedTick + 1))
        {
            throw new InvalidOperationException(
                $"Ragdoll PrepareFixedStep expected tick {_lastPreparedTick + 1}, but received {tick}.");
        }

        for (int slot = 0; slot < _instanceActive.Length; slot++)
        {
            if (_instanceActive[slot] != 0)
            {
                ValidateInstanceRuntimeState(slot, tick);
            }
        }

        for (int slot = 0; slot < _instanceActive.Length; slot++)
        {
            if (_instanceActive[slot] == 0 || _instanceActivePoseEnabled[slot] == 0)
            {
                continue;
            }

            int offset = InstanceBoneOffset(slot);
            int count = _instanceBoneCounts[slot];
            for (int boneIndex = 1; boneIndex < count; boneIndex++)
            {
                _physics.UpdateAngularServoTarget(
                    _boneActivePoseServos[offset + boneIndex],
                    _boneActivePoseTargets[offset + boneIndex]);
            }
        }

        _lastPreparedTick = tick;
        _stepPrepared = true;
    }

    public void ObserveFixedStep(long tick)
    {
        ThrowIfDisposed();
        if (!_stepPrepared || tick != _lastPreparedTick)
        {
            throw new InvalidOperationException(
                $"Ragdoll ObserveFixedStep expected prepared tick {_lastPreparedTick}, but received {tick}.");
        }

        for (int slot = 0; slot < _instanceActive.Length; slot++)
        {
            if (_instanceActive[slot] != 0)
            {
                ValidateInstanceRuntimeState(slot, tick);
            }
        }

        _stepPrepared = false;
    }

    public bool TryBuildRecoveryCandidate(
        RagdollInstanceId instance,
        long tick,
        in Character3DGeometry characterGeometry,
        Span<RagdollBonePose> candidateBonePoses,
        out RagdollRecoveryCandidate candidate)
    {
        ThrowIfDisposed();
        RequireObservedTick(tick);
        int slot = RequireInstanceSlot(instance);
        int boneCount = _instanceBoneCounts[slot];
        if (candidateBonePoses.Length != boneCount)
        {
            throw new ArgumentException(
                $"Recovery pose output has length {candidateBonePoses.Length}, but instance {instance} requires {boneCount}.",
                nameof(candidateBonePoses));
        }

        ValidateCharacterGeometry(in characterGeometry);
        ValidateInstanceRuntimeState(slot, tick);

        int recipeSlot = _instanceRecipeSlots[slot];
        int recipeOffset = _recipeBoneOffsets[recipeSlot];
        int instanceOffset = InstanceBoneOffset(slot);
        RagdollRecoverySettings recovery = _recipeRecovery[recipeSlot];
        Physics3DBodyState rootState = _physics.GetBodyState(_boneBodies[instanceOffset]);
        Quaternion standingOrientation = CreateStandingOrientation(rootState.Orientation, recovery.Strategy);

        _candidatePositionsCm[instanceOffset] = rootState.PositionCm;
        _candidateOrientations[instanceOffset] = standingOrientation;
        for (int boneIndex = 1; boneIndex < boneCount; boneIndex++)
        {
            int recipeBone = recipeOffset + boneIndex;
            int parentIndex = _recipeBoneParents[recipeBone];
            int parentInstanceBone = instanceOffset + parentIndex;
            Quaternion parentOrientation = _candidateOrientations[parentInstanceBone];
            _candidatePositionsCm[instanceOffset + boneIndex] =
                _candidatePositionsCm[parentInstanceBone] +
                Vector3.Transform(_recipeBoneLocalPositionsCm[recipeBone], parentOrientation);
            _candidateOrientations[instanceOffset + boneIndex] = Quaternion.Normalize(Quaternion.Concatenate(
                _recipeBoneLocalOrientations[recipeBone],
                parentOrientation));
        }

        Vector3 characterCenter = rootState.PositionCm +
                                  Vector3.Transform(recovery.CharacterCenterOffsetLocalCm, standingOrientation);
        Vector3 inheritedVelocity = ClampMagnitude(
            rootState.LinearVelocityCmPerSecond,
            recovery.MaximumInheritedLinearSpeedCmPerSecond);
        var filter = new Physics3DQueryFilter(
            characterGeometry.QueryLayer,
            characterGeometry.Body,
            includeSensors: false,
            ignoredAssemblyId: _instanceAssemblyIds[slot]);
        int blockerCount = _physics.OverlapCapsule(
            characterCenter,
            characterGeometry.RadiusCm,
            characterGeometry.CylinderLengthCm,
            Quaternion.Identity,
            filter,
            _recoveryOverlapHits);

        candidate = new RagdollRecoveryCandidate(
            blockerCount == 0,
            blockerCount,
            characterCenter,
            standingOrientation,
            inheritedVelocity);
        _instanceRecoveryBlockerCounts[slot] = blockerCount;
        if (blockerCount != 0)
        {
            _instanceRecoveryStates[slot] = RagdollRecoveryState.Blocked;
            return false;
        }

        for (int i = 0; i < boneCount; i++)
        {
            candidateBonePoses[i] = new RagdollBonePose(
                _boneStableIds[instanceOffset + i],
                _candidatePositionsCm[instanceOffset + i],
                _candidateOrientations[instanceOffset + i]);
        }

        _instanceRecoveryCharacterCentersCm[slot] = characterCenter;
        _instanceRecoveryCharacterOrientations[slot] = standingOrientation;
        _instanceRecoveryVelocitiesCmPerSecond[slot] = inheritedVelocity;
        _instanceRecoveryStates[slot] = RagdollRecoveryState.Ready;
        return true;
    }

    public RagdollRecoveryCandidate CommitRecovery(RagdollInstanceId instance, long tick)
    {
        ThrowIfDisposed();
        RequireObservedTick(tick);
        int slot = RequireInstanceSlot(instance);
        if (_instanceRecoveryStates[slot] != RagdollRecoveryState.Ready)
        {
            throw new InvalidOperationException(
                $"Ragdoll instance {instance} cannot recover at tick {tick} because its clearance state is {_instanceRecoveryStates[slot]}.");
        }

        var handoff = new RagdollRecoveryCandidate(
            true,
            0,
            _instanceRecoveryCharacterCentersCm[slot],
            _instanceRecoveryCharacterOrientations[slot],
            _instanceRecoveryVelocitiesCmPerSecond[slot]);
        RemoveInstance(slot);
        return handoff;
    }

    public void DestroyInstance(RagdollInstanceId instance, long tick)
    {
        ThrowIfDisposed();
        RequireBoundaryTick(tick);
        RemoveInstance(RequireInstanceSlot(instance));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_stepPrepared)
        {
            throw new InvalidOperationException(
                $"Ragdoll world cannot be disposed while tick {_lastPreparedTick} is prepared and not observed.");
        }

        for (int slot = _instanceActive.Length - 1; slot >= 0; slot--)
        {
            if (_instanceActive[slot] != 0)
            {
                RemoveInstance(slot);
            }
        }

        _disposed = true;
    }

    private void ValidateRecipe(RagdollRecipeDefinition definition, RagdollBoneDefinition[] bones)
    {
        if (definition.StableId <= 0)
        {
            throw new ArgumentOutOfRangeException($"{nameof(definition)}.{nameof(definition.StableId)}");
        }

        if (bones.Length == 0)
        {
            throw new ArgumentException("A ragdoll recipe requires at least one bone.", nameof(definition));
        }

        if (bones.Length > _maximumBonesPerInstance)
        {
            throw new RagdollCapacityExceededException("bones per instance", _maximumBonesPerInstance, bones.Length);
        }

        if (!Enum.IsDefined(definition.Recovery.Strategy))
        {
            throw new ArgumentOutOfRangeException($"{nameof(definition)}.{nameof(definition.Recovery)}.{nameof(definition.Recovery.Strategy)}");
        }

        RequireFinite(definition.Recovery.CharacterCenterOffsetLocalCm, $"{nameof(definition)}.{nameof(definition.Recovery)}.{nameof(definition.Recovery.CharacterCenterOffsetLocalCm)}");
        RequireFiniteNonNegative(
            definition.Recovery.MaximumInheritedLinearSpeedCmPerSecond,
            $"{nameof(definition)}.{nameof(definition.Recovery)}.{nameof(definition.Recovery.MaximumInheritedLinearSpeedCmPerSecond)}");

        float massRatioSum = 0f;
        uint usedSubgroups = 0u;
        for (int i = 0; i < bones.Length; i++)
        {
            RagdollBoneDefinition bone = bones[i]
                ?? throw new ArgumentNullException($"{nameof(definition)}.{nameof(definition.Bones)}[{i}]");
            string path = $"{nameof(definition)}.{nameof(definition.Bones)}[{i}]";
            if (bone.StableId <= 0)
            {
                throw new ArgumentOutOfRangeException($"{path}.{nameof(bone.StableId)}");
            }

            for (int previous = 0; previous < i; previous++)
            {
                if (bones[previous].StableId == bone.StableId)
                {
                    throw new ArgumentException($"Bone stable id {bone.StableId} is duplicated at indexes {previous} and {i}.", nameof(definition));
                }
            }

            if (i == 0)
            {
                if (bone.ParentIndex != -1)
                {
                    throw new ArgumentOutOfRangeException($"{path}.{nameof(bone.ParentIndex)}", "The first bone must be the single root.");
                }
            }
            else if (bone.ParentIndex < 0 || bone.ParentIndex >= i)
            {
                throw new ArgumentOutOfRangeException(
                    $"{path}.{nameof(bone.ParentIndex)}",
                    bone.ParentIndex,
                    "Parent index must reference an earlier bone so the recipe is topologically ordered.");
            }

            RequireFinite(bone.LocalPositionCm, $"{path}.{nameof(bone.LocalPositionCm)}");
            RequireUnitQuaternion(bone.LocalOrientation, $"{path}.{nameof(bone.LocalOrientation)}");
            RagdollShapeDefinition shape = bone.Shape;
            ValidateShape(in shape, $"{path}.{nameof(bone.Shape)}");
            RequireFinitePositive(bone.MassRatio, $"{path}.{nameof(bone.MassRatio)}");
            massRatioSum += bone.MassRatio;

            if (bone.CollisionSubgroupIndex < 0 || bone.CollisionSubgroupIndex >= 32)
            {
                throw new ArgumentOutOfRangeException($"{path}.{nameof(bone.CollisionSubgroupIndex)}");
            }

            uint subgroupBit = 1u << bone.CollisionSubgroupIndex;
            if ((usedSubgroups & subgroupBit) != 0)
            {
                throw new ArgumentException(
                    $"Collision subgroup index {bone.CollisionSubgroupIndex} is used by more than one bone.",
                    nameof(definition));
            }

            usedSubgroups |= subgroupBit;
            if (i == 0)
            {
                continue;
            }

            RequireFinite(bone.ParentAnchorLocalCm, $"{path}.{nameof(bone.ParentAnchorLocalCm)}");
            RequireFinite(bone.BoneAnchorLocalCm, $"{path}.{nameof(bone.BoneAnchorLocalCm)}");
            RequireUnitQuaternion(bone.JointFrameLocalParent, $"{path}.{nameof(bone.JointFrameLocalParent)}");
            RequireUnitQuaternion(bone.JointFrameLocalBone, $"{path}.{nameof(bone.JointFrameLocalBone)}");
            RequireFinite(bone.MaximumSwingAngleRadians, $"{path}.{nameof(bone.MaximumSwingAngleRadians)}");
            if (bone.MaximumSwingAngleRadians < 0f || bone.MaximumSwingAngleRadians > MathF.PI)
            {
                throw new ArgumentOutOfRangeException($"{path}.{nameof(bone.MaximumSwingAngleRadians)}");
            }

            RequireFinite(bone.MinimumTwistAngleRadians, $"{path}.{nameof(bone.MinimumTwistAngleRadians)}");
            RequireFinite(bone.MaximumTwistAngleRadians, $"{path}.{nameof(bone.MaximumTwistAngleRadians)}");
            if (bone.MinimumTwistAngleRadians < -MathF.PI ||
                bone.MaximumTwistAngleRadians > MathF.PI ||
                bone.MinimumTwistAngleRadians > bone.MaximumTwistAngleRadians)
            {
                throw new ArgumentOutOfRangeException($"{path}.TwistRange");
            }

            Physics3DSpringSettings jointSpring = bone.JointSpring;
            Physics3DServoSettings activePoseServo = bone.ActivePoseServo;
            Physics3DSpringSettings activePoseSpring = bone.ActivePoseSpring;
            ValidateSpring(in jointSpring, $"{path}.{nameof(bone.JointSpring)}");
            ValidateServo(in activePoseServo, $"{path}.{nameof(bone.ActivePoseServo)}");
            ValidateSpring(in activePoseSpring, $"{path}.{nameof(bone.ActivePoseSpring)}");
        }

        if (!float.IsFinite(massRatioSum) || MathF.Abs(massRatioSum - 1f) > MassRatioTolerance)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(definition)}.{nameof(definition.Bones)}",
                massRatioSum,
                $"Bone mass ratios must sum to one within {MassRatioTolerance}.");
        }
    }

    private static uint CompileCollisionMask(RagdollBoneDefinition[] bones, int boneIndex)
    {
        RagdollBoneDefinition bone = bones[boneIndex];
        uint mask = bone.CollidesWithSubgroupsMask & ~(1u << bone.CollisionSubgroupIndex);
        for (int otherIndex = 0; otherIndex < bones.Length; otherIndex++)
        {
            if (bones[otherIndex].ParentIndex == boneIndex || bone.ParentIndex == otherIndex)
            {
                mask &= ~(1u << bones[otherIndex].CollisionSubgroupIndex);
            }
        }

        return mask;
    }

    private Physics3DShapeId RegisterShape(in RagdollShapeDefinition shape)
    {
        return shape.Kind switch
        {
            RagdollShapeKind.Box => _physics.RegisterBoxShape(shape.DimensionsCm),
            RagdollShapeKind.Sphere => _physics.RegisterSphereShape(shape.DimensionsCm.X),
            RagdollShapeKind.Capsule => _physics.RegisterCapsuleShape(shape.DimensionsCm.X, shape.DimensionsCm.Y),
            _ => throw new InvalidOperationException($"Unsupported ragdoll shape '{shape.Kind}'.")
        };
    }

    private void CreatePassiveJointConstraints(int instanceSlot, int recipeSlot, int boneIndex)
    {
        int recipeBoneOffset = _recipeBoneOffsets[recipeSlot];
        int recipeBone = recipeBoneOffset + boneIndex;
        int parentIndex = _recipeBoneParents[recipeBone];
        int instanceBoneOffset = InstanceBoneOffset(instanceSlot);
        int instanceBone = instanceBoneOffset + boneIndex;
        Physics3DBodyId parentBody = _boneBodies[instanceBoneOffset + parentIndex];
        Physics3DBodyId boneBody = _boneBodies[instanceBone];
        Physics3DSpringSettings spring = _recipeBoneJointSprings[recipeBone];

        try
        {
            _boneBallSockets[instanceBone] = _physics.CreateBallSocketConstraint(
                parentBody,
                boneBody,
                _recipeBoneParentAnchorsCm[recipeBone],
                _recipeBoneAnchorsCm[recipeBone],
                spring);
            Vector3 axisParent = Vector3.Transform(Vector3.UnitY, _recipeBoneJointFramesParent[recipeBone]);
            Vector3 axisBone = Vector3.Transform(Vector3.UnitY, _recipeBoneJointFramesBone[recipeBone]);
            _boneSwingLimits[instanceBone] = _physics.CreateSwingLimitConstraint(
                parentBody,
                boneBody,
                new Physics3DSwingLimitDescription(
                    axisParent,
                    axisBone,
                    _recipeBoneMaximumSwingAngles[recipeBone],
                    spring));
            _boneTwistLimits[instanceBone] = _physics.CreateTwistLimitConstraint(
                parentBody,
                boneBody,
                new Physics3DTwistLimitDescription(
                    _recipeBoneJointFramesParent[recipeBone],
                    _recipeBoneJointFramesBone[recipeBone],
                    _recipeBoneMinimumTwistAngles[recipeBone],
                    _recipeBoneMaximumTwistAngles[recipeBone],
                    spring));
        }
        catch
        {
            DestroyConstraintIfLive(ref _boneTwistLimits[instanceBone]);
            DestroyConstraintIfLive(ref _boneSwingLimits[instanceBone]);
            DestroyConstraintIfLive(ref _boneBallSockets[instanceBone]);
            throw;
        }
    }

    private void CreateAllActivePoseServos(int instanceSlot, int recipeSlot, int boneCount)
    {
        int instanceOffset = InstanceBoneOffset(instanceSlot);
        int recipeOffset = _recipeBoneOffsets[recipeSlot];
        int created = 0;
        try
        {
            for (int boneIndex = 1; boneIndex < boneCount; boneIndex++)
            {
                int instanceBone = instanceOffset + boneIndex;
                int recipeBone = recipeOffset + boneIndex;
                int parentIndex = _recipeBoneParents[recipeBone];
                _boneActivePoseServos[instanceBone] = _physics.CreateAngularServoConstraint(
                    _boneBodies[instanceOffset + parentIndex],
                    _boneBodies[instanceBone],
                    new Physics3DAngularServoDescription(
                        _boneActivePoseTargets[instanceBone],
                        _recipeBoneActivePoseServos[recipeBone],
                        _recipeBoneActivePoseSprings[recipeBone]));
                created++;
            }
        }
        catch
        {
            for (int boneIndex = created; boneIndex >= 1; boneIndex--)
            {
                DestroyConstraintIfLive(ref _boneActivePoseServos[instanceOffset + boneIndex]);
            }

            throw;
        }
    }

    private void DestroyAllActivePoseServos(int instanceSlot, int boneCount)
    {
        int offset = InstanceBoneOffset(instanceSlot);
        for (int boneIndex = boneCount - 1; boneIndex >= 1; boneIndex--)
        {
            DestroyConstraintIfLive(ref _boneActivePoseServos[offset + boneIndex]);
        }
    }

    private void RollBackUncommittedInstance(
        int instanceSlot,
        int boneCount,
        int createdBodyCount,
        int createdJointCount)
    {
        int offset = InstanceBoneOffset(instanceSlot);
        DestroyAllActivePoseServos(instanceSlot, boneCount);
        for (int boneIndex = createdJointCount; boneIndex >= 1; boneIndex--)
        {
            DestroyConstraintIfLive(ref _boneTwistLimits[offset + boneIndex]);
            DestroyConstraintIfLive(ref _boneSwingLimits[offset + boneIndex]);
            DestroyConstraintIfLive(ref _boneBallSockets[offset + boneIndex]);
        }

        for (int boneIndex = createdBodyCount - 1; boneIndex >= 0; boneIndex--)
        {
            Physics3DBodyId body = _boneBodies[offset + boneIndex];
            if (_physics.ContainsBody(body))
            {
                _physics.DestroyBody(body);
            }
        }

        ClearInstanceBoneStorage(offset, boneCount);
    }

    private void RemoveInstance(int slot)
    {
        int boneCount = _instanceBoneCounts[slot];
        int offset = InstanceBoneOffset(slot);
        for (int boneIndex = boneCount - 1; boneIndex >= 1; boneIndex--)
        {
            DestroyConstraintIfLive(ref _boneActivePoseServos[offset + boneIndex]);
            DestroyConstraintIfLive(ref _boneTwistLimits[offset + boneIndex]);
            DestroyConstraintIfLive(ref _boneSwingLimits[offset + boneIndex]);
            DestroyConstraintIfLive(ref _boneBallSockets[offset + boneIndex]);
        }

        for (int boneIndex = boneCount - 1; boneIndex >= 0; boneIndex--)
        {
            Physics3DBodyId body = _boneBodies[offset + boneIndex];
            if (_physics.ContainsBody(body))
            {
                _physics.DestroyBody(body);
            }
        }

        _activeConstraintCount -= (boneCount - 1) * (_instanceActivePoseEnabled[slot] != 0 ? 4 : 3);
        _activeBoneCount -= boneCount;
        _activeInstanceCount--;
        ClearInstanceBoneStorage(offset, boneCount);
        _instanceActive[slot] = 0;
        _instanceGenerations[slot] = NextGeneration(_instanceGenerations[slot]);
        _instanceRecipeSlots[slot] = -1;
        _instanceBoneCounts[slot] = 0;
        _instanceAssemblyIds[slot] = 0;
        _instanceActivePoseEnabled[slot] = 0;
        _instanceRecoveryStates[slot] = RagdollRecoveryState.None;
        _instanceRecoveryBlockerCounts[slot] = 0;
        _instanceRecoveryCharacterCentersCm[slot] = default;
        _instanceRecoveryCharacterOrientations[slot] = default;
        _instanceRecoveryVelocitiesCmPerSecond[slot] = default;
        _instanceFree[_instanceFreeCount++] = slot;
    }

    private void ValidateInstanceRuntimeState(int instanceSlot, long tick)
    {
        var instance = new RagdollInstanceId(instanceSlot, _instanceGenerations[instanceSlot]);
        int boneCount = _instanceBoneCounts[instanceSlot];
        int offset = InstanceBoneOffset(instanceSlot);
        for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            int boneSlot = offset + boneIndex;
            Physics3DBodyId body = _boneBodies[boneSlot];
            if (!_physics.ContainsBody(body))
            {
                throw new RagdollStateException(instance, boneIndex, tick, $"body {body} is missing or stale.");
            }

            Physics3DBodyState state = _physics.GetBodyState(body);
            if (!IsFinite(state.PositionCm) ||
                !IsFinite(state.Orientation) ||
                state.Orientation.LengthSquared() <= 1e-12f ||
                !IsFinite(state.LinearVelocityCmPerSecond) ||
                !IsFinite(state.AngularVelocityRadiansPerSecond))
            {
                throw new RagdollStateException(instance, boneIndex, tick, $"body {body} contains a non-finite pose or velocity.");
            }

            if (boneIndex == 0)
            {
                continue;
            }

            ValidateConstraint(instance, boneIndex, tick, _boneBallSockets[boneSlot], "ball socket");
            ValidateConstraint(instance, boneIndex, tick, _boneSwingLimits[boneSlot], "swing limit");
            ValidateConstraint(instance, boneIndex, tick, _boneTwistLimits[boneSlot], "twist limit");
            if (_instanceActivePoseEnabled[instanceSlot] != 0)
            {
                ValidateConstraint(instance, boneIndex, tick, _boneActivePoseServos[boneSlot], "active pose servo");
            }
        }
    }

    private void ValidateConstraint(
        RagdollInstanceId instance,
        int boneIndex,
        long tick,
        Physics3DConstraintId constraint,
        string name)
    {
        if (!_physics.ContainsConstraint(constraint))
        {
            throw new RagdollStateException(instance, boneIndex, tick, $"{name} {constraint} is missing or stale.");
        }

        float impulse = _physics.GetConstraintImpulseMagnitude(constraint);
        if (!float.IsFinite(impulse))
        {
            throw new RagdollStateException(instance, boneIndex, tick, $"{name} {constraint} has non-finite impulse {impulse}.");
        }
    }

    private void DestroyConstraintIfLive(ref Physics3DConstraintId constraint)
    {
        if (constraint.IsValid && _physics.ContainsConstraint(constraint))
        {
            _physics.DestroyConstraint(constraint);
        }

        constraint = default;
    }

    private void ClearInstanceBoneStorage(int offset, int count)
    {
        Array.Clear(_boneStableIds, offset, count);
        Array.Clear(_boneBodies, offset, count);
        Array.Clear(_boneBallSockets, offset, count);
        Array.Clear(_boneSwingLimits, offset, count);
        Array.Clear(_boneTwistLimits, offset, count);
        Array.Clear(_boneActivePoseServos, offset, count);
        Array.Clear(_boneActivePoseTargets, offset, count);
        Array.Clear(_candidatePositionsCm, offset, count);
        Array.Clear(_candidateOrientations, offset, count);
    }

    private void EnsureAssemblyIsUnique(uint assemblyId)
    {
        for (int i = 0; i < _instanceActive.Length; i++)
        {
            if (_instanceActive[i] != 0 && _instanceAssemblyIds[i] == assemblyId)
            {
                throw new ArgumentException(
                    $"Collision assembly id {assemblyId} is already owned by ragdoll instance slot {i}.",
                    nameof(assemblyId));
            }
        }
    }

    private void ValidateActivation(in RagdollActivationDescription activation)
    {
        if (activation.CollisionAssemblyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activation.CollisionAssemblyId), "Collision assembly id zero is reserved.");
        }

        RequireFinitePositive(activation.TotalMass, nameof(activation.TotalMass));
        if (!Enum.IsDefined(activation.ContinuousDetection))
        {
            throw new ArgumentOutOfRangeException(nameof(activation.ContinuousDetection));
        }

        RequireFiniteNonNegative(activation.Material.FrictionCoefficient, $"{nameof(activation.Material)}.{nameof(activation.Material.FrictionCoefficient)}");
        RequireFiniteNonNegative(activation.Material.MaximumRecoveryVelocityCmPerSecond, $"{nameof(activation.Material)}.{nameof(activation.Material.MaximumRecoveryVelocityCmPerSecond)}");
        RequireFinitePositive(activation.Material.SpringAngularFrequency, $"{nameof(activation.Material)}.{nameof(activation.Material.SpringAngularFrequency)}");
        RequireFiniteNonNegative(activation.Material.SpringTwiceDampingRatio, $"{nameof(activation.Material)}.{nameof(activation.Material.SpringTwiceDampingRatio)}");
    }

    private static void ValidateAnimationHandoff(ReadOnlySpan<RagdollBoneHandoff> bones)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            RagdollBoneHandoff bone = bones[i];
            string path = $"{nameof(bones)}[{i}]";
            RequireFinite(bone.PositionCm, $"{path}.{nameof(bone.PositionCm)}");
            RequireUnitQuaternion(bone.Orientation, $"{path}.{nameof(bone.Orientation)}");
            RequireFinite(bone.LinearVelocityCmPerSecond, $"{path}.{nameof(bone.LinearVelocityCmPerSecond)}");
            RequireFinite(bone.AngularVelocityRadiansPerSecond, $"{path}.{nameof(bone.AngularVelocityRadiansPerSecond)}");
            for (int previous = 0; previous < i; previous++)
            {
                if (bones[previous].Entity == bone.Entity)
                {
                    throw new ArgumentException($"Animation bones {previous} and {i} reference the same entity.", nameof(bones));
                }
            }
        }
    }

    private static void ValidateCharacterGeometry(in Character3DGeometry geometry)
    {
        RequireFinitePositive(geometry.RadiusCm, $"{nameof(geometry)}.{nameof(geometry.RadiusCm)}");
        RequireFiniteNonNegative(geometry.CylinderLengthCm, $"{nameof(geometry)}.{nameof(geometry.CylinderLengthCm)}");
        if (geometry.QueryLayer.Mask == 0u)
        {
            throw new ArgumentOutOfRangeException($"{nameof(geometry)}.{nameof(geometry.QueryLayer)}", "Recovery query layer cannot be empty.");
        }
    }

    private static void ValidateShape(in RagdollShapeDefinition shape, string path)
    {
        if (!Enum.IsDefined(shape.Kind))
        {
            throw new ArgumentOutOfRangeException($"{path}.{nameof(shape.Kind)}");
        }

        RequireFinite(shape.DimensionsCm, $"{path}.{nameof(shape.DimensionsCm)}");
        switch (shape.Kind)
        {
            case RagdollShapeKind.Box:
                RequireFinitePositive(shape.DimensionsCm.X, $"{path}.SizeX");
                RequireFinitePositive(shape.DimensionsCm.Y, $"{path}.SizeY");
                RequireFinitePositive(shape.DimensionsCm.Z, $"{path}.SizeZ");
                break;
            case RagdollShapeKind.Sphere:
                RequireFinitePositive(shape.DimensionsCm.X, $"{path}.Radius");
                if (shape.DimensionsCm.Y != 0f || shape.DimensionsCm.Z != 0f)
                {
                    throw new ArgumentOutOfRangeException($"{path}.{nameof(shape.DimensionsCm)}", "Sphere stores only radius in X.");
                }
                break;
            case RagdollShapeKind.Capsule:
                RequireFinitePositive(shape.DimensionsCm.X, $"{path}.Radius");
                RequireFiniteNonNegative(shape.DimensionsCm.Y, $"{path}.CylinderLength");
                if (shape.DimensionsCm.Z != 0f)
                {
                    throw new ArgumentOutOfRangeException($"{path}.{nameof(shape.DimensionsCm)}", "Capsule stores radius in X and cylinder length in Y.");
                }
                break;
        }
    }

    private static void ValidateSpring(in Physics3DSpringSettings spring, string path)
    {
        RequireFinitePositive(spring.AngularFrequency, $"{path}.{nameof(spring.AngularFrequency)}");
        RequireFiniteNonNegative(spring.TwiceDampingRatio, $"{path}.{nameof(spring.TwiceDampingRatio)}");
    }

    private static void ValidateServo(in Physics3DServoSettings servo, string path)
    {
        RequireFiniteNonNegative(servo.MaximumSpeed, $"{path}.{nameof(servo.MaximumSpeed)}");
        RequireFiniteNonNegative(servo.BaseSpeed, $"{path}.{nameof(servo.BaseSpeed)}");
        RequireFinitePositive(servo.MaximumForce, $"{path}.{nameof(servo.MaximumForce)}");
    }

    private void RequireBoundaryTick(long tick)
    {
        if (_stepPrepared)
        {
            throw new InvalidOperationException(
                $"Ragdoll structural transition at tick {tick} is forbidden while tick {_lastPreparedTick} is in flight.");
        }

        if (tick < 0 || (_lastPreparedTick >= 0 && tick != _lastPreparedTick && tick != _lastPreparedTick + 1))
        {
            throw new InvalidOperationException(
                $"Ragdoll transition tick {tick} must be the observed tick {_lastPreparedTick} or next tick {_lastPreparedTick + 1}.");
        }
    }

    private void RequireObservedTick(long tick)
    {
        if (_stepPrepared || tick < 0 || tick != _lastPreparedTick)
        {
            throw new InvalidOperationException(
                $"Ragdoll recovery requires observed tick {_lastPreparedTick}, but received {tick}.");
        }
    }

    private int RequireRecipeSlot(RagdollRecipeId recipe)
    {
        if ((uint)recipe.Slot >= (uint)_recipeActive.Length ||
            _recipeActive[recipe.Slot] == 0 ||
            _recipeGenerations[recipe.Slot] != recipe.Generation)
        {
            throw new InvalidOperationException($"Ragdoll recipe {recipe} is stale or not registered.");
        }

        return recipe.Slot;
    }

    private int RequireInstanceSlot(RagdollInstanceId instance)
    {
        if ((uint)instance.Slot >= (uint)_instanceActive.Length ||
            _instanceActive[instance.Slot] == 0 ||
            _instanceGenerations[instance.Slot] != instance.Generation)
        {
            throw new InvalidOperationException($"Ragdoll instance {instance} is stale or not active.");
        }

        return instance.Slot;
    }

    private int InstanceBoneOffset(int instanceSlot) => checked(instanceSlot * _maximumBonesPerInstance);

    private static Quaternion CreateStandingOrientation(Quaternion rootOrientation, RagdollRecoveryStrategy strategy)
    {
        if (strategy == RagdollRecoveryStrategy.FaceWorldForward)
        {
            return Quaternion.Identity;
        }

        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rootOrientation);
        forward.Y = 0f;
        if (forward.LengthSquared() <= 1e-8f)
        {
            Vector3 right = Vector3.Transform(Vector3.UnitX, rootOrientation);
            right.Y = 0f;
            if (right.LengthSquared() <= 1e-8f)
            {
                throw new InvalidOperationException("Ragdoll root orientation has no finite horizontal recovery heading.");
            }

            forward = new Vector3(-right.Z, 0f, right.X);
        }

        forward = Vector3.Normalize(forward);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(forward.X, forward.Z));
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximum)
    {
        float lengthSquared = value.LengthSquared();
        float maximumSquared = maximum * maximum;
        if (lengthSquared <= maximumSquared || lengthSquared <= 1e-12f)
        {
            return value;
        }

        return value * (maximum / MathF.Sqrt(lengthSquared));
    }

    private static int NextGeneration(int current) => current == int.MaxValue ? 1 : current + 1;

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector must be finite.");
        }
    }

    private static void RequireUnitQuaternion(Quaternion value, string parameterName)
    {
        if (!IsUnitQuaternion(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Quaternion must be finite and normalized.");
        }
    }

    private static bool IsUnitQuaternion(Quaternion value)
        => IsFinite(value) && MathF.Abs(value.LengthSquared() - 1f) <= QuaternionLengthTolerance;

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite.");
        }
    }

    private static void RequireFinitePositive(float value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }

    private static void RequireFiniteNonNegative(float value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
