using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Ragdoll;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private static readonly Vector4 RagdollBoneColor = new(0.88f, 0.68f, 0.28f, 1f);
    private static readonly Vector4 RagdollActiveColor = new(0.30f, 0.78f, 0.52f, 1f);
    private static readonly Vector4 RagdollPendulumColor = new(0.86f, 0.24f, 0.22f, 1f);

    private RagdollWorld? _ragdollLabWorld;
    private RagdollRecipeId _ragdollLabRecipe;
    private RagdollInstanceId _ragdollLabInstance;
    private Physics3DBodyId[] _ragdollLabBodies = Array.Empty<Physics3DBodyId>();
    private Physics3DShapeId[] _ragdollLabShapes = Array.Empty<Physics3DShapeId>();
    private Quaternion[] _ragdollLabActivePoseTargets = Array.Empty<Quaternion>();
    private RagdollBonePose[] _ragdollLabRecoveryPoses = Array.Empty<RagdollBonePose>();
    private int _ragdollLabBodyStartIndex = -1;
    private int _ragdollLabPendulumBodyIndex = -1;
    private int _ragdollLabRecoveryBlockers;
    private bool _ragdollLabActivePose;
    private bool _ragdollLabRecovered;

    internal void BuildRagdollLabScene(Physics3DRagdollLabShowcaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate(nameof(config));
        if (_ragdollLabWorld != null)
        {
            throw new InvalidOperationException("Ragdoll Lab is already active.");
        }

        AddFloor();
        BuildRagdollLabStairs(config);
        BuildRagdollLabPendulum(config);

        int boneCount = config.Bones.Length;
        _ragdollLabWorld = new RagdollWorld(RequirePhysicsWorld(), new RagdollConfig
        {
            RecipeCapacity = 1,
            RecipeBoneCapacity = boneCount,
            InstanceCapacity = 1,
            MaximumBonesPerInstance = boneCount,
            RecoveryOverlapHitCapacity = config.RecoveryOverlapHitCapacity,
            FixedStepHz = 30
        });

        RagdollRecipeDefinition recipeDefinition = config.CreateRecipe();
        _ragdollLabRecipe = _ragdollLabWorld.RegisterRecipe(recipeDefinition);
        _ragdollLabBodies = new Physics3DBodyId[boneCount];
        _ragdollLabShapes = new Physics3DShapeId[boneCount];
        _ragdollLabActivePoseTargets = new Quaternion[boneCount];
        _ragdollLabRecoveryPoses = new RagdollBonePose[boneCount];
        _ragdollLabWorld.CopyRecipeShapeIds(_ragdollLabRecipe, _ragdollLabShapes);

        var entities = new Entity[boneCount];
        var handoff = new RagdollBoneHandoff[boneCount];
        Vector3 rootPosition = new(config.StairStartXCm, config.RagdollStartHeightCm, 0f);
        BuildRagdollLabAnimationHandoff(recipeDefinition, rootPosition, entities, handoff);
        try
        {
            _ragdollLabInstance = _ragdollLabWorld.TransitionFromAnimation(
                _ragdollLabRecipe,
                0,
                new RagdollActivationDescription(
                    config.CollisionAssemblyId,
                    config.TotalMass,
                    LayerMask.All,
                    CreateMaterial(),
                    Physics3DContinuousDetectionMode.Continuous,
                    activePoseEnabled: true),
                handoff,
                _ragdollLabBodies);
        }
        catch
        {
            World ecsWorld = RequireEcsWorld();
            for (int i = entities.Length - 1; i >= 0; i--)
            {
                if (ecsWorld.IsAlive(entities[i]))
                {
                    ecsWorld.Destroy(entities[i]);
                }
            }

            _ragdollLabWorld.Dispose();
            _ragdollLabWorld = null;
            throw;
        }

        _ragdollLabBodyStartIndex = _bodyCount;
        for (int i = 0; i < boneCount; i++)
        {
            _ragdollLabActivePoseTargets[i] = recipeDefinition.Bones[i].LocalOrientation;
            RegisterRagdollLabOwnedBody(
                _ragdollLabBodies[i],
                entities[i],
                recipeDefinition.Bones[i].Shape,
                handoff[i],
                RagdollActiveColor);
        }

        _ragdollLabActivePose = true;
        _ragdollLabRecovered = false;
        _ragdollLabRecoveryBlockers = 0;
        _lastAction = "Ragdoll Lab ready: launch the pendulum, release active pose, then request recovery when the landing space is clear.";
    }

    internal void PrepareRagdollLabFixedStep()
    {
        if (_ragdollLabWorld == null || _ragdollLabRecovered)
        {
            return;
        }

        _ragdollLabWorld.SubmitActivePose(_ragdollLabInstance, _sceneStep, _ragdollLabActivePoseTargets);
        _ragdollLabWorld.PrepareFixedStep(_sceneStep);
    }

    internal void ObserveRagdollLabFixedStep(long observedTick)
    {
        if (_ragdollLabWorld == null || _ragdollLabRecovered)
        {
            return;
        }

        _ragdollLabWorld.ObserveFixedStep(observedTick);
    }

    internal void LaunchRagdollLabPendulum(Physics3DRagdollLabShowcaseConfig config)
    {
        if (_ragdollLabWorld == null || _ragdollLabPendulumBodyIndex < 0)
        {
            throw new InvalidOperationException("Ragdoll Lab pendulum is unavailable.");
        }

        IPhysics3DWorld physics = RequirePhysicsWorld();
        if (physics.ActuationCommandCapacity - physics.PendingActuationCommandCount < 1)
        {
            throw new InvalidOperationException("Ragdoll Lab requires one free Physics3D actuation command for the pendulum launch.");
        }

        Physics3DBodyId pendulum = _bodyIds[_ragdollLabPendulumBodyIndex];
        Physics3DBodyState pendulumState = physics.GetBodyState(pendulum);
        Physics3DBodyState rootState = physics.GetBodyState(_ragdollLabBodies[0]);
        Vector3 direction = rootState.PositionCm - pendulumState.PositionCm;
        if (direction.LengthSquared() <= 1e-6f)
        {
            throw new InvalidOperationException("Ragdoll Lab pendulum and mannequin root cannot share one position.");
        }

        direction = Vector3.Normalize(direction);
        physics.EnqueueImpulseAtWorldPoint(
            pendulum,
            direction * config.PendulumLaunchImpulse,
            pendulumState.PositionCm + new Vector3(0f, -ActiveConfig.BodySizeCm * 0.35f, 0f));
        _lastAction = "The pendulum is swinging toward the mannequin at the top of the stairs.";
    }

    internal void ToggleRagdollLabActivePose()
    {
        RagdollWorld ragdolls = _ragdollLabWorld
            ?? throw new InvalidOperationException("Ragdoll Lab is unavailable.");
        if (_ragdollLabRecovered)
        {
            throw new InvalidOperationException("The mannequin has already handed back to its standing animation.");
        }

        bool enabled = !_ragdollLabActivePose;
        ragdolls.SetActivePoseEnabled(_ragdollLabInstance, _sceneStep, enabled);
        _ragdollLabActivePose = enabled;
        for (int i = 0; i < _ragdollLabBodies.Length; i++)
        {
            _bodyColors[_ragdollLabBodyStartIndex + i] = enabled ? RagdollActiveColor : RagdollBoneColor;
        }

        _lastAction = enabled
            ? "Active pose engaged: the mannequin is trying to hold its authored stance through the same physical joints."
            : "Active pose released: the mannequin is now fully passive while joint limits remain active.";
    }

    internal bool TryRecoverRagdollLab(Physics3DRagdollLabShowcaseConfig config)
    {
        RagdollWorld ragdolls = _ragdollLabWorld
            ?? throw new InvalidOperationException("Ragdoll Lab is unavailable.");
        if (_ragdollLabRecovered)
        {
            throw new InvalidOperationException("The mannequin has already recovered.");
        }

        long observedTick = _sceneStep - 1;
        if (observedTick < 0)
        {
            throw new InvalidOperationException("Ragdoll recovery requires at least one observed 30Hz step.");
        }

        var geometry = new Character3DGeometry(
            default,
            config.RecoveryCharacterRadiusCm,
            config.RecoveryCharacterCylinderLengthCm,
            LayerMask.All);
        bool clear = ragdolls.TryBuildRecoveryCandidate(
            _ragdollLabInstance,
            observedTick,
            geometry,
            _ragdollLabRecoveryPoses,
            out RagdollRecoveryCandidate candidate);
        _ragdollLabRecoveryBlockers = candidate.BlockerCount;
        if (!clear)
        {
            _lastAction = $"Recovery blocked by {candidate.BlockerCount} overlapping object(s); the mannequin remains a ragdoll.";
            return false;
        }

        RagdollRecoveryCandidate committed = ragdolls.CommitRecovery(_ragdollLabInstance, observedTick);
        HandBackRagdollLabStandingPose(committed);
        _ragdollLabRecovered = true;
        _ragdollLabActivePose = false;
        _lastAction = "Clearance passed. The mannequin handed back to its standing animation without teleporting through an obstacle.";
        return true;
    }

    internal string CreateRagdollLabSummary()
    {
        if (_ragdollLabWorld == null)
        {
            return "Ragdoll Lab is not active.";
        }

        if (_ragdollLabRecovered)
        {
            return "RECOVERED · clearance passed · standing animation owns the pose";
        }

        RagdollInstanceState state = _ragdollLabWorld.GetInstanceState(_ragdollLabInstance);
        string pose = state.ActivePoseEnabled ? "ACTIVE POSE" : "PASSIVE";
        return state.RecoveryState == RagdollRecoveryState.Blocked
            ? $"{pose} · RECOVERY BLOCKED by {_ragdollLabRecoveryBlockers} object(s)"
            : $"{pose} · {state.BoneCount} bones · joint limits and subgroup filtering live";
    }

    internal void ReleaseRagdollLabScene()
    {
        if (_ragdollLabWorld != null)
        {
            _ragdollLabWorld.Dispose();
            _ragdollLabWorld = null;
        }

        _ragdollLabRecipe = default;
        _ragdollLabInstance = default;
        _ragdollLabBodies = Array.Empty<Physics3DBodyId>();
        _ragdollLabShapes = Array.Empty<Physics3DShapeId>();
        _ragdollLabActivePoseTargets = Array.Empty<Quaternion>();
        _ragdollLabRecoveryPoses = Array.Empty<RagdollBonePose>();
        _ragdollLabBodyStartIndex = -1;
        _ragdollLabPendulumBodyIndex = -1;
        _ragdollLabRecoveryBlockers = 0;
        _ragdollLabActivePose = false;
        _ragdollLabRecovered = false;
    }

    private void BuildRagdollLabStairs(Physics3DRagdollLabShowcaseConfig config)
    {
        Vector3 stairSize = new(config.StairDepthCm, config.StairHeightCm, config.StairWidthCm);
        Physics3DShapeId stairShape = RequirePhysicsWorld().RegisterBoxShape(stairSize);
        for (int i = 0; i < config.StairCount; i++)
        {
            float x = config.StairStartXCm + (i * config.StairDepthCm);
            float y = ((config.StairCount - i) * config.StairHeightCm) - (config.StairHeightCm * 0.5f);
            AddOwnedBody(
                Physics3DBodyKind.Static,
                stairShape,
                Physics3DShapeKind.Box,
                stairSize,
                0f,
                new Vector3(x, y, 0f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Discrete,
                StaticColor);
        }
    }

    private void BuildRagdollLabPendulum(Physics3DRagdollLabShowcaseConfig config)
    {
        Vector3 anchorPosition = new(config.PendulumAnchorXCm, config.PendulumAnchorYCm, 0f);
        Physics3DBodyId anchor = AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            _projectileShape,
            Physics3DShapeKind.Sphere,
            new Vector3(ActiveConfig.BodySizeCm * 0.4f),
            0f,
            anchorPosition,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor);
        _ragdollLabPendulumBodyIndex = _bodyCount;
        Physics3DBodyId pendulum = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _sphereShape,
            Physics3DShapeKind.Sphere,
            new Vector3(ActiveConfig.BodySizeCm),
            0f,
            anchorPosition - new Vector3(0f, config.PendulumRopeLengthCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Continuous,
            RagdollPendulumColor,
            mass: 120f);
        AddOwnedConstraint(RequirePhysicsWorld().CreateBallSocketConstraint(
            anchor,
            pendulum,
            Vector3.Zero,
            new Vector3(0f, config.PendulumRopeLengthCm, 0f),
            CreateSpring()));
    }

    private void BuildRagdollLabAnimationHandoff(
        RagdollRecipeDefinition recipe,
        Vector3 rootPosition,
        Entity[] entities,
        RagdollBoneHandoff[] handoff)
    {
        World ecsWorld = RequireEcsWorld();
        for (int i = 0; i < recipe.Bones.Length; i++)
        {
            RagdollBoneDefinition bone = recipe.Bones[i];
            Vector3 position;
            Quaternion orientation;
            if (bone.ParentIndex < 0)
            {
                position = rootPosition;
                orientation = bone.LocalOrientation;
            }
            else
            {
                RagdollBoneHandoff parent = handoff[bone.ParentIndex];
                position = parent.PositionCm + Vector3.Transform(bone.LocalPositionCm, parent.Orientation);
                orientation = Quaternion.Normalize(Quaternion.Concatenate(bone.LocalOrientation, parent.Orientation));
            }

            Entity entity = ecsWorld.Create(
                new Physics3DBodyCm { Id = default, Kind = Physics3DBodyKind.Dynamic },
                new Physics3DPoseCm { Position = position, Orientation = orientation },
                new PreviousPhysics3DPoseCm { Position = position, Orientation = orientation });
            entities[i] = entity;
            handoff[i] = new RagdollBoneHandoff(entity, position, orientation, Vector3.Zero, Vector3.Zero);
        }
    }

    private void RegisterRagdollLabOwnedBody(
        Physics3DBodyId body,
        Entity entity,
        in RagdollShapeDefinition shape,
        in RagdollBoneHandoff handoff,
        Vector4 color)
    {
        if (_bodyCount >= _bodyIds.Length)
        {
            throw new InvalidOperationException($"Ragdoll Lab exceeded showcase body capacity {_bodyIds.Length}.");
        }

        RequireEcsWorld().Set(entity, new Physics3DBodyCm { Id = body, Kind = Physics3DBodyKind.Dynamic });
        int index = _bodyCount++;
        _bodyIds[index] = body;
        _bodyEntities[index] = entity;
        _bodyKinds[index] = Physics3DBodyKind.Dynamic;
        _bodyShapeKinds[index] = shape.Kind switch
        {
            RagdollShapeKind.Box => Physics3DShapeKind.Box,
            RagdollShapeKind.Sphere => Physics3DShapeKind.Sphere,
            RagdollShapeKind.Capsule => Physics3DShapeKind.Capsule,
            _ => throw new InvalidOperationException($"Unsupported Ragdoll Lab shape {shape.Kind}.")
        };
        _bodyVisualSizesCm[index] = shape.Kind switch
        {
            RagdollShapeKind.Box => shape.DimensionsCm,
            RagdollShapeKind.Sphere => new Vector3(shape.DimensionsCm.X * 2f),
            RagdollShapeKind.Capsule => new Vector3(
                shape.DimensionsCm.X * 2f,
                (shape.DimensionsCm.X * 2f) + shape.DimensionsCm.Y,
                shape.DimensionsCm.X * 2f),
            _ => default
        };
        _bodyCapsuleCylinderLengthsCm[index] = shape.Kind == RagdollShapeKind.Capsule ? shape.DimensionsCm.Y : 0f;
        _bodyColors[index] = color;
        _dynamicBodyCount++;
    }

    private void HandBackRagdollLabStandingPose(in RagdollRecoveryCandidate candidate)
    {
        World ecsWorld = RequireEcsWorld();
        IPhysics3DWorld physics = RequirePhysicsWorld();
        for (int i = 0; i < _ragdollLabRecoveryPoses.Length; i++)
        {
            int bodyIndex = _ragdollLabBodyStartIndex + i;
            Entity entity = _bodyEntities[bodyIndex];
            RagdollBonePose pose = _ragdollLabRecoveryPoses[i];
            Physics3DBodyId body = physics.CreateBody(new Physics3DBodyDescription(
                entity,
                Physics3DBodyKind.Kinematic,
                _ragdollLabShapes[i],
                pose.PositionCm,
                pose.Orientation,
                candidate.InheritedLinearVelocityCmPerSecond,
                Vector3.Zero,
                0f,
                LayerMask.All,
                CreateMaterial(),
                Physics3DContinuousDetectionMode.Passive));
            _ragdollLabBodies[i] = body;
            _bodyIds[bodyIndex] = body;
            _bodyKinds[bodyIndex] = Physics3DBodyKind.Kinematic;
            _bodyColors[bodyIndex] = RagdollActiveColor;
            ecsWorld.Set(entity, new Physics3DBodyCm { Id = body, Kind = Physics3DBodyKind.Kinematic });
            ecsWorld.Set(entity, new Physics3DPoseCm
            {
                Position = pose.PositionCm,
                Orientation = pose.Orientation,
                LinearVelocity = candidate.InheritedLinearVelocityCmPerSecond,
                AngularVelocity = Vector3.Zero
            });
            ecsWorld.Set(entity, new PreviousPhysics3DPoseCm { Position = pose.PositionCm, Orientation = pose.Orientation });
        }

        _dynamicBodyCount -= _ragdollLabRecoveryPoses.Length;
        _kinematicBodyCount += _ragdollLabRecoveryPoses.Length;
    }
}
