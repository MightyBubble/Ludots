using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using NUnit.Framework;

namespace Ludots.PresentationTests.Movement;

[TestFixture]
public sealed class MovementIntentAndExecutionLaneContractTests
{
    [TestCase(MovementExecutionKind.Nav, PhysicsPresenceKind.None, PoseAuthorityKind.Nav)]
    [TestCase(MovementExecutionKind.Nav, PhysicsPresenceKind.Kinematic, PoseAuthorityKind.Nav)]
    [TestCase(MovementExecutionKind.Motor, PhysicsPresenceKind.None, PoseAuthorityKind.Motor)]
    [TestCase(MovementExecutionKind.Motor, PhysicsPresenceKind.Kinematic, PoseAuthorityKind.Motor)]
    [TestCase(MovementExecutionKind.Physics, PhysicsPresenceKind.Dynamic, PoseAuthorityKind.Physics)]
    public void DeriveInitialPoseAuthority_ValidPairs(
        MovementExecutionKind execution,
        PhysicsPresenceKind presence,
        PoseAuthorityKind expected)
    {
        Assert.That(
            MovementParticipationRules.DeriveInitialPoseAuthority(execution, presence),
            Is.EqualTo(expected));
    }

    [TestCase(MovementExecutionKind.Nav, PhysicsPresenceKind.Dynamic)]
    [TestCase(MovementExecutionKind.Motor, PhysicsPresenceKind.Dynamic)]
    [TestCase(MovementExecutionKind.Physics, PhysicsPresenceKind.None)]
    [TestCase(MovementExecutionKind.Physics, PhysicsPresenceKind.Kinematic)]
    public void DeriveInitialPoseAuthority_InvalidPairs_Throw(
        MovementExecutionKind execution,
        PhysicsPresenceKind presence)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovementParticipationRules.DeriveInitialPoseAuthority(execution, presence));
    }

    [Test]
    public void MoveIntent_NoneRequiresZeroSpeed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovementIntentRules.Validate(new MoveIntent
            {
                Mode = MoveIntentMode.None,
                DesiredSpeedCmPerSec = 1f,
            }));
    }

    [Test]
    public void MoveIntent_DirectionRequiresPositiveSpeed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovementIntentRules.Validate(new MoveIntent
            {
                Mode = MoveIntentMode.Direction,
                DirectionRad = 0f,
                DesiredSpeedCmPerSec = 0f,
            }));

        Assert.DoesNotThrow(() =>
            MovementIntentRules.Validate(new MoveIntent
            {
                Mode = MoveIntentMode.Direction,
                DirectionRad = 1.5f,
                DesiredSpeedCmPerSec = 120f,
            }));
    }

    [Test]
    public void MoveIntent_TargetPointRequiresFinitePointAndSpeed()
    {
        Assert.DoesNotThrow(() =>
            MovementIntentRules.Validate(new MoveIntent
            {
                Mode = MoveIntentMode.TargetPoint,
                TargetWorldCm = Fix64Vec2.FromFloat(100f, 200f),
                DesiredSpeedCmPerSec = 80f,
            }));
    }

    [Test]
    public void FacingIntent_ExplicitYawRequiresFinite()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovementIntentRules.Validate(new FacingIntent
            {
                Mode = FacingIntentMode.ExplicitYaw,
                YawRad = float.NaN,
            }));
    }

    [Test]
    public void PoseAuthorityArbiter_DisplacementHandbackRestoresMotor()
    {
        using var world = Arch.Core.World.Create();
        var entity = world.Create(
            new MovementParticipation
            {
                Execution = MovementExecutionKind.Motor,
                PhysicsPresence = PhysicsPresenceKind.None,
                DisplacementAllowed = true,
                DisplacementHandbackSpeedThresholdCmPerSec = 1f,
                DisplacementMaxDurationMs = 5_000,
            },
            new PoseAuthority { Value = PoseAuthorityKind.Motor });

        var arbiter = new PoseAuthorityArbiter();
        using var commit = new PoseAuthorityCommitSystem(world, arbiter);
        float dt = 1f / 30f;

        arbiter.RequestDisplacementAuthority(world, entity);
        commit.Update(in dt);
        Assert.That(world.Get<PoseAuthority>(entity).Value, Is.EqualTo(PoseAuthorityKind.Displacement));

        arbiter.RequestDisplacementHandback(world, entity);
        commit.Update(in dt);
        Assert.That(world.Get<PoseAuthority>(entity).Value, Is.EqualTo(PoseAuthorityKind.Motor));
    }
}
