using System.Numerics;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerParamBlackboardTests
    {
        [Test]
        public void PerformerParamBlackboard_SettersStoreValuesAcrossAllLanes()
        {
            var blackboard = new PerformerParamBlackboard(
                handleCapacity: 4,
                floatCapacityPerHandle: 4,
                intCapacityPerHandle: 4,
                vectorCapacityPerHandle: 4);

            blackboard.SetFloat(0, 10, 1.25f);
            blackboard.SetInt(0, 20, 7);
            blackboard.SetVector(0, 30, new Vector4(1f, 2f, 3f, 4f));

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetFloat(0, 10, out float floatValue), Is.True);
                Assert.That(floatValue, Is.EqualTo(1.25f));

                Assert.That(blackboard.TryGetInt(0, 20, out int intValue), Is.True);
                Assert.That(intValue, Is.EqualTo(7));

                Assert.That(blackboard.TryGetVector(0, 30, out Vector4 vectorValue), Is.True);
                Assert.That(vectorValue, Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            });
        }

        [Test]
        public void PerformerParamBlackboard_ResolveFloatWalksParentChainAndPrefersNearestOverride()
        {
            var blackboard = new PerformerParamBlackboard(handleCapacity: 4);
            blackboard.SetFloat(0, 100, 1.5f);
            blackboard.SetFloat(1, 100, 2.5f);
            blackboard.SetParent(1, 0);
            blackboard.SetParent(2, 1);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.ResolveFloat(2, 100, defaultValue: -1f), Is.EqualTo(2.5f));
                Assert.That(blackboard.ResolveFloat(2, 999, defaultValue: 9f), Is.EqualTo(9f));
            });
        }

        [Test]
        public void PerformerParamBlackboard_ClearAllResetsHandleStateWithoutTouchingOtherHandles()
        {
            var blackboard = new PerformerParamBlackboard(handleCapacity: 4);
            blackboard.SetFloat(0, 1, 3.5f);
            blackboard.SetInt(0, 2, 4);
            blackboard.SetVector(0, 3, new Vector4(5f, 6f, 7f, 8f));
            blackboard.SetFloat(1, 1, 9.5f);
            blackboard.SetParent(0, 1);

            blackboard.ClearAll(0);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetFloat(0, 1, out _), Is.False);
                Assert.That(blackboard.TryGetInt(0, 2, out _), Is.False);
                Assert.That(blackboard.TryGetVector(0, 3, out _), Is.False);
                Assert.That(blackboard.GetParent(0), Is.EqualTo(-1));
                Assert.That(blackboard.TryGetFloat(1, 1, out float siblingValue), Is.True);
                Assert.That(siblingValue, Is.EqualTo(9.5f));
            });
        }

        [Test]
        public void PerformerParamBlackboard_ResolvePrefersCurrentValueButFallsBackToHandleDefaultBeforeParent()
        {
            var blackboard = new PerformerParamBlackboard(handleCapacity: 4);
            blackboard.SetIntDefault(0, 100, 1);
            blackboard.SetParent(1, 0);
            blackboard.SetIntDefault(1, 100, 2);

            Assert.That(blackboard.ResolveInt(1, 100, -1), Is.EqualTo(2), "Child default should shadow parent default.");

            blackboard.SetInt(1, 100, 3);
            Assert.That(blackboard.ResolveInt(1, 100, -1), Is.EqualTo(3), "Current value should shadow child default.");

            blackboard.ClearInt(1, 100);
            Assert.That(blackboard.ResolveInt(1, 100, -1), Is.EqualTo(2), "Clearing current value should restore child default.");
        }

        [Test]
        public void PerformerParamBlackboard_ResolvePrefersParentCurrentBeforeChildDefault()
        {
            var blackboard = new PerformerParamBlackboard(handleCapacity: 4);
            blackboard.SetInt(0, 100, 7);
            blackboard.SetParent(1, 0);
            blackboard.SetIntDefault(1, 100, 2);

            Assert.That(
                blackboard.ResolveInt(1, 100, -1),
                Is.EqualTo(7),
                "Parent current/binding value should override child default per override > binding > default.");
        }
    }
}
