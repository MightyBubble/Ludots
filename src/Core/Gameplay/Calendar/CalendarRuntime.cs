using System;
using System.Collections.Generic;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed class CalendarRuntime
    {
        private readonly CalendarDefinitionRegistry _registry;
        private readonly CalendarWorldConfig? _world;
        private CalendarDateSnapshot[] _projections;

        public CalendarRuntime(CalendarWorldConfig? world, CalendarDefinitionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _world = world;
            IsEnabled = world != null;
            DayIndex = world?.StartDayIndex ?? 0;
            TicksIntoDay = 0;
            _projections = IsEnabled ? BuildProjections(DayIndex) : Array.Empty<CalendarDateSnapshot>();
        }

        public bool IsEnabled { get; }

        public int DayIndex { get; private set; }

        public int TicksIntoDay { get; private set; }

        public string ActiveCalendarId => _world?.ActiveCalendarId ?? string.Empty;

        public void Advance(
            int consumedSteps,
            Func<ScriptContext>? contextFactory = null,
            Action<EventKey, ScriptContext>? fireEvent = null)
        {
            if (!IsEnabled || consumedSteps <= 0)
            {
                return;
            }

            if (consumedSteps < 0)
            {
                throw new InvalidOperationException("Calendar consumed steps must be >= 0.");
            }

            CalendarWorldConfig world = _world!;
            string previousDayPhaseId = CurrentDayPhaseId();
            TicksIntoDay = checked(TicksIntoDay + consumedSteps);
            while (TicksIntoDay >= world.TicksPerDay)
            {
                TicksIntoDay -= world.TicksPerDay;
                AdvanceOneDay(contextFactory, fireEvent);
            }

            string currentDayPhaseId = CurrentDayPhaseId();
            if (fireEvent != null && contextFactory != null &&
                !string.Equals(previousDayPhaseId, currentDayPhaseId, StringComparison.Ordinal))
            {
                FireDayPhaseChanged(currentDayPhaseId, contextFactory, fireEvent);
            }
        }

        public CalendarDateSnapshot Project(string calendarId)
        {
            EnsureEnabled();
            return CalendarProjection.Project(_registry.Require(calendarId), DayIndex);
        }

        public CalendarDateSnapshot ProjectActive()
        {
            EnsureEnabled();
            return Project(ActiveCalendarId);
        }

        public CalendarProgressSnapshot CaptureProgressSnapshot()
        {
            if (!IsEnabled)
            {
                return new CalendarProgressSnapshot(
                    Enabled: false,
                    DayIndex: 0,
                    TicksIntoDay: 0,
                    TicksPerDay: 0,
                    DayPermille: 0,
                    DayPhaseId: string.Empty,
                    DayPhaseLabel: string.Empty,
                    ActiveDate: null);
            }

            CalendarWorldConfig world = _world!;
            CalendarDayPhaseDefinition phase = CalendarProjection.ResolveDayPhase(
                world.DayPhases,
                TicksIntoDay,
                world.TicksPerDay);
            return new CalendarProgressSnapshot(
                Enabled: true,
                DayIndex: DayIndex,
                TicksIntoDay: TicksIntoDay,
                TicksPerDay: world.TicksPerDay,
                DayPermille: CalendarProjection.ComputeDayPermille(TicksIntoDay, world.TicksPerDay),
                DayPhaseId: phase.Id,
                DayPhaseLabel: phase.Label,
                ActiveDate: ProjectActive());
        }

        public CalendarWorldSnapshot CaptureSnapshot()
        {
            return new CalendarWorldSnapshot(
                IsEnabled,
                DayIndex,
                TicksIntoDay,
                ActiveCalendarId);
        }

        public void RestoreSnapshot(CalendarWorldSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.Enabled != IsEnabled)
            {
                throw new InvalidOperationException(
                    $"Calendar save enabled={snapshot.Enabled} does not match runtime enabled={IsEnabled}.");
            }

            if (!IsEnabled)
            {
                return;
            }

            if (!string.Equals(snapshot.ActiveCalendarId, ActiveCalendarId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Calendar save activeCalendarId '{snapshot.ActiveCalendarId}' does not match config '{ActiveCalendarId}'.");
            }

            if (snapshot.DayIndex < 0)
            {
                throw new InvalidOperationException("Calendar save dayIndex must be >= 0.");
            }

            if ((uint)snapshot.TicksIntoDay >= (uint)_world!.TicksPerDay)
            {
                throw new InvalidOperationException(
                    $"Calendar save ticksIntoDay must be in [0, {_world.TicksPerDay}).");
            }

            DayIndex = snapshot.DayIndex;
            TicksIntoDay = snapshot.TicksIntoDay;
            _projections = BuildProjections(DayIndex);
        }

        private void AdvanceOneDay(
            Func<ScriptContext>? contextFactory,
            Action<EventKey, ScriptContext>? fireEvent)
        {
            CalendarDateSnapshot[] previous = _projections;
            DayIndex = checked(DayIndex + 1);
            _projections = BuildProjections(DayIndex);

            if (fireEvent == null || contextFactory == null)
            {
                return;
            }

            for (int i = 0; i < _projections.Length; i++)
            {
                CalendarDateSnapshot next = _projections[i];
                CalendarDateSnapshot prior = previous[i];
                if (!string.Equals(prior.EraId, next.EraId, StringComparison.Ordinal))
                {
                    Fire(GameEvents.CalendarEraChanged, contextFactory, fireEvent, ctx =>
                    {
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarId, next.CalendarId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, next.DayIndex);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarEraId, next.EraId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarYear, next.Year);
                    });
                }

                for (int c = 0; c < next.Cycles.Count; c++)
                {
                    CalendarCycleSnapshot nextCycle = next.Cycles[c];
                    CalendarCycleSnapshot priorCycle = prior.Cycles[c];
                    if (string.Equals(priorCycle.PhaseId, nextCycle.PhaseId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Fire(GameEvents.CalendarCyclePhaseExited, contextFactory, fireEvent, ctx =>
                    {
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarId, next.CalendarId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, next.DayIndex);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, priorCycle.CycleId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, priorCycle.PhaseId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseIndex, priorCycle.PhaseIndex);
                    });
                    Fire(GameEvents.CalendarCyclePhaseEntered, contextFactory, fireEvent, ctx =>
                    {
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarId, next.CalendarId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, next.DayIndex);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, nextCycle.CycleId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, nextCycle.PhaseId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseIndex, nextCycle.PhaseIndex);
                    });
                }
            }

            CalendarDateSnapshot active = RequireActiveProjection();
            Fire(GameEvents.CalendarDayAdvanced, contextFactory, fireEvent, ctx =>
            {
                ctx.Set(MapTriggerEventPayloadKeys.CalendarId, active.CalendarId);
                ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, active.DayIndex);
                ctx.Set(MapTriggerEventPayloadKeys.CalendarYear, active.Year);
            });
        }

        private CalendarDateSnapshot[] BuildProjections(int dayIndex)
        {
            var calendars = new List<CalendarDefinition>(_registry.All);
            calendars.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var projections = new CalendarDateSnapshot[calendars.Count];
            for (int i = 0; i < calendars.Count; i++)
            {
                projections[i] = CalendarProjection.Project(calendars[i], dayIndex);
            }

            return projections;
        }

        private CalendarDateSnapshot RequireActiveProjection()
        {
            for (int i = 0; i < _projections.Length; i++)
            {
                if (string.Equals(_projections[i].CalendarId, ActiveCalendarId, StringComparison.Ordinal))
                {
                    return _projections[i];
                }
            }

            throw new InvalidOperationException(
                $"Active calendar '{ActiveCalendarId}' is missing from runtime projections.");
        }

        private string CurrentDayPhaseId()
        {
            return CalendarProjection.ResolveDayPhase(_world!.DayPhases, TicksIntoDay, _world.TicksPerDay).Id;
        }

        private void FireDayPhaseChanged(
            string phaseId,
            Func<ScriptContext> contextFactory,
            Action<EventKey, ScriptContext> fireEvent)
        {
            Fire(GameEvents.CalendarDayPhaseChanged, contextFactory, fireEvent, ctx =>
            {
                ctx.Set(MapTriggerEventPayloadKeys.CalendarId, ActiveCalendarId);
                ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, DayIndex);
                ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, phaseId);
            });
        }

        private static void Fire(
            EventKey eventKey,
            Func<ScriptContext> contextFactory,
            Action<EventKey, ScriptContext> fireEvent,
            Action<ScriptContext> write)
        {
            ScriptContext ctx = contextFactory();
            write(ctx);
            fireEvent(eventKey, ctx);
        }

        private void EnsureEnabled()
        {
            if (!IsEnabled)
            {
                throw new InvalidOperationException(
                    "Calendar is not enabled. Add Calendar/world.json to activate world calendar.");
            }
        }
    }
}
