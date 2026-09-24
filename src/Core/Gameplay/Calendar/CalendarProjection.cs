using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Calendar
{
    public static class CalendarProjection
    {
        public static CalendarDateSnapshot Project(CalendarDefinition calendar, int dayIndex)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }

            CalendarCycleDefinition? yearCycle = null;
            if (calendar.UsesYearCycle)
            {
                yearCycle = RequireYearCycle(calendar);
            }
            else if (!calendar.YearLengthDays.HasValue)
            {
                throw new InvalidOperationException(
                    $"Calendar '{calendar.Id}' must declare exactly one of yearLengthDays / yearCycleId.");
            }

            int year = YearCount(calendar, yearCycle, dayIndex);
            int dayOfYear = DayOfYears(calendar, yearCycle, dayIndex);
            CalendarEraDefinition era = ResolveEra(calendar.Eras, dayIndex);
            // 均匀年：自纪年起点满 yearLengthDays 整年进位（周年制）。
            // 相位表年：纪年内年号按跨过的年相位计数（年界制），起点相位即元年。
            int eraYear = yearCycle == null
                ? checked((dayIndex - era.StartDayIndex) / calendar.YearLengthDays!.Value + 1)
                : checked(year - YearCount(calendar, yearCycle, era.StartDayIndex) + 1);

            var cycles = new CalendarCycleSnapshot[calendar.Cycles.Count];
            for (int i = 0; i < calendar.Cycles.Count; i++)
            {
                cycles[i] = ProjectCycle(calendar.Cycles[i], dayIndex);
            }

            return new CalendarDateSnapshot(
                calendar.Id,
                dayIndex,
                year,
                dayOfYear,
                era.Id,
                era.Label,
                eraYear,
                cycles);
        }

        private static CalendarCycleDefinition RequireYearCycle(CalendarDefinition calendar)
        {
            for (int i = 0; i < calendar.Cycles.Count; i++)
            {
                if (string.Equals(calendar.Cycles[i].Id, calendar.YearCycleId, StringComparison.Ordinal))
                {
                    return calendar.Cycles[i];
                }
            }

            throw new InvalidOperationException(
                $"Calendar '{calendar.Id}' yearCycleId '{calendar.YearCycleId}' does not name one of its cycles.");
        }

        private static int YearCount(
            CalendarDefinition calendar,
            CalendarCycleDefinition? yearCycle,
            int dayIndex)
        {
            if (yearCycle == null)
            {
                return checked(dayIndex / calendar.YearLengthDays!.Value + 1);
            }

            // 相位表年：绝对年 = 完整圈数 × 每圈年相位数 + 圈内第几年（均 1 基）。
            CalendarCycleSnapshot phase = ProjectCycle(yearCycle, dayIndex);
            return checked((dayIndex / yearCycle.LengthDays) * yearCycle.Phases.Count + phase.PhaseIndex + 1);
        }

        private static int DayOfYears(
            CalendarDefinition calendar,
            CalendarCycleDefinition? yearCycle,
            int dayIndex)
        {
            if (yearCycle == null)
            {
                return dayIndex % calendar.YearLengthDays!.Value + 1;
            }

            return ProjectCycle(yearCycle, dayIndex).DayInPhase;
        }

        public static CalendarCycleSnapshot ProjectCycle(CalendarCycleDefinition cycle, int dayIndex)
        {
            ArgumentNullException.ThrowIfNull(cycle);
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }
            int offset = dayIndex % cycle.LengthDays;
            int cursor = 0;
            for (int i = 0; i < cycle.Phases.Count; i++)
            {
                CalendarPhaseDefinition phase = cycle.Phases[i];
                int next = cursor + phase.LengthDays;
                if (offset < next)
                {
                    return new CalendarCycleSnapshot(
                        cycle.Id,
                        phase.Id,
                        phase.Label,
                        i,
                        offset - cursor + 1,
                        phase.LengthDays);
                }

                cursor = next;
            }

            throw new InvalidOperationException(
                $"Calendar cycle '{cycle.Id}' could not resolve day offset {offset}.");
        }

        public static int PhaseIndex(CalendarCycleDefinition cycle, int dayIndex)
        {
            ArgumentNullException.ThrowIfNull(cycle);
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }

            int offset = dayIndex % cycle.LengthDays;
            int cursor = 0;
            IReadOnlyList<CalendarPhaseDefinition> phases = cycle.Phases;
            for (int i = 0; i < phases.Count; i++)
            {
                int next = cursor + phases[i].LengthDays;
                if (offset < next)
                {
                    return i;
                }

                cursor = next;
            }

            throw new InvalidOperationException(
                $"Calendar cycle '{cycle.Id}' could not resolve day offset {offset}.");
        }

        /// <summary>
        /// 已经在该相位里是 0。否则取下一次进入该相位起点的整日数。
        /// 同一相位名出现多次时取最近的一次。找不到该相位返回 false。
        /// </summary>
        public static bool TryDaysUntilPhase(
            CalendarCycleDefinition cycle,
            int dayIndex,
            string phaseId,
            out int days)
        {
            ArgumentNullException.ThrowIfNull(cycle);
            if (dayIndex < 0)
            {
                throw new InvalidOperationException("Calendar dayIndex must be >= 0.");
            }

            if (string.IsNullOrWhiteSpace(phaseId))
            {
                throw new InvalidOperationException("Calendar phase id is required.");
            }

            days = 0;
            int offset = dayIndex % cycle.LengthDays;
            int cursor = 0;
            int best = -1;
            bool found = false;
            IReadOnlyList<CalendarPhaseDefinition> phases = cycle.Phases;
            for (int i = 0; i < phases.Count; i++)
            {
                CalendarPhaseDefinition phase = phases[i];
                int next = cursor + phase.LengthDays;
                if (string.Equals(phase.Id, phaseId, StringComparison.Ordinal))
                {
                    found = true;
                    if (offset >= cursor && offset < next)
                    {
                        days = 0;
                        return true;
                    }

                    int delta = offset < cursor
                        ? cursor - offset
                        : cycle.LengthDays - offset + cursor;
                    if (best < 0 || delta < best)
                    {
                        best = delta;
                    }
                }

                cursor = next;
            }

            if (!found || best < 0)
            {
                return false;
            }

            days = best;
            return true;
        }

        public static CalendarDayPhaseDefinition ResolveDayPhase(
            IReadOnlyList<CalendarDayPhaseDefinition> phases,
            int ticksIntoDay,
            int ticksPerDay)
        {
            ArgumentNullException.ThrowIfNull(phases);
            if (phases.Count == 0)
            {
                throw new InvalidOperationException("Calendar dayPhases must contain at least one phase.");
            }

            if (ticksPerDay < 1)
            {
                throw new InvalidOperationException("Calendar ticksPerDay must be >= 1.");
            }

            if ((uint)ticksIntoDay >= (uint)ticksPerDay)
            {
                throw new InvalidOperationException("Calendar ticksIntoDay must be in [0, ticksPerDay).");
            }

            int permille = ComputeDayPermille(ticksIntoDay, ticksPerDay);
            CalendarDayPhaseDefinition current = phases[0];
            for (int i = 1; i < phases.Count; i++)
            {
                if (phases[i].StartPermille <= permille)
                {
                    current = phases[i];
                }
            }

            return current;
        }

        public static int ComputeDayPermille(int ticksIntoDay, int ticksPerDay)
        {
            if (ticksPerDay < 1)
            {
                throw new InvalidOperationException("Calendar ticksPerDay must be >= 1.");
            }

            if ((uint)ticksIntoDay >= (uint)ticksPerDay)
            {
                throw new InvalidOperationException("Calendar ticksIntoDay must be in [0, ticksPerDay).");
            }

            return (int)((long)ticksIntoDay * 1000L / ticksPerDay);
        }

        private static CalendarEraDefinition ResolveEra(IReadOnlyList<CalendarEraDefinition> eras, int dayIndex)
        {
            CalendarEraDefinition current = eras[0];
            for (int i = 1; i < eras.Count; i++)
            {
                if (eras[i].StartDayIndex <= dayIndex)
                {
                    current = eras[i];
                }
            }

            return current;
        }
    }
}
