using Ludots.Platform.Abstractions;
using System;
using System.Numerics;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.InputRouting
{
    [TestFixture]
    public sealed class MoveTargetLayoutPlannerTests
    {
        [Test]
        public void PreserveRelative_RealFrontlineFormation_AssignsStableForwardThenLateralSlots()
        {
            WorldCmInt2[] actors =
            {
                new(9300, 15400),
                new(9300, 14600),
                new(6951, 15151),
                new(7131, 14925),
            };
            var anchor = new Vector3(14700f, 0f, 15000f);

            int[] slots = ComputeSlots(actors, anchor, spacingCm: 140);

            Assert.That(slots, Is.EqualTo(new[] { 3, 1, 2, 0 }));
            Assert.That(
                ComputeMinimumMovingSeparation(actors, anchor, slots, spacingCm: 140),
                Is.GreaterThanOrEqualTo(139.999),
                "This authored fixture remains separated, but PreserveRelative is not a general collision-avoidance guarantee.");
        }

        [Test]
        public void PreserveRelative_DiagonalMove_AssignsForwardThenLateralSlots()
        {
            WorldCmInt2[] actors =
            {
                new(0, 0),
                new(0, 400),
                new(400, 0),
                new(400, 400),
            };
            var anchor = new Vector3(2000f, 0f, 2000f);

            int[] slots = ComputeSlots(actors, anchor, spacingCm: 200);

            Assert.That(slots, Is.EqualTo(new[] { 0, 2, 1, 3 }));
            Assert.That(
                ComputeMinimumMovingSeparation(actors, anchor, slots, spacingCm: 200),
                Is.GreaterThanOrEqualTo(199.999),
                "This authored fixture remains separated, but PreserveRelative is not a general collision-avoidance guarantee.");
        }

        [Test]
        public void PreserveRelative_IdenticalPositions_UsesStableOriginalOrdinal()
        {
            WorldCmInt2[] actors = { new(0, 0), new(0, 0) };

            int[] slots = ComputeSlots(actors, new Vector3(1000f, 0f, 0f), spacingCm: 140);

            Assert.That(slots, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void GridTargets_TreatConfiguredSpacingAsMinimumWithIntegerMovementMargin()
        {
            Vector3 anchor = new(1000f, 0f, 2000f);

            Vector3 left = MoveTargetLayoutPlanner.ComputeOffsetTarget(anchor, index: 0, totalCount: 2, spacingCm: 140);
            Vector3 right = MoveTargetLayoutPlanner.ComputeOffsetTarget(anchor, index: 1, totalCount: 2, spacingCm: 140);

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(new Vector3(930f, 0f, 2000f)));
                Assert.That(right, Is.EqualTo(new Vector3(1071f, 0f, 2000f)));
                Assert.That(right.X - left.X, Is.GreaterThan(140f),
                    "The authored spacing remains the minimum accepted separation after integer movement quantization.");
            });
        }

        [Test]
        public void PreserveRelative_AnchorAtCentroid_IsExplicitlyRejected()
        {
            WorldCmInt2[] actors = { new(-100, 0), new(100, 0) };
            int count = actors.Length;

            bool success = MoveTargetLayoutPlanner.TryComputePositionPreservingSlots(
                actors,
                Vector3.Zero,
                spacingCm: 140,
                new int[count],
                new int[count],
                new int[count],
                new Int128[count],
                new Int128[count],
                new Int128[count],
                new Int128[count]);

            Assert.That(success, Is.False);
        }

        [Test]
        public void PreserveRelative_AfterWarmup_DoesNotAllocate()
        {
            WorldCmInt2[] actors =
            {
                new(9300, 15400),
                new(9300, 14600),
                new(6951, 15151),
                new(7131, 14925),
            };
            int count = actors.Length;
            int[] slotByActor = new int[count];
            int[] actorIndices = new int[count];
            int[] slotIndices = new int[count];
            Int128[] actorForward = new Int128[count];
            Int128[] actorLateral = new Int128[count];
            Int128[] slotForward = new Int128[count];
            Int128[] slotLateral = new Int128[count];
            var anchor = new Vector3(14700f, 0f, 15000f);

            Assert.That(MoveTargetLayoutPlanner.TryComputePositionPreservingSlots(
                actors,
                anchor,
                140,
                slotByActor,
                actorIndices,
                slotIndices,
                actorForward,
                actorLateral,
                slotForward,
                slotLateral), Is.True);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                MoveTargetLayoutPlanner.TryComputePositionPreservingSlots(
                    actors,
                    anchor,
                    140,
                    slotByActor,
                    actorIndices,
                    slotIndices,
                    actorForward,
                    actorLateral,
                    slotForward,
                    slotLateral);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        private static int[] ComputeSlots(WorldCmInt2[] actors, Vector3 anchor, int spacingCm)
        {
            int count = actors.Length;
            var slots = new int[count];
            bool success = MoveTargetLayoutPlanner.TryComputePositionPreservingSlots(
                actors,
                anchor,
                spacingCm,
                slots,
                new int[count],
                new int[count],
                new Int128[count],
                new Int128[count],
                new Int128[count],
                new Int128[count]);
            Assert.That(success, Is.True);
            return slots;
        }

        private static double ComputeMinimumMovingSeparation(
            WorldCmInt2[] actors,
            Vector3 anchor,
            int[] slots,
            int spacingCm)
        {
            double minimum = double.MaxValue;
            for (int left = 0; left < actors.Length; left++)
            {
                for (int right = left + 1; right < actors.Length; right++)
                {
                    MotionPath leftPath = CreatePath(actors[left], anchor, slots[left], actors.Length, spacingCm);
                    MotionPath rightPath = CreatePath(actors[right], anchor, slots[right], actors.Length, spacingCm);
                    double firstArrival = Math.Min(leftPath.Length, rightPath.Length);
                    double lastArrival = Math.Max(leftPath.Length, rightPath.Length);
                    minimum = Math.Min(minimum, MinimumSeparationInInterval(leftPath, rightPath, 0d, firstArrival));
                    minimum = Math.Min(minimum, MinimumSeparationInInterval(leftPath, rightPath, firstArrival, lastArrival));
                }
            }

            return minimum;
        }

        private static MotionPath CreatePath(
            WorldCmInt2 start,
            Vector3 anchor,
            int slot,
            int count,
            int spacingCm)
        {
            Vector3 target = MoveTargetLayoutPlanner.ComputeOffsetTarget(anchor, slot, count, spacingCm);
            double deltaX = target.X - start.X;
            double deltaY = target.Z - start.Y;
            double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            return new MotionPath(start.X, start.Y, deltaX / length, deltaY / length, length);
        }

        private static double MinimumSeparationInInterval(
            in MotionPath left,
            in MotionPath right,
            double startTime,
            double endTime)
        {
            Vector2 start = PositionAt(in left, startTime) - PositionAt(in right, startTime);
            double duration = endTime - startTime;
            if (duration <= 0d)
            {
                return start.Length();
            }

            Vector2 end = PositionAt(in left, endTime) - PositionAt(in right, endTime);
            Vector2 velocity = (end - start) / (float)duration;
            double speedSquared = velocity.LengthSquared();
            double localTime = speedSquared <= double.Epsilon
                ? 0d
                : -Vector2.Dot(start, velocity) / speedSquared;
            localTime = Math.Clamp(localTime, 0d, duration);
            return (start + (velocity * (float)localTime)).Length();
        }

        private static Vector2 PositionAt(in MotionPath path, double time)
        {
            double distance = Math.Min(time, path.Length);
            return new Vector2(
                (float)(path.StartX + (path.DirectionX * distance)),
                (float)(path.StartY + (path.DirectionY * distance)));
        }

        private readonly record struct MotionPath(
            double StartX,
            double StartY,
            double DirectionX,
            double DirectionY,
            double Length);
    }
}
