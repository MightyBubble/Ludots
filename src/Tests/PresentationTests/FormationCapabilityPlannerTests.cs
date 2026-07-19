using System;
using System.Numerics;
using FormationCapabilityShowcaseMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class FormationCapabilityPlannerTests
    {
        [Test]
        public void Planner_SharedPosePreservesMemberRelativeLayout()
        {
            var pose = new FormationPose(new Vector2(10_000f, -2_000f), MathF.PI * 0.5f);
            var left = new FormationMember(0, 0, new Vector2(-120f, -60f));
            var right = new FormationMember(0, 1, new Vector2(120f, -60f));
            var rear = new FormationMember(0, 2, new Vector2(0f, -260f));

            FormationTargetPlan leftTarget = FormationTargetPlanner.PlanMemberTarget(in pose, in left);
            FormationTargetPlan rightTarget = FormationTargetPlanner.PlanMemberTarget(in pose, in right);
            FormationTargetPlan rearTarget = FormationTargetPlanner.PlanMemberTarget(in pose, in rear);

            Vector2 expectedLeftToRight = FormationTargetPlanner.RotateLocalOffset(
                right.LocalOffsetCm - left.LocalOffsetCm,
                pose.FacingRadians);
            Vector2 expectedLeftToRear = FormationTargetPlanner.RotateLocalOffset(
                rear.LocalOffsetCm - left.LocalOffsetCm,
                pose.FacingRadians);

            AssertVectorNearlyEqual(expectedLeftToRight, rightTarget.TargetWorldCm - leftTarget.TargetWorldCm);
            AssertVectorNearlyEqual(expectedLeftToRear, rearTarget.TargetWorldCm - leftTarget.TargetWorldCm);
            Assert.That(leftTarget.TargetWorldCm, Is.Not.EqualTo(rightTarget.TargetWorldCm));
            Assert.That(leftTarget.TargetWorldCm, Is.Not.EqualTo(rearTarget.TargetWorldCm));
        }

        private static void AssertVectorNearlyEqual(Vector2 expected, Vector2 actual)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.001f));
        }
    }
}
