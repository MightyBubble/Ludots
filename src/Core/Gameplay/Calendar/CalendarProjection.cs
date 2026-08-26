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

            int year = checked(dayIndex / calendar.YearLengthDays + 1);
            int dayOfYear = dayIndex % calendar.YearLengthDays + 1;
            CalendarEraDefinition era = ResolveEra(calendar.Eras, dayIndex);
            int eraYear = checked((dayIndex - era.StartDayIndex) / calendar.YearLengthDays + 1);

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

        public static CalendarCycleSnapshot ProjectCycle(CalendarCycleDefinition cycle, int dayIndex)
        {
            ArgumentNullException.ThrowIfNull(cycle);
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

            int permille = (int)((long)ticksIntoDay * 1000L / ticksPerDay);
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

        public static int ComputeElapsedMin(int dayIndex, int ticksIntoDay, int ticksPerDay, int minutesPerDay)
        {
            long elapsed = (long)dayIndex * minutesPerDay
                + (long)ticksIntoDay * minutesPerDay / ticksPerDay;
            if (elapsed > int.MaxValue)
            {
                throw new InvalidOperationException("Calendar elapsed minutes overflowed int.");
            }

            return (int)elapsed;
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
