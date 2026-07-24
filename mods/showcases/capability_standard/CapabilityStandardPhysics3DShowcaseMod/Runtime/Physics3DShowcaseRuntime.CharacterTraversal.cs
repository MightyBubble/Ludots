using System;
using System.Numerics;
using Ludots.Core.Character3D;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Traversal3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    internal const string CharacterTraversalInputContext = "Physics3D.Playground.Character";
    internal const string PlayerMoveAction = "Physics3D.PlayerMove";
    internal const string PlayerJumpAction = "Physics3D.PlayerJump";
    internal const string PlayerTraverseAction = "Physics3D.PlayerTraverse";

    private static readonly Vector4 CharacterNormalColor = new(0.18f, 0.88f, 0.58f, 1f);
    private static readonly Vector4 CharacterAttachedColor = new(1.00f, 0.75f, 0.20f, 1f);
    private static readonly Vector4 CharacterClimbingColor = new(0.76f, 0.45f, 1.00f, 1f);
    private static readonly Vector4 CharacterLedgeColor = new(1.00f, 0.34f, 0.28f, 1f);
    private static readonly Vector4 CharacterMantleColor = new(0.22f, 0.72f, 1.00f, 1f);
    private static readonly Vector4 TraversalSurfaceColor = new(0.12f, 0.78f, 0.78f, 0.65f);

    private Character3DControllerSet? _characterControllers;
    private Traversal3DControllerSet? _traversalControllers;
    private Character3DHandle _playerCharacter;
    private Traversal3DHandle _playerTraversal;
    private Physics3DBodyId _ladderSurface;
    private Physics3DBodyId _wallSurface;
    private int _playerBodyIndex = -1;
    private int _movingPlatformBodyIndex = -1;
    private int _rotatingPlatformBodyIndex = -1;
    private Vector2 _capturedPlayerMove;
    private Vector3 _playerFacing = Vector3.UnitX;
    private bool _capturedJump;
    private bool _capturedTraverse;
    private Traversal3DState _lastTraversalState = Traversal3DState.NormalMovement;

    private void BuildPlatformStationScene()
    {
        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        AddFloor();
        RegisterCharacterTraversalShapes(config, out Physics3DShapeId characterShape, out Physics3DShapeId anchorShape);
        Physics3DShapeId platformShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.PlatformSizeXCm,
            config.PlatformSizeYCm,
            config.PlatformSizeZCm));
        Physics3DShapeId rotatingShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.RotatingPlatformRadiusCm * 2f,
            config.PlatformSizeYCm,
            config.RotatingPlatformRadiusCm * 2f));

        AddStaticRouteDeck(
            new Vector3(
                config.PlatformStationStartXCm,
                config.DeckThicknessCm * 0.5f,
                config.PlatformStationStartZCm),
            new Vector3(
                config.PlatformStationStartDeckSizeXCm,
                config.DeckThicknessCm,
                config.PlatformStationStartDeckSizeZCm),
            DynamicGreen);
        _movingPlatformBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            platformShape,
            Physics3DShapeKind.Box,
            new Vector3(config.PlatformSizeXCm, config.PlatformSizeYCm, config.PlatformSizeZCm),
            0f,
            new Vector3(config.MovingPlatformCenterXCm, config.MovingPlatformCenterYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor);
        _rotatingPlatformBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            rotatingShape,
            Physics3DShapeKind.Box,
            new Vector3(config.RotatingPlatformRadiusCm * 2f, config.PlatformSizeYCm, config.RotatingPlatformRadiusCm * 2f),
            0f,
            new Vector3(config.RotatingPlatformCenterXCm, config.RotatingPlatformCenterYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor);
        Physics3DShapeId conveyorShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.PlatformStationConveyorSizeXCm,
            config.DeckThicknessCm,
            config.PlatformStationConveyorSizeZCm));
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            conveyorShape,
            Physics3DShapeKind.Box,
            new Vector3(
                config.PlatformStationConveyorSizeXCm,
                config.DeckThicknessCm,
                config.PlatformStationConveyorSizeZCm),
            0f,
            new Vector3(
                config.RotatingPlatformCenterXCm + config.PlatformStationConveyorOffsetXCm,
                config.PlatformStationConveyorCenterYCm,
                0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            DynamicGold,
            contactPolicy: Physics3DBodyContactPolicy.SurfaceVelocity(
                new Vector3(config.PlatformStationConveyorSpeedCmPerSecond, 0f, 0f)));

        Physics3DShapeId oneWayShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.PlatformStationOneWaySizeXCm,
            config.DeckThicknessCm,
            config.PlatformStationOneWaySizeZCm));
        AddOwnedBody(
            Physics3DBodyKind.Static,
            oneWayShape,
            Physics3DShapeKind.Box,
            new Vector3(
                config.PlatformStationOneWaySizeXCm,
                config.DeckThicknessCm,
                config.PlatformStationOneWaySizeZCm),
            0f,
            new Vector3(
                config.PlatformStationOneWayCenterXCm,
                config.PlatformStationOneWayCenterYCm,
                0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            CharacterAttachedColor,
            contactPolicy: Physics3DBodyContactPolicy.OneWayPlatform(
                Vector3.UnitY,
                config.PlatformStationOneWayMinimumNormalAlignment,
                config.PlatformStationOneWayBackfaceToleranceCm,
                config.PlatformStationOneWayMaximumPassThroughRelativeSpeedCmPerSecond));

        BuildPlayerController(
            characterShape,
            anchorShape,
            new Vector3(
                config.PlatformStationStartXCm,
                config.DeckThicknessCm + CharacterCenterHeightAboveFloor(config),
                config.PlatformStationStartZCm));
    }

    private void BuildTraversalCourseScene()
    {
        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        AddFloor();
        RegisterCharacterTraversalShapes(config, out Physics3DShapeId characterShape, out Physics3DShapeId anchorShape);

        Physics3DShapeId rampShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.RampLengthCm,
            config.RampHeightCm,
            config.RampWidthCm));
        AddOwnedBody(
            Physics3DBodyKind.Static,
            rampShape,
            Physics3DShapeKind.Box,
            new Vector3(config.RampLengthCm, config.RampHeightCm, config.RampWidthCm),
            0f,
            new Vector3(config.RampCenterXCm, config.RampCenterYCm, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, config.RampAngleDegrees * (MathF.PI / 180f)),
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            DynamicBlue);

        for (int i = 0; i < config.StepCount; i++)
        {
            float height = config.StepHeightCm * (i + 1);
            Physics3DShapeId stepShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
                config.StepDepthCm,
                height,
                config.StepWidthCm));
            AddOwnedBody(
                Physics3DBodyKind.Static,
                stepShape,
                Physics3DShapeKind.Box,
                new Vector3(config.StepDepthCm, height, config.StepWidthCm),
                0f,
                new Vector3(config.StepStartXCm + (i * config.StepDepthCm), height * 0.5f, 0f),
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Discrete,
                DynamicGold);
        }

        Physics3DShapeId platformShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.PlatformSizeXCm,
            config.PlatformSizeYCm,
            config.PlatformSizeZCm));
        _movingPlatformBodyIndex = _bodyCount;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            platformShape,
            Physics3DShapeKind.Box,
            new Vector3(config.PlatformSizeXCm, config.PlatformSizeYCm, config.PlatformSizeZCm),
            0f,
            new Vector3(config.MovingPlatformCenterXCm, config.MovingPlatformCenterYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor);

        Physics3DShapeId ladderShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.LadderThicknessCm,
            config.LadderHeightCm,
            config.LadderWidthCm));
        _ladderSurface = AddOwnedBody(
            Physics3DBodyKind.Static,
            ladderShape,
            Physics3DShapeKind.Box,
            new Vector3(config.LadderThicknessCm, config.LadderHeightCm, config.LadderWidthCm),
            0f,
            new Vector3(config.LadderCenterXCm, config.LadderCenterYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            TraversalSurfaceColor,
            contactPolicy: Physics3DBodyContactPolicy.Sensor());
        AddStaticRouteDeck(
            new Vector3(config.LadderDeckCenterXCm, config.LadderDeckCenterYCm, 0f),
            new Vector3(config.LadderDeckLengthCm, config.DeckThicknessCm, config.LadderWidthCm),
            DynamicGreen);

        Physics3DShapeId wallShape = RequirePhysicsWorld().RegisterBoxShape(new Vector3(
            config.WallThicknessCm,
            config.WallHeightCm,
            config.WallWidthCm));
        _wallSurface = AddOwnedBody(
            Physics3DBodyKind.Static,
            wallShape,
            Physics3DShapeKind.Box,
            new Vector3(config.WallThicknessCm, config.WallHeightCm, config.WallWidthCm),
            0f,
            new Vector3(config.WallCenterXCm, config.WallCenterYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            StaticColor);
        AddStaticRouteDeck(
            new Vector3(config.WallDeckCenterXCm, config.WallDeckCenterYCm, 0f),
            new Vector3(config.WallDeckLengthCm, config.DeckThicknessCm, config.WallWidthCm),
            DynamicGold);

        BuildPlayerController(
            characterShape,
            anchorShape,
            new Vector3(config.CourseStartXCm, CharacterCenterHeightAboveFloor(config), config.CourseStartZCm));
        Traversal3DControllerSet traversal = RequireTraversalControllers();
        traversal.RegisterSurface(_ladderSurface, Traversal3DSurfaceKind.Ladder);
        traversal.RegisterSurface(_wallSurface, Traversal3DSurfaceKind.ClimbableWall);
    }

    private void BuildPlayerController(
        Physics3DShapeId characterShape,
        Physics3DShapeId anchorShape,
        Vector3 startPositionCm)
    {
        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            Physics3DShapeKind.Sphere,
            new Vector3(config.UprightAnchorRadiusCm * 2f),
            0f,
            new Vector3(0f, config.UprightAnchorParkingYCm, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            new Vector4(0f));
        Physics3DBodyId anchor = _bodyIds[_bodyCount - 1];
        _playerBodyIndex = _bodyCount;
        Physics3DBodyId playerBody = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            characterShape,
            Physics3DShapeKind.Capsule,
            new Vector3(
                config.CharacterRadiusCm * 2f,
                (config.CharacterRadiusCm * 2f) + config.CharacterCylinderLengthCm,
                config.CharacterRadiusCm * 2f),
            config.CharacterCylinderLengthCm,
            startPositionCm,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Continuous,
            CharacterNormalColor,
            config.CharacterMass);

        _characterControllers = new Character3DControllerSet(
            RequirePhysicsWorld(),
            config.ControllerCapacity,
            config.OverlapHitCapacity);
        _playerCharacter = _characterControllers.Register(playerBody, anchor, CreateCharacterProfile(config));
        _traversalControllers = new Traversal3DControllerSet(
            RequirePhysicsWorld(),
            _characterControllers,
            config.ControllerCapacity,
            config.BodySlotCapacity,
            config.OverlapHitCapacity);
        _playerTraversal = _traversalControllers.RegisterCharacter(_playerCharacter, CreateTraversalProfile(config));
        _capturedPlayerMove = Vector2.Zero;
        _capturedJump = false;
        _capturedTraverse = false;
        _playerFacing = Vector3.UnitX;
        _lastTraversalState = Traversal3DState.NormalMovement;
    }

    private void RegisterCharacterTraversalShapes(
        Physics3DCharacterTraversalShowcaseConfig config,
        out Physics3DShapeId characterShape,
        out Physics3DShapeId anchorShape)
    {
        characterShape = RequirePhysicsWorld().RegisterCapsuleShape(
            config.CharacterRadiusCm,
            config.CharacterCylinderLengthCm);
        anchorShape = RequirePhysicsWorld().RegisterSphereShape(config.UprightAnchorRadiusCm);
    }

    private void AddStaticRouteDeck(Vector3 positionCm, Vector3 sizeCm, Vector4 color)
    {
        Physics3DShapeId shape = RequirePhysicsWorld().RegisterBoxShape(sizeCm);
        AddOwnedBody(
            Physics3DBodyKind.Static,
            shape,
            Physics3DShapeKind.Box,
            sizeCm,
            0f,
            positionCm,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Discrete,
            color);
    }

    private void PrepareCharacterTraversalStep()
    {
        AnimateTraversalPlatforms();
        Traversal3DControllerSet traversal = RequireTraversalControllers();
        Character3DControllerSet characters = RequireCharacterControllers();
        traversal.SubmitIntent(
            _playerTraversal,
            new Traversal3DIntent(
                _capturedPlayerMove,
                _playerFacing,
                _capturedTraverse,
                _capturedJump));
        traversal.PrepareFixedStep();
        characters.PrepareFixedStep();
        _capturedJump = false;
        _capturedTraverse = false;
    }

    private void ObserveCharacterTraversalStep()
    {
        Character3DControllerSet characters = RequireCharacterControllers();
        characters.ObserveFixedStep();
        Character3DState character = characters.GetState(_playerCharacter);
        Traversal3DStatus traversal = RequireTraversalControllers().GetStatus(_playerTraversal);
        _bodyColors[_playerBodyIndex] = traversal.State switch
        {
            Traversal3DState.NormalMovement => CharacterNormalColor,
            Traversal3DState.Attached => CharacterAttachedColor,
            Traversal3DState.Climbing => CharacterClimbingColor,
            Traversal3DState.LedgeHang => CharacterLedgeColor,
            Traversal3DState.Mantling => CharacterMantleColor,
            Traversal3DState.Detaching => DynamicRed,
            _ => throw new InvalidOperationException($"Unknown traversal state '{traversal.State}'.")
        };

        if (traversal.State != _lastTraversalState)
        {
            _lastTraversalState = traversal.State;
            _lastAction = traversal.State switch
            {
                Traversal3DState.NormalMovement when character.IsGrounded => "Landed with stable ground support.",
                Traversal3DState.NormalMovement => "Returned to free character movement.",
                Traversal3DState.Attached => "Attached to the marked traversal surface.",
                Traversal3DState.Climbing => "Climbing while preserving the surface's own motion.",
                Traversal3DState.LedgeHang => "Ledge caught after hand and landing clearance passed.",
                Traversal3DState.Mantling => "Mantling toward the validated standing capsule position.",
                Traversal3DState.Detaching => "Detached with an outward and upward release velocity.",
                _ => throw new InvalidOperationException($"Unknown traversal state '{traversal.State}'.")
            };
        }
    }

    private void AnimateTraversalPlatforms()
    {
        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        float step = _sceneStep + 1f;
        if (_movingPlatformBodyIndex >= 0)
        {
            float phase = step * config.MovingPlatformSpeedRadiansPerStep;
            Vector3 nextPosition = new(
                config.MovingPlatformCenterXCm,
                config.MovingPlatformCenterYCm + (MathF.Sin(phase) * config.MovingPlatformTravelCm),
                MathF.Cos(phase) * config.MovingPlatformTravelCm * 0.35f);
            SetKinematicCourseNextPose(_movingPlatformBodyIndex, nextPosition, Quaternion.Identity);
        }

        if (_rotatingPlatformBodyIndex >= 0)
        {
            float angle = step * config.RotatingPlatformRadiansPerStep;
            SetKinematicCourseNextPose(
                _rotatingPlatformBodyIndex,
                new Vector3(config.RotatingPlatformCenterXCm, config.RotatingPlatformCenterYCm, 0f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        }
    }

    private void SetKinematicCourseNextPose(int bodyIndex, Vector3 nextPositionCm, Quaternion nextOrientation)
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        Physics3DBodyId body = _bodyIds[bodyIndex];
        world.SetKinematicNextPose(body, nextPositionCm, nextOrientation);
        Physics3DBodyState current = world.GetBodyState(body);
        ref Physics3DPoseCm pose = ref RequireEcsWorld().Get<Physics3DPoseCm>(_bodyEntities[bodyIndex]);
        pose.Position = nextPositionCm;
        pose.Orientation = nextOrientation;
        pose.LinearVelocity = current.LinearVelocityCmPerSecond;
        pose.AngularVelocity = current.AngularVelocityRadiansPerSecond;
    }

    internal void SetCharacterIntentForTests(Vector2 planarMove, bool jumpRequested, bool traverseRequested)
    {
        if (_scene is not (Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse))
        {
            throw new InvalidOperationException("Character intent is only valid in the platform station or traversal course.");
        }

        StoreCharacterTraversalInput(planarMove, jumpRequested, traverseRequested);
    }

    internal Character3DState GetPlayerCharacterStateForTests()
        => RequireCharacterControllers().GetState(_playerCharacter);

    internal Traversal3DStatus GetPlayerTraversalStatusForTests()
        => RequireTraversalControllers().GetStatus(_playerTraversal);

    private void CaptureCharacterTraversalInput(IInputActionReader? input)
    {
        if (_scene is not (Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse))
        {
            return;
        }

        if (_engine != null && input == null)
        {
            throw new InvalidOperationException("Physics3D character scenes require authoritative input.");
        }

        if (input == null)
        {
            return;
        }

        Vector2 rawMove = input.ReadAction<Vector2>(PlayerMoveAction);
        StoreCharacterTraversalInput(
            new Vector2(rawMove.Y, rawMove.X),
            input.PressedThisFrame(PlayerJumpAction),
            input.PressedThisFrame(PlayerTraverseAction));
    }

    private void StoreCharacterTraversalInput(
        Vector2 planarMove,
        bool jumpRequested,
        bool traverseRequested)
    {
        if (!float.IsFinite(planarMove.X) || !float.IsFinite(planarMove.Y) || planarMove.LengthSquared() > 1.0001f)
        {
            throw new InvalidOperationException($"Physics3D player planar move '{planarMove}' is invalid.");
        }

        _capturedPlayerMove = planarMove;
        Vector3 worldMove = new(_capturedPlayerMove.X, 0f, _capturedPlayerMove.Y);
        if (worldMove.LengthSquared() > 1e-6f)
        {
            _playerFacing = Vector3.Normalize(worldMove);
        }

        _capturedJump |= jumpRequested;
        _capturedTraverse |= traverseRequested;
    }

    private static void RequireCharacterTraversalInputSchema(PlayerInputHandler input)
    {
        if (!input.HasContext(CharacterTraversalInputContext))
        {
            throw new InvalidOperationException($"Missing input context: {CharacterTraversalInputContext}");
        }

        RequireAction(input, PlayerMoveAction);
        RequireAction(input, PlayerJumpAction);
        RequireAction(input, PlayerTraverseAction);
    }

    private static void RequireAction(PlayerInputHandler input, string action)
    {
        if (!input.HasAction(action))
        {
            throw new InvalidOperationException($"Missing input action: {action}");
        }
    }

    private void ReleaseCharacterTraversalScene()
    {
        if (_traversalControllers != null && _playerTraversal.IsValid)
        {
            _traversalControllers.UnregisterCharacter(_playerTraversal);
        }

        if (_characterControllers != null && _playerCharacter.IsValid)
        {
            _characterControllers.Unregister(_playerCharacter);
        }

        _traversalControllers = null;
        _characterControllers = null;
        _playerTraversal = default;
        _playerCharacter = default;
        _ladderSurface = default;
        _wallSurface = default;
        _playerBodyIndex = -1;
        _movingPlatformBodyIndex = -1;
        _rotatingPlatformBodyIndex = -1;
    }

    private Character3DControllerSet RequireCharacterControllers() => _characterControllers
        ?? throw new InvalidOperationException("Character3D controllers are unavailable for the selected scene.");

    private Traversal3DControllerSet RequireTraversalControllers() => _traversalControllers
        ?? throw new InvalidOperationException("Traversal3D controllers are unavailable for the selected scene.");

    private Character3DProfile CreateCharacterProfile(Physics3DCharacterTraversalShowcaseConfig config)
        => new(
            config.CharacterRadiusCm,
            config.CharacterCylinderLengthCm,
            config.MaximumGroundSpeedCmPerSecond,
            config.MaximumGroundAccelerationCmPerSecondSquared,
            config.MaximumAirSpeedCmPerSecond,
            config.MaximumAirAccelerationCmPerSecondSquared,
            config.JumpSpeedCmPerSecond,
            config.MaximumSlopeDegrees,
            config.SupportProbeDistanceCm,
            config.SkinWidthCm,
            config.MaximumStepHeightCm,
            config.StepForwardProbeDistanceCm,
            config.StepAssistSpeedCmPerSecond,
            config.CoyoteTicks,
            LayerMask.All,
            new Physics3DServoSettings(config.UprightMaximumSpeed, 0f, config.UprightMaximumForce),
            CreateSpring());

    private static Traversal3DProfile CreateTraversalProfile(Physics3DCharacterTraversalShowcaseConfig config)
        => new(
            config.AttachProbeDistanceCm,
            config.AttachSpeedCmPerSecond,
            config.ClimbSpeedCmPerSecond,
            config.LateralClimbSpeedCmPerSecond,
            config.TraversalMaximumAccelerationCmPerSecondSquared,
            config.LedgeProbeHeightCm,
            config.LedgeProbeForwardCm,
            config.LedgeProbeDownCm,
            config.MinimumLedgeHeightCm,
            config.HandClearanceRadiusCm,
            config.MantleForwardCm,
            config.MantleSpeedCmPerSecond,
            config.MantleCompletionDistanceCm,
            config.MinimumTopNormalY,
            config.DetachUpSpeedCmPerSecond,
            config.DetachOutSpeedCmPerSecond);

    private static float CharacterCenterHeightAboveFloor(Physics3DCharacterTraversalShowcaseConfig config)
        => config.CharacterRadiusCm + (config.CharacterCylinderLengthCm * 0.5f) + config.SkinWidthCm;
}
