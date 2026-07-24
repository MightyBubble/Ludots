using System;
using System.Numerics;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;
using Ludots.Core.Traversal3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Traversal3DTests
{
    public enum LedgeBlocker : byte
    {
        None,
        Hand,
        Landing
    }

    [Test]
    public void SensorLadder_DrivesAttachedThenClimbingStatesAndVerticalMotion()
    {
        using var world = Character3DTests.CreateWorld(mobileCapacity: 3, staticCapacity: 1);
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DShapeId ladderShape = world.RegisterBoxShape(new Vector3(20f, 500f, 220f));
        Physics3DBodyId body = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 120f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId ladder = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Static,
            ladderShape,
            new Vector3(100f, 250f, 0f),
            Physics3DBodyContactPolicy.Sensor()));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, Character3DTests.CreateProfile());
        var traversal = new Traversal3DControllerSet(world, characters, 1, bodySlotCapacity: 8, overlapHitCapacity: 16);
        traversal.RegisterSurface(ladder, Traversal3DSurfaceKind.Ladder);
        Traversal3DHandle controller = traversal.RegisterCharacter(character, CreateProfile());

        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, true, false));
        Assert.That(traversal.GetStatus(controller).State, Is.EqualTo(Traversal3DState.Attached));

        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, false, false));
        Assert.That(traversal.GetStatus(controller).State, Is.EqualTo(Traversal3DState.Climbing));

        float beforeY = characters.GetState(character).PositionCm.Y;
        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.UnitY, Vector3.UnitX, false, false));
        Character3DState climbed = characters.GetState(character);
        Assert.Multiple(() =>
        {
            Assert.That(climbed.LocomotionMode, Is.EqualTo(Character3DLocomotionMode.Traversal));
            Assert.That(climbed.PositionCm.Y, Is.GreaterThan(beforeY));
            Assert.That(climbed.LinearVelocityCmPerSecond.Y, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void MissingTraversalIntent_FailsBeforeCharacterReceivesPartialInput()
    {
        using var world = Character3DTests.CreateWorld(mobileCapacity: 2, staticCapacity: 0);
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DBodyId body = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 500f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        var characters = new Character3DControllerSet(world, 1, 8);
        Character3DHandle character = characters.Register(body, anchor, Character3DTests.CreateProfile());
        var traversal = new Traversal3DControllerSet(world, characters, 1, 8, 8);
        traversal.RegisterCharacter(character, CreateProfile());

        InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(() => traversal.PrepareFixedStep());
        Assert.Multiple(() =>
        {
            Assert.That(missing!.Message, Does.Contain("no intent"));
            Assert.That(world.PendingActuationCommandCount, Is.Zero);
        });
    }

    [TestCase(LedgeBlocker.None, true)]
    [TestCase(LedgeBlocker.Hand, false)]
    [TestCase(LedgeBlocker.Landing, false)]
    public void ClimbableWall_LedgeRequiresHandAndLandingClearance(
        LedgeBlocker blocker,
        bool expectedLedgeHang)
    {
        int staticCapacity = blocker == LedgeBlocker.None ? 1 : 2;
        using var world = Character3DTests.CreateWorld(mobileCapacity: 2, staticCapacity: staticCapacity);
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DShapeId wallShape = world.RegisterBoxShape(new Vector3(300f, 200f, 300f));
        Physics3DBodyId body = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(-100f, 120f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId wall = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Static,
            wallShape,
            new Vector3(100f, 100f, 0f)));
        if (blocker != LedgeBlocker.None)
        {
            Physics3DShapeId blockerShape = world.RegisterBoxShape(new Vector3(10f, 20f, 20f));
            Vector3 blockerPosition = blocker == LedgeBlocker.Hand
                ? new Vector3(25f, 212f, 0f)
                : new Vector3(95f, 282f, 0f);
            world.CreateBody(Character3DTests.CreateBody(
                Physics3DBodyKind.Static,
                blockerShape,
                blockerPosition));
        }

        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, Character3DTests.CreateProfile());
        var traversal = new Traversal3DControllerSet(world, characters, 1, bodySlotCapacity: 8, overlapHitCapacity: 16);
        traversal.RegisterSurface(wall, Traversal3DSurfaceKind.ClimbableWall);
        Traversal3DHandle controller = traversal.RegisterCharacter(character, CreateProfile());

        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, true, false));
        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, false, false));
        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.UnitY, Vector3.UnitX, false, false));

        Traversal3DStatus status = traversal.GetStatus(controller);
        Assert.Multiple(() =>
        {
            Assert.That(status.State == Traversal3DState.LedgeHang, Is.EqualTo(expectedLedgeHang));
            Assert.That(status.ClearanceValid, Is.EqualTo(expectedLedgeHang));
        });
    }

    [Test]
    public void CandidateTopBelowCharacter_DoesNotBecomeLedgeHang()
    {
        using var world = Character3DTests.CreateWorld(mobileCapacity: 2, staticCapacity: 2);
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DShapeId ladderShape = world.RegisterBoxShape(new Vector3(20f, 500f, 220f));
        Physics3DShapeId floorShape = world.RegisterBoxShape(new Vector3(1_000f, 20f, 1_000f));
        world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Static,
            floorShape,
            new Vector3(0f, -10f, 0f)));
        Physics3DBodyId body = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 120f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        Physics3DBodyId ladder = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Static,
            ladderShape,
            new Vector3(100f, 250f, 0f),
            Physics3DBodyContactPolicy.Sensor()));
        var characters = new Character3DControllerSet(world, capacity: 1, overlapHitCapacity: 16);
        Character3DHandle character = characters.Register(body, anchor, Character3DTests.CreateProfile());
        var traversal = new Traversal3DControllerSet(world, characters, 1, bodySlotCapacity: 8, overlapHitCapacity: 16);
        traversal.RegisterSurface(ladder, Traversal3DSurfaceKind.Ladder);
        Traversal3DHandle controller = traversal.RegisterCharacter(
            character,
            CreateProfile(ledgeProbeDownCm: 300f));

        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, true, false));
        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.Zero, Vector3.UnitX, false, false));
        Step(world, characters, traversal, controller, new Traversal3DIntent(Vector2.UnitY, Vector3.UnitX, false, false));

        Assert.That(traversal.GetStatus(controller).State, Is.EqualTo(Traversal3DState.Climbing));
    }

    [Test]
    public void WarmedNormalTraversalLane_HasZeroManagedAllocations()
    {
        using var world = Character3DTests.CreateWorld(mobileCapacity: 2, staticCapacity: 0);
        Physics3DShapeId capsuleShape = world.RegisterCapsuleShape(30f, 100f);
        Physics3DShapeId anchorShape = world.RegisterSphereShape(1f);
        Physics3DBodyId body = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Dynamic,
            capsuleShape,
            new Vector3(0f, 5_000f, 0f)));
        Physics3DBodyId anchor = world.CreateBody(Character3DTests.CreateBody(
            Physics3DBodyKind.Kinematic,
            anchorShape,
            new Vector3(0f, -10_000f, 0f)));
        var characters = new Character3DControllerSet(world, 1, 8);
        Character3DHandle character = characters.Register(body, anchor, Character3DTests.CreateProfile());
        var traversal = new Traversal3DControllerSet(world, characters, 1, 8, 8);
        Traversal3DHandle controller = traversal.RegisterCharacter(character, CreateProfile());
        var intent = new Traversal3DIntent(new Vector2(0.1f, 0f), Vector3.UnitX, false, false);

        for (int i = 0; i < 60; i++)
        {
            Step(world, characters, traversal, controller, intent);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            Step(world, characters, traversal, controller, intent);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed Traversal3D normal lane allocated {allocated} managed bytes.");
    }

    private static Traversal3DProfile CreateProfile(float ledgeProbeDownCm = 180f)
        => new(
            attachProbeDistanceCm: 150f,
            attachSpeedCmPerSecond: 500f,
            climbSpeedCmPerSecond: 320f,
            lateralSpeedCmPerSecond: 220f,
            maximumAccelerationCmPerSecondSquared: 12_000f,
            ledgeProbeHeightCm: 140f,
            ledgeProbeForwardCm: 110f,
            ledgeProbeDownCm: ledgeProbeDownCm,
            minimumLedgeHeightCm: 40f,
            handClearanceRadiusCm: 12f,
            mantleForwardCm: 50f,
            mantleSpeedCmPerSecond: 500f,
            mantleCompletionDistanceCm: 10f,
            minimumTopNormalY: 0.7f,
            detachUpSpeedCmPerSecond: 260f,
            detachOutSpeedCmPerSecond: 220f);

    private static void Step(
        Physics3DWorld world,
        Character3DControllerSet characters,
        Traversal3DControllerSet traversal,
        Traversal3DHandle controller,
        in Traversal3DIntent intent)
    {
        traversal.SubmitIntent(controller, intent);
        traversal.PrepareFixedStep();
        characters.PrepareFixedStep();
        world.Step();
        characters.ObserveFixedStep();
    }
}
