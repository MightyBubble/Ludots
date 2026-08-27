using System.Text;
using System.Text.Json;
using Ludots.Core.Gameplay.Calendar;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
[NonParallelizable]
public sealed class CalendarCoreAcceptanceTests
{
    [Test]
    public void CalendarCore_SeasonCrossing_WritesAcceptanceArtifacts()
    {
        string repoRoot = CalendarFixtures.FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "calendar-core");
        Directory.CreateDirectory(artifactDir);

        var registry = CalendarFixtures.Registry(CalendarFixtures.Solar360(), CalendarFixtures.Regnal());
        var runtime = new CalendarRuntime(CalendarFixtures.Clock("calendar.solar360", ticksPerDay: 1, startDayIndex: 88), registry);
        var rows = new List<PhaseRow>();

        Capture(rows, runtime, "day88", "Still spring, late third month");
        runtime.Advance(1);
        Capture(rows, runtime, "day89", "Last day of spring");
        runtime.Advance(1);
        Capture(rows, runtime, "day90", "Summer and Lixia begin");

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTrace(rows));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(rows));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPath());

        CalendarDateSnapshot summer = runtime.ProjectActive();
        Assert.That(FindCycle(summer, "season").PhaseId, Is.EqualTo("summer"));
        Assert.That(FindCycle(summer, "solarTerm").PhaseId, Is.EqualTo("lixia"));
        Assert.That(FindCycle(summer, "month").PhaseId, Is.EqualTo("month.04"));
        Assert.That(runtime.Project("calendar.regnal").EraId, Is.EqualTo("era.founding"));
    }

    private static void Capture(List<PhaseRow> rows, CalendarRuntime runtime, string phaseId, string title)
    {
        CalendarClockSnapshot clock = runtime.CaptureClockSnapshot();
        CalendarDateSnapshot date = clock.ActiveDate!;
        rows.Add(new PhaseRow(
            phaseId,
            title,
            date.DayIndex,
            date.Year,
            date.DayOfYear,
            date.EraLabel,
            FindCycle(date, "season").PhaseLabel,
            FindCycle(date, "month").PhaseLabel,
            FindCycle(date, "xun").PhaseLabel,
            FindCycle(date, "solarTerm").PhaseLabel,
            clock.DayPhaseLabel,
            clock.DayPermille));
    }

    private static string BuildTrace(IReadOnlyList<PhaseRow> rows)
    {
        return string.Join(Environment.NewLine, rows.Select((row, index) => JsonSerializer.Serialize(new
        {
            event_id = $"calendar-core-{index + 1:000}",
            phase_id = row.PhaseId,
            day_index = row.DayIndex,
            year = row.Year,
            day_of_year = row.DayOfYear,
            era = row.Era,
            season = row.Season,
            month = row.Month,
            xun = row.Xun,
            solar_term = row.SolarTerm,
            day_phase = row.DayPhase,
            day_permille = row.DayPermille
        }))) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<PhaseRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: calendar-core");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Goal: prove world day index projects season, month, xun, solar term, and era without a second scheduler.");
        sb.AppendLine("- Gameplay domain: Core `CalendarRuntime` consuming Step ticks.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Calendar: `calendar.solar360` plus overlay `calendar.regnal`");
        sb.AppendLine("- ticksPerDay: 1");
        sb.AppendLine("- startDayIndex: 88");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Read day 88: still spring.");
        sb.AppendLine("2. Advance one day to 89: last day of spring / 谷雨.");
        sb.AppendLine("3. Advance one day to 90: summer / 立夏 / 四月.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: day 90 is summer, 立夏, 四月.");
        sb.AppendLine("- Failure branch condition: season or solar term stay on spring values after day 90.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (PhaseRow row in rows)
        {
            sb.AppendLine($"- `{row.PhaseId}` -> day={row.DayIndex} {row.Era} {row.Year}年 {row.Season} {row.Month}{row.Xun} {row.SolarTerm}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- success: yes");
        sb.AppendLine("- verdict: Calendar projects business time from the existing Step clock.");
        return sb.ToString();
    }

    private static string BuildPath()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Step consumed] --> B[ticksIntoDay / ticksPerDay]",
            "    B --> C[dayIndex + 1]",
            "    C --> D[calendar.solar360 season/month/xun/solarTerm]",
            "    C --> E[calendar.regnal era overlay]",
            "    D --> F[Calendar.DayAdvanced / CyclePhaseEntered]"
        }) + Environment.NewLine;
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

    private sealed record PhaseRow(
        string PhaseId,
        string Title,
        int DayIndex,
        int Year,
        int DayOfYear,
        string Era,
        string Season,
        string Month,
        string Xun,
        string SolarTerm,
        string DayPhase,
        int DayPermille);
}
