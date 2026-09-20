using System;
using System.Collections.Generic;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed class CalendarRuntime
    {
        private readonly CalendarDefinitionRegistry _registry;
        private readonly CalendarWorldConfig? _world;
        private readonly CalendarDefinition[] _sortedCalendars;
        private CalendarDateSnapshot[] _projections;
        private bool _projectionsStale;

        public CalendarRuntime(CalendarWorldConfig? world, CalendarDefinitionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _world = world;
            IsEnabled = world != null;
            DayIndex = world?.StartDayIndex ?? 0;
            TicksIntoDay = 0;
            _sortedCalendars = SortCalendars(registry);
            _projections = IsEnabled ? BuildProjections(DayIndex) : Array.Empty<CalendarDateSnapshot>();
        }

        public bool IsEnabled { get; }

        public int DayIndex { get; private set; }

        public int TicksIntoDay { get; private set; }

        public string ActiveCalendarId => _world?.ActiveCalendarId ?? string.Empty;

        /// <summary>
        /// 推进日序并按订阅派发事件。hasSubscribers 是订阅探针：对某个事件键返回 false 时，
        /// 该事件不派发，且为它准备的计算（全历投影重建、相位 diff）整体跳过；
        /// 传 null 表示调用方显式要求派发，按全部有订阅处理。没有订阅者时只推日序。
        /// 订阅空窗期跨过的相位切换不补发——事件是通知，不是历史。
        /// </summary>
        public void Advance(
            int consumedSteps,
            Func<ScriptContext>? contextFactory = null,
            Action<EventKey, ScriptContext>? fireEvent = null,
            Func<EventKey, bool>? hasSubscribers = null)
        {
            if (consumedSteps < 0)
            {
                throw new InvalidOperationException("Calendar consumed steps must be >= 0.");
            }

            if (!IsEnabled || consumedSteps == 0)
            {
                return;
            }

            CalendarWorldConfig world = _world!;
            bool wantsAnyEvent = fireEvent != null && contextFactory != null;
            bool wantsDayPhase = wantsAnyEvent &&
                (hasSubscribers?.Invoke(GameEvents.CalendarDayPhaseChanged) ?? true);
            string? previousDayPhaseId = wantsDayPhase ? CurrentDayPhaseId() : null;

            TicksIntoDay = checked(TicksIntoDay + consumedSteps);
            while (TicksIntoDay >= world.TicksPerDay)
            {
                TicksIntoDay -= world.TicksPerDay;
                AdvanceOneDay(contextFactory, fireEvent, hasSubscribers, wantsAnyEvent);
            }

            if (previousDayPhaseId != null)
            {
                string currentDayPhaseId = CurrentDayPhaseId();
                if (!string.Equals(previousDayPhaseId, currentDayPhaseId, StringComparison.Ordinal))
                {
                    FireDayPhaseChanged(currentDayPhaseId, contextFactory!, fireEvent!);
                }
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
            _projectionsStale = false;
        }

        private void AdvanceOneDay(
            Func<ScriptContext>? contextFactory,
            Action<EventKey, ScriptContext>? fireEvent,
            Func<EventKey, bool>? hasSubscribers,
            bool wantsAnyEvent)
        {
            bool wantsEra = wantsAnyEvent &&
                (hasSubscribers?.Invoke(GameEvents.CalendarEraChanged) ?? true);
            bool wantsCycleExited = wantsAnyEvent &&
                (hasSubscribers?.Invoke(GameEvents.CalendarCyclePhaseExited) ?? true);
            bool wantsCycleEntered = wantsAnyEvent &&
                (hasSubscribers?.Invoke(GameEvents.CalendarCyclePhaseEntered) ?? true);
            bool wantsProjections = wantsEra || wantsCycleExited || wantsCycleEntered;

            CalendarDateSnapshot[] previous = _projections;
            if (wantsProjections && _projectionsStale)
            {
                // 订阅空窗期跳过的投影在这里静默追平：空窗内跨过的相位切换不补发。
                previous = BuildProjections(DayIndex);
            }

            DayIndex = checked(DayIndex + 1);
            if (!wantsProjections)
            {
                _projectionsStale = true;
            }
            else
            {
                _projections = BuildProjections(DayIndex);
                _projectionsStale = false;
                FireCycleEvents(previous, _projections, contextFactory!, fireEvent!, wantsEra, wantsCycleExited, wantsCycleEntered);
            }

            if (wantsAnyEvent &&
                (hasSubscribers?.Invoke(GameEvents.CalendarDayAdvanced) ?? true))
            {
                CalendarDateSnapshot active = ProjectActive();
                Fire(GameEvents.CalendarDayAdvanced, contextFactory!, fireEvent!, ctx =>
                {
                    ctx.Set(MapTriggerEventPayloadKeys.CalendarId, active.CalendarId);
                    ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, active.DayIndex);
                    ctx.Set(MapTriggerEventPayloadKeys.CalendarYear, active.Year);
                });
            }
        }

        private void FireCycleEvents(
            CalendarDateSnapshot[] previous,
            CalendarDateSnapshot[] next,
            Func<ScriptContext> contextFactory,
            Action<EventKey, ScriptContext> fireEvent,
            bool wantsEra,
            bool wantsCycleExited,
            bool wantsCycleEntered)
        {
            for (int i = 0; i < next.Length; i++)
            {
                CalendarDateSnapshot nextDate = next[i];
                CalendarDateSnapshot priorDate = previous[i];
                if (wantsEra &&
                    !string.Equals(priorDate.EraId, nextDate.EraId, StringComparison.Ordinal))
                {
                    Fire(GameEvents.CalendarEraChanged, contextFactory, fireEvent, ctx =>
                    {
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarId, nextDate.CalendarId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarEraId, nextDate.EraId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarYear, nextDate.Year);
                    });
                }

                for (int c = 0; c < nextDate.Cycles.Count; c++)
                {
                    CalendarCycleSnapshot nextCycle = nextDate.Cycles[c];
                    CalendarCycleSnapshot priorCycle = priorDate.Cycles[c];
                    if (string.Equals(priorCycle.PhaseId, nextCycle.PhaseId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (wantsCycleExited)
                    {
                        Fire(GameEvents.CalendarCyclePhaseExited, contextFactory, fireEvent, ctx =>
                        {
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarId, nextDate.CalendarId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, priorCycle.CycleId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, priorCycle.PhaseId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseIndex, priorCycle.PhaseIndex);
                        });
                    }

                    if (wantsCycleEntered)
                    {
                        Fire(GameEvents.CalendarCyclePhaseEntered, contextFactory, fireEvent, ctx =>
                        {
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarId, nextDate.CalendarId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, nextCycle.CycleId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, nextCycle.PhaseId);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseIndex, nextCycle.PhaseIndex);
                        });
                    }
                }
            }
        }

        private static CalendarDefinition[] SortCalendars(CalendarDefinitionRegistry registry)
        {
            var calendars = new CalendarDefinition[registry.All.Count];
            int i = 0;
            foreach (CalendarDefinition calendar in registry.All)
            {
                calendars[i++] = calendar;
            }

            Array.Sort(calendars, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            return calendars;
        }

        private CalendarDateSnapshot[] BuildProjections(int dayIndex)
        {
            var projections = new CalendarDateSnapshot[_sortedCalendars.Length];
            for (int i = 0; i < _sortedCalendars.Length; i++)
            {
                projections[i] = CalendarProjection.Project(_sortedCalendars[i], dayIndex);
            }

            return projections;
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
