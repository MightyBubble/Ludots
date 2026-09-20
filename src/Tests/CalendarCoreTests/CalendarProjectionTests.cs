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

        CalendarDateSnapshot pacification = CalendarProjection.Project(calendar, 7200);
        Assert.That(pacification.EraId, Is.EqualTo("era.pacification"));
        Assert.That(pacification.EraYear, Is.EqualTo(1));
        Assert.That(pacification.Year, Is.EqualTo(21));

        CalendarDateSnapshot restoration = CalendarProjection.Project(calendar, 10800);
        Assert.That(restoration.EraId, Is.EqualTo("era.restoration"));
        Assert.That(restoration.EraLabel, Is.EqualTo("中兴"));
        Assert.That(restoration.EraYear, Is.EqualTo(1));
        Assert.That(restoration.Year, Is.EqualTo(31));
    }

    [Test]
    public void Lunisolar_CountsYearsThroughPhaseTable()
    {
        CalendarDefinition calendar = CalendarFixtures.Lunisolar();
        Assert.That(calendar.UsesYearCycle, Is.True);

        CalendarDateSnapshot firstYear = CalendarProjection.Project(calendar, 0);
        Assert.That(firstYear.Year, Is.EqualTo(1));
        Assert.That(firstYear.DayOfYear, Is.EqualTo(1));
        Assert.That(FindCycle(firstYear, "month").PhaseId, Is.EqualTo("month.001"));
        Assert.That(FindCycle(firstYear, "month").PhaseLabel, Is.EqualTo("正月"));
        Assert.That(FindCycle(firstYear, "year").PhaseId, Is.EqualTo("year.01"));

        CalendarDateSnapshot secondYear = CalendarProjection.Project(calendar, 354);
        Assert.That(secondYear.Year, Is.EqualTo(2));
        Assert.That(secondYear.DayOfYear, Is.EqualTo(1));

        // 闰年（第三年 385 天）与年相位切换。
        CalendarDateSnapshot leapYear = CalendarProjection.Project(calendar, 708);
        Assert.That(leapYear.Year, Is.EqualTo(3));
        Assert.That(leapYear.DayOfYear, Is.EqualTo(1));
        CalendarDateSnapshot leapYearEnd = CalendarProjection.Project(calendar, 1092);
        Assert.That(leapYearEnd.Year, Is.EqualTo(3));
        Assert.That(leapYearEnd.DayOfYear, Is.EqualTo(385));
        CalendarDateSnapshot fourthYear = CalendarProjection.Project(calendar, 1093);
        Assert.That(fourthYear.Year, Is.EqualTo(4));
        Assert.That(fourthYear.DayOfYear, Is.EqualTo(1));

        // 章末 wrap：第二章第一年。
        CalendarDateSnapshot zhangTwo = CalendarProjection.Project(calendar, 6940);
        Assert.That(zhangTwo.Year, Is.EqualTo(20));
        Assert.That(zhangTwo.DayOfYear, Is.EqualTo(1));
        Assert.That(FindCycle(zhangTwo, "year").PhaseId, Is.EqualTo("year.01"));
        Assert.That(zhangTwo.EraYear, Is.EqualTo(20));
    }

    [Test]
    public void Lunisolar_LeapMonthIsAnExplicitPhase()
    {
        CalendarDefinition calendar = CalendarFixtures.Lunisolar();

        CalendarCycleSnapshot leap = FindCycle(CalendarProjection.Project(calendar, 885), "month");
        Assert.That(leap.PhaseId, Is.EqualTo("month.031"));
        Assert.That(leap.PhaseLabel, Is.EqualTo("闰六月"));
        Assert.That(leap.PhaseLengthDays, Is.EqualTo(30));

        CalendarCycleSnapshot afterLeap = FindCycle(CalendarProjection.Project(calendar, 915), "month");
        Assert.That(afterLeap.PhaseId, Is.EqualTo("month.032"));
        Assert.That(afterLeap.PhaseLabel, Is.EqualTo("七月"));
    }

    [Test]
    public void Solar360_FestivalPhasesSitOnTheirAuthoredDays()
    {
        CalendarDefinition calendar = CalendarFixtures.Solar360();

        AssertFestival(calendar, 0, "chunjie", "春节");
        AssertFestival(calendar, 4, "chunjie", "春节");
        AssertFestival(calendar, 14, "yuanxiao", "元宵");
        AssertFestival(calendar, 124, "duanwu", "端午");
        AssertFestival(calendar, 186, "qixi", "七夕");
        AssertFestival(calendar, 224, "zhongqiu", "中秋");
        AssertFestival(calendar, 248, "chongyang", "重阳");
        AssertFestival(calendar, 359, "chuxi", "除夕");
        AssertFestival(calendar, 5, "ordinary.p01", "平日");
        AssertFestival(calendar, 358, "ordinary.p06", "平日");
    }

    private static void AssertFestival(CalendarDefinition calendar, int dayIndex, string phaseId, string label)
    {
        CalendarCycleSnapshot festival = FindCycle(CalendarProjection.Project(calendar, dayIndex), "festival");
        Assert.That(festival.PhaseId, Is.EqualTo(phaseId), $"dayIndex {dayIndex}");
        Assert.That(festival.PhaseLabel, Is.EqualTo(label), $"dayIndex {dayIndex}");
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
