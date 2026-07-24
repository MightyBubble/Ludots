using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private Physics3DConstraintId _forgeLinearServo;
    private Physics3DConstraintId _forgeAxisMotor;
    private Physics3DConstraintId _forgeAngularServo;

    private void BuildConstraintForgeScene()
    {
        Physics3DConstraintForgeShowcaseConfig config = ActiveConfig.ConstraintForge;
        config.Validate(nameof(ActiveConfig.ConstraintForge));

        BuildJointsScene();
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
                config.LinearTargetCenterCm,
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
        AddForgePivot(hingeAnchor, door, config.PairSeparationYCm, spring);
        AddOwnedConstraint(RequirePhysicsWorld().CreateAngularHingeConstraint(
            hingeAnchor,
            door,
            new Physics3DAngularHingeDescription(Vector3.UnitY, Vector3.UnitY, spring)));
        _forgeAxisMotor = RequirePhysicsWorld().CreateAngularAxisMotorConstraint(
            hingeAnchor,
            door,
            new Physics3DAngularAxisMotorDescription(Vector3.UnitY, config.AxisMotorSpeedRadiansPerSecond, motor));
        AddOwnedConstraint(_forgeAxisMotor);

        CreateConstraintForgePair(2, subgroup, out Physics3DBodyId gimbalAnchor, out Physics3DBodyId gimbal);
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

        float phase = _sceneStep * config.TargetCycleRadiansPerTick;
        float wave = MathF.Sin(phase);
        IPhysics3DWorld physics = RequirePhysicsWorld();
        physics.UpdateLinearAxisServoTarget(
            _forgeLinearServo,
            config.LinearTargetCenterCm + (wave * config.LinearTargetAmplitudeCm));
        physics.UpdateAngularAxisMotorTarget(
            _forgeAxisMotor,
            wave >= 0f ? config.AxisMotorSpeedRadiansPerSecond : -config.AxisMotorSpeedRadiansPerSecond);
        physics.UpdateAngularServoTarget(
            _forgeAngularServo,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, wave * config.AngularServoAmplitudeRadians));
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
