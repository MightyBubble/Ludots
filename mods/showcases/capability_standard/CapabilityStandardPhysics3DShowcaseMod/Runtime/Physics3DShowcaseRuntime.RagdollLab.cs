using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Ragdoll;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal enum Physics3DRagdollLabPhase : byte
{
    Ready = 0,
    PendulumLaunched = 1,
    ImpactConfirmed = 2,
    Tumbling = 3,
    Settling = 4,
    Recoverable = 5,
    RecoveryBlocked = 6,
    Recovered = 7
}

internal readonly record struct Physics3DRagdollLabShowcaseState(
    Physics3DRagdollLabPhase Phase,
    bool ActivePoseEnabled,
    bool ImpactConfirmed,
    int StairStepsDescended,
    int SettledTicks,
    int RequiredSettledTicks,
    float MaximumLinearSpeedCmPerSecond,
    float MaximumAngularSpeedRadiansPerSecond,
    int RecoveryBlockerCount)
{
    public bool CanRecover => Phase is Physics3DRagdollLabPhase.Recoverable or Physics3DRagdollLabPhase.RecoveryBlocked;
    public bool IsRecovered => Phase == Physics3DRagdollLabPhase.Recovered;
}

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
    private RagdollBoneState[] _ragdollLabBoneStates = Array.Empty<RagdollBoneState>();
    private Physics3DContactPair[] _ragdollLabContactPairs = Array.Empty<Physics3DContactPair>();
    private int _ragdollLabBodyStartIndex = -1;
    private int _ragdollLabPendulumBodyIndex = -1;
    private int _ragdollLabRecoveryBlockers;
    private int _ragdollLabStairStepsDescended;
    private int _ragdollLabSettledTicks;
    private long _ragdollLabPendulumLaunchTick = -1;
    private float _ragdollLabMaximumLinearSpeedCmPerSecond;
    private float _ragdollLabMaximumAngularSpeedRadiansPerSecond;
    private float _ragdollLabClosestPendulumBoneDistanceCm;
    private Vector3 _ragdollLabInitialRootPositionCm;
    private Physics3DRagdollLabPhase _ragdollLabPhase;
    private bool _ragdollLabActivePose;
    private bool _ragdollLabRecovered;

    internal Physics3DRagdollLabShowcaseState RagdollLabState => new(
        _ragdollLabPhase,
        _ragdollLabActivePose,
        _ragdollLabPhase is not Physics3DRagdollLabPhase.Ready and
            not Physics3DRagdollLabPhase.PendulumLaunched,
        _ragdollLabStairStepsDescended,
        _ragdollLabSettledTicks,
        ActiveConfig.RagdollLab.RequiredSettledTicks,
        _ragdollLabMaximumLinearSpeedCmPerSecond,
        _ragdollLabMaximumAngularSpeedRadiansPerSecond,
        _ragdollLabRecoveryBlockers);

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
        _ragdollLabBoneStates = new RagdollBoneState[boneCount];
        _ragdollLabContactPairs = new Physics3DContactPair[config.ContactPairCapacity];
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
        _ragdollLabStairStepsDescended = 0;
        _ragdollLabSettledTicks = 0;
        _ragdollLabPendulumLaunchTick = -1;
        _ragdollLabMaximumLinearSpeedCmPerSecond = 0f;
        _ragdollLabMaximumAngularSpeedRadiansPerSecond = 0f;
        _ragdollLabClosestPendulumBoneDistanceCm = float.PositiveInfinity;
        _ragdollLabInitialRootPositionCm = rootPosition;
        _ragdollLabPhase = Physics3DRagdollLabPhase.Ready;
        _lastAction = "Ragdoll Lab ready: launch the pendulum, release active pose, then request recovery when the landing space is clear.";
    }

    internal void PrepareRagdollLabFixedStep()
    {
        if (_ragdollLabWorld == null || _ragdollLabRecovered)
        {
            return;
        }

        if (_ragdollLabActivePose)
        {
            _ragdollLabWorld.SubmitActivePose(_ragdollLabInstance, _sceneStep, _ragdollLabActivePoseTargets);
        }

        _ragdollLabWorld.PrepareFixedStep(_sceneStep);
    }

    internal void ObserveRagdollLabFixedStep(long observedTick)
    {
        if (_ragdollLabWorld == null || _ragdollLabRecovered)
        {
            return;
        }

        _ragdollLabWorld.ObserveFixedStep(observedTick);
        ObserveRagdollLabProgress(ActiveConfig.RagdollLab, observedTick);
    }

    internal void LaunchRagdollLabPendulum(Physics3DRagdollLabShowcaseConfig config)
    {
        if (_ragdollLabWorld == null || _ragdollLabPendulumBodyIndex < 0)
        {
            throw new InvalidOperationException("Ragdoll Lab pendulum is unavailable.");
        }

        if (_ragdollLabRecovered)
        {
            _lastAction = "The mannequin has already finished the ragdoll route in its standing display pose. Restart the route to launch again.";
            return;
        }

        if (_ragdollLabPhase != Physics3DRagdollLabPhase.Ready)
        {
            _lastAction = "The pendulum run is already in progress. Restart the route before launching another run.";
            return;
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
        _ragdollLabPendulumLaunchTick = _sceneStep;
        _ragdollLabPhase = Physics3DRagdollLabPhase.PendulumLaunched;
        _lastAction = "The pendulum is swinging toward the mannequin at the top of the stairs.";
    }

    internal void ToggleRagdollLabActivePose()
    {
        RagdollWorld ragdolls = _ragdollLabWorld
            ?? throw new InvalidOperationException("Ragdoll Lab is unavailable.");
        if (_ragdollLabRecovered)
        {
            _lastAction = "The mannequin is already in its standing display pose. Restart the route to test active pose again.";
            return;
        }

        bool enabled = !_ragdollLabActivePose;
        ragdolls.SetActivePoseEnabled(_ragdollLabInstance, _sceneStep, enabled);
        _ragdollLabActivePose = enabled;
        _ragdollLabSettledTicks = 0;
        if (_ragdollLabPhase is Physics3DRagdollLabPhase.ImpactConfirmed or
            Physics3DRagdollLabPhase.Tumbling or
            Physics3DRagdollLabPhase.Settling or
            Physics3DRagdollLabPhase.Recoverable or
            Physics3DRagdollLabPhase.RecoveryBlocked)
        {
            _ragdollLabPhase = enabled
                ? Physics3DRagdollLabPhase.ImpactConfirmed
                : Physics3DRagdollLabPhase.Tumbling;
        }
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
            _lastAction = "Recovery is already complete. The mannequin remains in its standing display pose until the route is restarted.";
            return true;
        }

        if (_ragdollLabActivePose)
        {
            _lastAction = "Recovery is unavailable while active pose is holding the mannequin. Release active pose first.";
            return false;
        }

        if (_ragdollLabPhase is not Physics3DRagdollLabPhase.Recoverable and
            not Physics3DRagdollLabPhase.RecoveryBlocked)
        {
            _lastAction =
                $"Recovery is not ready: the mannequin is still moving or has not cleared {config.MinimumStairStepsDescended} stair steps " +
                $"(stable {_ragdollLabSettledTicks}/{config.RequiredSettledTicks}).";
            return false;
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
            _ragdollLabPhase = Physics3DRagdollLabPhase.RecoveryBlocked;
            _lastAction = $"Recovery blocked by {candidate.BlockerCount} overlapping object(s); the mannequin remains a ragdoll.";
            return false;
        }

        RagdollRecoveryCandidate committed = ragdolls.CommitRecovery(_ragdollLabInstance, observedTick);
        BuildRagdollLabStandingDisplayPose(committed);
        _ragdollLabRecovered = true;
        _ragdollLabActivePose = false;
        _ragdollLabPhase = Physics3DRagdollLabPhase.Recovered;
        _lastAction = "Clearance passed. Dynamic ragdoll simulation ended and the mannequin moved to its standing display pose.";
        return true;
    }

    private void ObserveRagdollLabProgress(Physics3DRagdollLabShowcaseConfig config, long observedTick)
    {
        RagdollWorld ragdolls = _ragdollLabWorld
            ?? throw new InvalidOperationException("Ragdoll Lab is unavailable while observing progress.");
        int boneCount = ragdolls.CopyBoneStates(
            _ragdollLabInstance,
            observedTick,
            _ragdollLabBoneStates);
        float maximumLinearSpeed = 0f;
        float maximumAngularSpeed = 0f;
        for (int i = 0; i < boneCount; i++)
        {
            Physics3DBodyState state = _ragdollLabBoneStates[i].State;
            maximumLinearSpeed = MathF.Max(maximumLinearSpeed, state.LinearVelocityCmPerSecond.Length());
            maximumAngularSpeed = MathF.Max(maximumAngularSpeed, state.AngularVelocityRadiansPerSecond.Length());
        }

        _ragdollLabMaximumLinearSpeedCmPerSecond = maximumLinearSpeed;
        _ragdollLabMaximumAngularSpeedRadiansPerSecond = maximumAngularSpeed;
        Physics3DBodyState pendulumState = RequirePhysicsWorld().GetBodyState(
            _bodyIds[_ragdollLabPendulumBodyIndex]);
        for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            _ragdollLabClosestPendulumBoneDistanceCm = MathF.Min(
                _ragdollLabClosestPendulumBoneDistanceCm,
                Vector3.Distance(pendulumState.PositionCm, _ragdollLabBoneStates[boneIndex].State.PositionCm));
        }

        if (_ragdollLabPhase == Physics3DRagdollLabPhase.PendulumLaunched)
        {
            if (HasRagdollLabPendulumContact())
            {
                _ragdollLabPhase = Physics3DRagdollLabPhase.ImpactConfirmed;
                _lastAction = "Impact confirmed: the pendulum contacted a mannequin bone in the live physics world.";
            }
            else if (observedTick - _ragdollLabPendulumLaunchTick >= config.PendulumImpactTimeoutTicks)
            {
                throw new InvalidOperationException(
                    $"Ragdoll Lab pendulum did not contact a mannequin bone within {config.PendulumImpactTimeoutTicks} fixed ticks. " +
                    $"Closest center distance was {_ragdollLabClosestPendulumBoneDistanceCm:0.###} cm; " +
                    $"pendulum={pendulumState.PositionCm}, root={_ragdollLabBoneStates[0].State.PositionCm}. " +
                    "The authored anchor, rope, impulse, and mannequin placement must define a real collision route.");
            }
        }

        if (_ragdollLabPhase is Physics3DRagdollLabPhase.Ready or
            Physics3DRagdollLabPhase.PendulumLaunched)
        {
            return;
        }

        float downhillTravelCm = _ragdollLabBoneStates[0].State.PositionCm.X - _ragdollLabInitialRootPositionCm.X;
        int observedStairStepsDescended = Math.Clamp(
            (int)MathF.Floor(MathF.Max(0f, downhillTravelCm) / config.StairDepthCm),
            0,
            config.StairCount - 1);
        _ragdollLabStairStepsDescended = Math.Max(
            _ragdollLabStairStepsDescended,
            observedStairStepsDescended);

        if (_ragdollLabPhase == Physics3DRagdollLabPhase.RecoveryBlocked)
        {
            return;
        }

        if (_ragdollLabActivePose)
        {
            _ragdollLabSettledTicks = 0;
            _ragdollLabPhase = Physics3DRagdollLabPhase.ImpactConfirmed;
            return;
        }

        bool clearedRequiredSteps = _ragdollLabStairStepsDescended >= config.MinimumStairStepsDescended;
        bool motionSettled = IsRagdollLabMotionBelowSettleThreshold(config);
        if (!clearedRequiredSteps || !motionSettled)
        {
            _ragdollLabSettledTicks = 0;
            _ragdollLabPhase = Physics3DRagdollLabPhase.Tumbling;
            return;
        }

        if (_ragdollLabSettledTicks < config.RequiredSettledTicks)
        {
            _ragdollLabSettledTicks++;
        }

        _ragdollLabPhase = _ragdollLabSettledTicks >= config.RequiredSettledTicks
            ? Physics3DRagdollLabPhase.Recoverable
            : Physics3DRagdollLabPhase.Settling;
        if (_ragdollLabPhase == Physics3DRagdollLabPhase.Recoverable &&
            _ragdollLabSettledTicks == config.RequiredSettledTicks)
        {
            _lastAction = "The mannequin cleared the stair route and stayed still long enough for a recovery attempt.";
        }
    }

    private bool HasRagdollLabPendulumContact()
    {
        IPhysics3DWorld physics = RequirePhysicsWorld();
        int contactCount = physics.CopyContactPairs(_ragdollLabContactPairs);
        Physics3DBodyId pendulum = _bodyIds[_ragdollLabPendulumBodyIndex];
        for (int contactIndex = 0; contactIndex < contactCount; contactIndex++)
        {
            Physics3DContactPair contact = _ragdollLabContactPairs[contactIndex];
            Physics3DBodyId other;
            if (contact.BodyA == pendulum)
            {
                other = contact.BodyB;
            }
            else if (contact.BodyB == pendulum)
            {
                other = contact.BodyA;
            }
            else
            {
                continue;
            }

            for (int boneIndex = 0; boneIndex < _ragdollLabBodies.Length; boneIndex++)
            {
                if (_ragdollLabBodies[boneIndex] == other)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsRagdollLabMotionBelowSettleThreshold(Physics3DRagdollLabShowcaseConfig config)
    {
        return _ragdollLabMaximumLinearSpeedCmPerSecond <= config.SettledLinearSpeedCmPerSecond &&
               _ragdollLabMaximumAngularSpeedRadiansPerSecond <= config.SettledAngularSpeedRadiansPerSecond;
    }

    internal string CreateRagdollLabSummary()
    {
        if (_ragdollLabWorld == null)
        {
            return "Ragdoll Lab is not active.";
        }

        if (_ragdollLabRecovered)
        {
            return "RECOVERED · clearance passed · dynamic ragdoll retired · standing display pose";
        }

        RagdollInstanceState state = _ragdollLabWorld.GetInstanceState(_ragdollLabInstance);
        string pose = state.ActivePoseEnabled ? "ACTIVE POSE" : "PASSIVE";
        return _ragdollLabPhase switch
        {
            Physics3DRagdollLabPhase.Ready =>
                $"READY · {pose} · {state.BoneCount} bones · launch the pendulum",
            Physics3DRagdollLabPhase.PendulumLaunched =>
                $"PENDULUM IN FLIGHT · {pose} · waiting for real bone contact",
            Physics3DRagdollLabPhase.ImpactConfirmed =>
                $"IMPACT CONFIRMED · {pose} · release active pose to tumble",
            Physics3DRagdollLabPhase.Tumbling =>
                $"TUMBLING · PASSIVE · {_ragdollLabStairStepsDescended} stair steps cleared",
            Physics3DRagdollLabPhase.Settling =>
                $"SETTLING · {_ragdollLabStairStepsDescended} steps · stable {_ragdollLabSettledTicks}/{ActiveConfig.RagdollLab.RequiredSettledTicks}",
            Physics3DRagdollLabPhase.Recoverable =>
                $"RECOVERY READY · {_ragdollLabStairStepsDescended} steps · landing motion settled",
            Physics3DRagdollLabPhase.RecoveryBlocked =>
                $"RECOVERY BLOCKED by {_ragdollLabRecoveryBlockers} object(s) · mannequin remains dynamic",
            _ => throw new InvalidOperationException($"Unsupported Ragdoll Lab phase '{_ragdollLabPhase}'.")
        };
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
        _ragdollLabBoneStates = Array.Empty<RagdollBoneState>();
        _ragdollLabContactPairs = Array.Empty<Physics3DContactPair>();
        _ragdollLabBodyStartIndex = -1;
        _ragdollLabPendulumBodyIndex = -1;
        _ragdollLabRecoveryBlockers = 0;
        _ragdollLabStairStepsDescended = 0;
        _ragdollLabSettledTicks = 0;
        _ragdollLabPendulumLaunchTick = -1;
        _ragdollLabMaximumLinearSpeedCmPerSecond = 0f;
        _ragdollLabMaximumAngularSpeedRadiansPerSecond = 0f;
        _ragdollLabClosestPendulumBoneDistanceCm = float.PositiveInfinity;
        _ragdollLabInitialRootPositionCm = default;
        _ragdollLabPhase = Physics3DRagdollLabPhase.Ready;
        _ragdollLabActivePose = false;
        _ragdollLabRecovered = false;
    }

    private void BuildRagdollLabStairs(Physics3DRagdollLabShowcaseConfig config)
    {
        Vector3 stairSize = new(config.StairDepthCm, config.StairHeightCm, config.StairWidthCm);
        Physics3DShapeId stairShape = RequirePhysicsWorld().RegisterBoxShape(stairSize);
        Vector3 topLandingSize = new(config.TopLandingDepthCm, config.StairHeightCm, config.StairWidthCm);
        Physics3DShapeId topLandingShape = RequirePhysicsWorld().RegisterBoxShape(topLandingSize);
        for (int i = 0; i < config.StairCount; i++)
        {
            bool isTopLanding = i == 0;
            float x = isTopLanding
                ? config.StairStartXCm - ((config.TopLandingDepthCm - config.StairDepthCm) * 0.5f)
                : config.StairStartXCm + (i * config.StairDepthCm);
            float y = ((config.StairCount - i) * config.StairHeightCm) - (config.StairHeightCm * 0.5f);
            AddOwnedBody(
                Physics3DBodyKind.Static,
                isTopLanding ? topLandingShape : stairShape,
                Physics3DShapeKind.Box,
                isTopLanding ? topLandingSize : stairSize,
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
        Physics3DShapeId pendulumShape = RequirePhysicsWorld().RegisterSphereShape(config.PendulumRadiusCm);
        Physics3DBodyId pendulum = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            pendulumShape,
            Physics3DShapeKind.Sphere,
            new Vector3(config.PendulumRadiusCm * 2f),
            0f,
            anchorPosition - new Vector3(0f, config.PendulumRopeLengthCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Continuous,
            RagdollPendulumColor,
            mass: config.PendulumMass);
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

    private void BuildRagdollLabStandingDisplayPose(in RagdollRecoveryCandidate candidate)
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
