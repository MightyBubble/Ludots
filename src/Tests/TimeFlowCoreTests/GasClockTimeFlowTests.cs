using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.TimeFlowCore;

[TestFixture]
public sealed class GasClockTimeFlowTests
{
    [Test]
    public void AutoScale_CanConsumeMultipleStepsPerFixedTick()
    {
        var clock = new DiscreteClock();
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 2);
        var system = new GasClockSystem(clock, policy);
        var clocks = new GasClocks(clock);

        policy.SetScalePermille(2000);
        for (int i = 0; i < 3; i++)
        {
            system.Update(0.016f);
        }

        Assert.Multiple(() =>
        {
            Assert.That(clocks.FixedFrameNow, Is.EqualTo(3));
            Assert.That(clocks.StepNow, Is.EqualTo(3));
            Assert.That(policy.ScalePermille, Is.EqualTo(2000));
        });
    }

    [Test]
    public void AutoScale_BelowRealtime_AccumulatesAcrossFixedTicks()
    {
        var clock = new DiscreteClock();
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 2);
        var system = new GasClockSystem(clock, policy);
        var clocks = new GasClocks(clock);

        policy.SetScalePermille(500);
        for (int i = 0; i < 8; i++)
        {
            system.Update(0.016f);
        }

        Assert.That(clocks.StepNow, Is.EqualTo(2));
    }

    [Test]
    public void ManualMode_IgnoresAutoScaleUntilStepsAreRequested()
    {
        var clock = new DiscreteClock();
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, mode: GasStepMode.Manual);
        var system = new GasClockSystem(clock, policy);
        var clocks = new GasClocks(clock);

        policy.SetScalePermille(4000);
        for (int i = 0; i < 4; i++)
        {
            system.Update(0.016f);
        }

        Assert.That(clocks.StepNow, Is.EqualTo(0));

        policy.RequestStep(2);
        system.Update(0.016f);
        system.Update(0.016f);

        Assert.That(clocks.StepNow, Is.EqualTo(2));
    }
}
