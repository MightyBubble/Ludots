using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class GasClockStepPolicyTests
    {
        [Test]
        public void GasClockSystem_AdvancesStepAccordingToPolicy()
        {
            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(6);
            var system = new GasClockSystem(clock, policy);
            var view = new GasClocks(clock);

            for (int i = 0; i < 60; i++)
            {
                system.Update(0.016f);
            }

            That(view.FixedFrameNow, Is.EqualTo(60));
            That(view.StepNow, Is.EqualTo(10));
        }
    }
}
