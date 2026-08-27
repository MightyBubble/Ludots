using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed record CalendarEraDefinition(string Id, string Label, int StartDayIndex);

    public sealed record CalendarPhaseDefinition(string Id, string Label, int LengthDays);

    public sealed record CalendarCycleDefinition(
        string Id,
        int LengthDays,
        IReadOnlyList<CalendarPhaseDefinition> Phases);

    public sealed record CalendarDayPhaseDefinition(string Id, string Label, int StartPermille);

    public sealed record CalendarDefinition(
        string Id,
        int YearLengthDays,
        IReadOnlyList<CalendarEraDefinition> Eras,
        IReadOnlyList<CalendarCycleDefinition> Cycles);

    public sealed record CalendarWorldConfig(
        string TickSource,
        int TicksPerDay,
        int StartDayIndex,
        string ActiveCalendarId,
        IReadOnlyList<CalendarDayPhaseDefinition> DayPhases);

    public sealed record CalendarCycleSnapshot(
        string CycleId,
        string PhaseId,
        string PhaseLabel,
        int PhaseIndex,
        int DayInPhase,
        int PhaseLengthDays);

    public sealed record CalendarDateSnapshot(
        string CalendarId,
        int DayIndex,
        int Year,
        int DayOfYear,
        string EraId,
        string EraLabel,
        int EraYear,
        IReadOnlyList<CalendarCycleSnapshot> Cycles);

    public sealed record CalendarWorldSnapshot(
        bool Enabled,
        int DayIndex,
        int TicksIntoDay,
        string ActiveCalendarId);

    public sealed record CalendarProgressSnapshot(
        bool Enabled,
        int DayIndex,
        int TicksIntoDay,
        int TicksPerDay,
        int DayPermille,
        string DayPhaseId,
        string DayPhaseLabel,
        CalendarDateSnapshot? ActiveDate);
}
