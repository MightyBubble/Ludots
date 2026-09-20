using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Map;
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
        var runtime = new CalendarRuntime(CalendarFixtures.World("calendar.regnal", startDayIndex: 3599), registry);
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
        Assert.That(runtime.CaptureProgressSnapshot().DayPhaseId, Is.EqualTo("day"));
        Assert.That(runtime.CaptureProgressSnapshot().DayPermille, Is.EqualTo(250));
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
        var runtime = new CalendarRuntime(world: null, CalendarFixtures.Registry());
        Assert.That(runtime.IsEnabled, Is.False);
        Assert.Throws<InvalidOperationException>(() => runtime.ProjectActive());
    }

    [Test]
    public void Restore_RejectsEnabledMismatch()
    {
        var disabled = new CalendarRuntime(world: null, CalendarFixtures.Registry());
        CalendarRuntime enabled = CreateRuntime();
        Assert.Throws<InvalidOperationException>(() => disabled.RestoreSnapshot(enabled.CaptureSnapshot()));
    }

    [Test]
    public void Advance_RejectsNegativeSteps()
    {
        CalendarRuntime runtime = CreateRuntime();
        Assert.Throws<InvalidOperationException>(() => runtime.Advance(-1));
        Assert.That(runtime.DayIndex, Is.EqualTo(0));
    }

    [Test]
    public void Advance_NoSubscribers_AdvancesDayWithoutFiringOrDiffing()
    {
        CalendarRuntime runtime = CreateRuntime(startDayIndex: 89);
        var fired = new List<string>();
        runtime.Advance(20, () => new ScriptContext(), (key, ctx) => fired.Add(key.Value), _ => false);

        Assert.That(runtime.DayIndex, Is.EqualTo(90), "day must advance regardless of subscribers");
        Assert.That(fired, Is.Empty, "zero subscribers must fire nothing");
        Assert.That(
            FindCycle(runtime.ProjectActive(), "season").PhaseId,
            Is.EqualTo("summer"),
            "projection reads stay correct after skipped diffs");
    }

    [Test]
    public void Advance_OnlySubscribedKeysFire()
    {
        CalendarRuntime runtime = CreateRuntime(startDayIndex: 89);
        var fired = new List<string>();
        runtime.Advance(20, () => new ScriptContext(), (key, ctx) => fired.Add(key.Value),
            key => key.Value == GameEvents.CalendarDayAdvanced.Value);

        Assert.That(fired, Is.EqualTo(new[] { GameEvents.CalendarDayAdvanced.Value }));
        Assert.That(fired.All(k => k != GameEvents.CalendarCyclePhaseEntered.Value), Is.True);
    }

    [Test]
    public void Advance_LateSubscriber_DoesNotReplayWindowTransitions()
    {
        CalendarRuntime runtime = CreateRuntime(startDayIndex: 89);
        runtime.Advance(20, () => new ScriptContext(), (key, ctx) => { }, _ => false);

        var fired = new List<string>();
        runtime.Advance(
            20,
            () => new ScriptContext(),
            (key, ctx) =>
            {
                string phaseId = ctx.Get<string>(MapTriggerEventPayloadKeys.CalendarPhaseId) ?? string.Empty;
                int dayIndex = ctx.Get<int>(MapTriggerEventPayloadKeys.CalendarDayIndex);
                fired.Add(string.IsNullOrEmpty(phaseId) ? $"{key.Value}:{dayIndex}" : $"{key.Value}:{phaseId}");
            },
            _ => true);

        Assert.That(fired, Does.Contain("Calendar.DayAdvanced:91"));
        Assert.That(
            fired.All(k => !k.Contains("summer") && !k.Contains("spring")),
            Is.True,
            "the spring→summer switch crossed while unsubscribed must not replay");
    }

    [Test]
    public void Advance_GlobalDispatch_ReachesMapGlobalSubscriptions()
    {
        var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
        var seen = new List<string>();
        var probe = new ProbeTrigger(GameEvents.CalendarCyclePhaseEntered, seen);
        manager.RegisterGlobalTriggers(new MapId("calendar_probe_map"), new Trigger[] { probe });

        CalendarRuntime runtime = new(
            CalendarFixtures.World(startDayIndex: 89),
            CalendarFixtures.Registry(CalendarFixtures.Solar360()));
        runtime.Advance(
            20,
            () => new ScriptContext(),
            manager.FireGlobalEvent,
            manager.HasGlobalEventSubscribers);

        Assert.That(seen, Is.Not.Empty, "map global subscription must hear calendar events (#1384 P0)");
        Assert.That(seen, Has.Some.Contains("summer"));

        manager.UnregisterGlobalTriggers(new MapId("calendar_probe_map"));
        seen.Clear();
        runtime.Advance(
            20,
            () => new ScriptContext(),
            manager.FireGlobalEvent,
            manager.HasGlobalEventSubscribers);
        Assert.That(seen, Is.Empty, "detached subscription must hear nothing further");
    }

    [Test]
    public void Advance_GlobalDispatch_SkipsWorkWhenNobodySubscribes()
    {
        var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
        var fired = new List<string>();
        manager.RegisterEventHandler(GameEvents.CalendarDayAdvanced, ctx =>
        {
            fired.Add(GameEvents.CalendarDayAdvanced.Value);
            return Task.CompletedTask;
        });

        CalendarRuntime runtime = new(
            CalendarFixtures.World(startDayIndex: 89),
            CalendarFixtures.Registry(CalendarFixtures.Solar360()));
        runtime.Advance(
            20,
            () => new ScriptContext(),
            manager.FireGlobalEvent,
            manager.HasGlobalEventSubscribers);

        Assert.That(fired, Is.EqualTo(new[] { GameEvents.CalendarDayAdvanced.Value }),
            "mod event handler counts as a subscriber; unsubscribed keys fire nothing");
    }

    private sealed class ProbeTrigger : Trigger
    {
        private readonly List<string> _seen;

        public ProbeTrigger(EventKey eventKey, List<string> seen)
        {
            EventKey = eventKey;
            _seen = seen;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            _seen.Add(context.Get<string>(MapTriggerEventPayloadKeys.CalendarPhaseId) ?? string.Empty);
            return Task.CompletedTask;
        }
    }

    private static CalendarRuntime CreateRuntime(int startDayIndex = 0)
    {
        return new CalendarRuntime(
            CalendarFixtures.World(startDayIndex: startDayIndex),
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
