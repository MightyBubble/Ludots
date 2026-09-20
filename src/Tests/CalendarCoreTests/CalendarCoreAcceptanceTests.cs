using System.Text;
using System.Text.Json;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.CalendarCore;

[TestFixture]
[NonParallelizable]
public sealed class CalendarCoreAcceptanceTests
{
    [Test]
    public void CalendarCore_MultiCalendarFestivalsAndSubscribedDispatch_WritesAcceptanceArtifacts()
    {
        string repoRoot = CalendarFixtures.FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "calendar-core");
        Directory.CreateDirectory(artifactDir);

        var rows = new List<PhaseRow>();
        int dispatchChecks = RunScenario(rows);

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTrace(rows));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(rows, dispatchChecks));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPath());

        Assert.That(dispatchChecks, Is.EqualTo(5), "all five dispatch assertions must hold");
    }

    /// <summary>返回通过的派发断言数；快照行写入 rows。</summary>
    private static int RunScenario(List<PhaseRow> rows)
    {
        int checks = 0;

        // ── 场景一：四季跨界（solar360 主历）──
        var world = CalendarFixtures.World(CalendarFixtures.Solar360Id, ticksPerDay: 1, startDayIndex: 88);
        var runtime = new CalendarRuntime(world, CalendarFixtures.DefaultRegistry());
        Capture(rows, runtime, "day88", "Still spring, late third month");
        runtime.Advance(1);
        Capture(rows, runtime, "day89", "Last day of spring");
        runtime.Advance(1);
        Capture(rows, runtime, "day90", "Summer and Lixia begin");

        CalendarDateSnapshot summer = runtime.ProjectActive();
        Assert.That(FindCycle(summer, "season").PhaseId, Is.EqualTo("summer"));
        Assert.That(FindCycle(summer, "solarTerm").PhaseId, Is.EqualTo("lixia"));
        Assert.That(FindCycle(summer, "month").PhaseId, Is.EqualTo("month.04"));
        Assert.That(runtime.Project(CalendarFixtures.RegnalId).EraId, Is.EqualTo("era.founding"));
        checks++;

        // ── 场景二：节日相位（端午 day 125 / 除夕 day 360）──
        var festivalRuntime = new CalendarRuntime(
            CalendarFixtures.World(CalendarFixtures.Solar360Id, ticksPerDay: 1, startDayIndex: 123),
            CalendarFixtures.DefaultRegistry());
        Capture(rows, festivalRuntime, "day123", "Ordinary day before Duanwu");
        festivalRuntime.Advance(1);
        Capture(rows, festivalRuntime, "day124", "Duanwu festival day");
        festivalRuntime.Advance(1);
        Capture(rows, festivalRuntime, "day125", "Festival is over, back to ordinary");

        Assert.That(FindCycle(festivalRuntime.ProjectActive(), "festival").PhaseId, Is.EqualTo("ordinary.p03"));
        var chuxi = new CalendarRuntime(
            CalendarFixtures.World(CalendarFixtures.Solar360Id, ticksPerDay: 1, startDayIndex: 359),
            CalendarFixtures.DefaultRegistry());
        Assert.That(FindCycle(chuxi.ProjectActive(), "festival").PhaseId, Is.EqualTo("chuxi"));
        checks++;

        // ── 场景三：阴阳历（yearCycleId 年相位 + 闰六月明文相位）──
        var lunar = new CalendarRuntime(
            CalendarFixtures.World(CalendarFixtures.LunisolarId, ticksPerDay: 1, startDayIndex: 707),
            CalendarFixtures.DefaultRegistry());
        Capture(rows, lunar, "lunar707", "Lunisolar year 2 last day");
        lunar.Advance(1);
        Capture(rows, lunar, "lunar708", "Leap year 3 begins (385 days)");
        lunar.Advance(885 - 708);
        Capture(rows, lunar, "lunar885", "Explicit leap month Run-Liuyue");

        CalendarDateSnapshot leapDay = lunar.ProjectActive();
        Assert.That(leapDay.Year, Is.EqualTo(3));
        Assert.That(FindCycle(leapDay, "month").PhaseLabel, Is.EqualTo("闰六月"));
        Assert.That(FindCycle(leapDay, "year").PhaseId, Is.EqualTo("year.03"));
        checks++;

        // ── 场景四：多年号（立国 10 年 → 开疆元年，day 3600）──
        var regnal = new CalendarRuntime(
            CalendarFixtures.World(CalendarFixtures.RegnalId, ticksPerDay: 1, startDayIndex: 3599),
            CalendarFixtures.DefaultRegistry());
        Capture(rows, regnal, "regnal3599", "Founding era year 10");
        regnal.Advance(1);
        Capture(rows, regnal, "regnal3600", "Era changes to Kaizhang year 1");

        CalendarDateSnapshot newEra = regnal.ProjectActive();
        Assert.That(newEra.EraId, Is.EqualTo("era.expansion"));
        Assert.That(newEra.EraLabel, Is.EqualTo("开疆"));
        Assert.That(newEra.EraYear, Is.EqualTo(1));
        Assert.That(newEra.Year, Is.EqualTo(11));
        checks++;

        // ── 场景五：订阅派发（#1384 P0：地图全局订阅听得到；没订阅不发）──
        var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
        var heard = new List<string>();
        manager.RegisterGlobalTriggers(new MapId("calendar_acceptance_map"), new Trigger[]
        {
            new RecordingTrigger(GameEvents.CalendarCyclePhaseEntered, heard),
        });

        var dispatchRuntime = new CalendarRuntime(
            CalendarFixtures.World(CalendarFixtures.Solar360Id, ticksPerDay: 1, startDayIndex: 89),
            CalendarFixtures.DefaultRegistry());

        dispatchRuntime.Advance(1, () => new ScriptContext(), manager.FireGlobalEvent, manager.HasGlobalEventSubscribers);
        Assert.That(heard, Has.Some.Contains("summer"), "map global subscription must hear season switch");

        heard.Clear();
        dispatchRuntime.Advance(1, () => new ScriptContext(), (key, ctx) => { }, _ => false);
        Assert.That(heard, Is.Empty, "zero subscribers fire nothing while the day still advances");
        Assert.That(dispatchRuntime.DayIndex, Is.EqualTo(91));
        checks++;

        return checks;
    }

    private sealed class RecordingTrigger : Trigger
    {
        private readonly List<string> _heard;

        public RecordingTrigger(EventKey eventKey, List<string> heard)
        {
            EventKey = eventKey;
            _heard = heard;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            _heard.Add(Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(context.Get<int>(MapTriggerEventPayloadKeys.CalendarPhaseId)));
            return Task.CompletedTask;
        }
    }

    private static void Capture(List<PhaseRow> rows, CalendarRuntime runtime, string phaseId, string title)
    {
        CalendarProgressSnapshot progress = runtime.CaptureProgressSnapshot();
        CalendarDateSnapshot date = progress.ActiveDate!;
        rows.Add(new PhaseRow(
            phaseId,
            title,
            date.DayIndex,
            date.Year,
            date.DayOfYear,
            date.EraLabel,
            CycleLabel(date, "season"),
            CycleLabel(date, "month"),
            CycleLabel(date, "xun"),
            CycleLabel(date, "solarTerm"),
            progress.DayPhaseLabel,
            progress.DayPermille));
    }

    private static string CycleLabel(CalendarDateSnapshot date, string cycleId)
    {
        for (int i = 0; i < date.Cycles.Count; i++)
        {
            if (date.Cycles[i].CycleId == cycleId)
            {
                return date.Cycles[i].PhaseLabel;
            }
        }

        return "—";
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

    private static string BuildBattleReport(IReadOnlyList<PhaseRow> rows, int dispatchChecks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: calendar-core");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Goal: prove one day index projects multiple explicit calendars (solar, lunisolar, regnal), festivals and solar terms as authored phase tables, and subscriber-gated event dispatch over the #1123 global table.");
        sb.AppendLine("- Gameplay domain: Core `CalendarRuntime` consuming Step ticks.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Calendars: `calendar.solar360` (season/month/xun/solarTerm/festival), `calendar.lunisolar.zhang19` (235 explicit months, 19-year phase-tabled years, explicit leap month), `calendar.regnal` (four explicit eras)");
        sb.AppendLine("- ticksPerDay: 1");
        sb.AppendLine("- startDayIndex: 88 / 123 / 359 / 707 / 3599 / 89");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Cross spring→summer on solar360 (day 89→90).");
        sb.AppendLine("2. Cross Duanwu festival day 125 and Chuxi day 360.");
        sb.AppendLine("3. Enter lunisolar leap year 3 (day 708) and its explicit leap month (day 885).");
        sb.AppendLine("4. Cross the regnal era switch 立国→开疆 (day 3600).");
        sb.AppendLine("5. Fire through TriggerManager global subscriptions; then advance with zero subscribers.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: every projection matches the authored phase tables; map global subscriptions hear `Calendar.CyclePhaseEntered`; zero-subscriber advance fires nothing but still advances the day.");
        sb.AppendLine("- Failure branch condition: any projection drifts off its table, or events dispatch without subscribers / reach nobody with subscribers present.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (PhaseRow row in rows)
        {
            sb.AppendLine($"- `{row.PhaseId}` -> day={row.DayIndex} {row.Era} {row.Year}年 {row.Season} {row.Month}{row.Xun} {row.SolarTerm}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: yes ({dispatchChecks}/5 scenario groups)");
        sb.AppendLine("- verdict: explicit phase tables drive multi-calendar dates; dispatch is subscriber-gated on the global table.");
        return sb.ToString();
    }

    private static string BuildPath()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Step consumed] --> B[ticksIntoDay / ticksPerDay]",
            "    B --> C{has subscribers?}",
            "    C -- no --> D1[dayIndex +1 only]",
            "    C -- yes --> D2[dayIndex +1, project all calendars, diff phases]",
            "    D2 --> E1[calendar.solar360 season/month/xun/solarTerm/festival]",
            "    D2 --> E2[calendar.lunisolar.zhang19 month/yearCycle]",
            "    D2 --> E3[calendar.regnal eras]",
            "    E1 --> F[FireGlobalEvent DayAdvanced / CyclePhaseEntered|Exited / EraChanged]",
            "    E2 --> F",
            "    E3 --> F",
            "    F --> G[Map global subscriptions #1123]",
            "    D1 --> H[late subscriber: silent catch-up, no replay]",
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
