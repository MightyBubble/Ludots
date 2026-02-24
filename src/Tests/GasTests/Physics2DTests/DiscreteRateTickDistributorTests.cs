using Ludots.Core.Engine.Physics2D;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Physics2D
{
    public sealed class DiscreteRateTickDistributorTests
    {
        [Test]
        public void NextStepCount_AccumulatesToExpectedTotal_WhenTargetHzBelowFixedHz()
        {
            var dist = new DiscreteRateTickDistributor(fixedHz: 60, targetHz: 10, maxStepsPerFixedTick: 8);

            int total = 0;
            for (int i = 0; i < 60; i++)
            {
                total += dist.NextStepCount();
            }

            That(total, Is.EqualTo(10));
        }

        [Test]
        public void NextStepCount_AccumulatesToExpectedTotal_WhenTargetHzAboveFixedHz()
        {
            var dist = new DiscreteRateTickDistributor(fixedHz: 60, targetHz: 100, maxStepsPerFixedTick: 8);

            int total = 0;
            int maxPerTick = 0;
            for (int i = 0; i < 60; i++)
            {
                int n = dist.NextStepCount();
                total += n;
                if (n > maxPerTick) maxPerTick = n;
            }

            That(total, Is.EqualTo(100));
            That(maxPerTick, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Reset_Throws_WhenMaxStepsPerFixedTickTooSmall()
        {
            Throws<System.InvalidOperationException>(() =>
                new DiscreteRateTickDistributor(fixedHz: 60, targetHz: 100, maxStepsPerFixedTick: 1));
        }
    }
}
