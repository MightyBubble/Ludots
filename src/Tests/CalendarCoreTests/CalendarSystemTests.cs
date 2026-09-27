using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
public sealed class CalendarSystemTests
{
    [Test]
    public void CalendarSystem_AdvancesOnlyWhenGasStepIsConsumed()
    {
        var clock = new DiscreteClock();
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, GasStepMode.Auto);
        var gasClock = new GasClockSystem(clock, policy);
        CalendarRuntime runtime = new(
            CalendarFixtures.World(ticksPerDay: 1),
            CalendarFixtures.Registry(CalendarFixtures.Solar360()));
        var calendar = new CalendarSystem(runtime, policy);

        gasClock.Update(0f);
        calendar.Update(0f);
        Assert.That(runtime.DayIndex, Is.EqualTo(1));

        policy.SetMode(GasStepMode.Paused);
        gasClock.Update(0f);
        calendar.Update(0f);
        Assert.That(runtime.DayIndex, Is.EqualTo(1));
    }

    [Test]
    public void CalendarSystem_DoesNotAdvanceWhenNoStepsWereConsumed()
    {
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, GasStepMode.Manual);
        CalendarRuntime runtime = new(
            CalendarFixtures.World(ticksPerDay: 1),
            CalendarFixtures.Registry(CalendarFixtures.Solar360()));
        var calendar = new CalendarSystem(runtime, policy);

        calendar.Update(0f);
        Assert.That(runtime.DayIndex, Is.EqualTo(0));
    }

    [Test]
    public void CalendarSystem_RejectsDisabledRuntime()
    {
        var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, GasStepMode.Auto);
        var runtime = new CalendarRuntime(world: null, CalendarFixtures.Registry());
        Assert.Throws<InvalidOperationException>(() => new CalendarSystem(runtime, policy));
    }
}
