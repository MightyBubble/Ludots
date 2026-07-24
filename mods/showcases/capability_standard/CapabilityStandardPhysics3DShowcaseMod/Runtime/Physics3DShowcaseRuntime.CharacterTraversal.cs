using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Character3D;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;
using Ludots.Core.Traversal3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal enum Physics3DShowcaseRouteStatus : byte
{
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

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
    private Physics3DBodyId _routeMovingSurface;
    private Physics3DBodyId _routeRotatingSurface;
    private Physics3DBodyId _routeConveyorSurface;
    private Physics3DBodyId _routeFinishSurface;
    private Physics3DBodyId _routeWallDeckSurface;
    private int _playerBodyIndex = -1;
    private int _movingPlatformBodyIndex = -1;
    private int _rotatingPlatformBodyIndex = -1;
    private Vector2 _capturedPlayerMove;
    private Vector3 _playerFacing = Vector3.UnitX;
    private bool _capturedJump;
    private bool _capturedTraverse;
    private Traversal3DState _lastTraversalState = Traversal3DState.NormalMovement;
    private Physics3DShowcaseRouteStatus _characterRouteStatus = Physics3DShowcaseRouteStatus.InProgress;
    private int _characterRouteCheckpointIndex;
    private bool _characterCameraActive;

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
        _routeMovingSurface = AddOwnedBody(
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
        _routeRotatingSurface = AddOwnedBody(
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
        _routeConveyorSurface = AddOwnedBody(
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
        _routeFinishSurface = AddOwnedBody(
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
        _routeMovingSurface = AddOwnedBody(
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
        _routeWallDeckSurface = AddStaticRouteDeck(
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
        _characterRouteStatus = Physics3DShowcaseRouteStatus.InProgress;
        _characterRouteCheckpointIndex = 0;
        ActivateCharacterRouteCamera();
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

    private Physics3DBodyId AddStaticRouteDeck(Vector3 positionCm, Vector3 sizeCm, Vector4 color)
    {
        Physics3DShapeId shape = RequirePhysicsWorld().RegisterBoxShape(sizeCm);
        return AddOwnedBody(
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

        ObserveCharacterRoute(in character, in traversal);
    }

    private void ObserveCharacterRoute(
        in Character3DState character,
        in Traversal3DStatus traversal)
    {
        if (_characterRouteStatus != Physics3DShowcaseRouteStatus.InProgress)
        {
            return;
        }

        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        int timeLimitTicks = CharacterRouteTimeLimitTicks(config);
        if (_sceneStep >= timeLimitTicks)
        {
            FailCharacterRoute("Time expired before the finish. Press Restart Route to try again.");
            return;
        }

        float routeCenterZ = _scene == Physics3DShowcaseScene.PlatformStation
            ? config.PlatformStationStartZCm
            : config.CourseStartZCm;
        if (character.PositionCm.Y < config.RouteFailureMinimumYCm)
        {
            FailCharacterRoute("You fell below the course. Press Restart Route to return to the start.");
            return;
        }

        if (MathF.Abs(character.PositionCm.Z - routeCenterZ) > config.RouteMaximumLateralOffsetCm)
        {
            FailCharacterRoute("You left the marked lane. Press Restart Route to return to the start.");
            return;
        }

        bool checkpointReached = _scene switch
        {
            Physics3DShowcaseScene.PlatformStation => PlatformCheckpointReached(in character),
            Physics3DShowcaseScene.TraversalCourse => TraversalCheckpointReached(in character, in traversal, config),
            _ => throw new InvalidOperationException($"Character route state is unavailable for scene '{_scene}'.")
        };
        if (!checkpointReached)
        {
            return;
        }

        _characterRouteCheckpointIndex++;
        int checkpointCount = CharacterRouteCheckpointCount;
        if (_characterRouteCheckpointIndex >= checkpointCount)
        {
            _characterRouteStatus = Physics3DShowcaseRouteStatus.Completed;
            _lastAction = _scene == Physics3DShowcaseScene.PlatformStation
                ? "Route complete: you crossed all four live platform surfaces. Restart Route to run it again."
                : "Route complete: you reached the upper deck after both mantles. Restart Route to run it again.";
            return;
        }

        _lastAction = $"Checkpoint {_characterRouteCheckpointIndex}/{checkpointCount} complete. {CharacterRouteNextAction}";
    }

    private bool PlatformCheckpointReached(in Character3DState character)
    {
        if (!character.IsGrounded)
        {
            return false;
        }

        Physics3DBodyId expectedSupport = _characterRouteCheckpointIndex switch
        {
            0 => _routeMovingSurface,
            1 => _routeRotatingSurface,
            2 => _routeConveyorSurface,
            3 => _routeFinishSurface,
            _ => throw new InvalidOperationException(
                $"Platform Station route checkpoint {_characterRouteCheckpointIndex} is outside its authored range.")
        };
        return character.SupportBody == expectedSupport;
    }

    private bool TraversalCheckpointReached(
        in Character3DState character,
        in Traversal3DStatus traversal,
        Physics3DCharacterTraversalShowcaseConfig config)
    {
        return _characterRouteCheckpointIndex switch
        {
            0 => character.PositionCm.X >= config.RampCenterXCm,
            1 => character.PositionCm.X >= config.StepStartXCm + ((config.StepCount - 1) * config.StepDepthCm),
            2 => character.PositionCm.X >= config.MovingPlatformCenterXCm,
            3 => traversal.SurfaceBody == _ladderSurface && traversal.State == Traversal3DState.Mantling,
            4 => traversal.SurfaceBody == _wallSurface && traversal.State == Traversal3DState.Mantling,
            5 => traversal.State == Traversal3DState.NormalMovement &&
                 character.IsGrounded &&
                 character.SupportBody == _routeWallDeckSurface &&
                 character.PositionCm.Y >= TraversalFinishMinimumCharacterHeightCm(config),
            _ => throw new InvalidOperationException(
                $"Traversal Course route checkpoint {_characterRouteCheckpointIndex} is outside its authored range.")
        };
    }

    private void FailCharacterRoute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A visible route failure reason is required.", nameof(reason));
        }

        _characterRouteStatus = Physics3DShowcaseRouteStatus.Failed;
        _lastAction = $"Route failed: {reason}";
    }

    private int CharacterRouteTimeLimitTicks(Physics3DCharacterTraversalShowcaseConfig config)
        => _scene switch
        {
            Physics3DShowcaseScene.PlatformStation => config.PlatformRouteTimeLimitTicks,
            Physics3DShowcaseScene.TraversalCourse => config.TraversalRouteTimeLimitTicks,
            _ => throw new InvalidOperationException($"Scene '{_scene}' does not have a character route time limit.")
        };

    private static float TraversalFinishMinimumCharacterHeightCm(
        Physics3DCharacterTraversalShowcaseConfig config)
        => config.WallDeckCenterYCm +
           (config.DeckThicknessCm * 0.5f) +
           CharacterCenterHeightAboveFloor(config) -
           config.RouteCompletionHeightToleranceCm;

    internal int CharacterRouteCheckpointCount => _scene switch
    {
        Physics3DShowcaseScene.PlatformStation => 4,
        Physics3DShowcaseScene.TraversalCourse => 6,
        _ => 0
    };

    internal int CharacterRouteCheckpointIndex => _characterRouteCheckpointIndex;

    internal Physics3DShowcaseRouteStatus CharacterRouteStatus => _characterRouteStatus;

    internal int CharacterRouteTicksRemaining
    {
        get
        {
            if (_scene is not (Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse))
            {
                return 0;
            }

            return Math.Max(0, CharacterRouteTimeLimitTicks(ActiveConfig.CharacterTraversal) - checked((int)_sceneStep));
        }
    }

    internal string CharacterRouteNextAction
    {
        get
        {
            if (_characterRouteStatus == Physics3DShowcaseRouteStatus.Failed)
            {
                return "Press Restart Route to return to the authored start state.";
            }

            if (_characterRouteStatus == Physics3DShowcaseRouteStatus.Completed)
            {
                return "Route complete. Restart whenever you want another run.";
            }

            return _scene switch
            {
                Physics3DShowcaseScene.PlatformStation => _characterRouteCheckpointIndex switch
                {
                    0 => "Board the purple moving lift.",
                    1 => "Land on the purple rotating platform.",
                    2 => "Cross the gold conveyor without leaving the lane.",
                    3 => "Land on the orange one-way finish platform.",
                    _ => throw new InvalidOperationException("Platform Station route progress is invalid.")
                },
                Physics3DShowcaseScene.TraversalCourse => _characterRouteCheckpointIndex switch
                {
                    0 => "Run up the blue slope.",
                    1 => "Climb the gold steps.",
                    2 => "Cross the purple moving-platform checkpoint.",
                    3 => "Press E at the cyan ladder, climb, and mantle the ledge.",
                    4 => "Jump to the wall, press E, and mantle the upper ledge.",
                    5 => "Finish the mantle and stand securely on the upper deck.",
                    _ => throw new InvalidOperationException("Traversal Course route progress is invalid.")
                },
                _ => string.Empty
            };
        }
    }

    internal string CharacterRouteSummary => _characterRouteStatus switch
    {
        Physics3DShowcaseRouteStatus.InProgress =>
            $"RUNNING | {_characterRouteCheckpointIndex}/{CharacterRouteCheckpointCount} checkpoints | {CharacterRouteTicksRemaining} ticks left",
        Physics3DShowcaseRouteStatus.Completed =>
            $"COMPLETE | {CharacterRouteCheckpointCount}/{CharacterRouteCheckpointCount} checkpoints",
        Physics3DShowcaseRouteStatus.Failed =>
            $"FAILED | {_characterRouteCheckpointIndex}/{CharacterRouteCheckpointCount} checkpoints | restart required",
        _ => throw new InvalidOperationException($"Unknown character route status '{_characterRouteStatus}'.")
    };

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

    internal void PlacePlayerOnPlatformCheckpointForTests(int checkpointIndex)
    {
        if (_scene != Physics3DShowcaseScene.PlatformStation)
        {
            throw new InvalidOperationException("Platform checkpoint placement is only valid in Platform Station.");
        }

        if (checkpointIndex != _characterRouteCheckpointIndex || (uint)checkpointIndex >= 4u)
        {
            throw new InvalidOperationException(
                $"Platform checkpoint {checkpointIndex} cannot be placed while route progress is {_characterRouteCheckpointIndex}/4.");
        }

        Physics3DCharacterTraversalShowcaseConfig config = ActiveConfig.CharacterTraversal;
        Physics3DBodyId support = checkpointIndex switch
        {
            0 => _routeMovingSurface,
            1 => _routeRotatingSurface,
            2 => _routeConveyorSurface,
            3 => _routeFinishSurface,
            _ => throw new InvalidOperationException($"Platform checkpoint {checkpointIndex} is not authored.")
        };
        float supportHeightCm = checkpointIndex < 2
            ? config.PlatformSizeYCm
            : config.DeckThicknessCm;
        Physics3DBodyState supportState = RequirePhysicsWorld().GetBodyState(support);
        Physics3DBodyState playerState = RequirePhysicsWorld().GetBodyState(_bodyIds[_playerBodyIndex]);
        playerState.PositionCm = supportState.PositionCm +
                                 new Vector3(0f, (supportHeightCm * 0.5f) + CharacterCenterHeightAboveFloor(config), 0f);
        playerState.Orientation = Quaternion.Identity;
        playerState.LinearVelocityCmPerSecond = supportState.LinearVelocityCmPerSecond;
        playerState.AngularVelocityRadiansPerSecond = Vector3.Zero;
        playerState.Awake = true;
        RequirePhysicsWorld().SetBodyState(_bodyIds[_playerBodyIndex], in playerState);
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
        ReleaseCharacterRouteCamera();
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
        _routeMovingSurface = default;
        _routeRotatingSurface = default;
        _routeConveyorSurface = default;
        _routeFinishSurface = default;
        _routeWallDeckSurface = default;
        _playerBodyIndex = -1;
        _movingPlatformBodyIndex = -1;
        _rotatingPlatformBodyIndex = -1;
        _characterRouteCheckpointIndex = 0;
        _characterRouteStatus = Physics3DShowcaseRouteStatus.InProgress;
    }

    internal void SynchronizeCharacterRouteCameraAfterMapFocus()
    {
        if (_scene is Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse)
        {
            ActivateCharacterRouteCamera();
        }
    }

    private void ActivateCharacterRouteCamera()
    {
        if (_engine == null)
        {
            return;
        }

        if (_playerBodyIndex < 0)
        {
            throw new InvalidOperationException("Character route camera cannot activate before the player body exists.");
        }

        string cameraId = ActiveConfig.CharacterTraversal.CharacterCameraId;
        VirtualCameraRegistry registry = _engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException("Physics3D character routes require VirtualCameraRegistry.");
        VirtualCameraDefinition definition = registry.Get(cameraId);
        if (definition.TargetSource != VirtualCameraTargetSource.FollowTarget ||
            definition.FollowMode != CameraFollowMode.AlwaysFollow)
        {
            throw new InvalidOperationException(
                $"Physics3D character route camera '{cameraId}' must use FollowTarget and AlwaysFollow.");
        }

        _engine.GameSession.Camera.ResetVirtualCameras();
        _engine.GameSession.Camera.ActivateVirtualCamera(
            cameraId,
            blendDurationSeconds: 0f,
            followTarget: new DirectTransformFollowTarget(CapturePlayerCameraTarget),
            snapToFollowTargetWhenAvailable: true,
            resetRuntimeState: true);
        _engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
        _characterCameraActive = true;
    }

    private CameraTargetTransformSnapshot CapturePlayerCameraTarget()
    {
        if (_playerBodyIndex < 0 || _playerBodyIndex >= _bodyCount)
        {
            throw new InvalidOperationException("Physics3D character route camera lost its player body.");
        }

        Physics3DBodyState state = RequirePhysicsWorld().GetBodyState(_bodyIds[_playerBodyIndex]);
        return new CameraTargetTransformSnapshot(
            new Vector2(state.PositionCm.X, state.PositionCm.Z),
            hasHeightCm: true,
            heightCm: state.PositionCm.Y + ActiveConfig.CharacterTraversal.CharacterCameraTargetHeightOffsetCm);
    }

    private void ReleaseCharacterRouteCamera()
    {
        if (!_characterCameraActive)
        {
            return;
        }

        GameEngine engine = _engine
            ?? throw new InvalidOperationException("Physics3D character route lost GameEngine before camera release.");
        var cameraConfig = engine.CurrentMapSession?.MapConfig?.DefaultCamera
            ?? throw new InvalidOperationException("Physics3D character route requires map DefaultCamera for release.");
        if (string.IsNullOrWhiteSpace(cameraConfig.VirtualCameraId))
        {
            throw new InvalidOperationException("Physics3D character route requires an explicit default virtual camera id.");
        }

        VirtualCameraRegistry registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException("Physics3D character route requires VirtualCameraRegistry for release.");
        VirtualCameraDefinition definition = registry.Get(cameraConfig.VirtualCameraId);
        engine.GameSession.Camera.ResetVirtualCameras();
        engine.GameSession.Camera.ActivateVirtualCamera(
            cameraConfig.VirtualCameraId,
            blendDurationSeconds: 0f,
            followTarget: CameraFollowTargetFactory.Build(
                engine.World,
                engine.GlobalContext,
                definition.FollowTargetKind,
                Entity.Null,
                definition.FollowCollectionKey),
            snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable,
            resetRuntimeState: true);
        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = cameraConfig.VirtualCameraId,
            TargetCm = cameraConfig.TargetXCm.HasValue || cameraConfig.TargetYCm.HasValue
                ? new Vector2(cameraConfig.TargetXCm ?? 0f, cameraConfig.TargetYCm ?? 0f)
                : null,
            Yaw = cameraConfig.Yaw,
            Pitch = cameraConfig.Pitch,
            DistanceCm = cameraConfig.DistanceCm,
            FovYDeg = cameraConfig.FovYDeg
        });
        engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
        _characterCameraActive = false;
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
