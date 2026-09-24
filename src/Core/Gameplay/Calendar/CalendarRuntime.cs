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
        // 开局日序只在「还没人看见日子被改过」时能整段换掉。跨日、昼夜相位被订阅者看见、
        // 或作者写过日序/当天步数之后，开局窗口关闭；再改日子只能往前走。
        private bool _openingCommitted;

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
            bool crossedDay = false;
            while (TicksIntoDay >= world.TicksPerDay)
            {
                TicksIntoDay -= world.TicksPerDay;
                AdvanceOneDay(contextFactory, fireEvent, hasSubscribers, wantsAnyEvent);
                crossedDay = true;
            }

            if (crossedDay)
            {
                _openingCommitted = true;
            }

            if (previousDayPhaseId != null)
            {
                string currentDayPhaseId = CurrentDayPhaseId();
                if (!string.Equals(previousDayPhaseId, currentDayPhaseId, StringComparison.Ordinal))
                {
                    FireDayPhaseChanged(currentDayPhaseId, contextFactory!, fireEvent!);
                    _openingCommitted = true;
                }
            }
        }

        public int ReadDayIndex()
        {
            EnsureEnabled();
            return DayIndex;
        }

        public int ReadTicksIntoDay()
        {
            EnsureEnabled();
            return TicksIntoDay;
        }

        public int ReadYear(string calendarId)
        {
            EnsureEnabled();
            return Project(calendarId).Year;
        }

        public int ReadDayPermille()
        {
            EnsureEnabled();
            return CalendarProjection.ComputeDayPermille(TicksIntoDay, _world!.TicksPerDay);
        }

        public int ReadDayPhaseKeyId()
        {
            EnsureEnabled();
            return RequireKeyId(CurrentDayPhaseId());
        }

        public int ReadCyclePhaseKeyId(string calendarId, string cycleId)
        {
            return RequireKeyId(RequireCycle(calendarId, cycleId).PhaseId);
        }

        public int ReadCycleDay(string calendarId, string cycleId)
        {
            return RequireCycle(calendarId, cycleId).DayInPhase;
        }

        public int ReadCyclePhaseIndex(string calendarId, string cycleId)
        {
            EnsureEnabled();
            CalendarCycleDefinition cycle = RequireCycleDefinition(calendarId, cycleId);
            return CalendarProjection.PhaseIndex(cycle, DayIndex);
        }

        public int ReadDaysUntilPhase(string calendarId, string cycleId, string phaseId, int dayInPhase = 0)
        {
            EnsureEnabled();
            CalendarCycleDefinition cycle = RequireCycleDefinition(calendarId, cycleId);
            CalendarDaysUntilStatus status = CalendarProjection.TryDaysUntilPhase(
                cycle, DayIndex, phaseId, dayInPhase, out int days);
            if (status == CalendarDaysUntilStatus.Found)
            {
                return days;
            }

            if (status == CalendarDaysUntilStatus.MissingPhase)
            {
                throw new InvalidOperationException(
                    $"Calendar '{calendarId}' cycle '{cycleId}' has no phase '{phaseId}'.");
            }

            throw new InvalidOperationException(
                $"Calendar '{calendarId}' cycle '{cycleId}' phase '{phaseId}' does not contain day {dayInPhase}.");
        }

        /// <summary>
        /// 开局落定：在日子还没被提交前，把日序和当天步数换成作者给的开局值，不发事件。
        /// 请求与当前值相同（包括已经提交过）是空操作。提交之后再写成别的值会失败。
        /// </summary>
        public void ApplyInitialState(int dayIndex, int ticksIntoDay)
        {
            EnsureEnabled();
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }

            if ((uint)ticksIntoDay >= (uint)_world!.TicksPerDay)
            {
                throw new InvalidOperationException(
                    $"Calendar ticksIntoDay must be in [0, {_world.TicksPerDay}).");
            }

            if (_openingCommitted)
            {
                if (dayIndex == DayIndex && ticksIntoDay == TicksIntoDay)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Calendar opening is already committed at dayIndex={DayIndex}, ticksIntoDay={TicksIntoDay}. " +
                    $"Requested dayIndex={dayIndex}, ticksIntoDay={ticksIntoDay}.");
            }

            DayIndex = dayIndex;
            TicksIntoDay = ticksIntoDay;
            _projections = BuildProjections(DayIndex);
            _projectionsStale = false;
            _openingCommitted = true;
        }

        /// <summary>
        /// 把日序拨到绝对值。小于当前日序失败。相等是空操作。更大时按天往前走，
        /// 每天的相位进出和日序事件与 <see cref="Advance"/> 同一条路径。
        /// </summary>
        public void SetDayIndex(
            int dayIndex,
            Func<ScriptContext>? contextFactory = null,
            Action<EventKey, ScriptContext>? fireEvent = null,
            Func<EventKey, bool>? hasSubscribers = null)
        {
            EnsureEnabled();
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }

            if (dayIndex < DayIndex)
            {
                throw new InvalidOperationException(
                    $"Calendar dayIndex cannot move backward from {DayIndex} to {dayIndex}.");
            }

            _openingCommitted = true;
            bool wantsAnyEvent = fireEvent != null && contextFactory != null;
            while (DayIndex < dayIndex)
            {
                AdvanceOneDay(contextFactory, fireEvent, hasSubscribers, wantsAnyEvent);
            }
        }

        public void SetTicksIntoDay(
            int ticksIntoDay,
            Func<ScriptContext>? contextFactory = null,
            Action<EventKey, ScriptContext>? fireEvent = null,
            Func<EventKey, bool>? hasSubscribers = null)
        {
            EnsureEnabled();
            if ((uint)ticksIntoDay >= (uint)_world!.TicksPerDay)
            {
                throw new InvalidOperationException(
                    $"Calendar ticksIntoDay must be in [0, {_world.TicksPerDay}).");
            }

            _openingCommitted = true;
            bool wantsDayPhase = fireEvent != null && contextFactory != null &&
                (hasSubscribers?.Invoke(GameEvents.CalendarDayPhaseChanged) ?? true);
            string? previousDayPhaseId = wantsDayPhase ? CurrentDayPhaseId() : null;
            TicksIntoDay = ticksIntoDay;
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
            _openingCommitted = true;
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
                CalendarDateSnapshot active = wantsProjections
                    ? RequireActiveProjection()
                    : ProjectActive();
                int activeCalendarKeyId = RequireKeyId(active.CalendarId);
                Fire(GameEvents.CalendarDayAdvanced, contextFactory!, fireEvent!, ctx =>
                {
                    ctx.Set(MapTriggerEventPayloadKeys.CalendarId, activeCalendarKeyId);
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
                    int calendarKeyId = RequireKeyId(nextDate.CalendarId);
                    Fire(GameEvents.CalendarEraChanged, contextFactory, fireEvent, ctx =>
                    {
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarId, calendarKeyId);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                        ctx.Set(MapTriggerEventPayloadKeys.CalendarEraId, RequireKeyId(nextDate.EraId));
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
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarId, RequireKeyId(nextDate.CalendarId));
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, RequireKeyId(priorCycle.CycleId));
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, RequireKeyId(priorCycle.PhaseId));
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseIndex, priorCycle.PhaseIndex);
                        });
                    }

                    if (wantsCycleEntered)
                    {
                        Fire(GameEvents.CalendarCyclePhaseEntered, contextFactory, fireEvent, ctx =>
                        {
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarId, RequireKeyId(nextDate.CalendarId));
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, nextDate.DayIndex);
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarCycleId, RequireKeyId(nextCycle.CycleId));
                            ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, RequireKeyId(nextCycle.PhaseId));
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

        private CalendarDateSnapshot[] BuildProjections(int dayIndex)
        {
            var projections = new CalendarDateSnapshot[_sortedCalendars.Length];
            for (int i = 0; i < _sortedCalendars.Length; i++)
            {
                projections[i] = CalendarProjection.Project(_sortedCalendars[i], dayIndex);
            }

            return projections;
        }

        private CalendarCycleDefinition RequireCycleDefinition(string calendarId, string cycleId)
        {
            if (string.IsNullOrWhiteSpace(cycleId))
            {
                throw new InvalidOperationException("Calendar cycle id is required.");
            }

            CalendarDefinition calendar = _registry.Require(calendarId);
            IReadOnlyList<CalendarCycleDefinition> cycles = calendar.Cycles;
            for (int i = 0; i < cycles.Count; i++)
            {
                if (string.Equals(cycles[i].Id, cycleId, StringComparison.Ordinal))
                {
                    return cycles[i];
                }
            }

            throw new InvalidOperationException($"Calendar '{calendarId}' has no cycle '{cycleId}'.");
        }

        private CalendarCycleSnapshot RequireCycle(string calendarId, string cycleId)
        {
            if (string.IsNullOrWhiteSpace(cycleId))
            {
                throw new InvalidOperationException("Calendar cycle id is required.");
            }

            CalendarDateSnapshot date = Project(calendarId);
            for (int i = 0; i < date.Cycles.Count; i++)
            {
                if (string.Equals(date.Cycles[i].CycleId, cycleId, StringComparison.Ordinal))
                {
                    return date.Cycles[i];
                }
            }

            throw new InvalidOperationException($"Calendar '{calendarId}' has no cycle '{cycleId}'.");
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
                ctx.Set(MapTriggerEventPayloadKeys.CalendarId, RequireKeyId(ActiveCalendarId));
                ctx.Set(MapTriggerEventPayloadKeys.CalendarDayIndex, DayIndex);
                ctx.Set(MapTriggerEventPayloadKeys.CalendarPhaseId, RequireKeyId(phaseId));
            });
        }

        /// <summary>符号在装载期已注册（CalendarConfigLoader.RegisterConfigKeys）；发不出去
        /// 的 id 说明表装载绕过了装载器，fail fast 而不是发一个没人能匹配的 0。</summary>
        private static int RequireKeyId(string symbol)
        {
            int id = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetId(symbol);
            if (id == Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Calendar symbol '{symbol}' is not registered in ConfigKeyRegistry; load calendars through CalendarConfigLoader.");
            }

            return id;
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
