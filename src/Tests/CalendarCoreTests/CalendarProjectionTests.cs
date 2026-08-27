using Ludots.Core.Gameplay.Calendar;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
public sealed class CalendarProjectionTests
{
    [Test]
    public void Solar360_ProjectsYearSeasonMonthXunAndSolarTerm()
    {
        CalendarDefinition calendar = CalendarFixtures.Solar360();

        CalendarDateSnapshot spring = CalendarProjection.Project(calendar, dayIndex: 0);
        Assert.That(spring.Year, Is.EqualTo(1));
        Assert.That(spring.DayOfYear, Is.EqualTo(1));
        Assert.That(spring.EraId, Is.EqualTo("era.founding"));
        Assert.That(spring.EraYear, Is.EqualTo(1));
        Assert.That(FindCycle(spring, "season").PhaseId, Is.EqualTo("spring"));
        Assert.That(FindCycle(spring, "month").PhaseId, Is.EqualTo("month.01"));
        Assert.That(FindCycle(spring, "xun").PhaseId, Is.EqualTo("early"));
        Assert.That(FindCycle(spring, "solarTerm").PhaseId, Is.EqualTo("lichun"));

        CalendarDateSnapshot lateSpring = CalendarProjection.Project(calendar, dayIndex: 89);
        Assert.That(FindCycle(lateSpring, "season").PhaseId, Is.EqualTo("spring"));
        Assert.That(FindCycle(lateSpring, "solarTerm").PhaseId, Is.EqualTo("guyu"));

        CalendarDateSnapshot summer = CalendarProjection.Project(calendar, dayIndex: 90);
        Assert.That(FindCycle(summer, "season").PhaseId, Is.EqualTo("summer"));
        Assert.That(FindCycle(summer, "month").PhaseId, Is.EqualTo("month.04"));
        Assert.That(FindCycle(summer, "xun").PhaseId, Is.EqualTo("early"));
        Assert.That(FindCycle(summer, "solarTerm").PhaseId, Is.EqualTo("lixia"));

        CalendarDateSnapshot yearTwo = CalendarProjection.Project(calendar, dayIndex: 360);
        Assert.That(yearTwo.Year, Is.EqualTo(2));
        Assert.That(yearTwo.DayOfYear, Is.EqualTo(1));
        Assert.That(FindCycle(yearTwo, "season").PhaseId, Is.EqualTo("spring"));
    }

    [Test]
    public void Era_UsesLatestStartDayIndex()
    {
        CalendarDefinition calendar = CalendarFixtures.Regnal();
        CalendarDateSnapshot founding = CalendarProjection.Project(calendar, 3599);
        Assert.That(founding.EraId, Is.EqualTo("era.founding"));
        Assert.That(founding.EraYear, Is.EqualTo(10));

        CalendarDateSnapshot expansion = CalendarProjection.Project(calendar, 3600);
        Assert.That(expansion.EraId, Is.EqualTo("era.expansion"));
        Assert.That(expansion.EraYear, Is.EqualTo(1));
        Assert.That(expansion.Year, Is.EqualTo(11));
    }

    [Test]
    public void DayPhase_UsesPermilleOfCurrentDay()
    {
        IReadOnlyList<CalendarDayPhaseDefinition> phases = CalendarFixtures.DefaultDayPhases();
        Assert.That(CalendarProjection.ResolveDayPhase(phases, ticksIntoDay: 0, ticksPerDay: 20).Id, Is.EqualTo("dawn"));
        Assert.That(CalendarProjection.ResolveDayPhase(phases, ticksIntoDay: 5, ticksPerDay: 20).Id, Is.EqualTo("day"));
        Assert.That(CalendarProjection.ResolveDayPhase(phases, ticksIntoDay: 15, ticksPerDay: 20).Id, Is.EqualTo("dusk"));
        Assert.That(CalendarProjection.ResolveDayPhase(phases, ticksIntoDay: 18, ticksPerDay: 20).Id, Is.EqualTo("night"));
    }

    [Test]
    public void DayPermille_UsesOnlyTicksIntoTheCurrentDay()
    {
        Assert.That(CalendarProjection.ComputeDayPermille(0, 20), Is.EqualTo(0));
        Assert.That(CalendarProjection.ComputeDayPermille(5, 20), Is.EqualTo(250));
        Assert.That(CalendarProjection.ComputeDayPermille(10, 20), Is.EqualTo(500));
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
