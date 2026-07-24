using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private Physics3DConstraintId _forgeLinearServo;
    private Physics3DConstraintId _forgeAxisMotor;
    private Physics3DConstraintId _forgeAngularServo;
    private Physics3DBodyId _forgeLinearAnchor;
    private Physics3DBodyId _forgeLinearCarriage;
    private Physics3DBodyId _forgeDoor;
    private Physics3DBodyId _forgeGimbal;
    private bool _forgeDriveEnabled;
    private Physics3DShowcaseDriveDirection _forgeDriveDirection = Physics3DShowcaseDriveDirection.Forward;
    private float _forgeDrivePhase;
    private float _forgeLinearTargetCm;
    private float _forgeMotorTargetSpeed;
    private float _forgeAngularTargetRadians;

    private void BuildConstraintForgeScene()
    {
        Physics3DConstraintForgeShowcaseConfig config = ActiveConfig.ConstraintForge;
        config.Validate(nameof(ActiveConfig.ConstraintForge));
        _forgeDriveEnabled = config.InitialDriveEnabled;
        _forgeDriveDirection = config.InitialDriveDirection;
        _forgeDrivePhase = 0f;
        _forgeLinearTargetCm = config.LinearTargetCenterCm;
        _forgeMotorTargetSpeed = _forgeDriveEnabled
            ? config.AxisMotorSpeedRadiansPerSecond * (float)_forgeDriveDirection
            : 0f;
        _forgeAngularTargetRadians = 0f;

        BuildConstraintForgeLegacyExhibits();
        Physics3DSpringSettings spring = CreateSpring();
        var servo = new Physics3DServoSettings(
            config.ServoMaximumSpeed,
            baseSpeed: 0f,
            config.ServoMaximumForce);
        var motor = new Physics3DMotorSettings(config.MotorMaximumForce, config.MotorSoftness);
        Physics3DCollisionSubgroup subgroup = new(
            config.CollisionAssemblyId,
            subgroupIndex: 0,
            collidesWithSubgroups: 0u);

        CreateConstraintForgePair(0, subgroup, out Physics3DBodyId linearAnchor, out Physics3DBodyId linearCarriage);
        _forgeLinearAnchor = linearAnchor;
        _forgeLinearCarriage = linearCarriage;
        AddOwnedConstraint(RequirePhysicsWorld().CreatePointOnLineServoConstraint(
            linearAnchor,
            linearCarriage,
            new Physics3DPointOnLineServoDescription(
                Vector3.Zero,
                Vector3.Zero,
                Vector3.UnitY,
                servo,
                spring)));
        _forgeLinearServo = RequirePhysicsWorld().CreateLinearAxisServoConstraint(
            linearAnchor,
            linearCarriage,
            new Physics3DLinearAxisServoDescription(
                Vector3.Zero,
                Vector3.Zero,
                Vector3.UnitY,
                _forgeLinearTargetCm,
                servo,
                spring));
        AddOwnedConstraint(_forgeLinearServo);
        AddOwnedConstraint(RequirePhysicsWorld().CreateLinearAxisLimitConstraint(
            linearAnchor,
            linearCarriage,
            new Physics3DLinearAxisLimitDescription(
                Vector3.Zero,
                Vector3.Zero,
                Vector3.UnitY,
                config.LinearMinimumCm,
                config.LinearMaximumCm,
                spring)));

        CreateConstraintForgePair(1, subgroup, out Physics3DBodyId hingeAnchor, out Physics3DBodyId door);
        _forgeDoor = door;
        AddForgePivot(hingeAnchor, door, config.PairSeparationYCm, spring);
        AddOwnedConstraint(RequirePhysicsWorld().CreateAngularHingeConstraint(
            hingeAnchor,
            door,
            new Physics3DAngularHingeDescription(Vector3.UnitY, Vector3.UnitY, spring)));
        _forgeAxisMotor = RequirePhysicsWorld().CreateAngularAxisMotorConstraint(
            hingeAnchor,
            door,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitY, _forgeMotorTargetSpeed, motor));
        AddOwnedConstraint(_forgeAxisMotor);

        CreateConstraintForgePair(2, subgroup, out Physics3DBodyId gimbalAnchor, out Physics3DBodyId gimbal);
        _forgeGimbal = gimbal;
        AddForgePivot(gimbalAnchor, gimbal, config.PairSeparationYCm, spring);
        AddOwnedConstraint(RequirePhysicsWorld().CreateSwingLimitConstraint(
            gimbalAnchor,
            gimbal,
            new Physics3DSwingLimitDescription(
                Vector3.UnitY,
                Vector3.UnitY,
                config.SwingLimitRadians,
                spring)));
        AddOwnedConstraint(RequirePhysicsWorld().CreateTwistLimitConstraint(
            gimbalAnchor,
            gimbal,
            new Physics3DTwistLimitDescription(
                Quaternion.Identity,
                Quaternion.Identity,
                config.MinimumTwistRadians,
                config.MaximumTwistRadians,
                spring)));
        _forgeAngularServo = RequirePhysicsWorld().CreateAngularServoConstraint(
            gimbalAnchor,
            gimbal,
            new Physics3DAngularServoDescription(Quaternion.Identity, servo, spring));
        AddOwnedConstraint(_forgeAngularServo);

        CreateConstraintForgePair(3, subgroup, out Physics3DBodyId rotorAnchor, out Physics3DBodyId rotor);
        AddForgePivot(rotorAnchor, rotor, config.PairSeparationYCm, spring);
        AddOwnedConstraint(RequirePhysicsWorld().CreateAngularMotorConstraint(
            rotorAnchor,
            rotor,
            new Physics3DAngularMotorDescription(
                new Vector3(0f, config.AxisMotorSpeedRadiansPerSecond, 0f),
                motor)));

        for (int i = 0; i < _constraintCount; i++)
        {
            if (!RequirePhysicsWorld().ContainsConstraint(_constraintIds[i]))
            {
                throw new InvalidOperationException($"Constraint Forge created invalid constraint '{_constraintIds[i]}'.");
            }
        }
    }

    private void PrepareConstraintForgeStep()
    {
        Physics3DConstraintForgeShowcaseConfig config = ActiveConfig.ConstraintForge;
        if (!_forgeLinearServo.IsValid || !_forgeAxisMotor.IsValid || !_forgeAngularServo.IsValid)
        {
            throw new InvalidOperationException("Constraint Forge target handles are unavailable.");
        }

        if (!_forgeDriveEnabled)
        {
            return;
        }

        _forgeDrivePhase += config.TargetCycleRadiansPerTick * (float)_forgeDriveDirection;
        float wave = MathF.Sin(_forgeDrivePhase);
        _forgeLinearTargetCm = config.LinearTargetCenterCm + (wave * config.LinearTargetAmplitudeCm);
        _forgeMotorTargetSpeed = config.AxisMotorSpeedRadiansPerSecond * (float)_forgeDriveDirection;
        _forgeAngularTargetRadians = wave * config.AngularServoAmplitudeRadians;
        IPhysics3DWorld physics = RequirePhysicsWorld();
        physics.UpdateLinearAxisServoTarget(
            _forgeLinearServo,
            _forgeLinearTargetCm);
        physics.UpdateAngularAxisMotorTarget(
            _forgeAxisMotor,
            _forgeMotorTargetSpeed);
        physics.UpdateAngularServoTarget(
            _forgeAngularServo,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _forgeAngularTargetRadians));
    }

    private void ToggleConstraintForgeDrive()
    {
        RequireConstraintForgeCommand(nameof(Physics3DShowcaseCommandKind.ToggleConstraintDrive));
        _forgeDriveEnabled = !_forgeDriveEnabled;
        if (!_forgeDriveEnabled)
        {
            _forgeMotorTargetSpeed = 0f;
            RequirePhysicsWorld().UpdateAngularAxisMotorTarget(_forgeAxisMotor, 0f);
            _lastAction = "Constraint drives paused: the motor target is zero and both servo targets are held.";
            return;
        }

        _forgeMotorTargetSpeed = ActiveConfig.ConstraintForge.AxisMotorSpeedRadiansPerSecond *
            (float)_forgeDriveDirection;
        RequirePhysicsWorld().UpdateAngularAxisMotorTarget(_forgeAxisMotor, _forgeMotorTargetSpeed);
        _lastAction = $"Constraint drives started {DriveDirectionLabel(_forgeDriveDirection)} from their held targets.";
    }

    private void ReverseConstraintForgeDrive()
    {
        RequireConstraintForgeCommand(nameof(Physics3DShowcaseCommandKind.ReverseConstraintDrive));
        _forgeDriveDirection = _forgeDriveDirection == Physics3DShowcaseDriveDirection.Forward
            ? Physics3DShowcaseDriveDirection.Reverse
            : Physics3DShowcaseDriveDirection.Forward;
        if (_forgeDriveEnabled)
        {
            _forgeMotorTargetSpeed = ActiveConfig.ConstraintForge.AxisMotorSpeedRadiansPerSecond *
                (float)_forgeDriveDirection;
            RequirePhysicsWorld().UpdateAngularAxisMotorTarget(_forgeAxisMotor, _forgeMotorTargetSpeed);
        }

        _lastAction = _forgeDriveEnabled
            ? $"Constraint motor and moving servo targets now run {DriveDirectionLabel(_forgeDriveDirection)}."
            : $"Drive direction staged as {DriveDirectionLabel(_forgeDriveDirection)}; drives remain paused.";
    }

    private string CreateConstraintForgeSummary()
    {
        if (_scene != Physics3DShowcaseScene.ConstraintForge)
        {
            return "Visit Constraint Forge to control the door, slider, and servo.";
        }

        IPhysics3DWorld physics = RequirePhysicsWorld();
        if (!physics.ContainsBody(_forgeLinearAnchor) ||
            !physics.ContainsBody(_forgeLinearCarriage) ||
            !physics.ContainsBody(_forgeDoor) ||
            !physics.ContainsBody(_forgeGimbal))
        {
            throw new InvalidOperationException("Constraint Forge lost one of its player-visible controlled bodies.");
        }

        if (!TryGetConstraintForgePlayerState(
                out float sliderOffsetCm,
                out float doorAngularSpeedRadiansPerSecond,
                out float servoAngleRadians))
        {
            throw new InvalidOperationException("Constraint Forge player state is unavailable while the station is active.");
        }
        return $"{(_forgeDriveEnabled ? "RUNNING" : "PAUSED")} {DriveDirectionLabel(_forgeDriveDirection)} | " +
               $"door {doorAngularSpeedRadiansPerSecond:0.00} rad/s | " +
               $"slider {sliderOffsetCm:0} cm (target {_forgeLinearTargetCm:0}) | " +
               $"servo {servoAngleRadians:0.00} rad";
    }

    internal bool TryGetConstraintForgePlayerState(
        out float sliderOffsetCm,
        out float doorAngularSpeedRadiansPerSecond,
        out float servoAngleRadians)
    {
        if (_scene != Physics3DShowcaseScene.ConstraintForge)
        {
            sliderOffsetCm = 0f;
            doorAngularSpeedRadiansPerSecond = 0f;
            servoAngleRadians = 0f;
            return false;
        }

        IPhysics3DWorld physics = RequirePhysicsWorld();
        Physics3DBodyState anchor = physics.GetBodyState(_forgeLinearAnchor);
        Physics3DBodyState carriage = physics.GetBodyState(_forgeLinearCarriage);
        Physics3DBodyState door = physics.GetBodyState(_forgeDoor);
        Physics3DBodyState gimbal = physics.GetBodyState(_forgeGimbal);
        sliderOffsetCm = Vector3.Dot(carriage.PositionCm - anchor.PositionCm, Vector3.UnitY);
        doorAngularSpeedRadiansPerSecond = door.AngularVelocityRadiansPerSecond.Y;
        servoAngleRadians = 2f * MathF.Atan2(gimbal.Orientation.Z, gimbal.Orientation.W);
        return true;
    }

    private void RequireConstraintForgeCommand(string commandName)
    {
        if (_scene != Physics3DShowcaseScene.ConstraintForge)
        {
            throw new InvalidOperationException($"{commandName} requires the active Constraint Forge station.");
        }
        if (!_forgeLinearServo.IsValid || !_forgeAxisMotor.IsValid || !_forgeAngularServo.IsValid)
        {
            throw new InvalidOperationException($"{commandName} cannot run because Constraint Forge handles are unavailable.");
        }
    }

    private void CreateConstraintForgePair(
        int exhibitIndex,
        in Physics3DCollisionSubgroup subgroup,
        out Physics3DBodyId anchor,
        out Physics3DBodyId exhibit)
    {
        Physics3DConstraintForgeShowcaseConfig config = ActiveConfig.ConstraintForge;
        float x = config.FirstExhibitXCm + (exhibitIndex * config.ExhibitSpacingXCm);
        Vector3 anchorPosition = new(x, config.AnchorYCm, 0f);
        Vector3 exhibitPosition = anchorPosition + new Vector3(0f, config.PairSeparationYCm, 0f);
        float size = ActiveConfig.BodySizeCm;
        anchor = AddOwnedBody(
            Physics3DBodyKind.Kinematic,
            _sphereShape,
            Physics3DShapeKind.Sphere,
            new Vector3(size),
            0f,
            anchorPosition,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            KinematicColor,
            collisionSubgroup: subgroup);
        exhibit = AddOwnedBody(
            Physics3DBodyKind.Dynamic,
            _plankShape,
            Physics3DShapeKind.Box,
            PlankVisualSize(ActiveConfig),
            0f,
            exhibitPosition,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DContinuousDetectionMode.Passive,
            exhibitIndex switch
            {
                0 => DynamicBlue,
                1 => DynamicGold,
                2 => DynamicGreen,
                _ => DynamicRed
            },
            mass: 12f,
            collisionSubgroup: subgroup);
    }

    private void AddForgePivot(
        Physics3DBodyId anchor,
        Physics3DBodyId exhibit,
        float separationCm,
        in Physics3DSpringSettings spring)
    {
        AddOwnedConstraint(RequirePhysicsWorld().CreateBallSocketConstraint(
            anchor,
            exhibit,
            Vector3.Zero,
            new Vector3(0f, -separationCm, 0f),
            spring));
    }
}
