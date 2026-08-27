using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
public sealed class CalendarRuntimeTests
{
    [Test]
    public void Advance_ConsumesStepsIntoDaysAndProjectsActiveCalendar()
    {
        CalendarRuntime runtime = CreateRuntime();
        runtime.Advance(consumedSteps: 20);

        CalendarDateSnapshot date = runtime.ProjectActive();
        Assert.That(runtime.DayIndex, Is.EqualTo(1));
        Assert.That(runtime.TicksIntoDay, Is.EqualTo(0));
        Assert.That(date.Year, Is.EqualTo(1));
        Assert.That(date.DayOfYear, Is.EqualTo(2));
        Assert.That(FindCycle(date, "season").PhaseId, Is.EqualTo("spring"));
    }

    [Test]
    public void Advance_ZeroStepsDoesNotMoveTheDay()
    {
        CalendarRuntime runtime = CreateRuntime();
        runtime.Advance(0);
        Assert.That(runtime.DayIndex, Is.EqualTo(0));
        Assert.That(runtime.TicksIntoDay, Is.EqualTo(0));
    }

    [Test]
    public void Advance_FiresDayAndCycleEventsWhenSeasonChanges()
    {
        CalendarRuntime runtime = CreateRuntime(startDayIndex: 89);
        var events = new List<string>();
        runtime.Advance(20, () => new ScriptContext(), (key, ctx) =>
        {
            string phaseId = ctx.Get<string>(MapTriggerEventPayloadKeys.CalendarPhaseId) ?? string.Empty;
            int dayIndex = ctx.Get<int>(MapTriggerEventPayloadKeys.CalendarDayIndex);
            events.Add(string.IsNullOrEmpty(phaseId) ? $"{key.Value}:{dayIndex}" : $"{key.Value}:{phaseId}");
        });

        Assert.That(events, Does.Contain("Calendar.CyclePhaseExited:spring"));
        Assert.That(events, Does.Contain("Calendar.CyclePhaseEntered:summer"));
        Assert.That(events, Does.Contain("Calendar.CyclePhaseExited:guyu"));
        Assert.That(events, Does.Contain("Calendar.CyclePhaseEntered:lixia"));
        Assert.That(events, Does.Contain("Calendar.DayAdvanced:90"));
    }

    [Test]
    public void Advance_FiresEraChangedWhenCrossingEraBoundary()
    {
        var registry = CalendarFixtures.Registry(CalendarFixtures.Solar360(), CalendarFixtures.Regnal());
        var runtime = new CalendarRuntime(CalendarFixtures.Clock("calendar.regnal", startDayIndex: 3599), registry);
        var eras = new List<string>();
        runtime.Advance(20, () => new ScriptContext(), (key, ctx) =>
        {
            if (key.Value == GameEvents.CalendarEraChanged.Value)
            {
                eras.Add(ctx.Get<string>(MapTriggerEventPayloadKeys.CalendarEraId)!);
            }
        });

        Assert.That(eras, Is.EqualTo(new[] { "era.expansion" }));
        Assert.That(runtime.Project("calendar.regnal").EraId, Is.EqualTo("era.expansion"));
    }

    [Test]
    public void Advance_FiresDayPhaseChangedWithoutAdvancingTheDay()
    {
        CalendarRuntime runtime = CreateRuntime();
        var phases = new List<string>();
        runtime.Advance(5, () => new ScriptContext(), (key, ctx) =>
        {
            if (key.Value == GameEvents.CalendarDayPhaseChanged.Value)
            {
                phases.Add(ctx.Get<string>(MapTriggerEventPayloadKeys.CalendarPhaseId)!);
            }
        });

        Assert.That(runtime.DayIndex, Is.EqualTo(0));
        Assert.That(runtime.TicksIntoDay, Is.EqualTo(5));
        Assert.That(phases, Is.EqualTo(new[] { "day" }));
        Assert.That(runtime.CaptureClockSnapshot().DayPhaseId, Is.EqualTo("day"));
        Assert.That(runtime.CaptureClockSnapshot().DayPermille, Is.EqualTo(250));
    }

    [Test]
    public void Restore_ReplaysDayIndexWithoutFiringEvents()
    {
        CalendarRuntime source = CreateRuntime();
        source.Advance(40);
        CalendarRuntime target = CreateRuntime();
        var fired = 0;
        target.RestoreSnapshot(source.CaptureSnapshot());
        target.Advance(0, () => new ScriptContext(), (_, _) => fired++);

        Assert.That(target.DayIndex, Is.EqualTo(2));
        Assert.That(fired, Is.EqualTo(0));
    }

    [Test]
    public void DisabledRuntime_RejectsProjection()
    {
        var runtime = new CalendarRuntime(clock: null, CalendarFixtures.Registry());
        Assert.That(runtime.IsEnabled, Is.False);
        Assert.Throws<InvalidOperationException>(() => runtime.ProjectActive());
    }

    [Test]
    public void Restore_RejectsEnabledMismatch()
    {
        var disabled = new CalendarRuntime(clock: null, CalendarFixtures.Registry());
        CalendarRuntime enabled = CreateRuntime();
        Assert.Throws<InvalidOperationException>(() => disabled.RestoreSnapshot(enabled.CaptureSnapshot()));
    }

    private static CalendarRuntime CreateRuntime(int startDayIndex = 0)
    {
        return new CalendarRuntime(
            CalendarFixtures.Clock(startDayIndex: startDayIndex),
            CalendarFixtures.Registry(CalendarFixtures.Solar360()));
    }

    private static CalendarCycleSnapshot FindCycle(CalendarDateSnapshot date, string cycleId)
    {
        for (int i = 0; i < date.Cycles.Count; i++)
        {
            if (date.Cycles[i].CycleId == cycleId)
            {
                return date.Cycles[i];
            }
        }

        throw new AssertionException($"Cycle '{cycleId}' was not projected.");
    }
}
